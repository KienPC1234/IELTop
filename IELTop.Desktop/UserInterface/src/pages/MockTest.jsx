import { useEffect, useState } from 'react'
import { call, onEvent } from '../bridge.js'
import { ErrorBar } from '../components/ui.jsx'
import ExamSetup from './exam/ExamSetup.jsx'

/// The Mock Test screen in the main window: pick a paper and start. The test
/// itself opens in its own native window, so a full screen exam never shares
/// the shell. Only the setup lives here.
export default function MockTest() {
  const [exam, setExam] = useState(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)
  const [open, setOpen] = useState(false)

  function apply(next) {
    if (!next) return
    setExam(next)
    // A run that left the Setup phase lives in the exam window.
    const phase = next.run?.phase
    if (phase === 'PartIntro' || phase === 'Running' || phase === 'Finished') setOpen(true)
    if (phase === 'Setup') setOpen(false)
  }

  async function refresh() {
    setLoading(true)
    setError('')
    try {
      apply(await call('exam.snapshot'))
    } catch (e) {
      setError(e.message)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    refresh()
    return onEvent((evt) => {
      if (evt.exam) apply(evt.exam)
    })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  async function startTest() {
    const next = await call('exam.start')
    apply(next)
    try {
      await call('exam.openWindow')
    } catch (e) {
      setError(e.message)
    }
  }

  async function openWindow() {
    try {
      await call('exam.openWindow')
    } catch (e) {
      setError(e.message)
    }
  }

  if (loading && !exam) return <div className="loading">Loading the test screen...</div>
  if (error && !exam) return <div className="error">{error}</div>
  if (!exam) return null

  const phase = exam.run?.phase

  return (
    <>
      <ErrorBar message={error} onDismiss={() => setError('')} />
      <h1 className="page-title">Mock Test</h1>
      <p className="page-sub">
        A full test or one skill, scored offline. Bands are practice estimates only.
      </p>

      {open && phase !== 'Setup' ? (
        <div className="panel">
          <h2>The test is running in its own window</h2>
          <p className="hint">
            The exam opens separately so you can go full screen without losing the app.
            Close that window to stop, or reopen it below.
          </p>
          <div className="actions">
            <button className="btn primary" onClick={openWindow}>
              Open the test window
            </button>
            <button className="btn" onClick={refresh}>
              Refresh the state
            </button>
          </div>
        </div>
      ) : (
        <ExamSetupWithStart setup={exam.setup} onApply={apply} onStart={startTest} />
      )}
    </>
  )
}

/// The setup form, with Start wired to open the exam window.
function ExamSetupWithStart({ setup, onApply, onStart }) {
  return <ExamSetup setup={setup} onApply={onApply} onStart={onStart} />
}
