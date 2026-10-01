import { useState } from 'react'
import { usePage } from '../hooks.js'
import { Confirm, ErrorBar } from '../components/ui.jsx'

/// Past mock test results with band ranges and the official criteria tables.
/// Bands are practice estimates, never official scores.
export default function Results() {
  const page = usePage('results.snapshot')
  const [confirm, setConfirm] = useState(false)
  const [open, setOpen] = useState(null)

  const d = page.data
  if (page.loading && !d) return <div className="loading">Loading results...</div>
  if (page.error && !d) return <div className="error">{page.error}</div>
  if (!d) return null

  return (
    <>
      <ErrorBar message={page.error} onDismiss={() => page.setError('')} />
      <h1 className="page-title">Results</h1>
      <p className="page-sub">{d.summaryLabel}</p>

      <div className="panel">
        <div className="row">
          <select value={d.scopeFilter} onChange={(e) => page.run('results.setScope', { value: e.target.value })}>
            {d.scopeFilters.map((s) => (
              <option key={s} value={s}>{s}</option>
            ))}
          </select>
          <input
            type="text"
            placeholder="Search paper title"
            value={d.searchText}
            onChange={(e) => page.run('results.setSearch', { value: e.target.value })}
          />
          <button className="btn" onClick={() => page.run('results.snapshot')}>Reload</button>
          <button className="btn" disabled={!d.hasAttempts} onClick={() => setConfirm(true)}>
            Clear all
          </button>
        </div>
      </div>

      <div className="panel">
        {d.filtered.length === 0 ? (
          <div className="empty">{d.statusMessage}</div>
        ) : (
          <table className="data-table">
            <thead>
              <tr>
                <th>Date</th>
                <th>Paper</th>
                <th>Scope</th>
                <th>Band</th>
                <th>Score</th>
                <th>Marking</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {d.filtered.map((a) => (
                <tr key={a.id}>
                  <td>{a.createdAt}</td>
                  <td>{a.paperTitle}</td>
                  <td>{a.scope}</td>
                  <td className="band">{a.bandLabel}</td>
                  <td>
                    {a.correct}/{a.total}
                    {a.violations > 0 && <span className="tag warn-tag">left test {a.violations}x</span>}
                  </td>
                  <td>
                    {a.strictness}
                    {a.writingBand && <span className="tag">Writing {a.writingBand}</span>}
                    {a.speakingBand && <span className="tag">Speaking {a.speakingBand}</span>}
                  </td>
                  <td>
                    <button className="btn" onClick={() => setOpen(open === a.id ? null : a.id)}>
                      {open === a.id ? 'Hide' : 'Detail'}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}

        {open != null && (
          <div className="result-detail">
            {(() => {
              const a = d.filtered.find((x) => x.id === open)
              if (!a) return null
              return (
                <>
                  <h3>Summary</h3>
                  <p>{a.summary}</p>
                  {a.aiFeedback && (
                    <>
                      <h3>AI feedback</h3>
                      <pre className="feedback">{a.aiFeedback}</pre>
                    </>
                  )}
                </>
              )
            })()}
          </div>
        )}
      </div>

      <div className="panel">
        <h2>Marking criteria</h2>
        <p className="hint">{d.criteriaHint}</p>
        <div className="grid2">
          <div>
            <h3>Writing</h3>
            <ul className="plain">
              {d.writingTable.map((line, i) => (
                <li key={i}>{line}</li>
              ))}
            </ul>
          </div>
          <div>
            <h3>Speaking</h3>
            <ul className="plain">
              {d.speakingTable.map((line, i) => (
                <li key={i}>{line}</li>
              ))}
            </ul>
          </div>
        </div>
      </div>

      <Confirm
        open={confirm}
        title="Clear results"
        body="Delete all finished test results from this computer?"
        confirmLabel="Clear all"
        onCancel={() => setConfirm(false)}
        onConfirm={async () => {
          setConfirm(false)
          await page.run('results.clearAll')
        }}
      />
    </>
  )
}
