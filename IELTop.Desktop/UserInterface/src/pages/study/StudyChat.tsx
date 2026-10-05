import { useEffect, useRef, useState } from 'react'
import {
  Plus, Send, Trash2, Pin, PinOff, Search,
  BookOpen, Languages, ListChecks, Sparkles, Wrench, Repeat,
} from 'lucide-react'
import { call } from '@/bridge'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import { Textarea } from '@/components/ui/textarea'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { EmptyState } from '@/components/shared'

/// The tutor chat. The host searches the lesson text, sends the best sources
/// with the question, and keeps every message in the session so the whole thread
/// can be reopened later. The tool buttons below the box run a tool (look up,
/// translate, summarize, flashcards, fix, paraphrase) on the typed text. Offline
/// it still answers, telling the student a model is needed for a real reply.
const TOOLS = [
  { key: 'lookup', label: 'Look up', icon: BookOpen },
  { key: 'translate', label: 'Translate', icon: Languages },
  { key: 'summarize', label: 'Summarize', icon: ListChecks },
  { key: 'flashcards', label: 'Flashcards', icon: Sparkles },
  { key: 'fix', label: 'Fix English', icon: Wrench },
  { key: 'paraphrase', label: 'Paraphrase', icon: Repeat },
]

export default function StudyChat({ units, canUseAi, onError }) {
  const [snapshot, setSnapshot] = useState<any>(null)
  const [question, setQuestion] = useState('')
  const [busy, setBusy] = useState(false)
  const [renaming, setRenaming] = useState(false)
  const [titleDraft, setTitleDraft] = useState('')
  const [sessionQuery, setSessionQuery] = useState('')
  const bottomRef = useRef<HTMLDivElement | null>(null)

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
    run('study.chat.snapshot', { sessionId: 0, unit: '', query: '' })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [snapshot?.messages?.length])

  async function ask() {
    const text = question.trim()
    if (!text || busy) return
    setQuestion('')
    await run('study.chat.ask', {
      sessionId: snapshot?.selectedSessionId ?? 0,
      question: text,
      unit: snapshot?.selectedUnit ?? '',
    })
  }

  async function runTool(tool: string) {
    const text = question.trim()
    if (!text || busy) return
    setQuestion('')
    await run('study.chat.runTool', {
      sessionId: snapshot?.selectedSessionId ?? 0,
      tool,
      input: text,
      unit: snapshot?.selectedUnit ?? '',
    })
  }

  function onKeyDown(e) {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      ask()
    }
  }

  const messages = snapshot?.messages ?? []
  const sessions = snapshot?.sessions ?? []

  return (
    <div className="grid gap-4 lg:grid-cols-[280px_1fr]">
      {/* Sessions */}
      <Card className="h-fit">
        <CardContent className="flex flex-col gap-2 p-3">
          <div className="flex items-center justify-between">
            <span className="text-sm font-semibold">Sessions</span>
            <Button
              size="sm" variant="outline" className="h-7 gap-1 text-xs"
              disabled={busy}
              onClick={() => run('study.chat.newSession', { unit: snapshot?.selectedUnit ?? '' })}
            >
              <Plus className="h-3.5 w-3.5" />
              New
            </Button>
          </div>

          <div className="relative">
            <Search className="pointer-events-none absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground" />
            <input
              className="h-8 w-full rounded-md border border-input bg-transparent pl-8 pr-2 text-sm"
              placeholder="Search sessions"
              value={sessionQuery}
              onChange={(e) => setSessionQuery(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') run('study.chat.snapshot', { sessionId: snapshot?.selectedSessionId ?? 0, unit: '', query: sessionQuery })
              }}
              onBlur={() => run('study.chat.snapshot', { sessionId: snapshot?.selectedSessionId ?? 0, unit: '', query: sessionQuery })}
            />
          </div>

          <Select
            value={String(snapshot?.selectedSessionId ?? '')}
            onValueChange={(v) => run('study.chat.selectSession', { sessionId: Number(v), unit: snapshot?.selectedUnit ?? '' })}
          >
            <SelectTrigger><SelectValue placeholder="Pick a session" /></SelectTrigger>
            <SelectContent>
              {sessions.map((s: any) => (
                <SelectItem key={s.id} value={String(s.id)}>
                  {(s.pinned ? '* ' : '') + s.title} ({s.itemCount})
                </SelectItem>
              ))}
            </SelectContent>
          </Select>

          <div className="flex items-center gap-2">
            <Button
              size="sm" variant="outline" className="h-7 gap-1 text-xs"
              disabled={busy}
              title={sessions.find((s: any) => s.id === snapshot?.selectedSessionId)?.pinned ? 'Unpin' : 'Pin'}
              onClick={() => run('study.chat.pinSession', { sessionId: snapshot?.selectedSessionId })}
            >
              {sessions.find((s: any) => s.id === snapshot?.selectedSessionId)?.pinned
                ? <PinOff className="h-3.5 w-3.5" />
                : <Pin className="h-3.5 w-3.5" />}
              Pin
            </Button>
            <Button
              size="sm" variant="outline" className="h-7 gap-1 text-xs text-destructive"
              disabled={busy}
              onClick={() => run('study.chat.deleteSession', { sessionId: snapshot?.selectedSessionId })}
            >
              <Trash2 className="h-3.5 w-3.5" />
              Delete
            </Button>
          </div>

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
                  await run('study.chat.renameSession', { sessionId: snapshot?.selectedSessionId, title: titleDraft })
                  setRenaming(false)
                }}
              >
                Save
              </Button>
            </div>
          ) : (
            <Button
              size="sm" variant="outline" className="h-7 text-xs"
              onClick={() => { setTitleDraft(snapshot?.selectedTitle ?? ''); setRenaming(true) }}
            >
              Rename
            </Button>
          )}

          <div className="mt-1 flex flex-col gap-1.5">
            <span className="text-xs font-semibold text-muted-foreground">Limit to a unit</span>
            <Select
              value={snapshot?.selectedUnit || 'all'}
              onValueChange={(v) =>
                run('study.chat.setUnit', {
                  sessionId: snapshot?.selectedSessionId,
                  unit: v === 'all' ? '' : v,
                })
              }
            >
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All lessons</SelectItem>
                {(units ?? []).map((u: any) => (
                  <SelectItem key={u.slug} value={u.slug}>{u.title}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        </CardContent>
      </Card>

      {/* Thread */}
      <Card className="flex min-h-[540px] flex-col">
        <CardContent className="flex flex-1 flex-col gap-3 p-4">
          <div className="flex-1 space-y-3 overflow-y-auto pr-1">
            {messages.length === 0 ? (
              <EmptyState
                title="Ask about the lesson"
                body="Type a question, or type a word or sentence and use a tool below: look up, translate, summarize, flashcards, fix, paraphrase."
              />
            ) : (
              messages.map((m: any) => (
                <div
                  key={m.id}
                  className={
                    m.role === 'user'
                      ? 'ml-auto max-w-[85%] rounded-lg bg-primary px-3.5 py-2.5 text-sm text-primary-foreground'
                      : 'mr-auto max-w-[90%] rounded-lg border bg-muted/40 px-3.5 py-2.5 text-sm leading-relaxed'
                  }
                >
                  <p className="whitespace-pre-wrap">{m.text}</p>
                  {m.role === 'assistant' && (m.sources ?? []).length > 0 && (
                    <div className="mt-2 flex flex-wrap gap-1.5 border-t border-border/40 pt-2">
                      {(m.sources ?? []).map((s: any, i: number) => (
                        <Badge key={i} variant="secondary" className="text-xs" title={s.snippet}>
                          {s.unit} / {s.section}
                        </Badge>
                      ))}
                    </div>
                  )}
                </div>
              ))
            )}
            <div ref={bottomRef} />
          </div>

          <div className="flex items-end gap-2 border-t border-border pt-3">
            <Textarea
              rows={2}
              className="flex-1 resize-none align-top"
              placeholder="Ask the tutor, or type text to use a tool"
              value={question}
              disabled={busy}
              onChange={(e) => setQuestion(e.target.value)}
              onKeyDown={onKeyDown}
            />
            <Button className="gap-1.5" disabled={busy || !question.trim()} onClick={ask}>
              <Send className="h-4 w-4" />
              {busy ? 'Thinking...' : 'Send'}
            </Button>
          </div>

          <div className="flex flex-wrap gap-1.5">
            {TOOLS.map((t) => {
              const Icon = t.icon
              return (
                <Button
                  key={t.key}
                  size="sm" variant="outline" className="h-7 gap-1 text-xs"
                  disabled={busy || !question.trim() || !canUseAi}
                  title={canUseAi ? `Run ${t.label} on the text above` : 'Add a language model in Settings first'}
                  onClick={() => runTool(t.key)}
                >
                  <Icon className="h-3.5 w-3.5" />
                  {t.label}
                </Button>
              )
            })}
          </div>
        </CardContent>
      </Card>
    </div>
  )
}
