import { useEffect, useRef, useState } from 'react'
import {
  Plus, Trash2, Pin, PinOff, Search, Send, Sparkles, BookOpen,
  CheckCircle2, XCircle, ArrowRight, RefreshCw, MessageSquare, Wrench
} from 'lucide-react'
import { call, onEvent } from '@/bridge'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { ScrollArea } from '@/components/ui/scroll-area'

const EXERCISE_KINDS = [
  { id: 'single', label: 'Multiple Choice' },
  { id: 'gap', label: 'Sentence Gap Fill' },
  { id: 'completion', label: 'Sentence Completion' },
  { id: 'reorder', label: 'Word Reorder' },
  { id: 'identify_error', label: 'Error Identification' },
  { id: 'rewrite', label: 'Band 8 Rewrite' },
  { id: 'match', label: 'Matching Terms' },
  { id: 'short', label: 'Short Answer' }
]

const QUICK_PROMPTS = [
  'Explain Relative Clauses with Band 7+ examples',
  'Teach Academic Vocabulary for Climate Change',
  'Practice Complex Sentences for Writing Task 2',
  'Quiz me on Dependent Prepositions'
]

export default function StudyChatTutor({ onError }: { onError?: (msg: string) => void }) {
  const [snapshot, setSnapshot] = useState<any>(null)
  const [curriculum, setCurriculum] = useState<any[]>([])
  const [selectedUnit, setSelectedUnit] = useState('All')
  const [input, setInput] = useState('')
  const [busy, setBusy] = useState(false)
  const [sessionQuery, setSessionQuery] = useState('')
  const [answers, setAnswers] = useState<Record<string, string>>({})
  const [checkResults, setCheckResults] = useState<Record<string, any>>({})

  // Real-time streaming state
  const [isStreaming, setIsStreaming] = useState(false)
  const [streamingText, setStreamingText] = useState('')
  const [streamingTool, setStreamingTool] = useState<string | null>(null)

  const scrollRef = useRef<HTMLDivElement>(null)

  const sid = snapshot?.selectedSessionId ?? 0
  const sessions = snapshot?.sessions ?? []
  const messages = snapshot?.messages ?? []
  const canUseAi = !!snapshot?.canUseAi

  async function loadData(targetSid = 0) {
    setBusy(true)
    try {
      const unitParam = selectedUnit === 'All' ? '' : selectedUnit
      const snap: any = await call('study.chat.snapshot', { sessionId: targetSid, unit: unitParam, query: sessionQuery })
      setSnapshot(snap)
      const curr: any = await call('study.tutor.curriculum', {})
      if (Array.isArray(curr)) setCurriculum(curr)
    } catch (e: any) {
      onError?.(e.message)
    } finally {
      setBusy(false)
    }
  }

  useEffect(() => {
    loadData()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // Listen to real-time streaming chunks and tool calls from .NET host
  useEffect(() => {
    const unbind = onEvent((evt: any) => {
      const isChunk = evt?.event === 'study.chat.chunk' || evt?.type === 'study.chat.chunk'
      if (isChunk) {
        const payload = evt.payload || evt
        if (payload.sessionId === sid || payload.sessionId === 0) {
          if (payload.toolName) {
            setStreamingTool(payload.toolName)
          }
          if (payload.delta) {
            setStreamingText((prev) => prev + payload.delta)
          }
          if (payload.isDone) {
            setIsStreaming(false)
            setStreamingTool(null)
            setStreamingText('')
            loadData(sid)
          }
        }
      }
    })
    return () => unbind()
  }, [sid])

  useEffect(() => {
    if (scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight
    }
  }, [messages, streamingText])

  async function handleSend(textToSend?: string) {
    const text = (textToSend || input).trim()
    if (!text || busy) return

    setInput('')
    setBusy(true)
    setIsStreaming(true)
    setStreamingText('')
    setStreamingTool(null)

    try {
      const unitParam = selectedUnit === 'All' ? '' : selectedUnit
      const next: any = await call('study.chat.ask', { sessionId: sid, question: text, unit: unitParam })
      setSnapshot(next)
    } catch (e: any) {
      onError?.(e.message)
    } finally {
      setBusy(false)
      setIsStreaming(false)
      setStreamingText('')
      setStreamingTool(null)
    }
  }

  async function handleTeachTopic(topicName?: string) {
    setBusy(true)
    setIsStreaming(true)
    setStreamingText('')
    setStreamingTool(null)

    try {
      const unitParam = selectedUnit === 'All' ? '' : selectedUnit
      const topic = topicName || 'Core Concept'
      const next: any = await call('study.tutor.teachTopic', { sessionId: sid, unit: unitParam, topic })
      setSnapshot(next)
    } catch (e: any) {
      onError?.(e.message)
    } finally {
      setBusy(false)
      setIsStreaming(false)
      setStreamingText('')
      setStreamingTool(null)
    }
  }

  async function handleGenerateExercise(kind: string) {
    setBusy(true)
    try {
      const unitParam = selectedUnit === 'All' ? '' : selectedUnit
      const next: any = await call('study.tutor.generateExercise', {
        sessionId: sid,
        skill: 'Reading',
        topic: 'Curriculum Practice',
        kind,
        unit: unitParam
      })
      setSnapshot(next)
    } catch (e: any) {
      onError?.(e.message)
    } finally {
      setBusy(false)
    }
  }

  async function handleNewSession() {
    try {
      const unitParam = selectedUnit === 'All' ? '' : selectedUnit
      const next: any = await call('study.chat.newSession', { unit: unitParam })
      setSnapshot(next)
    } catch (e: any) {
      onError?.(e.message)
    }
  }

  async function handleDeleteSession(idToDelete: number) {
    try {
      const unitParam = selectedUnit === 'All' ? '' : selectedUnit
      const next: any = await call('study.chat.deleteSession', { sessionId: idToDelete, unit: unitParam })
      setSnapshot(next)
    } catch (e: any) {
      onError?.(e.message)
    }
  }

  async function handleCheckAnswer(msgId: number, questionObj: any) {
    const userAns = answers[msgId]
    if (!userAns) return

    try {
      const res: any = await call('study.tutor.checkAnswer', {
        questionJson: JSON.stringify(questionObj),
        userAnswer: userAns
      })
      setCheckResults((prev) => ({ ...prev, [msgId]: res }))
    } catch (e: any) {
      onError?.(e.message)
    }
  }

  return (
    <div className="flex h-[calc(100vh-170px)] min-h-[520px] gap-4">
      {/* Left Sidebar: Sessions and Curriculum Topics */}
      <div className="flex w-64 shrink-0 flex-col gap-3 rounded-lg border bg-card p-3 shadow-xs">
        <div className="flex items-center justify-between">
          <span className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">
            Conversations
          </span>
          <Button size="icon" variant="ghost" className="h-7 w-7" onClick={handleNewSession} title="New Chat">
            <Plus className="h-4 w-4" />
          </Button>
        </div>

        {/* Filter by Curriculum Unit */}
        <div className="flex flex-col gap-1.5">
          <span className="text-[11px] font-medium text-muted-foreground">Curriculum Unit</span>
          <Select value={selectedUnit} onValueChange={(val) => { setSelectedUnit(val); loadData(sid); }}>
            <SelectTrigger className="h-8 text-xs truncate">
              <SelectValue placeholder="All Units" />
            </SelectTrigger>
            <SelectContent className="max-h-72">
              <SelectItem value="All">All 14 Units (General IELTS)</SelectItem>
              {curriculum.map((c) => (
                <SelectItem key={c.slug} value={c.slug} className="text-xs">
                  <div className="flex flex-col gap-0.5 text-left py-0.5 max-w-[260px]">
                    <span className="font-medium text-foreground truncate">{c.title}</span>
                    {c.mainTheme && (
                      <span className="text-[10px] text-muted-foreground truncate">
                        Topic: {c.mainTheme}
                      </span>
                    )}
                  </div>
                </SelectItem>
              ))}
            </SelectContent>
          </Select>

          {/* Unit Topic Overview Card */}
          {selectedUnit !== 'All' && (() => {
            const activeUnit = curriculum.find((c) => c.slug === selectedUnit)
            if (!activeUnit) return null
            return (
              <div className="rounded-md border bg-muted/30 p-2 flex flex-col gap-1 text-[11px] mt-1">
                <div className="flex items-center justify-between font-semibold text-primary">
                  <span className="truncate">{activeUnit.title.split(':')[0] || 'Unit'}</span>
                  <Badge variant="outline" className="text-[10px] px-1.5 py-0 font-normal">
                    {activeUnit.vocabCount} words
                  </Badge>
                </div>
                {activeUnit.mainTheme && (
                  <div className="text-foreground leading-tight text-[11px]">
                    <span className="text-muted-foreground font-medium">Topic: </span>
                    <span className="font-normal">{activeUnit.mainTheme}</span>
                  </div>
                )}
                {activeUnit.grammarFocus && (
                  <div className="text-foreground leading-tight text-[11px]">
                    <span className="text-muted-foreground font-medium">Grammar: </span>
                    <span className="font-normal">{activeUnit.grammarFocus}</span>
                  </div>
                )}
                <Button
                  variant="outline"
                  size="sm"
                  className="h-6 text-[10px] mt-1 justify-center gap-1"
                  disabled={busy}
                  onClick={() => handleTeachTopic(activeUnit.mainTheme || activeUnit.title)}
                >
                  <Sparkles className="h-3 w-3 text-primary" />
                  Teach Unit Topic
                </Button>
              </div>
            )
          })()}
        </div>

        {/* Sessions List */}
        <ScrollArea className="flex-1">
          <div className="flex flex-col gap-1 pr-2">
            {sessions.map((s: any) => {
              const isSelected = s.id === sid
              return (
                <div
                  key={s.id}
                  onClick={() => loadData(s.id)}
                  className={`group flex items-center justify-between rounded-md px-2.5 py-1.5 text-xs transition-colors cursor-pointer ${
                    isSelected
                      ? 'bg-primary text-primary-foreground font-medium'
                      : 'hover:bg-muted/60 text-muted-foreground hover:text-foreground'
                  }`}
                >
                  <span className="truncate max-w-[170px]">{s.title || 'Study session'}</span>
                  <div className="flex items-center gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
                    <Button
                      size="icon"
                      variant="ghost"
                      className="h-5 w-5 p-0 hover:bg-destructive/20 text-current"
                      onClick={(e) => {
                        e.stopPropagation()
                        handleDeleteSession(s.id)
                      }}
                      title="Delete conversation"
                    >
                      <Trash2 className="h-3 w-3" />
                    </Button>
                  </div>
                </div>
              )
            })}
          </div>
        </ScrollArea>

        {/* Practice Exercises Shortcuts */}
        <div className="border-t pt-2 flex flex-col gap-1.5">
          <span className="text-[11px] font-semibold text-muted-foreground uppercase tracking-wider">
            Quick Exercise
          </span>
          <div className="grid grid-cols-2 gap-1 text-[11px]">
            {EXERCISE_KINDS.slice(0, 4).map((k) => (
              <Button
                key={k.id}
                variant="outline"
                size="sm"
                className="h-7 text-[11px] px-1.5 justify-start truncate"
                disabled={busy}
                onClick={() => handleGenerateExercise(k.id)}
              >
                {k.label}
              </Button>
            ))}
          </div>
        </div>
      </div>

      {/* Main Chat Area */}
      <div className="flex flex-1 flex-col rounded-lg border bg-card shadow-xs overflow-hidden">
        {/* Top Header */}
        <div className="flex items-center justify-between border-b px-4 py-2.5 bg-muted/20">
          <div className="flex items-center gap-2">
            <MessageSquare className="h-4 w-4 text-primary" />
            <span className="text-sm font-semibold text-foreground">
              {snapshot?.sessions?.find((s: any) => s.id === sid)?.title || 'Interactive AI Study Tutor'}
            </span>
          </div>

          <div className="flex items-center gap-2">
            <Select onValueChange={(val) => handleTeachTopic(val)}>
              <SelectTrigger className="h-7 text-xs w-[220px]">
                <SelectValue placeholder="Teach a lesson topic..." />
              </SelectTrigger>
              <SelectContent className="max-h-72">
                {curriculum.map((c) => (
                  <SelectItem key={c.slug} value={c.mainTheme || c.title} className="text-xs">
                    <span className="truncate">{c.title}</span>
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>

            <Button
              variant="outline"
              size="sm"
              className="h-7 text-xs gap-1"
              disabled={busy}
              onClick={() => handleTeachTopic('Key Grammar Points')}
            >
              <Sparkles className="h-3.5 w-3.5 text-primary" />
              Teach Unit
            </Button>
          </div>
        </div>

        {/* Message Stream */}
        <div ref={scrollRef} className="flex-1 overflow-y-auto p-4 flex flex-col gap-4">
          {messages.length === 0 && !isStreaming && (
            <div className="flex flex-col items-center justify-center h-full text-center p-6 gap-3">
              <div className="h-12 w-12 rounded-full bg-primary/10 flex items-center justify-center text-primary">
                <BookOpen className="h-6 w-6" />
              </div>
              <div className="flex flex-col gap-1 max-w-md">
                <h3 className="font-semibold text-sm">Ground-Truth IELTS Curriculum Tutor</h3>
                <p className="text-xs text-muted-foreground">
                  Ask any question on grammar rules, reading passages, academic vocabulary, or IELTS strategies.
                  The tutor searches the complete 14-unit curriculum and generates interactive question drills.
                </p>
              </div>

              <div className="flex flex-wrap items-center justify-center gap-1.5 max-w-lg mt-2">
                {QUICK_PROMPTS.map((qp, idx) => (
                  <Button
                    key={idx}
                    variant="outline"
                    size="sm"
                    className="text-xs h-7 rounded-full"
                    onClick={() => handleSend(qp)}
                  >
                    {qp}
                  </Button>
                ))}
              </div>
            </div>
          )}

          {messages.map((m: any) => {
            const isUser = m.role === 'user'
            const exerciseObj = m.exerciseJson

            return (
              <div
                key={m.id}
                className={`flex flex-col gap-1.5 max-w-[85%] ${isUser ? 'self-end items-end' : 'self-start items-start'}`}
              >
                <div
                  className={`rounded-lg p-3.5 text-xs leading-relaxed ${
                    isUser
                      ? 'bg-primary text-primary-foreground font-medium'
                      : 'bg-muted/50 border text-foreground'
                  }`}
                >
                  <div className="whitespace-pre-wrap">{m.text}</div>

                  {/* Sources citation tags */}
                  {m.sources && m.sources.length > 0 && (
                    <div className="mt-2.5 pt-2 border-t border-border/50 flex flex-wrap gap-1">
                      <span className="text-[10px] text-muted-foreground mr-1">Curriculum sources:</span>
                      {m.sources.map((src: any, sIdx: number) => (
                        <Badge key={sIdx} variant="secondary" className="text-[10px] font-normal py-0">
                          {src.section} ({src.unit})
                        </Badge>
                      ))}
                    </div>
                  )}
                </div>

                {/* Inline Interactive Exercise Card */}
                {exerciseObj && (
                  <div className="w-full min-w-[340px] rounded-lg border bg-card p-3 shadow-xs flex flex-col gap-2.5">
                    <div className="flex items-center justify-between border-b pb-1.5">
                      <Badge variant="outline" className="text-[10px] font-semibold text-primary">
                        {exerciseObj.skill || 'IELTS'} : {exerciseObj.kind || 'Practice'}
                      </Badge>
                      <span className="text-[11px] text-muted-foreground font-medium">Interactive Question</span>
                    </div>

                    <div className="text-xs font-medium text-foreground">{exerciseObj.prompt}</div>

                    {exerciseObj.passage && (
                      <div className="rounded bg-muted/40 p-2 text-[11px] text-muted-foreground italic border">
                        {exerciseObj.passage}
                      </div>
                    )}

                    {/* Single choice options */}
                    {exerciseObj.options && exerciseObj.options.length > 0 && (
                      <div className="flex flex-col gap-1.5 pt-1">
                        {exerciseObj.options.map((opt: any) => {
                          const isSelected = answers[m.id] === opt.key
                          return (
                            <button
                              key={opt.key}
                              type="button"
                              onClick={() => setAnswers((prev) => ({ ...prev, [m.id]: opt.key }))}
                              className={`flex items-center gap-2 rounded border p-2 text-left text-xs transition-colors cursor-pointer ${
                                isSelected
                                  ? 'bg-primary/10 border-primary text-primary font-medium'
                                  : 'hover:bg-muted/50 text-foreground'
                              }`}
                            >
                              <span className="font-bold">{opt.key}.</span>
                              <span>{opt.text}</span>
                            </button>
                          )
                        })}
                      </div>
                    )}

                    {/* Text input for gap/completion/reorder */}
                    {(!exerciseObj.options || exerciseObj.options.length === 0) && (
                      <Input
                        value={answers[m.id] || ''}
                        onChange={(e) => setAnswers((prev) => ({ ...prev, [m.id]: e.target.value }))}
                        placeholder="Type your answer here..."
                        className="h-8 text-xs"
                      />
                    )}

                    {/* Check Answer Button & Feedback */}
                    <div className="flex items-center justify-between pt-1">
                      <Button
                        size="sm"
                        className="h-7 text-xs font-semibold"
                        onClick={() => handleCheckAnswer(m.id, exerciseObj)}
                        disabled={!answers[m.id]}
                      >
                        Check Answer
                      </Button>

                      {checkResults[m.id] && (
                        <div className="flex items-center gap-1.5 text-xs font-semibold">
                          {checkResults[m.id].isCorrect ? (
                            <span className="flex items-center gap-1 text-emerald-600 dark:text-emerald-400">
                              <CheckCircle2 className="h-4 w-4" /> Correct!
                            </span>
                          ) : (
                            <span className="flex items-center gap-1 text-destructive">
                              <XCircle className="h-4 w-4" /> Try again
                            </span>
                          )}
                        </div>
                      )}
                    </div>

                    {checkResults[m.id] && (
                      <div className="rounded bg-muted/40 p-2 text-[11px] text-muted-foreground border">
                        <span className="font-semibold text-foreground">Explanation:</span>{' '}
                        {checkResults[m.id].explanation || exerciseObj.explanation}
                      </div>
                    )}
                  </div>
                )}
              </div>
            )
          })}

          {/* Active Real-Time Streaming Message Bubble */}
          {isStreaming && (
            <div className="flex flex-col gap-1.5 self-start max-w-[85%] animate-in fade-in-50">
              <div className="rounded-lg p-3.5 text-xs leading-relaxed bg-muted/50 border text-foreground shadow-xs flex flex-col gap-2">
                <div className="flex items-center gap-2 text-xs font-semibold text-primary">
                  <RefreshCw className="h-3.5 w-3.5 animate-spin" />
                  <span>IELTS Tutor</span>
                  {streamingTool && (
                    <Badge variant="outline" className="text-[10px] font-mono gap-1 text-primary animate-pulse">
                      <Wrench className="h-3 w-3" />
                      Calling: {streamingTool}
                    </Badge>
                  )}
                </div>

                <div className="whitespace-pre-wrap">
                  {streamingText || (
                    <span className="text-muted-foreground italic text-xs animate-pulse">
                      Synthesizing curriculum concepts...
                    </span>
                  )}
                </div>
              </div>
            </div>
          )}
        </div>

        {/* Input Bar */}
        <div className="border-t p-3 bg-card flex flex-col gap-2">
          <div className="flex items-center gap-2">
            <Textarea
              value={input}
              onChange={(e) => setInput(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && !e.shiftKey) {
                  e.preventDefault()
                  handleSend()
                }
              }}
              placeholder="Ask anything or request an exercise (e.g. 'Quiz me on Academic Collocations')..."
              rows={1}
              className="min-h-[40px] max-h-[100px] text-xs resize-none flex-1 font-medium"
            />
            <Button
              size="icon"
              className="h-10 w-10 shrink-0"
              disabled={!input.trim() || busy}
              onClick={() => handleSend()}
              title="Send message"
            >
              <Send className="h-4 w-4" />
            </Button>
          </div>

          <div className="flex items-center justify-between text-[11px] text-muted-foreground">
            <span>Press Enter to send. Shift + Enter for new line.</span>
            <span>Grounded in 14 curriculum units & 1,754 vocabulary entries.</span>
          </div>
        </div>
      </div>
    </div>
  )
}
