import { useState } from 'react'
import { call } from '../../bridge.js'

/// The result screen: band, objective score, review, and AI marking.
export default function ExamResult({ exam, onApply }) {
  const [busy, setBusy] = useState(false)
  const run = exam.run

  async function runCall(method, args) {
    setBusy(true)
    try {
      onApply(await call(method, args))
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
    <>
      <div className="panel result-head">
        <div>
          <div className="stat-label">Objective band</div>
          <div className="stat-value">{run.bandLabel || 'Not scored'}</div>
        </div>
        <p className="hint">{run.resultText}</p>
        {run.criteriaHint && <p className="hint">{run.criteriaHint}</p>}
        {run.strictViolations > 0 && <p className="warn">{run.violationLabel}</p>}
      </div>

      <div className="actions">
        <button className="btn primary" disabled={busy} onClick={() => runCall('exam.toggleReview')}>
          {run.showReview ? 'Hide review' : 'Review answers'}
        </button>
        <button className="btn" disabled={busy || run.isGrading} onClick={() => runCall('exam.aiMark')}>
          {run.isGrading ? run.loadingLabel || 'Marking...' : 'Grade with AI'}
        </button>
        {run.isGrading && (
          <button className="btn" onClick={() => runCall('exam.stopAiMark')}>
            Stop
          </button>
        )}
        <button
          className="btn"
          disabled={busy}
          onClick={async () => {
            await runCall('exam.backToSetup')
            call('exam.closeWindow').catch(() => {})
          }}
        >
          Back to setup
        </button>
      </div>

      {run.hasAiFeedback && (
        <div className="panel">
          <div className="row" style={{ justifyContent: 'space-between' }}>
            <h2 style={{ margin: 0 }}>AI feedback</h2>
            <button className="btn" onClick={copyFeedback}>
              Copy feedback
            </button>
          </div>
          <pre className="feedback">{run.aiFeedbackLines.join('\n')}</pre>
        </div>
      )}

      {run.showReview && (
        <div className="panel">
          <h2>Review</h2>
          {run.reviewItems.length === 0 ? (
            <div className="empty">Nothing to review.</div>
          ) : (
            <ul className="review">
              {run.reviewItems.map((item, i) => (
                <li
                  key={i}
                  className={
                    item.isGood === true ? 'good' : item.isGood === false ? 'bad' : 'neutral'
                  }
                >
                  {item.text}
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

      <p className="hint">
        Writing and Speaking are marked by AI only. Bands are practice estimates,
        not official IELTS scores.
      </p>
    </>
  )
}
