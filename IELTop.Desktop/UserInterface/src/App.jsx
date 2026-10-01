import { useEffect, useState } from 'react'
import {
  Gauge,
  FileText,
  BookOpen,
  PencilLine,
  BarChart3,
  Server,
  Settings as SettingsIcon,
} from 'lucide-react'
import { call } from './bridge.js'
import Overview from './pages/Overview.jsx'
import MockTest from './pages/MockTest.jsx'
import Library from './pages/Library.jsx'
import Editor from './pages/Editor.jsx'
import Results from './pages/Results.jsx'
import Servers from './pages/Servers.jsx'
import Settings from './pages/Settings.jsx'

const NAV = [
  { key: 'overview', label: 'Overview', Icon: Gauge },
  { key: 'mock', label: 'Mock Test', Icon: FileText },
  { key: 'library', label: 'Library', Icon: BookOpen },
  { key: 'editor', label: 'Editor', Icon: PencilLine },
  { key: 'results', label: 'Results', Icon: BarChart3 },
  { key: 'servers', label: 'Servers', Icon: Server },
  { key: 'settings', label: 'Settings', Icon: SettingsIcon },
]

export default function App() {
  const [page, setPage] = useState('overview')
  const [dashboard, setDashboard] = useState(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)

  async function loadDashboard() {
    setLoading(true)
    setError('')
    try {
      setDashboard(await call('dashboard.get'))
    } catch (e) {
      setError(e.message)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    loadDashboard()
  }, [])

  const current = NAV.find((n) => n.key === page)
  const examActive = page === 'mock'

  const navigate = (key) => setPage(key)

  return (
    <div className={`app${examActive ? ' exam-mode' : ''}`}>
      <aside className="sidebar">
        <div className="brand">
          <span className="brand-mark">IELTop</span>
        </div>
        {NAV.map((item) => (
          <button
            key={item.key}
            className={`nav-item${page === item.key ? ' active' : ''}`}
            onClick={() => setPage(item.key)}
          >
            <item.Icon size={17} strokeWidth={2} aria-hidden="true" />
            <span>{item.label}</span>
          </button>
        ))}
        <div className="spacer" />
        <div className="version-block">
          <div className="label">Version</div>
          <div className="value">{dashboard?.version ?? '1.0.0'}</div>
        </div>
      </aside>

      <main className={`main${examActive ? ' exam-main' : ''}`}>
        {page === 'overview' && (
          <Overview data={dashboard} loading={loading} error={error} onRefresh={loadDashboard} />
        )}
        {page === 'mock' && <MockTest />}
        {page === 'library' && <Library onNavigate={navigate} />}
        {page === 'editor' && <Editor onNavigate={navigate} />}
        {page === 'results' && <Results />}
        {page === 'servers' && <Servers onNavigate={navigate} />}
        {page === 'settings' && <Settings />}
      </main>
    </div>
  )
}
