import { useEffect, useState } from 'react'
import { usePage, readTextFile, readImageFiles, readFileBase64, isBinaryDocument } from '../hooks.js'
import { Confirm, ErrorBar, FileImport } from '../components/ui.jsx'

/// Structured paper editor: paper fields, parts, and questions, with live
/// validation, optional AI drafting, import, save, and a test run.
export default function Editor({ onNavigate }) {
  const page = usePage('editor.snapshot')
  const [confirm, setConfirm] = useState(null)

  // The service asks the shell to move when Save and test starts a run.
  const navigate = page.data?.navigateTo
  useEffect(() => {
    if (navigate) onNavigate?.(navigate)
  }, [navigate, onNavigate])

  const d = page.data
  if (page.loading && !d) return <div className="loading">Loading the editor...</div>
  if (page.error && !d) return <div className="error">{page.error}</div>
  if (!d) return null

  const set = (field, value) => page.run('editor.setPaperField', { field, value })
  const part = d.selectedPartIndex >= 0 ? d.parts[d.selectedPartIndex] : null
  const question =
    part && d.selectedQuestionIndex >= 0 ? part.questions[d.selectedQuestionIndex] : null

  const setPart = (field, value) =>
    page.run('editor.setPartField', { index: d.selectedPartIndex, field, value })
  const setQuestion = (field, value) =>
    page.run('editor.setQuestionField', {
      partIndex: d.selectedPartIndex,
      questionIndex: d.selectedQuestionIndex,
      field,
      value,
    })

  async function importIntoPart(files) {
    if (!files.length) return
    const file = files[0]
    try {
      if (isBinaryDocument(file.name)) {
        const { name, base64 } = await readFileBase64(file)
        await page.run('editor.importBinary', { fileName: name, base64 })
      } else {
        const { name, content } = await readTextFile(file)
        await page.run('editor.importContent', { fileName: name, content })
      }
    } catch (e) {
      console.error(e)
    }
  }

  async function readIntoPart(files) {
    const images = await readImageFiles(files)
    if (images.length === 0) {
      console.warn('No supported images were picked.')
      return
    }
    await page.run('editor.readImages', { images })
  }

  return (
    <>
      <ErrorBar message={page.error} onDismiss={() => page.setError('')} />
      <h1 className="page-title">Editor</h1>
      <p className="page-sub">{d.validationSummary}</p>

      <div className="panel">
        <div className="grid2">
          <div className="field">
            <label>Paper title</label>
            <input type="text" value={d.paperTitle} onChange={(e) => set('title', e.target.value)} />
          </div>
          <div className="field">
            <label>Category</label>
            <input type="text" value={d.category} onChange={(e) => set('category', e.target.value)} />
          </div>
          <div className="field">
            <label>Level</label>
            <input type="text" value={d.level} onChange={(e) => set('level', e.target.value)} />
          </div>
          <div className="field">
            <label>Tags (comma separated)</label>
            <input type="text" value={d.tagsText} onChange={(e) => set('tags', e.target.value)} />
          </div>
          <div className="field">
            <label>Source</label>
            <input type="text" value={d.source} onChange={(e) => set('source', e.target.value)} />
          </div>
        </div>
        <div className="actions">
          <button className="btn primary" onClick={() => page.run('editor.save')}>Save</button>
          <button className="btn" onClick={() => page.run('editor.validate')}>Validate</button>
          <button className="btn" onClick={() => page.run('editor.new')}>New paper</button>
          <button className="btn" onClick={() => page.run('editor.duplicate')}>Duplicate</button>
          <button className="btn" onClick={() => page.run('editor.testPaper')}>Save and test</button>
          <button
            className="btn"
            onClick={() =>
              setConfirm({
                title: 'Delete paper',
                body: `Delete ${d.paperTitle} from this computer?`,
                onConfirm: async () => {
                  setConfirm(null)
                  await page.run('editor.delete')
                },
              })
            }
          >
            Delete
          </button>
        </div>
      </div>

      <div className="panel">
        <h2>Parts</h2>
        <div className="row">
          {d.skills.map((s) => (
            <button key={s} className="btn" onClick={() => page.run('editor.addPart', { skill: s })}>
              Add {s}
            </button>
          ))}
        </div>
        <ul className="part-list">
          {d.parts.map((p, i) => (
            <li key={i} className={`part-item${i === d.selectedPartIndex ? ' on' : ''}`}>
              <button className="link" onClick={() => page.run('editor.selectPart', { index: i })}>
                {p.summary}
              </button>
              <button className="btn" onClick={() => page.run('editor.removePart', { index: i })}>
                Remove
              </button>
            </li>
          ))}
        </ul>
      </div>

      {part && (
        <div className="panel">
          <h2>Part fields</h2>
          <div className="grid2">
            <div className="field">
              <label>Id</label>
              <input type="text" value={part.id} onChange={(e) => setPart('id', e.target.value)} />
            </div>
            <div className="field">
              <label>Skill</label>
              <select value={part.skill} onChange={(e) => setPart('skill', e.target.value)}>
                {d.skills.map((s) => (
                  <option key={s} value={s}>{s}</option>
                ))}
              </select>
            </div>
            <div className="field">
              <label>Title</label>
              <input type="text" value={part.title} onChange={(e) => setPart('title', e.target.value)} />
            </div>
            <div className="field">
              <label>Task type</label>
              <input type="text" value={part.taskType} onChange={(e) => setPart('taskType', e.target.value)} />
            </div>
            <div className="field">
              <label>Topic</label>
              <input type="text" value={part.topic} onChange={(e) => setPart('topic', e.target.value)} />
            </div>
            <div className="field">
              <label>Minutes</label>
              <input type="number" value={part.minutes} onChange={(e) => setPart('minutes', e.target.value)} />
            </div>
            <div className="field">
              <label>Audio file name</label>
              <input type="text" value={part.audioFile} onChange={(e) => setPart('audioFile', e.target.value)} />
            </div>
          </div>
          <div className="field">
            <label>Instructions</label>
            <textarea
              className="essay"
              rows={2}
              value={part.instructions}
              onChange={(e) => setPart('instructions', e.target.value)}
            />
          </div>
          <div className="field">
            <label>Material</label>
            <textarea
              className="essay mono"
              rows={8}
              value={part.material}
              onChange={(e) => setPart('material', e.target.value)}
            />
          </div>
          <FileImport
            label="Import text into this part"
            accept=".txt,.md,.json,.csv,.pdf,.docx"
            disabled={page.busy}
            onFiles={importIntoPart}
          />
          <FileImport
            label="Read pictures into this part"
            accept=".png,.jpg,.jpeg"
            disabled={page.busy || !d.canUseVision}
            onFiles={readIntoPart}
          />
          <p className="hint">{d.visionHint}</p>

          <div className="field">
            <label>Or paste into this part</label>
            <textarea
              className="essay"
              rows={4}
              value={d.pasteText}
              onChange={(e) => set('paste', e.target.value)}
            />
          </div>
          <div className="actions">
            <select value={d.pasteSkill} onChange={(e) => set('pasteSkill', e.target.value)}>
              {d.skills.map((s) => (
                <option key={s} value={s}>{s}</option>
              ))}
            </select>
            <button className="btn" onClick={() => page.run('editor.buildPartFromPaste')}>
              Paste into part
            </button>
            <button
              className="btn"
              disabled={!d.canUseAi || page.busy}
              title={d.aiHint}
              onClick={() => page.run('editor.aiDraft')}
            >
              AI draft paper
            </button>
            {page.busy && <button className="btn" onClick={() => page.run('editor.cancelAi')}>Stop</button>}
          </div>
          <p className="hint">{d.aiHint}</p>
        </div>
      )}

      {part && (
        <div className="panel">
          <h2>Questions</h2>
          <button className="btn" onClick={() => page.run('editor.addQuestion', { partIndex: d.selectedPartIndex })}>
            Add question
          </button>
          <ul className="part-list">
            {part.questions.map((q, i) => (
              <li key={i} className={`part-item${i === d.selectedQuestionIndex ? ' on' : ''}`}>
                <button className="link" onClick={() => page.run('editor.selectQuestion', { index: i })}>
                  {q.summary}
                </button>
                <button
                  className="btn"
                  onClick={() =>
                    page.run('editor.removeQuestion', {
                      partIndex: d.selectedPartIndex,
                      questionIndex: i,
                    })
                  }
                >
                  Remove
                </button>
              </li>
            ))}
          </ul>

          {question && (
            <div className="grid2">
              <div className="field">
                <label>Number</label>
                <input type="number" value={question.number} onChange={(e) => setQuestion('number', e.target.value)} />
              </div>
              <div className="field">
                <label>Kind</label>
                <select value={question.kind} onChange={(e) => setQuestion('kind', e.target.value)}>
                  {d.kinds.map((k) => (
                    <option key={k} value={k}>{k}</option>
                  ))}
                </select>
              </div>
              <div className="field span2">
                <label>Prompt</label>
                <textarea
                  className="essay"
                  rows={2}
                  value={question.prompt}
                  onChange={(e) => setQuestion('prompt', e.target.value)}
                />
              </div>
              <div className="field">
                <label>Options (A. text per line)</label>
                <textarea
                  className="essay mono"
                  rows={4}
                  value={question.optionsText}
                  onChange={(e) => setQuestion('options', e.target.value)}
                />
              </div>
              <div className="field">
                <label>Correct key</label>
                <input
                  type="text"
                  value={question.correctKey}
                  onChange={(e) => setQuestion('correctKey', e.target.value)}
                />
              </div>
              <div className="field">
                <label>Gap answer (use | for alternatives)</label>
                <input
                  type="text"
                  value={question.gapAnswer}
                  onChange={(e) => setQuestion('gapAnswer', e.target.value)}
                />
              </div>
              <div className="field">
                <label>Bank (one per line)</label>
                <textarea
                  className="essay mono"
                  rows={3}
                  value={question.bankText}
                  onChange={(e) => setQuestion('bank', e.target.value)}
                />
              </div>
              <div className="field span2">
                <label>Match rows (label =&gt; answer per line)</label>
                <textarea
                  className="essay mono"
                  rows={3}
                  value={question.matchRowsText}
                  onChange={(e) => setQuestion('matchRows', e.target.value)}
                />
              </div>
              <div className="field span2">
                <label>Explanation</label>
                <textarea
                  className="essay"
                  rows={2}
                  value={question.explanation}
                  onChange={(e) => setQuestion('explanation', e.target.value)}
                />
              </div>
            </div>
          )}
        </div>
      )}

      {d.validationIssues.length > 0 && (
        <div className="panel">
          <h2>Problems to fix</h2>
          <ul className="issues">
            {d.validationIssues.map((issue, i) => (
              <li key={i}>{issue}</li>
            ))}
          </ul>
        </div>
      )}

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
