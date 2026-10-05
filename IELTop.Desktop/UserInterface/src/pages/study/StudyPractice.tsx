import { useEffect, useMemo, useState } from 'react'
import { CheckCircle2, Flag, Sparkles, Pin, PinOff, Trash2, Search } from 'lucide-react'
import { call } from '@/bridge'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { EmptyState } from '@/components/shared'
import { Field } from '@/components/Field'
import { cn } from '@/lib/utils'

/// The practice builder and the saved sets. A set is built by the model from the
/// lesson text, then saved with the student's answers and the verdict, so the
/// detail view can be reopened exactly as it was scored. The list on the right is
/// every saved set, filtered by session or skill, so any set can be found again.
export default function StudyPractice({ units, canUseAi, onError }) {
  const [snapshot, setSnapshot] = useState<any>(null)
  const [busy, setBusy] = useState(false)
  const [unit, setUnit] = useState('')
  const [skill, setSkill] = useState('Grammar')
  const [difficulty, setDifficulty] = useState('Standard')
  const [count, setCount] = useState('6')
  const [answers, setAnswers] = useState<Record<number, string>>({})
  const [match, setMatch] = useState<Record<number, Record<string, string>>>({})
  const [explain, setExplain] = useState<Record<number, string>>({})
  const [sessionQuery, setSessionQuery] = useState('')
  const [renaming, setRenaming] = useState(false)
  const [titleDraft, setTitleDraft] = useState('')

  async function run(method: string, args: any = {}) {
    setBusy(true)
    try {
      const next = await call(method, args)
      if (next) setSnapshot(next)
      return next
    } catch (e: any) {
      onError?.(e.message)
      return null
    } finally {
      setBusy(false)
    }
  }

  useEffect(() => {
    run('study.practice.snapshot', { sessionId: 0, setId: 0, scope: 'All', query: '' })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const open = snapshot?.openSet
  useEffect(() => {
    // A fresh set clears the local answer boxes and seeds the match rows from
    // whatever the student already placed.
    setAnswers({})
    setMatch({})
    setExplain({})
    const seed: Record<number, Record<string, string>> = {}
    for (const q of snapshot?.openSet?.questions ?? []) {
      if (q.kind === 'match') seed[q.id] = parseMatch(q.userAnswer)
    }
    setMatch(seed)
  }, [snapshot?.openSet?.id])

  const sets = snapshot?.sets ?? []
  const sessions = snapshot?.sessions ?? []
  const scope = snapshot?.scope ?? 'All'

  async function build() {
    await run('study.practice.build', {
      sessionId: snapshot?.selectedSessionId ?? 0,
      unit,
      skill,
      difficulty,
      count: Number(count) || 6,
      scope,
    })
  }

  function answerOf(q: any) {
    return answers[q.id] ?? q.userAnswer ?? ''
  }

  async function commitAnswer(q: any, value: string) {
    setAnswers((prev) => ({ ...prev, [q.id]: value }))
    await run('study.practice.answer', { setId: open.id, questionId: q.id, answer: value, scope })
  }

  async function commitMatch(q: any, label: string, value: string) {
    const rows = (snapshot?.openSet?.questions ?? []).find((x: any) => x.id === q.id)?.matchRows ?? []
    const next = { ...(match[q.id] ?? {}), [label]: value }
    setMatch((prev) => ({ ...prev, [q.id]: next }))
    const joined = rows.map((r: any) => `${r.label}=${next[r.label] ?? ''}`).join(';')
    await run('study.practice.answer', { setId: open.id, questionId: q.id, answer: joined, scope })
  }

  return (
    <div className="grid gap-4 lg:grid-cols-[300px_1fr]">
      {/* Builder + sessions */}
      <div className="flex flex-col gap-4">
        <Card>
          <CardHeader><CardTitle className="text-base">Build a set</CardTitle></CardHeader>
          <CardContent className="flex flex-col gap-3">
            <Field label="Unit">
              <Select value={unit || 'all'} onValueChange={(v) => setUnit(v === 'all' ? '' : v)}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">All lessons</SelectItem>
                  {(units ?? []).map((u: any) => (
                    <SelectItem key={u.slug} value={u.slug}>{u.title}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Field>
            <Field label="Skill">
              <Select value={skill} onValueChange={setSkill}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  {(snapshot?.skills ?? []).map((s: string) => (
                    <SelectItem key={s} value={s}>{s}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Field>
            <Field label="Difficulty">
              <Select value={difficulty} onValueChange={setDifficulty}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  {(snapshot?.difficulties ?? []).map((d: string) => (
                    <SelectItem key={d} value={d}>{d}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Field>
            <Field label="Questions">
              <Input type="number" min={1} max={20} value={count} onChange={(e) => setCount(e.target.value)} />
            </Field>
            <Button
              className="gap-1.5"
              disabled={busy || !canUseAi}
              title={canUseAi ? '' : snapshot?.aiHint}
              onClick={build}
            >
              <Sparkles className="h-4 w-4" />
              {busy ? 'Building...' : 'Build practice set'}
            </Button>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex-row items-center justify-between gap-2">
            <CardTitle className="text-base">Sessions</CardTitle>
            <Button
              size="sm" variant="outline" className="h-7 text-xs"
              disabled={busy}
              onClick={() => run('study.practice.newSession', { unit: unit || '', topic: '', scope })}
            >
              New
            </Button>
          </CardHeader>
          <CardContent className="flex flex-col gap-2">
            <Field label="Filter">
              <Select
                value={scope}
                onValueChange={(v) => run('study.practice.snapshot', { sessionId: 0, setId: 0, scope: v, query: sessionQuery })}
              >
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="All">All skills</SelectItem>
                  {(snapshot?.skills ?? []).map((s: string) => (
                    <SelectItem key={s} value={s}>{s}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Field>
            <div className="relative">
              <Search className="pointer-events-none absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground" />
              <input
                className="h-8 w-full rounded-md border border-input bg-transparent pl-8 pr-2 text-sm"
                placeholder="Search sessions"
                value={sessionQuery}
                onChange={(e) => setSessionQuery(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') run('study.practice.snapshot', { sessionId: 0, setId: 0, scope, query: sessionQuery })
                }}
                onBlur={() => run('study.practice.snapshot', { sessionId: 0, setId: 0, scope, query: sessionQuery })}
              />
            </div>

            {sessions.length === 0 ? (
              <p className="text-sm text-muted-foreground">No sessions match.</p>
            ) : (
              <ul className="flex max-h-72 flex-col gap-1 overflow-y-auto pr-1">
                {sessions.map((s: any) => (
                  <li key={s.id}>
                    <div
                      className={cn(
                        'flex items-center gap-1.5 rounded-md border px-2 py-1.5 text-sm',
                        snapshot?.selectedSessionId === s.id && 'border-primary/50 bg-primary/5'
                      )}
                    >
                      <button
                        type="button"
                        className="min-w-0 flex-1 truncate text-left"
                        title={s.title}
                        onClick={() => run('study.practice.snapshot', { sessionId: s.id, setId: 0, scope: 'All', query: sessionQuery })}
                      >
                        {s.pinned && <Pin className="mr-1 inline h-3 w-3" />}
                        {s.title}
                      </button>
                      <button
                        type="button"
                        className="shrink-0 text-muted-foreground hover:text-foreground"
                        title={s.pinned ? 'Unpin' : 'Pin'}
                        onClick={() => run('study.practice.pinSession', { sessionId: s.id, scope })}
                      >
                        {s.pinned ? <PinOff className="h-3.5 w-3.5" /> : <Pin className="h-3.5 w-3.5" />}
                      </button>
                      <button
                        type="button"
                        className="shrink-0 text-destructive"
                        title="Delete session"
                        onClick={() => run('study.practice.deleteSession', { sessionId: s.id, scope })}
                      >
                        <Trash2 className="h-3.5 w-3.5" />
                      </button>
                    </div>
                  </li>
                ))}
              </ul>
            )}

            {renaming ? (
              <div className="flex items-center gap-1.5">
                <input
                  className="h-8 w-full rounded-md border border-input bg-transparent px-2 text-sm"
                  value={titleDraft}
                  onChange={(e) => setTitleDraft(e.target.value)}
                  placeholder="Session name"
                />
                <Button
                  size="sm" className="h-8"
                  onClick={async () => {
                    await run('study.practice.renameSession', { sessionId: snapshot?.selectedSessionId, title: titleDraft, scope })
                    setRenaming(false)
                  }}
                >
                  Save
                </Button>
              </div>
            ) : (
              <Button
                size="sm" variant="outline" className="h-7 text-xs"
                disabled={busy || !snapshot?.selectedSessionId}
                onClick={() => { setTitleDraft(snapshot?.selectedTitle ?? ''); setRenaming(true) }}
              >
                Rename selected
              </Button>
            )}
          </CardContent>
        </Card>
      </div>

      {/* Saved sets + detail */}
      <div className="flex flex-col gap-4">
        <Card>
          <CardHeader className="flex-row items-center justify-between gap-3">
            <CardTitle className="text-base">Saved sets</CardTitle>
            <Badge variant="outline">{sets.length} {scope === 'All' ? 'total' : scope}</Badge>
          </CardHeader>
          <CardContent>
            {sets.length === 0 ? (
              <EmptyState title="No saved sets" body="Build a set on the left, it is saved here for review." />
            ) : (
              <ul className="flex flex-col gap-1.5">
                {sets.map((s: any) => (
                  <li key={s.id}>
                    <button
                      type="button"
                      onClick={() => run('study.practice.openSet', { setId: s.id, scope })}
                      className={cn(
                        'flex w-full flex-wrap items-center justify-between gap-2 rounded-md border px-2.5 py-2 text-left text-sm transition-colors hover:bg-muted/50',
                        open?.id === s.id && 'border-primary/50 bg-primary/5'
                      )}
                    >
                      <span className="min-w-0 flex-1 truncate">{s.title}</span>
                      <Badge variant="secondary" className="text-xs">{s.skill}</Badge>
                      <Badge variant={s.status === 'Submitted' ? 'secondary' : 'outline'} className="text-xs">
                        {s.status === 'Submitted' ? `${s.score}/${s.total}` : `${s.questionCount} q`}
                      </Badge>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </CardContent>
        </Card>

        <Card className="flex flex-col">
          <CardHeader className="flex-row items-center justify-between gap-3">
            <CardTitle className="text-base">{open?.title || 'Practice detail'}</CardTitle>
            {open && (
              <div className="flex items-center gap-2">
                <Badge variant="outline">{open.skill}</Badge>
                {open.status === 'Submitted' && (
                  <Badge variant="secondary">{open.score}/{open.total} correct</Badge>
                )}
                <Button
                  size="sm" className="gap-1"
                  disabled={busy}
                  onClick={() => run('study.practice.submit', { setId: open.id, scope })}
                >
                  <CheckCircle2 className="h-4 w-4" />
                  Score set
                </Button>
              </div>
            )}
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            {!open ? (
              <EmptyState title="No set open" body="Pick a saved set above, or build a new one from a lesson unit." />
            ) : open.questions.length === 0 ? (
              <p className="text-sm text-muted-foreground">This set has no questions.</p>
            ) : (
              open.questions.map((q: any) => (
                <div
                  key={q.id}
                  className={cn(
                    'rounded-lg border p-3.5',
                    open.status === 'Submitted' && q.isCorrect && 'border-success/40 bg-success/5',
                    open.status === 'Submitted' && !q.isCorrect && 'border-destructive/40 bg-destructive/5'
                  )}
                >
                  <div className="flex items-start gap-3">
                    <span className="shrink-0 text-sm font-bold tabular-nums">{q.number}</span>
                    <p className="flex-1 text-sm leading-relaxed">{q.prompt}</p>
                    <Button
                      size="sm" variant="ghost"
                      className={cn('h-7 shrink-0 px-1.5', q.isFlagged && 'text-warning')}
                      onClick={() => run('study.practice.answer', { setId: open.id, questionId: q.id, flag: true, scope })}
                      aria-label="Flag question"
                    >
                      <Flag className={cn('h-4 w-4', q.isFlagged && 'fill-current')} />
                    </Button>
                  </div>

                  <div className="mt-2.5 pl-6">
                    {q.kind === 'single' || q.kind === 'multiple' ? (
                      <div className="flex flex-col gap-2">
                        {q.options.map((o: any) => {
                          const value = answerOf(q)
                          const picked = q.kind === 'multiple'
                            ? value.split(',').map((x: string) => x.trim()).includes(o.key)
                            : value === o.key
                          return (
                            <label key={o.key} className="flex cursor-pointer items-center gap-2.5 text-sm">
                              <input
                                type={q.kind === 'multiple' ? 'checkbox' : 'radio'}
                                name={`q-${q.id}`}
                                checked={picked}
                                onChange={() => {
                                  if (q.kind === 'multiple') {
                                    const set = new Set(value.split(',').map((x: string) => x.trim()).filter(Boolean))
                                    set.has(o.key) ? set.delete(o.key) : set.add(o.key)
                                    commitAnswer(q, [...set].join(','))
                                  } else {
                                    commitAnswer(q, o.key)
                                  }
                                }}
                              />
                              <span>{o.text || o.key}</span>
                            </label>
                          )
                        })}
                      </div>
                    ) : q.kind === 'match' ? (
                      <div className="flex flex-col gap-2">
                        {(q.matchRows ?? []).map((r: any) => (
                          <div key={r.label} className="flex items-center gap-2.5 text-sm">
                            <span className="min-w-[110px] font-medium">{r.label}</span>
                            <Input
                              className="max-w-sm"
                              placeholder="Match with"
                              value={(match[q.id] ?? {})[r.label] ?? ''}
                              onChange={(e) => setMatch((prev) => ({ ...prev, [q.id]: { ...(prev[q.id] ?? {}), [r.label]: e.target.value } }))}
                              onBlur={(e) => commitMatch(q, r.label, e.target.value)}
                            />
                          </div>
                        ))}
                      </div>
                    ) : (
                      <Input
                        className="max-w-md"
                        placeholder="Your answer"
                        value={answerOf(q)}
                        onChange={(e) => setAnswers((prev) => ({ ...prev, [q.id]: e.target.value }))}
                        onBlur={(e) => commitAnswer(q, e.target.value)}
                      />
                    )}

                    {open.status === 'Submitted' && (
                      <div className="mt-2 flex flex-wrap items-center gap-2 text-sm">
                        <span className={q.isCorrect ? 'text-success' : 'text-destructive'}>
                          {q.isCorrect ? 'Correct' : 'Not quite'}
                        </span>
                        {!q.isCorrect && (
                          <span className="text-muted-foreground">
                            Answer: {answerText(q)}
                          </span>
                        )}
                        <Button
                          size="sm" variant="outline" className="h-7 text-xs"
                          disabled={busy || !canUseAi}
                          onClick={async () => {
                            setBusy(true)
                            try {
                              const text = await call('study.practice.explain', { questionId: q.id })
                              setExplain((prev) => ({ ...prev, [q.id]: text }))
                            } catch (e: any) {
                              onError?.(e.message)
                            } finally {
                              setBusy(false)
                            }
                          }}
                        >
                          Explain
                        </Button>
                      </div>
                    )}

                    {q.explanation && (
                      <p className="mt-1.5 text-sm leading-relaxed text-muted-foreground">{q.explanation}</p>
                    )}
                    {explain[q.id] && (
                      <p className="mt-1.5 whitespace-pre-wrap rounded-md border border-border bg-muted/40 p-2.5 text-sm leading-relaxed">
                        {explain[q.id]}
                      </p>
                    )}
                  </div>
                </div>
              ))
            )}
          </CardContent>
        </Card>
      </div>
    </div>
  )
}

/// The correct answer shown after scoring, for whichever kind the question is.
function answerText(q: any) {
  if (q.kind === 'match') {
    return (q.matchRows ?? []).map((r: any) => `${r.label} = ${r.answer}`).join('; ')
  }
  return q.gapAnswer || q.correctKey
}

/// Reads "label=value;label=value" back into a map for the match inputs.
function parseMatch(value: string): Record<string, string> {
  const out: Record<string, string> = {}
  for (const part of (value || '').split(';')) {
    const at = part.indexOf('=')
    if (at <= 0) continue
    out[part.slice(0, at).trim()] = part.slice(at + 1).trim()
  }
  return out
}
