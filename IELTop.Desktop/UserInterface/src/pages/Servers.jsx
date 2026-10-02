import { usePage } from '../hooks.js'
import { ErrorBar } from '../components/ui.jsx'

/// Browse IELTop content servers, connect, and download papers. Servers can be
/// anonymous, need an access code, or need a login. Secrets stay on this machine.
export default function Servers({ onNavigate }) {
  const page = usePage('servers.snapshot')
  const d = page.data
  if (page.loading && !d) return <div className="loading">Loading servers...</div>
  if (page.error && !d) return <div className="error">{page.error}</div>
  if (!d) return null

  const run = page.run

  return (
    <>
      <ErrorBar message={page.error} onDismiss={() => page.setError('')} />
      <h1 className="page-title">Servers</h1>
      <p className="page-sub">Download shared papers, then run them in Mock Test.</p>

      <div className="panel">
        <h2>Server list</h2>
        <ul className="server-list">
          {d.servers.map((s) => (
            <li key={s.url} className={`server-item${d.selectedServerUrl === s.url ? ' on' : ''}`}>
              <button className="link" onClick={() => run('servers.select', { url: s.url })}>
                <span className="server-name">{s.name}</span>
                <span className="hint">{s.detail} | {s.url}</span>
              </button>
              <button
                className="btn"
                onClick={async () => {
                  await run('servers.select', { url: s.url })
                  await run('servers.connect')
                }}
              >
                Connect
              </button>
            </li>
          ))}
        </ul>
        {d.serverInfo && <div className="hint">{d.serverInfo}</div>}
      </div>

      <div className="panel">
        <h2>Papers from this server</h2>
        <div className="row">
          <input
            type="text"
            placeholder="Search title"
            value={d.searchText}
            onChange={(e) => run('servers.setSearch', { value: e.target.value })}
          />
          <select value={d.selectedCategory} onChange={(e) => run('servers.setCategory', { value: e.target.value })}>
            {d.categories.map((c) => (
              <option key={c} value={c}>{c}</option>
            ))}
          </select>
          <button className="btn" disabled={page.busy} onClick={() => run('servers.connect')}>Connect</button>
          <button className="btn" disabled={!d.hasPapers || page.busy} onClick={() => run('servers.downloadAll')}>
            Download all
          </button>
          {page.busy && <button className="btn" onClick={() => run('servers.cancel')}>Stop</button>}
        </div>

        {d.papers.length === 0 ? (
          <div className="empty">Pick a server and press Connect to list its papers.</div>
        ) : (
          <ul className="paper-list">
            {d.papers.map((p) => (
              <li key={p.id} className="paper-item">
                <div className="paper-main">
                  <div className="paper-title">
                    {p.title}
                    {p.isDownloaded && <span className="tag">Downloaded</span>}
                    {p.isUpdateAvailable && <span className="tag warn-tag">Update</span>}
                  </div>
                  <div className="hint">{p.summary}</div>
                </div>
                <button
                  className="btn primary"
                  disabled={page.busy}
                  onClick={() => run('servers.downloadPaper', { id: p.id })}
                >
                  {p.isDownloaded ? 'Re-download' : 'Download'}
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>

      <div className="panel">
        <h2>Add a server</h2>
        <div className="grid2">
          <div className="field">
            <label>Name</label>
            <input type="text" value={d.newName} onChange={(e) => run('servers.setNewName', { value: e.target.value })} />
          </div>
          <div className="field">
            <label>Address</label>
            <input
              type="text"
              placeholder="https://example.com"
              value={d.newUrl}
              onChange={(e) => run('servers.setNewUrl', { value: e.target.value })}
            />
          </div>
          <div className="field">
            <label>Auth mode</label>
            <select value={d.authMode} onChange={(e) => run('servers.setAuthMode', { value: e.target.value })}>
              {d.authModes.map((m) => (
                <option key={m} value={m}>{m}</option>
              ))}
            </select>
          </div>
          <div className="field">
            <label>Username (login mode)</label>
            <input type="text" value={d.username} onChange={(e) => run('servers.setUsername', { value: e.target.value })} />
          </div>
          <div className="field">
            <label>Access code or password</label>
            <input
              type="password"
              value={d.secret}
              placeholder={d.hasSecret ? 'Saved' : ''}
              onChange={(e) => run('servers.setSecret', { value: e.target.value })}
            />
          </div>
          <label className="check">
            <input
              type="checkbox"
              checked={d.allowInsecure}
              onChange={(e) => run('servers.setAllowInsecure', { value: e.target.checked })}
            />
            Allow plain http on a remote host
          </label>
        </div>
        <div className="actions">
          <button className="btn primary" onClick={() => run('servers.add')}>Save server</button>
          <button className="btn" disabled={!d.hasSelection} onClick={() => run('servers.remove')}>
            Remove saved server
          </button>
        </div>
      </div>

      <div className="status-line">{d.statusMessage}</div>
    </>
  )
}
