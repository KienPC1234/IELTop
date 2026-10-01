import ActivityBars from '../components/ActivityBars.jsx'
import BandTrend from '../components/BandTrend.jsx'

export default function Overview({ data, loading, error, onRefresh }) {
  if (loading && !data) return <div className="loading">Loading dashboard...</div>
  if (error) return <div className="error">{error}</div>
  if (!data) return null

  const hasActivity = data.weeklyActivity.some((d) => d.count > 0)
  const hasTrend = data.bandTrend.length >= 2

  return (
    <>
      <h1 className="page-title">Overview</h1>
      <p className="page-sub">Your study at a glance, read from local results only.</p>

      <div className="cards">
        <div className="card streak">
          <div className="stat-label">Streak</div>
          <div className="stat-value">{data.streakLabel}</div>
          <div className="stat-hint">{data.streakHint}</div>
        </div>
        <div className="card">
          <div className="stat-label">Tests finished</div>
          <div className="stat-value">{data.examAttempts}</div>
          <div className="stat-hint">Last band {data.lastBandLabel}</div>
        </div>
        <div className="card">
          <div className="stat-label">Models ready</div>
          <div className="stat-value">
            {data.modelsReady} of {data.modelsTotal}
          </div>
          <div className="stat-hint">Offline models in Assets/Models</div>
        </div>
        <div className="card">
          <div className="stat-label">Writing assistant</div>
          <div className="stat-value">{data.llmConfigured ? 'Ready' : 'Off'}</div>
          <div className="stat-hint">{data.llmSummary}</div>
        </div>
      </div>

      <div className="panel">
        <h2>Activity, last 7 days</h2>
        {hasActivity ? (
          <ActivityBars days={data.weeklyActivity} />
        ) : (
          <div className="empty">
            No study activity yet. Finish a test or a speaking practice to fill this in.
          </div>
        )}
      </div>

      <div className="panel">
        <h2>Band trend, last tests</h2>
        {hasTrend ? (
          <BandTrend points={data.bandTrend} />
        ) : (
          <div className="empty">
            Finish at least two tests to see a band trend.
          </div>
        )}
      </div>

      <button className="nav-item" style={{ color: 'var(--accent)' }} onClick={onRefresh}>
        Refresh dashboard
      </button>
    </>
  )
}
