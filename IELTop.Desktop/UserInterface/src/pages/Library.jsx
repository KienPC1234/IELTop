import { useEffect, useState } from 'react'
import { usePage, readTextFile, readImageFiles, readFileBase64, isBinaryDocument } from '../hooks.js'
import { Confirm, ErrorBar, FileImport } from '../components/ui.jsx'

/// Paper library: browse and filter, build a basket, import text or JSON,
/// draft questions offline or with AI, and start a custom test.
export default function Library({ onNavigate }) {
  const page = usePage('library.snapshot')
  const [confirm, setConfirm] = useState(null)

  // The service asks the shell to move when an action starts a test.
  useEffect(() => {
    if (page.data?.navigateTo) onNavigate?.(page.data.navigateTo)
  }, [page.data?.navigateTo, onNavigate])

  const d = page.data
  if (page.loading && !d) return <div className="loading">Loading the library...</div>
  if (page.error && !d) return <div className="error">{page.error}</div>
  if (!d) return null

  async function importFiles(files) {
    for (const file of files) {
      try {
        if (isBinaryDocument(file.name)) {
          // pdf and docx go as bytes; the host decodes them to text.
          const { name, base64 } = await readFileBase64(file)
          await page.run('library.importBinary', { fileName: name, base64 })
        } else {
          const { name, content } = await readTextFile(file)
          await page.run('library.importContent', { fileName: name, content })
        }
      } catch (e) {
        // A single unreadable file must not stop the rest.
        console.error(e)
      }
    }
  }

  async function readImages(files) {
    const images = await readImageFiles(files)
    if (images.length === 0) {
      console.warn('No supported images were picked.')
      return
    }
    await page.run('library.readImages', { images })
  }

  return (
    <>
      <ErrorBar message={page.error} onDismiss={() => page.setError('')} />
      <h1 className="page-title">Library</h1>
      <p className="page-sub">{d.librarySummary}</p>

      <div className="panel">
        <div className="row">
          <input
            type="text"
            placeholder="Search title or tag"
            value={d.searchText}
            onChange={(e) => page.run('library.setSearch', { value: e.target.value })}
          />
          <select value={d.selectedCategory} onChange={(e) => page.run('library.setCategory', { value: e.target.value })}>
            {d.categories.map((c) => (
              <option key={c} value={c}>{c}</option>
            ))}
          </select>
          <select value={d.selectedSkill} onChange={(e) => page.run('library.setSkill', { value: e.target.value })}>
            {d.skills.map((s) => (
              <option key={s} value={s}>{s}</option>
            ))}
          </select>
          <button className="btn" onClick={() => page.run('library.clearFilters')}>Clear filters</button>
          <button className="btn" onClick={() => page.run('library.reload')}>Reload</button>
        </div>
      </div>

      <div className="panel">
        {d.rows.length === 0 ? (
          <div className="empty">No papers match. Clear the filters or import a file below.</div>
        ) : (
          <ul className="paper-list">
            {d.rows.map((row) => (
              <li key={row.title} className="paper-item">
                <div className="paper-main">
                  <div className="paper-title">
                    {row.title}
                    <span className="tag">{row.isUserPaper ? 'Yours' : 'Built in'}</span>
                  </div>
                  <div className="hint">{row.detail}</div>
                </div>
                <div className="paper-actions">
                  <button className="btn primary" onClick={() => page.run('library.startPaper', { title: row.title })}>
                    Use
                  </button>
                  <button className="btn" onClick={() => page.run('library.addToBasket', { title: row.title })}>
                    Add to basket
                  </button>
                  <button className="btn" onClick={() => page.run('library.duplicatePaper', { title: row.title })}>
                    Duplicate
                  </button>
                  <button className="btn" onClick={() => page.run('library.editPaper', { title: row.title })}>
                    Edit
                  </button>
                  <button
                    className="btn"
                    disabled={!d.canUseAi || page.busy}
                    title={d.aiHint}
                    onClick={() => page.run('library.aiCheckPaper', { title: row.title })}
                  >
                    AI check
                  </button>
                  {row.isUserPaper && (
                    <button
                      className="btn"
                      onClick={() =>
                        setConfirm({
                          title: 'Delete paper',
                          body: `Delete ${row.title} from this computer?`,
                          onConfirm: async () => {
                            setConfirm(null)
                            await page.run('library.deletePaper', { title: row.title })
                          },
                        })
                      }
                    >
                      Delete
                    </button>
                  )}
                </div>
              </li>
            ))}
          </ul>
        )}
      </div>

      <div className="panel">
        <h2>Custom test basket</h2>
        <div className="hint">{d.basketLabel}</div>
        {d.basket.length > 0 && (
          <ul className="basket">
            {d.basket.map((b) => (
              <li key={b.paperTitle}>
                <span>{b.paperTitle}</span>
                <span className="hint">{b.detail}</span>
                <button className="btn" onClick={() => page.run('library.removeFromBasket', { title: b.paperTitle })}>
                  Remove
                </button>
              </li>
            ))}
          </ul>
        )}
        <div className="actions">
          <button className="btn primary" disabled={!d.hasBasket} onClick={() => page.run('library.startBasket')}>
            Start custom test
          </button>
          <button className="btn" disabled={!d.hasBasket} onClick={() => page.run('library.clearBasket')}>
            Clear basket
          </button>
        </div>
      </div>

      <div className="panel">
        <h2>Import and draft</h2>
        <FileImport
          label="Import files"
          accept=".txt,.md,.json,.csv,.pdf,.docx"
          disabled={page.busy}
          onFiles={importFiles}
        />
        <FileImport
          label="Read pictures"
          accept=".png,.jpg,.jpeg"
          disabled={page.busy || !d.canUseVision}
          onFiles={readImages}
        />
        <p className="hint">
          Supported: txt, md, json, csv, pdf, docx. Text files save as offline drafts you can
          edit. No model is needed to import.
        </p>
        <p className="hint">{d.visionHint}</p>

        <div className="field">
          <label>Paste text</label>
          <textarea
            className="essay"
            rows={6}
            value={d.pasteText}
            placeholder="Paste a passage or task here, then build a draft."
            onChange={(e) => page.run('library.setPasteText', { value: e.target.value })}
          />
        </div>
        <div className="row">
          <input
            type="text"
            placeholder="Draft title (optional)"
            value={d.draftTitle}
            onChange={(e) => page.run('library.setDraftTitle', { value: e.target.value })}
          />
          <select value={d.draftSkill} onChange={(e) => page.run('library.setDraftSkill', { value: e.target.value })}>
            {d.draftSkills.map((s) => (
              <option key={s} value={s}>{s}</option>
            ))}
          </select>
          <button className="btn" disabled={page.busy} onClick={() => page.run('library.buildDraftFromPaste')}>
            Build draft
          </button>
          <button
            className="btn"
            disabled={!d.canUseAi || page.busy}
            title={d.aiHint}
            onClick={() => page.run('library.aiFormatPaste')}
          >
            AI draft questions
          </button>
          <button className="btn" onClick={() => page.run('library.newManualTemplate')}>Manual template</button>
          {page.busy && <button className="btn" onClick={() => page.run('library.cancelAi')}>Stop</button>}
        </div>
        <p className="hint">{d.aiHint}</p>

        <div className="field">
          <label>JSON draft</label>
          <textarea
            className="essay mono"
            rows={6}
            value={d.draftJson}
            placeholder="Make a template or import JSON, then save it here."
            onChange={(e) => page.run('library.setDraftJson', { value: e.target.value })}
          />
        </div>
        <div className="actions">
          <button className="btn primary" onClick={() => page.run('library.saveDraftJson')}>Save draft</button>
          <button className="btn" onClick={() => page.run('library.exportShown')}>Export shown</button>
          <button className="btn" onClick={() => page.run('library.openExportFolder')}>Open export folder</button>
        </div>
      </div>

      <div className="status-line">{d.statusMessage}</div>

      <Confirm
        open={!!confirm}
        title={confirm?.title ?? ''}
        body={confirm?.body ?? ''}
        onCancel={() => setConfirm(null)}
        onConfirm={() => confirm?.onConfirm?.()}
      />
    </>
  )
}
