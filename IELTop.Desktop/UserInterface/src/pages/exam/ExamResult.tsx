import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { cn } from '@/lib/utils'
import { call, closeExamWindow } from '@/bridge'
import { ErrorBar } from '@/components/shared'

/// The result screen: band, objective score, review, and AI marking.
export default function ExamResult({ exam, onApply }) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const run = exam.run

  async function runCall(method: string, args: any = {}) {
    setBusy(true)
    setError('')
    try {
      onApply(await call(method, args))
    } catch (e) {
      setError(e.message)
    } finally {
      setBusy(false)
    }
  }

  async function copyFeedback() {
    try {
      const text = await call('exam.feedbackText')
      await navigator.clipboard.writeText(text ?? '')
    } catch {
      // The clipboard can be blocked; the text is visible on screen anyway.
    }
  }

  return (
    <div className="mx-auto flex w-full max-w-3xl flex-col gap-5 p-6">
      <ErrorBar message={error} onDismiss={() => setError('')} />

      <Card>
        <CardContent className="flex flex-col gap-2">
          <div>
            <div className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Objective band</div>
            <div className="mt-0.5 text-2xl font-bold tracking-tight tabular-nums">{run.bandLabel || 'Not scored'}</div>
          </div>
          <p className="text-sm leading-relaxed text-muted-foreground">{run.resultText}</p>
          {run.criteriaHint && <p className="text-sm leading-relaxed text-muted-foreground">{run.criteriaHint}</p>}
          {run.strictViolations > 0 && <p className="text-sm leading-relaxed text-amber-700 dark:text-amber-300">{run.violationLabel}</p>}
        </CardContent>
      </Card>

      <div className="flex flex-wrap gap-2">
        <Button disabled={busy} onClick={() => runCall('exam.toggleReview')}>
          {run.showReview ? 'Hide review' : 'Review answers'}
        </Button>
        <Button
          variant="outline"
          disabled={busy || run.isGrading || !run.canUseAi}
          title={run.canUseAi ? '' : 'Add a language model in Settings to grade with AI.'}
          onClick={() => runCall('exam.aiMark')}
        >
          {run.isGrading ? run.loadingLabel || 'Marking...' : 'Grade with AI'}
        </Button>
        {run.isGrading && <Button variant="destructive" onClick={() => runCall('exam.stopAiMark')}>Stop</Button>}
        <Button
          variant="outline"
          disabled={busy}
          onClick={async () => {
            await runCall('exam.backToSetup')
            closeExamWindow()
          }}
        >
          Back to setup
        </Button>
      </div>

      {run.hasAiFeedback && (
        <Card>
          <CardContent>
            <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
              <h2 className="text-sm font-semibold leading-relaxed">AI feedback</h2>
              <Button size="sm" variant="outline" onClick={copyFeedback}>Copy feedback</Button>
            </div>
            <pre className="whitespace-pre-wrap text-sm leading-relaxed">{(run.aiFeedbackLines ?? []).join('\n')}</pre>
          </CardContent>
        </Card>
      )}

      {run.showReview && (
        <Card>
          <CardContent>
            <h2 className="mb-2 text-sm font-semibold leading-relaxed">Review</h2>
            {(run.reviewItems ?? []).length === 0 ? (
              <p className="text-sm leading-relaxed text-muted-foreground">Nothing to review.</p>
            ) : (
              <ul className="flex flex-col">
                {(run.reviewItems ?? []).map((item, i) => (
                  <li
                    key={i}
                    className={cn(
                      'border-b border-border py-1.5 text-sm leading-relaxed last:border-0',
                      item.isGood === true && 'text-green-700 dark:text-green-400',
                      item.isGood === false && 'text-destructive'
                    )}
                  >
                    {item.text}
                  </li>
                ))}
              </ul>
            )}
          </CardContent>
        </Card>
      )}

      <p className="text-sm leading-relaxed text-muted-foreground">
        Writing and Speaking are marked by AI only. Bands are practice estimates, not official IELTS scores.
      </p>
    </div>
  )
}
