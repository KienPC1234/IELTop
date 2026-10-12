import { useEffect, useRef, useState } from 'react'
import {
  Plus, Trash2, Send, Sparkles, BookOpen,
  CheckCircle2, XCircle, RefreshCw, MessageSquare, Wrench,
  HelpCircle, Volume2, PanelLeftClose, PanelLeftOpen, Loader2, Pin, Pencil
} from 'lucide-react'
import { call, onEvent } from '@/bridge'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { ScrollArea } from '@/components/ui/scroll-area'
import { MarkdownRenderer } from '@/components/MarkdownRenderer'

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

const CHAT_TOOLS = [
  { id: 'lookup', label: 'Lookup' },
  { id: 'translate', label: 'Translate' },
  { id: 'summarize', label: 'Summarize' },
  { id: 'flashcards', label: 'Flashcards' },
  { id: 'fix', label: 'Fix grammar' },
  { id: 'paraphrase', label: 'Paraphrase' }
]

function parseExercise(raw: any, messageText?: string) {
  let jsonString = ''
  if (typeof raw === 'object' && raw !== null) {
    jsonString = JSON.stringify(raw)
  } else if (typeof raw === 'string' && raw.trim().length > 0) {
    jsonString = raw.trim()
  } else if (typeof messageText === 'string') {
    const exMatch = messageText.match(/<!-- EXERCISE -->([\s\S]*?)<!-- \/EXERCISE -->/)
    if (exMatch) {
      jsonString = exMatch[1].trim()
    } else {
      const jsonBlock = messageText.match(/```json\s*([\s\S]*?)\s*```/i)
      if (jsonBlock) {
        jsonString = jsonBlock[1].trim()
      }
    }
  }

  if (!jsonString) return null

  let obj: any = null
  try {
    obj = JSON.parse(jsonString)
  } catch {
    const s = jsonString.indexOf('{')
    const e = jsonString.lastIndexOf('}')
    if (s >= 0 && e > s) {
      try {
        obj = JSON.parse(jsonString.substring(s, e + 1))
      } catch {
        return null
      }
    } else {
      return null
    }
  }

  if (!obj || typeof obj !== 'object') return null
  const prompt = obj.prompt || obj.question || obj.stem || obj.text || ''
  if (!prompt || typeof prompt !== 'string' || prompt.trim().length === 0) return null

  let options: { key: string; text: string }[] = []
  if (Array.isArray(obj.options)) {
    options = obj.options.map((opt: any, idx: number) => {
      if (typeof opt === 'string') {
        const m = opt.match(/^([A-Da-d])[\.\)]\s*(.*)$/)
        if (m) return { key: m[1].toUpperCase(), text: m[2] }
        return { key: String.fromCharCode(65 + idx), text: opt }
      }
      return {
        key: String(opt.key || opt.label || String.fromCharCode(65 + idx)).toUpperCase(),
        text: String(opt.text || opt.value || '')
      }
    })
  }

  return {
    ...obj,
    prompt: prompt.trim(),
    kind: (obj.kind || 'single').toLowerCase(),
    passage: obj.passage || obj.contextText || obj.context || '',
    options,
    matchRows: Array.isArray(obj.matchRows) ? obj.matchRows : (Array.isArray(obj.rows) ? obj.rows : []),
    reorderTokens: Array.isArray(obj.reorderTokens) ? obj.reorderTokens : [],
    audioUrl: obj.audioUrl || ''
  }
}

export default function StudyChatTutor({ onError }: { onError?: (msg: string) => void }) {
  const [snapshot, setSnapshot] = useState<any>(null)
  const [curriculum, setCurriculum] = useState<any[]>([])
  const [selectedUnit, setSelectedUnit] = useState('All')
  const [input, setInput] = useState('')
  const [busy, setBusy] = useState(false)
  const [pendingUserText, setPendingUserText] = useState<string | null>(null)
  const sessionQuery = ''
  const [answers, setAnswers] = useState<Record<string, string>>({})
  const [checkResults, setCheckResults] = useState<Record<string, any>>({})
  const [checkingMsg, setCheckingMsg] = useState<Record<string, boolean>>({})
  const [showSessions, setShowSessions] = useState(true)
  const [renamingId, setRenamingId] = useState<number | null>(null)
  const [renameDraft, setRenameDraft] = useState('')

  // Real-time streaming state
  const [isStreaming, setIsStreaming] = useState(false)
  const [streamingText, setStreamingText] = useState('')
  const [streamingTool, setStreamingTool] = useState<string | null>(null)

  const scrollRef = useRef<HTMLDivElement>(null)

  const sid = snapshot?.selectedSessionId ?? 0
  const sessions = snapshot?.sessions ?? []
  const messages = snapshot?.messages ?? []
  const canUseAi = snapshot?.canUseAi ?? true

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
        if (sid === 0 || payload.sessionId === sid || !payload.sessionId) {
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
  }, [messages, streamingText, pendingUserText])

  async function handleSend(textToSend?: string) {
    const text = (textToSend || input).trim()
    if (!text || busy) return

    setInput('')
    setPendingUserText(text)
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
      setPendingUserText(null)
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

  async function handlePinSession(idToPin: number) {
    try {
      const next: any = await call('study.chat.pinSession', { sessionId: idToPin })
      setSnapshot(next)
    } catch (e: any) {
      onError?.(e.message)
    }
  }

  async function handleRenameSession(idToRename: number) {
    const title = renameDraft.trim()
    if (!title) {
      setRenamingId(null)
      return
    }
    try {
      const next: any = await call('study.chat.renameSession', { sessionId: idToRename, title })
      setSnapshot(next)
    } catch (e: any) {
      onError?.(e.message)
    } finally {
      setRenamingId(null)
    }
  }

  async function handleRunTool(tool: string) {
    const text = input.trim()
    if (!text || busy || !canUseAi) return
    setInput('')
    setBusy(true)
    try {
      const unitParam = selectedUnit === 'All' ? '' : selectedUnit
      const next: any = await call('study.chat.runTool', { sessionId: sid, tool, input: text, unit: unitParam })
      setSnapshot(next)
    } catch (e: any) {
      onError?.(e.message)
    } finally {
      setBusy(false)
    }
  }

  async function handleUnitChange(val: string) {
    setSelectedUnit(val)
    const unitParam = val === 'All' ? '' : val
    setBusy(true)
    try {
      const next: any = sid > 0
        ? await call('study.chat.setUnit', { sessionId: sid, unit: unitParam })
        : await call('study.chat.snapshot', { sessionId: 0, unit: unitParam, query: '' })
      setSnapshot(next)
    } catch (e: any) {
      onError?.(e.message)
    } finally {
      setBusy(false)
    }
  }

  async function handleCheckAnswer(msgId: number, questionObj: any) {
    const userAns = answers[msgId]
    if (!userAns) return

    setCheckingMsg((prev) => ({ ...prev, [msgId]: true }))
    try {
      const res: any = await call('study.tutor.checkAnswer', {
        questionJson: typeof questionObj === 'string' ? questionObj : JSON.stringify(questionObj),
        userAnswer: userAns
      })
      setCheckResults((prev) => ({ ...prev, [msgId]: res }))
    } catch (e: any) {
      onError?.(e.message)
    } finally {
      setCheckingMsg((prev) => ({ ...prev, [msgId]: false }))
    }
  }

  return (
    <div className="flex h-[calc(100vh-170px)] min-h-[520px] gap-4">
      {/* Left Sidebar: Sessions and Curriculum Topics */}
      {showSessions && (
        <div className="flex w-64 shrink-0 flex-col gap-3 rounded-lg border bg-card p-3 shadow-xs">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">
              Conversations
            </span>
            <div className="flex items-center gap-1">
              <Button size="icon" variant="ghost" className="h-7 w-7" onClick={handleNewSession} title="New Chat">
                <Plus className="h-4 w-4" />
              </Button>
              <Button size="icon" variant="ghost" className="h-7 w-7" onClick={() => setShowSessions(false)} title="Collapse conversations">
                <PanelLeftClose className="h-3.5 w-3.5" />
              </Button>
            </div>
          </div>

        {/* Filter by Curriculum Unit */}
        <div className="flex flex-col gap-1.5">
          <span className="text-[11px] font-medium text-muted-foreground">Curriculum Unit</span>
          <Select value={selectedUnit} onValueChange={(val) => handleUnitChange(val)}>
            <SelectTrigger className="h-9 min-h-[2.25rem] w-full max-w-full text-xs truncate [&>span]:truncate [&>span]:block [&>span]:w-full">
              <SelectValue placeholder="All Units" />
            </SelectTrigger>
            <SelectContent className="max-h-72 w-[280px]">
              <SelectItem value="All" textValue="All 14 Units (General IELTS)">All 14 Units (General IELTS)</SelectItem>
              {curriculum.map((c) => (
                <SelectItem key={c.slug} value={c.slug} textValue={c.title} className="text-xs">
                  <div className="flex flex-col gap-0.5 text-left py-0.5 max-w-[240px]">
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
                  {renamingId === s.id ? (
                    <Input
                      autoFocus
                      value={renameDraft}
                      onChange={(e) => setRenameDraft(e.target.value)}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter') handleRenameSession(s.id)
                        if (e.key === 'Escape') setRenamingId(null)
                      }}
                      onBlur={() => setRenamingId(null)}
                      onClick={(e) => e.stopPropagation()}
                      className="h-6 text-xs px-1.5 max-w-[150px]"
                    />
                  ) : (
                    <span className="truncate max-w-[150px]">{s.title || 'Study session'}</span>
                  )}
                  <div className="flex items-center gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
                    <Button
                      size="icon"
                      variant="ghost"
                      className="h-5 w-5 p-0 text-current"
                      onClick={(e) => {
                        e.stopPropagation()
                        handlePinSession(s.id)
                      }}
                      title={s.pinned ? 'Unpin conversation' : 'Pin conversation'}
                    >
                      <Pin className={`h-3 w-3 ${s.pinned ? 'fill-current' : ''}`} />
                    </Button>
                    <Button
                      size="icon"
                      variant="ghost"
                      className="h-5 w-5 p-0 text-current"
                      onClick={(e) => {
                        e.stopPropagation()
                        setRenamingId(s.id)
                        setRenameDraft(s.title || '')
                      }}
                      title="Rename conversation"
                    >
                      <Pencil className="h-3 w-3" />
                    </Button>
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
            {EXERCISE_KINDS.map((k) => (
              <Button
                key={k.id}
                variant="outline"
                size="sm"
                className="h-7 text-[10px] px-1.5 justify-start truncate"
                disabled={busy}
                onClick={() => handleGenerateExercise(k.id)}
                title={k.label}
              >
                {k.label}
              </Button>
            ))}
          </div>
        </div>
      </div>
      )}

      {/* Main Chat Area */}
      <div className="flex flex-1 flex-col rounded-lg border bg-card shadow-xs overflow-hidden">
        {/* Top Header */}
        <div className="flex items-center justify-between border-b px-4 py-2.5 bg-muted/20">
          <div className="flex items-center gap-2 min-w-0">
            {!showSessions && (
              <Button
                variant="outline"
                size="sm"
                className="h-7 text-xs gap-1 mr-1"
                onClick={() => setShowSessions(true)}
                title="Show conversations"
              >
                <PanelLeftOpen className="h-3.5 w-3.5 text-primary" />
                <span className="hidden sm:inline">Conversations</span>
              </Button>
            )}
            <MessageSquare className="h-4 w-4 text-primary shrink-0" />
            <span className="text-sm font-semibold text-foreground truncate max-w-[240px]">
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
            const exerciseObj = parseExercise(m.exerciseJson, m.text)
            let displayText = m.text || ''
            if (exerciseObj && !m.exerciseJson) {
              // Clean markdown json fence from displayText if it was parsed from it
              displayText = displayText.replace(/```json[\s\S]*?```/gi, '').trim()
            }

            return (
              <div
                key={m.id}
                className={`flex flex-col gap-1.5 w-full max-w-[94%] sm:max-w-[88%] ${isUser ? 'self-end items-end' : 'self-start items-start'}`}
              >
                {displayText && (
                  <div
                    className={`rounded-lg p-3.5 text-xs leading-relaxed ${
                      isUser
                        ? 'bg-primary text-primary-foreground font-medium'
                        : 'bg-muted/50 border text-foreground'
                    }`}
                  >
                    {isUser ? (
                      <div className="whitespace-pre-wrap">{displayText}</div>
                    ) : (
                      <MarkdownRenderer content={displayText} />
                    )}

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
                )}

                {/* Inline Interactive Exercise Card */}
                {exerciseObj && (
                  <div className="w-full min-w-[340px] rounded-lg border bg-card p-3.5 shadow-xs flex flex-col gap-3">
                    <div className="flex items-center justify-between border-b pb-2">
                      <div className="flex items-center gap-1.5 flex-wrap">
                        <Badge variant="outline" className="text-[10px] font-semibold text-primary uppercase">
                          {exerciseObj.skill || 'IELTS'} : {exerciseObj.kind || 'Practice'}
                        </Badge>
                        {exerciseObj.topic && (
                          <Badge variant="secondary" className="text-[10px] font-normal">
                            {exerciseObj.topic}
                          </Badge>
                        )}
                      </div>
                      <span className="text-[11px] text-muted-foreground font-medium">Interactive Question</span>
                    </div>

                    {exerciseObj.audioUrl && (
                      <div className="rounded bg-muted/30 p-2 border flex flex-col gap-1">
                        <div className="flex items-center gap-1.5 text-[11px] font-medium text-foreground">
                          <Volume2 className="h-3.5 w-3.5 text-primary" />
                          <span>Listening Audio Material</span>
                        </div>
                        <audio controls className="w-full h-8 mt-1" src={exerciseObj.audioUrl} />
                      </div>
                    )}

                    {exerciseObj.passage && (
                      <div className="rounded bg-muted/40 p-2.5 text-[11px] text-muted-foreground italic border leading-relaxed">
                        {exerciseObj.passage}
                      </div>
                    )}

                    <div className="text-xs font-semibold text-foreground leading-relaxed">
                      {exerciseObj.prompt}
                    </div>

                    {/* Multiple Choice (single or multiple) */}
                    {exerciseObj.options && exerciseObj.options.length > 0 && (
                      <div className="flex flex-col gap-1.5 pt-1">
                        {exerciseObj.options.map((opt: any) => {
                          const isMultiple = exerciseObj.kind === 'multiple'
                          const currentSelected = answers[m.id] || ''
                          const isSelected = isMultiple
                            ? currentSelected.split(',').map((x: string) => x.trim()).includes(opt.key)
                            : currentSelected === opt.key

                          return (
                            <button
                              key={opt.key}
                              type="button"
                              onClick={() => {
                                if (isMultiple) {
                                  const set = new Set(currentSelected.split(',').map((x: string) => x.trim()).filter(Boolean))
                                  if (set.has(opt.key)) set.delete(opt.key)
                                  else set.add(opt.key)
                                  const nextVal = Array.from(set).sort().join(', ')
                                  setAnswers((prev) => ({ ...prev, [m.id]: nextVal }))
                                } else {
                                  setAnswers((prev) => ({ ...prev, [m.id]: opt.key }))
                                }
                              }}
                              className={`flex items-center gap-2.5 rounded border p-2 text-left text-xs transition-colors cursor-pointer ${
                                isSelected
                                  ? 'bg-primary/10 border-primary text-primary font-medium shadow-xs'
                                  : 'hover:bg-muted/50 text-foreground'
                              }`}
                            >
                              <span className="font-bold flex h-5 w-5 shrink-0 items-center justify-center rounded bg-muted text-[11px]">
                                {opt.key}
                              </span>
                              <span>{opt.text}</span>
                            </button>
                          )
                        })}
                      </div>
                    )}

                    {/* True / False / Not Given */}
                    {(exerciseObj.kind === 'tfng' || exerciseObj.kind === 'yes_no_ng') && (!exerciseObj.options || exerciseObj.options.length === 0) && (
                      <div className="flex items-center gap-2 pt-1">
                        {(exerciseObj.kind === 'yes_no_ng' ? ['YES', 'NO', 'NOT GIVEN'] : ['TRUE', 'FALSE', 'NOT GIVEN']).map((val) => {
                          const isSelected = (answers[m.id] || '').toUpperCase() === val
                          return (
                            <button
                              key={val}
                              type="button"
                              onClick={() => setAnswers((prev) => ({ ...prev, [m.id]: val }))}
                              className={`flex-1 py-1.5 px-2 rounded border text-xs font-semibold text-center transition-colors cursor-pointer ${
                                isSelected
                                  ? 'bg-primary text-primary-foreground border-primary'
                                  : 'hover:bg-muted/50 text-foreground'
                              }`}
                            >
                              {val}
                            </button>
                          )
                        })}
                      </div>
                    )}

                    {/* Word Reorder Tokens */}
                    {exerciseObj.reorderTokens && exerciseObj.reorderTokens.length > 0 && (
                      <div className="flex flex-col gap-2 pt-1">
                        <span className="text-[11px] text-muted-foreground">Click words in sequence:</span>
                        <div className="flex flex-wrap gap-1.5">
                          {exerciseObj.reorderTokens.map((token: string, tIdx: number) => (
                            <Badge
                              key={tIdx}
                              variant="outline"
                              className="cursor-pointer hover:bg-primary/10 hover:border-primary text-xs py-1 px-2 font-medium"
                              onClick={() => {
                                const cur = answers[m.id] || ''
                                const next = cur ? `${cur} ${token}` : token
                                setAnswers((prev) => ({ ...prev, [m.id]: next }))
                              }}
                            >
                              {token}
                            </Badge>
                          ))}
                        </div>
                        <div className="flex items-center gap-2">
                          <Input
                            value={answers[m.id] || ''}
                            onChange={(e) => setAnswers((prev) => ({ ...prev, [m.id]: e.target.value }))}
                            placeholder="Constructed sentence will appear here..."
                            className="h-8 text-xs flex-1"
                          />
                          <Button
                            type="button"
                            size="sm"
                            variant="ghost"
                            className="h-8 text-xs"
                            onClick={() => setAnswers((prev) => ({ ...prev, [m.id]: '' }))}
                          >
                            Clear
                          </Button>
                        </div>
                      </div>
                    )}

                    {/* Matching Rows */}
                    {exerciseObj.matchRows && exerciseObj.matchRows.length > 0 && (
                      <div className="flex flex-col gap-2 pt-1">
                        <span className="text-[11px] text-muted-foreground">Enter matches (format: Item=Answer):</span>
                        <Input
                          value={answers[m.id] || ''}
                          onChange={(e) => setAnswers((prev) => ({ ...prev, [m.id]: e.target.value }))}
                          placeholder="e.g. 1=B, 2=A, 3=D"
                          className="h-8 text-xs"
                        />
                        <div className="rounded bg-muted/20 border p-2 flex flex-col gap-1 text-[11px]">
                          {exerciseObj.matchRows.map((row: any, rIdx: number) => (
                            <div key={rIdx} className="flex items-center justify-between">
                              <span className="font-medium text-foreground">{row.label || row.item}</span>
                              <span className="text-muted-foreground font-mono">
                                {checkResults[m.id] ? (row.answer || row.match || '') : '[ _____ ]'}
                              </span>
                            </div>
                          ))}
                        </div>
                      </div>
                    )}

                    {/* Text input for gap/completion/short/error_fix */}
                    {(!exerciseObj.options || exerciseObj.options.length === 0) &&
                      exerciseObj.kind !== 'tfng' &&
                      exerciseObj.kind !== 'yes_no_ng' &&
                      (!exerciseObj.reorderTokens || exerciseObj.reorderTokens.length === 0) &&
                      (!exerciseObj.matchRows || exerciseObj.matchRows.length === 0) && (
                        <div className="flex flex-col gap-1.5 pt-1">
                          {exerciseObj.kind === 'error_fix' || exerciseObj.kind === 'rewrite' ? (
                            <Textarea
                              value={answers[m.id] || ''}
                              onChange={(e) => setAnswers((prev) => ({ ...prev, [m.id]: e.target.value }))}
                              placeholder="Write your revised sentence or correction here..."
                              rows={2}
                              className="text-xs resize-none"
                            />
                          ) : (
                            <Input
                              value={answers[m.id] || ''}
                              onChange={(e) => setAnswers((prev) => ({ ...prev, [m.id]: e.target.value }))}
                              placeholder="Type your answer here..."
                              className="h-8 text-xs"
                            />
                          )}
                        </div>
                      )}

                    {/* Check Answer Button & Feedback */}
                    <div className="flex items-center justify-between pt-1 border-t">
                      <Button
                        size="sm"
                        className="h-7 text-xs font-semibold gap-1.5"
                        onClick={() => handleCheckAnswer(m.id, exerciseObj)}
                        disabled={!answers[m.id] || checkingMsg[m.id]}
                      >
                        {checkingMsg[m.id] && <Loader2 className="h-3 w-3 animate-spin" />}
                        {checkingMsg[m.id] ? 'Checking...' : 'Check Answer'}
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
                      <div className="rounded bg-muted/40 p-2.5 text-[11px] text-muted-foreground border flex flex-col gap-2">
                        {checkResults[m.id].modelAnswer && (
                          <div>
                            <span className="font-semibold text-foreground">Model Answer:</span>{' '}
                            <span className="font-mono text-primary font-medium">{checkResults[m.id].modelAnswer}</span>
                          </div>
                        )}
                        <div>
                          <span className="font-semibold text-foreground">Explanation:</span>{' '}
                          <span>{checkResults[m.id].explanation || exerciseObj.explanation}</span>
                        </div>

                        {/* Interactive follow-ups */}
                        <div className="flex flex-wrap items-center gap-2 pt-1.5 border-t border-border/50">
                          {!checkResults[m.id].isCorrect && (
                            <Button
                              type="button"
                              variant="outline"
                              size="sm"
                              className="h-6 text-[10px] gap-1 font-medium"
                              onClick={() =>
                                handleSend(
                                  `Please explain why my answer '${answers[m.id]}' is incorrect for this question, and why the correct answer is '${checkResults[m.id].modelAnswer}'. Prompt: "${exerciseObj.prompt}"`
                                )
                              }
                            >
                              <HelpCircle className="h-3 w-3" /> Explain my error
                            </Button>
                          )}
                          <Button
                            type="button"
                            variant="outline"
                            size="sm"
                            className="h-6 text-[10px] gap-1 font-medium"
                            onClick={() =>
                              handleSend(
                                `Generate another practice exercise testing ${exerciseObj.topic || exerciseObj.skill || 'this concept'}.`
                              )
                            }
                          >
                            <Sparkles className="h-3 w-3 text-primary" /> Another question
                          </Button>
                        </div>
                      </div>
                    )}
                  </div>
                )}

                {/* Interactive suggestion chips under assistant message */}
                {!isUser && !exerciseObj && (
                  <div className="flex flex-wrap items-center gap-1.5 pt-0.5">
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      className="h-6 text-[10px] px-2 text-muted-foreground hover:text-foreground rounded-full border border-border/50 hover:bg-muted"
                      onClick={() => handleSend("Quiz me on this concept with an interactive practice exercise.")}
                    >
                      Practice question
                    </Button>
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      className="h-6 text-[10px] px-2 text-muted-foreground hover:text-foreground rounded-full border border-border/50 hover:bg-muted"
                      onClick={() => handleSend("Provide 3 authentic IELTS examples illustrating this.")}
                    >
                      3 Examples
                    </Button>
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      className="h-6 text-[10px] px-2 text-muted-foreground hover:text-foreground rounded-full border border-border/50 hover:bg-muted"
                      onClick={() => handleSend("How can I upgrade this vocabulary and sentence structure to Band 8+?")}
                    >
                      Band 8 upgrade
                    </Button>
                  </div>
                )}
              </div>
            )
          })}

          {/* Optimistic Pending User Message Bubble */}
          {pendingUserText && (
            <div className="flex flex-col gap-1.5 max-w-[85%] self-end items-end animate-in fade-in-50">
              <div className="rounded-lg p-3.5 text-xs leading-relaxed bg-primary text-primary-foreground font-medium">
                <div className="whitespace-pre-wrap">{pendingUserText}</div>
              </div>
            </div>
          )}

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

                <div>
                  {streamingText ? (
                    <MarkdownRenderer content={streamingText} />
                  ) : (
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
          {canUseAi && (
            <div className="flex flex-wrap items-center gap-1.5">
              <span className="text-[11px] text-muted-foreground inline-flex items-center gap-1">
                <Wrench className="h-3 w-3" />Tools:
              </span>
              {CHAT_TOOLS.map((t) => (
                <Button
                  key={t.id}
                  type="button"
                  variant="ghost"
                  size="sm"
                  className="h-6 text-[10px] px-2 rounded-full border border-border/50"
                  disabled={busy || !input.trim()}
                  onClick={() => handleRunTool(t.id)}
                  title={`${t.label} the text in the input box`}
                >
                  {t.label}
                </Button>
              ))}
            </div>
          )}
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
              placeholder={canUseAi ? "Ask anything or request an exercise (e.g. 'Quiz me on Academic Collocations')..." : "Configure AI in Settings to chat with the IELTS tutor..."}
              disabled={!canUseAi}
              rows={1}
              className="min-h-[40px] max-h-[100px] text-xs resize-none flex-1 font-medium"
            />
            <Button
              size="icon"
              className="h-10 w-10 shrink-0"
              disabled={!canUseAi || !input.trim() || busy}
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
