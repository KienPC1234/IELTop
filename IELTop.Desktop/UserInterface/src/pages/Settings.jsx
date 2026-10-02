import { useEffect, useState } from 'react'
import { usePage } from '../hooks.js'
import { call } from '../bridge.js'
import { Confirm, ErrorBar } from '../components/ui.jsx'
import { listDevices, playTestTone, recordAndPlayback, mediaSupported } from '../audio.js'

/// Language model settings, exam preferences, About, and updates. All are
/// optional: with no model set the app works fully offline.
export default function Settings() {
  const page = usePage('settings.snapshot')
  const [confirm, setConfirm] = useState(null)
  const [models, setModels] = useState(null)
  const [devices, setDevices] = useState({ speakers: [], mics: [] })
  const [audioBusy, setAudioBusy] = useState('')
  const [audioNote, setAudioNote] = useState('')
  const d = page.data

  useEffect(() => {
    call('settings.models')
      .then(setModels)
      .catch(() => setModels([]))
  }, [])

  async function refreshDevices(unlock = false) {
    try {
      if (unlock && mediaSupported()) {
        // A brief capture unlocks device labels in the WebView.
        const stream = await navigator.mediaDevices.getUserMedia({ audio: true })
        stream.getTracks().forEach((t) => t.stop())
      }
      setDevices(await listDevices())
    } catch {
      setDevices({ speakers: [], mics: [] })
    }
  }

  useEffect(() => {
    refreshDevices(false)
  }, [])

  if (page.loading && !d) return <div className="loading">Loading settings...</div>
  if (page.error && !d) return <div className="error">{page.error}</div>
  if (!d) return null

  const run = page.run
  const set = (method, value) => run(method, { value })

  return (
    <>
      <ErrorBar message={page.error} onDismiss={() => page.setError('')} />
      <h1 className="page-title">Settings</h1>
      <p className="page-sub">Connect any OpenAI compatible chat server. Everything else works offline.</p>

      <div className="panel">
        <h2>Language model</h2>
        <div className="grid2">
          <div className="field">
            <label>Base URL</label>
            <input
              type="text"
              placeholder="https://api.openai.com/v1"
              value={d.baseUrl}
              onChange={(e) => set('settings.setBaseUrl', e.target.value)}
            />
          </div>
          <div className="field">
            <label>Model</label>
            <input type="text" placeholder="gpt-4o-mini" value={d.model} onChange={(e) => set('settings.setModel', e.target.value)} />
          </div>
          <div className="field">
            <label>API key</label>
            <input
              type="password"
              value={d.apiKey}
              placeholder={d.hasApiKey ? 'Saved' : ''}
              onChange={(e) => set('settings.setApiKey', e.target.value)}
            />
          </div>
          <div className="field">
            <label>Temperature</label>
            <input
              type="number"
              step="0.1"
              min="0"
              max="2"
              value={d.temperature}
              onChange={(e) => set('settings.setTemperature', Number(e.target.value))}
            />
          </div>
          <div className="field">
            <label>Max tokens</label>
            <input type="number" min="64" value={d.maxTokens} onChange={(e) => set('settings.setMaxTokens', Number(e.target.value))} />
          </div>
          <div className="field">
            <label>Top P</label>
            <input type="number" step="0.05" min="0" max="1" value={d.topP} onChange={(e) => set('settings.setTopP', Number(e.target.value))} />
          </div>
          <div className="field">
            <label>Timeout (seconds, 15 to 300)</label>
            <input type="number" min="15" max="300" value={d.timeoutSeconds} onChange={(e) => set('settings.setTimeout', Number(e.target.value))} />
          </div>
        </div>
        <div className="field">
          <label>Custom examiner prompt (empty uses the built in one)</label>
          <textarea
            className="essay"
            rows={3}
            value={d.systemPrompt}
            onChange={(e) => set('settings.setSystemPrompt', e.target.value)}
          />
        </div>
        <label className="check">
          <input type="checkbox" checked={d.useStreaming} onChange={(e) => set('settings.setUseStreaming', e.target.checked)} />
          Stream replies
        </label>
        <label className="check">
          <input type="checkbox" checked={d.visionEnabled} onChange={(e) => set('settings.setVision', e.target.checked)} />
          Send images to models that accept them
        </label>
        <label className="check">
          <input type="checkbox" checked={d.modelAutoLoad} onChange={(e) => set('settings.setModelAutoLoad', e.target.checked)} />
          Load offline models when a feature starts, unload after
        </label>

        {d.problems.length > 0 && (
          <ul className="issues">
            {d.problems.map((p, i) => (
              <li key={i} className={p.isError ? 'bad' : ''}>{p.text}</li>
            ))}
          </ul>
        )}
        <p className="hint">{d.checkSummary}</p>

        <div className="actions">
          <button className="btn primary" onClick={() => run('settings.save')}>Save</button>
          <button className="btn" disabled={!d.isValid || page.busy} onClick={() => run('settings.testConnection')}>
            {page.busy ? 'Working...' : 'Test connection'}
          </button>
          <button className="btn" onClick={() => run('settings.compactDatabase')}>Compact database</button>
          <button
            className="btn"
            onClick={() =>
              setConfirm({
                title: 'Reset settings',
                body: 'Reset every setting on this page to its default?',
                confirmLabel: 'Reset',
                onConfirm: async () => {
                  setConfirm(null)
                  await run('settings.reset')
                },
              })
            }
          >
            Reset settings
          </button>
        </div>
        <p className="hint">
          Test connection only checks what is on this screen. Press Save to keep the values.
        </p>
      </div>

      <div className="panel">
        <h2>Exam preferences</h2>
        <div className="field">
          <label>Test text size</label>
          <div className="chips">
            {d.textSizeOptions.map((t) => (
              <button
                key={t}
                className={`chip${d.selectedTextSize === t ? ' on' : ''}`}
                onClick={() => set('settings.setTextSize', t)}
              >
                {t}
              </button>
            ))}
          </div>
          <div className="hint">{d.textSizeSummary}</div>
        </div>
        <label className="check">
          <input
            type="checkbox"
            checked={d.fullscreenOnStart}
            onChange={(e) => set('settings.setFullscreenOnStart', e.target.checked)}
          />
          Open a test full screen on its own
        </label>
        <div className="hint">{d.fullscreenSummary}</div>
        <div className="hint">{d.modelModeSummary}</div>
      </div>

      <div className="panel">
        <h2>Sound and microphone</h2>
        <p className="hint">
          Pick the speaker for Listening clips and the microphone for Speaking. Test each
          one here before a test.
        </p>
        <div className="grid2">
          <div className="field">
            <label>Speaker</label>
            <select
              value={d.audioOutputDeviceId}
              onChange={(e) => set('settings.setAudioOutput', e.target.value)}
            >
              <option value="">System default</option>
              {devices.speakers.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.label}
                </option>
              ))}
            </select>
          </div>
          <div className="field">
            <label>Microphone</label>
            <select
              value={d.audioInputDeviceId}
              onChange={(e) => set('settings.setAudioInput', e.target.value)}
            >
              <option value="">System default</option>
              {devices.mics.map((m) => (
                <option key={m.id} value={m.id}>
                  {m.label}
                </option>
              ))}
            </select>
          </div>
        </div>
        <div className="actions">
          <button className="btn" onClick={() => refreshDevices(true)}>
            Refresh devices
          </button>
          <button
            className="btn"
            disabled={!!audioBusy}
            onClick={async () => {
              setAudioBusy('speaker')
              setAudioNote('Playing a test tone on the chosen speaker.')
              try {
                await playTestTone(d.audioOutputDeviceId)
                setAudioNote('If you heard two beeps, the speaker works.')
              } catch {
                setAudioNote('Could not play on that speaker. Check the device and try again.')
              } finally {
                setAudioBusy('')
              }
            }}
          >
            {audioBusy === 'speaker' ? 'Playing...' : 'Test speaker'}
          </button>
          <button
            className="btn"
            disabled={!!audioBusy || !mediaSupported()}
            onClick={async () => {
              setAudioBusy('mic')
              setAudioNote('Recording 3 seconds. Speak now.')
              try {
                const blob = await recordAndPlayback(d.audioInputDeviceId, 3)
                setAudioNote(blob
                  ? 'Played back your recording. If you heard it, the microphone works.'
                  : 'Could not record. Check the microphone and the app permission.')
                // Asking once unlocks the device labels for the pickers.
                refreshDevices(false)
              } catch {
                setAudioNote('Could not record. Allow the microphone, or pick another device.')
              } finally {
                setAudioBusy('')
              }
            }}
          >
            {audioBusy === 'mic' ? 'Recording...' : 'Test microphone'}
          </button>
        </div>
        {audioNote && <div className="hint">{audioNote}</div>}
        {!mediaSupported() && (
          <div className="hint">This build cannot reach the microphone or speakers.</div>
        )}
      </div>

      <div className="panel">
        <h2>Updates</h2>
        <div className="hint">{d.appVersionLabel}</div>
        <div className="hint">{d.installKindLabel}</div>
        <div className="hint">{d.updateSourceLabel}</div>
        <label className="check">
          <input
            type="checkbox"
            checked={d.updateCheckOnStartup}
            onChange={(e) => set('settings.setUpdateCheckOnStartup', e.target.checked)}
          />
          Check for updates on startup
        </label>
        <div className="actions">
          <button className="btn" disabled={page.busy} onClick={() => run('settings.checkUpdate')}>
            Check for updates
          </button>
          {d.updateAvailable && (
            <button className="btn" disabled={page.busy} onClick={() => run('settings.downloadUpdate')}>
              Download update
            </button>
          )}
          {d.updateDownloaded && (
            <button className="btn primary" onClick={() => run('settings.applyUpdate')}>
              Open the release page
            </button>
          )}
        </div>
        {d.updateStatus && <div className="hint">{d.updateStatus}</div>}
      </div>

      <div className="panel">
        <h2>Offline models</h2>
        <p className="hint">
          These run in the app, on this machine. Missing files are fine: the parts that
          need them fall back to offline scoring or a typed answer.
        </p>
        {!models ? (
          <div className="loading">Loading models...</div>
        ) : (
          <ul className="model-list">
            {models.map((m) => (
              <li key={m.name}>
                <div className="model-head">
                  <span className="model-name">{m.name}</span>
                  <span className={`tag${m.ready ? '' : ' warn-tag'}`}>{m.ready ? 'Ready' : 'Missing'}</span>
                  <span className="tag">{m.skill}</span>
                </div>
                <div className="hint">{m.purpose}</div>
                <div className="hint">
                  License: {m.license}. Source: {m.source}.
                </div>
              </li>
            ))}
          </ul>
        )}
      </div>

      <div className="panel">
        <h2>About</h2>
        <div className="hint">{d.aboutLine}</div>
        <div className="hint">{d.authorLine}</div>
        <div className="hint">{d.licenseLine}</div>
        <div className="hint">{d.repoLine}</div>
        <div className="hint">Data folder: {d.dataFolder}</div>
        <div className="actions">
          <button className="btn" onClick={() => run('settings.openDataFolder')}>Open data folder</button>
          <button className="btn" onClick={() => run('settings.openProjectPage')}>Open project page</button>
        </div>
      </div>

      {d.hasDebugDetails && (
        <div className="panel">
          <h2>Last connection test</h2>
          <pre className="feedback">{d.debugDetails}</pre>
        </div>
      )}

      <div className="status-line">{d.statusMessage}</div>

      <Confirm
        open={!!confirm}
        title={confirm?.title ?? ''}
        body={confirm?.body ?? ''}
        confirmLabel={confirm?.confirmLabel ?? 'Confirm'}
        onCancel={() => setConfirm(null)}
        onConfirm={() => confirm?.onConfirm?.()}
      />
    </>
  )
}
