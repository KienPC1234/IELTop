import { useEffect, useState } from 'react'
import { call, onEvent } from '@/bridge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card'
import { ErrorBar, PageHeader } from '@/components/shared'
import ExamSetupPanel from './exam/ExamSetup'

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
    setError('')
    try {
      apply(await call('exam.start'))
      await call('exam.openWindow')
      await call('window.setFullscreen', { value: true }).catch(() => {})
    } catch (e) {
      setError(e.message)
    }
  }

  async function openWindow() {
    try {
      await call('exam.openWindow')
      await call('window.setFullscreen', { value: true }).catch(() => {})
    } catch (e) {
      setError(e.message)
    }
  }

  async function cancelTest() {
    setError('')
    try {
      apply(await call('exam.cancel'))
      await call('exam.closeWindow').catch(() => {})
      setOpen(false)
    } catch (e) {
      setError(e.message)
    }
  }

  if (loading && !exam) return <div className="text-muted-foreground">Loading the test screen...</div>
  if (error && !exam) return <div className="text-destructive">{error}</div>
  if (!exam) return null

  const phase = exam.run?.phase

  return (
    <div className="flex flex-col gap-5">
      <ErrorBar message={error} onDismiss={() => setError('')} />
      <PageHeader
        title="Mock Test"
        description="A full test or one skill, scored offline. Bands are practice estimates only."
      />

      {open && phase !== 'Setup' ? (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">The test is running in its own window</CardTitle>
            <CardDescription>The exam opens separately so you can go full screen without losing the app.</CardDescription>
          </CardHeader>
          <CardContent>
            <p className="mb-4 text-sm leading-relaxed text-muted-foreground">
              Close that window to stop, or reopen it below.
            </p>
            <div className="flex flex-wrap gap-2">
              <Button onClick={openWindow}>Open the test window</Button>
              <Button variant="outline" onClick={refresh}>Refresh status</Button>
              <Button variant="destructive" onClick={cancelTest}>Cancel test</Button>
            </div>
          </CardContent>
        </Card>
      ) : (
        <ExamSetupPanel setup={exam.setup} onApply={apply} onStart={startTest} />
      )}
    </div>
  )
}
