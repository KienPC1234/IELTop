import { useEffect, useRef, useState } from 'react'
import { call, onEvent } from './bridge.js'
import { applyOutputDevice } from './audio.js'
import ExamRunner from './pages/exam/ExamRunner.jsx'
import ExamResult from './pages/exam/ExamResult.jsx'

/// The dedicated exam window. It owns the running test: the clock, the parts,
/// the audio, and the result. The main window only shows the setup, so the
/// exam can go full screen on its own without touching the app shell.
export default function ExamWindow() {
  const [exam, setExam] = useState(null)
  const [error, setError] = useState('')
  const [settings, setSettings] = useState({ fontScale: 1, audioIn: '', audioOut: '' })
  const mediaRef = useRef({ player: null })

  function apply(next) {
    if (next) setExam(next)
  }

  useEffect(() => {
    // Text size and the chosen devices come from Settings.
    call('settings.snapshot')
      .then((s) =>
        setSettings({
          fontScale: s?.fontScale ?? 1,
          audioIn: s?.audioInputDeviceId ?? '',
          audioOut: s?.audioOutputDeviceId ?? '',
        })
      )
      .catch(() => {})

    call('exam.snapshot')
      .then(apply)
      .catch((e) => setError(e.message))

    return onEvent((evt) => {
      if (evt.exam) apply(evt.exam)
      if (evt.event === 'playAudio' && evt.url) playOnce(evt.url)
      if (evt.event === 'record') startRecording(evt.seconds ?? 60)
    })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  function playOnce(url) {
    try {
      mediaRef.current.player?.pause()
      const audio = new Audio(url)
      audio.volume = Math.max(0, Math.min(1, (exam?.run?.volume ?? 80) / 100))
      mediaRef.current.player = audio
      // Play on the chosen speaker where the platform allows it.
      applyOutputDevice(audio, settings.audioOut)
      audio.onended = () => call('exam.audioFinished', { ok: true })
      audio.onerror = () => call('exam.audioFinished', { ok: false })
      audio.play().catch(() => call('exam.audioFinished', { ok: false }))
    } catch {
      call('exam.audioFinished', { ok: false })
    }
  }

  // Speaking: record in the page, then hand the wav to the host for the models.
  async function startRecording(seconds) {
    try {
      if (!navigator.mediaDevices?.getUserMedia) {
        await call('exam.recordingFailed')
        return
      }
      const constraints = settings.audioIn
        ? { audio: { deviceId: { exact: settings.audioIn } } }
        : { audio: true }
      const stream = await navigator.mediaDevices.getUserMedia(constraints)
      const chunks = []
      let cancelled = false
      const mime = MediaRecorder.isTypeSupported('audio/webm') ? 'audio/webm' : ''
      const recorder = new MediaRecorder(stream, mime ? { mimeType: mime } : undefined)
      recorder.ondataavailable = (e) => e.data.size && chunks.push(e.data)
      recorder.onstop = async () => {
        stream.getTracks().forEach((t) => t.stop())
        // Stopping because the test was left is not a recording result.
        if (cancelled) return
        const blob = new Blob(chunks, { type: recorder.mimeType || 'audio/webm' })
        const wav = await toWavBase64(blob)
        const next = wav
          ? await call('exam.recordingDone', { base64: wav })
          : await call('exam.recordingFailed')
        apply(next)
      }
      mediaRef.current.recorder = {
        cancel: () => {
          cancelled = true
          try {
            recorder.stop()
          } catch {
            /* already stopped */
          }
        },
      }
      recorder.start()
      mediaRef.current.recorderTimer = setTimeout(() => {
        try {
          recorder.stop()
        } catch {
          /* already stopped */
        }
      }, Math.max(10, seconds) * 1000)
    } catch {
      apply(await call('exam.recordingFailed'))
    }
  }

  const phase = exam?.run?.phase

  // Leaving the run stops any recording without saving it.
  useEffect(() => {
    if (phase !== 'Running' && phase !== 'PartIntro') {
      mediaRef.current.recorder?.cancel()
      clearTimeout(mediaRef.current.recorderTimer)
      mediaRef.current.recorder = null
    }
  }, [phase])

  // Enter full screen when the test starts if the user asked for it in Settings.
  useEffect(() => {
    if (phase === 'PartIntro' || phase === 'Running') {
      call('settings.snapshot')
        .then((s) => {
          if (s?.fullscreenOnStart) call('window.setFullscreen', { value: true }).catch(() => {})
        })
        .catch(() => {})
    }
  }, [phase])

  if (error) return <div className="error" style={{ margin: 24 }}>{error}</div>
  if (!exam) return <div className="loading" style={{ margin: 24 }}>Loading the test...</div>

  if (phase === 'Finished') {
    return (
      <div className="main exam-main">
        <ExamResult exam={exam} onApply={apply} />
      </div>
    )
  }

  if (phase === 'Setup') {
    return (
      <div className="main exam-main">
        <h1 className="page-title">No test is running</h1>
        <p className="sub">Start a test from the Mock Test screen in the main window.</p>
      </div>
    )
  }

  return <ExamRunner exam={exam} onApply={apply} fontScale={exam.run?.fontScale ?? settings.fontScale} />
}

/// Decodes a recorded blob to 16 kHz mono WAV, base64, the model input format.
async function toWavBase64(blob) {
  const Ctx = window.AudioContext || window.webkitAudioContext
  const Offline = window.OfflineAudioContext || window.webkitOfflineAudioContext
  let decodeCtx
  try {
    const buf = await blob.arrayBuffer()
    decodeCtx = new Ctx()
    const decoded = await decodeCtx.decodeAudioData(buf.slice(0))
    const frames = Math.max(1, Math.round(decoded.duration * 16000))
    const offline = new Offline(1, frames, 16000)
    const source = offline.createBufferSource()
    source.buffer = decoded
    source.connect(offline.destination)
    source.start()
    const rendered = await offline.startRendering()
    return encodeWavBase64(rendered.getChannelData(0), 16000)
  } catch {
    return ''
  } finally {
    decodeCtx?.close().catch(() => {})
  }
}

function encodeWavBase64(samples, sampleRate) {
  const buffer = new ArrayBuffer(44 + samples.length * 2)
  const view = new DataView(buffer)
  const writeStr = (offset, text) => {
    for (let i = 0; i < text.length; i++) view.setUint8(offset + i, text.charCodeAt(i))
  }
  writeStr(0, 'RIFF')
  view.setUint32(4, 36 + samples.length * 2, true)
  writeStr(8, 'WAVE')
  writeStr(12, 'fmt ')
  view.setUint32(16, 16, true)
  view.setUint16(20, 1, true)
  view.setUint16(22, 1, true)
  view.setUint32(24, sampleRate, true)
  view.setUint32(28, sampleRate * 2, true)
  view.setUint16(32, 2, true)
  view.setUint16(34, 16, true)
  writeStr(36, 'data')
  view.setUint32(40, samples.length * 2, true)
  let offset = 44
  for (let i = 0; i < samples.length; i++) {
    const s = Math.max(-1, Math.min(1, samples[i]))
    view.setInt16(offset, s < 0 ? s * 0x8000 : s * 0x7fff, true)
    offset += 2
  }
  let binary = ''
  const bytes = new Uint8Array(buffer)
  for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i])
  return 'data:audio/wav;base64,' + btoa(binary)
}
