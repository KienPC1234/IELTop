import { useEffect, useRef, useState } from 'react'
import { call, closeExamWindow, onEvent } from '@/bridge'
import { Button } from '@/components/ui/button'
import { applyOutputDevice } from '@/audio'
import { applyExamDark, readExamDark } from '@/lib/theme'
import ExamRunner from './pages/exam/ExamRunner'
import ExamResult from './pages/exam/ExamResult'

/// The dedicated exam window. It owns the running test: the clock, the parts,
/// the audio, and the result. The main window only shows the setup, so the
/// exam can go full screen on its own without touching the app shell.
/// The exam always opens light, like the real test; the student can switch it
/// dark from the top bar. That choice stays in this window only.
interface MediaRefState {
  player?: HTMLAudioElement | null
  recorder?: { cancel: () => void } | null
  recorderTimer?: any
}

export default function ExamWindow() {
  const [exam, setExam] = useState<any>(null)
  const [error, setError] = useState('')
  const [settings, setSettings] = useState({ fontScale: 1, audioIn: '', audioOut: '' })
  const [examDark, setExamDark] = useState(() => readExamDark())
  const mediaRef = useRef<MediaRefState>({ player: null })

  useEffect(() => {
    applyExamDark(examDark)
  }, [examDark])

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
        // A failed call still pushes the engine state, so the audio status on
        // the part carries the reason; do not let the rejection escape.
        try {
          const next = wav
            ? await call('exam.recordingDone', { base64: wav })
            : await call('exam.recordingFailed')
          apply(next)
        } catch {
          /* the engine pushed the reason with the snapshot */
        }
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
      try {
        apply(await call('exam.recordingFailed'))
      } catch {
        /* the engine pushed the reason with the snapshot */
      }
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

  // Escape leaves full screen, so a full screen test always has a way out.
  useEffect(() => {
    function onKey(e) {
      if (e.key === 'Escape') call('window.setFullscreen', { value: false }).catch(() => {})
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

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

  if (error) {
    return (
      <div className="grid h-screen place-items-center bg-background p-10">
        <div className="w-[480px] max-w-full rounded-none border border-destructive/30 bg-destructive/10 px-6 py-5 text-center">
          <p className="text-sm leading-relaxed text-destructive">{error}</p>
          <Button className="mt-4" variant="outline" onClick={() => closeExamWindow()}>
            Close this window
          </Button>
        </div>
      </div>
    )
  }
  if (!exam) {
    return (
      <div className="grid h-screen place-items-center bg-background p-10">
        <p className="text-sm text-muted-foreground">Loading the test...</p>
      </div>
    )
  }

  if (phase === 'Finished') {
    return (
      <div className="h-full overflow-y-auto">
        <ExamResult exam={exam} onApply={apply} />
      </div>
    )
  }

  if (phase === 'Setup') {
    return (
      <div className="grid h-screen place-items-center bg-background p-10">
        <div className="w-[480px] max-w-full rounded-none border border-border bg-card px-8 py-7 text-center shadow-sm">
          <p className="text-xl font-bold tracking-tight">No test is running</p>
          <p className="mt-1 text-sm leading-relaxed text-muted-foreground">
            Start a test from the Mock Test screen in the main window.
          </p>
          <Button className="mt-5" variant="outline" onClick={() => closeExamWindow()}>
            Close this window
          </Button>
        </div>
      </div>
    )
  }

  return <ExamRunner exam={exam} onApply={apply} fontScale={exam.run?.fontScale ?? settings.fontScale} examDark={examDark} onToggleExamDark={() => setExamDark((v) => !v)} />
}

/// Decodes a recorded blob to 16 kHz mono WAV, base64, the model input format.
async function toWavBase64(blob: Blob) {
  const Ctx = window.AudioContext || (window as any).webkitAudioContext
  const Offline = window.OfflineAudioContext || (window as any).webkitOfflineAudioContext
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
