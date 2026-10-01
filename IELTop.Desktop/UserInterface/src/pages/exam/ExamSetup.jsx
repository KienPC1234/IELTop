import { useState } from 'react'
import { call } from '../../bridge.js'

/// The setup screen: pick a paper, tick skills and task types, then start.
export default function ExamSetup({ setup, onApply, onStart }) {
  const [busy, setBusy] = useState(false)

  async function run(method, args) {
    setBusy(true)
    try {
      onApply(await call(method, args))
    } finally {
      setBusy(false)
    }
  }

  if (!setup.hasPapers) {
    return (
      <div className="panel">
        <div className="empty">{setup.materialWarning}</div>
      </div>
    )
  }

  return (
    <>
      <div className="panel">
        <h2>Build your test</h2>
        <div className="field">
          <label>Test paper</label>
          <select
            value={setup.selectedPaperTitle}
            disabled={setup.mixAllPapers}
            onChange={(e) => run('exam.selectPaper', { title: e.target.value })}
          >
            {setup.papers.map((p) => (
              <option key={p.title} value={p.title}>
                {p.title} ({p.category || 'General'}, {p.level || 'any level'})
              </option>
            ))}
          </select>
        </div>

        <label className="check">
          <input
            type="checkbox"
            checked={setup.mixAllPapers}
            onChange={(e) => run('exam.setMixAllPapers', { value: e.target.checked })}
          />
          Mix all papers
        </label>

        <label className="check">
          <input
            type="checkbox"
            checked={setup.shuffleParts}
            onChange={(e) => run('exam.setShuffleParts', { value: e.target.checked })}
          />
          Shuffle the part order
        </label>

        <div className="field">
          <label>Skills</label>
          <div className="chips">
            {setup.skills.map((s) => (
              <button
                key={s.name}
                className={`chip${s.isSelected ? ' on' : ''}${s.partCount === 0 ? ' off' : ''}`}
                disabled={s.partCount === 0 || busy}
                onClick={() => run('exam.toggleSkill', { name: s.name })}
                title={s.partCount === 0 ? 'No parts in this paper' : `${s.partCount} part(s)`}
              >
                {s.name}
                <span className="chip-count">{s.partCount > 0 ? s.partCount : 'none'}</span>
              </button>
            ))}
          </div>
          <div className="hint">{setup.selectedSkillsLabel}</div>
        </div>

        {setup.taskTypes.length > 0 && (
          <div className="field">
            <label>Task types</label>
            <div className="chips">
              {setup.taskTypes.map((t) => (
                <button
                  key={t.name}
                  className={`chip small${t.isSelected ? ' on' : ''}`}
                  onClick={() => run('exam.toggleTaskType', { name: t.name })}
                >
                  {t.name}
                </button>
              ))}
            </div>
            <div className="hint">{setup.selectedTaskTypesLabel}</div>
          </div>
        )}
      </div>

      <div className="panel">
        <h2>How it is marked</h2>
        <div className="field">
          <label>Marking level</label>
          <div className="chips">
            {setup.strictnessOptions.map((s) => (
              <button
                key={s}
                className={`chip${setup.strictness === s ? ' on' : ''}`}
                onClick={() => run('exam.setStrictness', { value: s })}
              >
                {s}
              </button>
            ))}
          </div>
        </div>
        <div className="field">
          <label>Part order</label>
          <div className="chips">
            {setup.buildModes.map((m) => (
              <button
                key={m}
                className={`chip${setup.buildMode === m ? ' on' : ''}`}
                onClick={() => run('exam.setBuildMode', { value: m })}
              >
                {m}
              </button>
            ))}
          </div>
        </div>
        <label className="check">
          <input
            type="checkbox"
            checked={setup.strictMode}
            onChange={(e) => run('exam.setStrictMode', { value: e.target.checked })}
          />
          Strict mode (full screen, counts focus switches)
        </label>
        <div className="hint">{setup.paperCountLabel}</div>
      </div>

      <div className="actions">
        <button className="btn primary" disabled={!setup.canStart || busy} onClick={() => onStart?.()}>
          Start test
        </button>
        {!setup.canStart && (
          <span className="hint">
            Listening cannot run on its own. Tick Reading, Writing, or Speaking too.
          </span>
        )}
      </div>
    </>
  )
}
