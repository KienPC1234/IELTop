import { useEffect, useRef, useState } from 'react'
import { call, closeExamWindow, onEvent } from '@/bridge'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { applyOutputDevice, blobToWavBase64 } from '@/audio'
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
  const [closeConfirm, setCloseConfirm] = useState(false)
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
      // The host held back a close of the title bar button or Alt+F4 and asks
      // here instead, because the answers are the thing at risk.
      if (evt.event === 'exam.closeRequested') setCloseConfirm(true)
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
        const wav = await blobToWavBase64(blob)
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

  // Closing while the test is on the clock is refused by the host, which
  // answers with needsConfirm so the question is asked here first.
  async function requestClose() {
    const answer = await closeExamWindow()
    if (answer?.needsConfirm) setCloseConfirm(true)
  }

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

  // Enter full screen when the test starts.
  useEffect(() => {
    if (phase === 'PartIntro' || phase === 'Running') {
      call('window.setFullscreen', { value: true }).catch(() => {})
    }
  }, [phase])

  if (error) {
    return (
      <div className="grid h-screen place-items-center bg-exam-surface p-10">
        <div className="w-[520px] max-w-full border border-destructive/30 bg-exam-block px-6 py-7 text-center">
          <p className="text-base font-bold text-foreground">The test window hit a problem</p>
          <p className="mt-1 text-sm leading-relaxed text-destructive">{error}</p>
          <Button className="mt-5 rounded-none" variant="outline" onClick={requestClose}>
            Close this window
          </Button>
        </div>
      </div>
    )
  }
  if (!exam) {
    return (
      <div className="grid h-screen place-items-center bg-exam-surface p-10">
        <div className="w-[420px] max-w-full border border-exam-block-border bg-exam-block px-6 py-7 text-center">
          <p className="text-base font-bold text-foreground">Loading the test</p>
          <p className="mt-1 text-sm leading-relaxed text-muted-foreground">
            The paper is being prepared.
          </p>
        </div>
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
      <div className="grid h-screen place-items-center bg-exam-surface p-10">
        <div className="w-[480px] max-w-full border border-exam-block-border bg-exam-block px-8 py-7 text-center">
          <p className="text-xl font-bold tracking-tight">No test is running</p>
          <p className="mt-1 text-sm leading-relaxed text-muted-foreground">
            Start a test from the Mock Test screen in the main window.
          </p>
          <Button className="mt-5 rounded-none" variant="outline" onClick={requestClose}>
            Close this window
          </Button>
        </div>
      </div>
    )
  }

  return (
    <>
      <ExamRunner exam={exam} onApply={apply} fontScale={exam.run?.fontScale ?? settings.fontScale} examDark={examDark} onToggleExamDark={() => setExamDark((v) => !v)} />
      <Dialog open={closeConfirm} onOpenChange={setCloseConfirm}>
        <DialogContent className="max-w-md">
          <DialogHeader>
            <DialogTitle>Leave the test</DialogTitle>
            <DialogDescription>
              This test has not been submitted. Closing the window discards the answers.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setCloseConfirm(false)}>
              Back to the test
            </Button>
            <Button variant="destructive" onClick={() => call('exam.confirmCloseWindow')}>
              Close and discard
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  )
}
