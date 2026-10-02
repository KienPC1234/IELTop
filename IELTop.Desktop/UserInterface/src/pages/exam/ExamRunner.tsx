import { useEffect, useRef, useState } from 'react'
import {
  ArrowLeftRight,
  Maximize2,
  Menu as MenuIcon,
  Moon,
  Pencil,
  Sun,
  Volume2,
  VolumeX,
} from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Slider } from '@/components/ui/slider'
import { Badge } from '@/components/ui/badge'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { Separator } from '@/components/ui/separator'
import { cn } from '@/lib/utils'
import { call, closeExamWindow } from '@/bridge'
import { useDraftText } from '@/hooks'
import { ErrorBar } from '@/components/shared'
import QuestionBlock from './QuestionBlock'
import ExamBottomBar from './ExamBottomBar'
import WritingPanel from './WritingPanel'
import SpeakingPanel from './SpeakingPanel'
import SubmitConfirm from './SubmitConfirm'

export default function ExamRunner({ exam, onApply, fontScale = 1, examDark = false, onToggleExamDark }) {
  const run = exam?.run ?? {}
  const part = run?.currentPart
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [confirmSubmit, setConfirmSubmit] = useState(false)
  const [muted, setMuted] = useState(false)
  const [lastVolume, setLastVolume] = useState(80)
  const [vol, setVol] = useState(run.volume ?? 80)
  useEffect(() => { setVol(run.volume ?? 80) }, [run.volume])

  async function runCall(method: string, args: any = {}) {
    setBusy(true)
    setError('')
    try {
      onApply(await call(method, args))
    } catch (e) {
      setError(e.message)
      call('exam.snapshot').then(onApply).catch(() => {})
    } finally {
      setBusy(false)
    }
  }

  function toggleFullscreen() {
    call('window.toggleFullscreen').catch(() => {})
  }

  function toggleMute() {
    const next = !muted
    setMuted(next)
    if (next) {
      setLastVolume(vol)
      runCall('exam.setVolume', { value: 0 })
    } else {
      runCall('exam.setVolume', { value: lastVolume || 80 })
    }
  }

  function openNotes() {
    const selected = window.getSelection ? String(window.getSelection()) : ''
    runCall('exam.openNotes', { selectedText: selected })
  }

  async function leaveTest() {
    await runCall('exam.cancel')
    closeExamWindow()
  }

  if (!part) return null

  const isIntro = run.isPartIntro || run.phase === 'PartIntro' || run.phase === 1
  const showAudioControls = part.isListening || part.hasAudio

  // Dedicated Section Introduction Screen - appears ONCE when entering each new skill
  if (isIntro) {
    return (
      <div
        className={cn(
          'exam-idp flex h-screen w-full flex-col overflow-hidden bg-card text-foreground',
          part.contrastOn && 'exam-contrast'
        )}
        style={{ '--exam-scale': fontScale } as React.CSSProperties}
      >
        <ErrorBar message={error} onDismiss={() => setError('')} />

        <div className="flex h-14 shrink-0 items-center justify-between border-b border-border bg-card px-6">
          <span className="truncate text-base font-bold text-foreground">
            {run.title || part.paperName || 'IELTop Test'}
          </span>
          <Button variant="outline" size="sm" className="h-8 text-xs" onClick={leaveTest}>
            Leave test
          </Button>
        </div>

        <div className="flex flex-1 min-h-0 items-center justify-center overflow-y-auto p-6">
          <IntroCard exam={exam} part={part} busy={busy} runCall={runCall} onLeave={leaveTest} />
        </div>
      </div>
    )
  }

  // Active Running Exam Screen
  return (
    <div
      className={cn(
        'exam-idp flex h-screen w-full flex-col overflow-hidden bg-card text-foreground',
        part.contrastOn && 'exam-contrast'
      )}
      style={{ '--exam-scale': fontScale } as React.CSSProperties}
    >
      <ErrorBar message={error} onDismiss={() => setError('')} />

      {/* Top Navigation Bar */}
      <div className="flex h-14 shrink-0 items-center justify-between border-b border-border bg-card px-6">
        {/* Left: Test paper title */}
        <div className="flex min-w-0 items-center">
          <span className="truncate text-sm sm:text-base font-bold text-foreground">
            {run.title || part.paperName || 'IELTop Test'}
          </span>
        </div>

        {/* Center: Countdown timer */}
        <div className="shrink-0 px-2 text-center">
          {part.isTimerHidden ? (
            <button
              type="button"
              className="text-sm font-medium text-muted-foreground underline underline-offset-4 hover:text-foreground"
              onClick={() => runCall('exam.toggleTimer')}
            >
              Show time
            </button>
          ) : (
            <button
              type="button"
              title="Click to hide or show clock"
              className={cn(
                'text-sm font-semibold tabular-nums transition-colors hover:opacity-80',
                part.isCriticalTime
                  ? 'text-destructive font-bold'
                  : part.isLowTime
                    ? 'text-amber-600 dark:text-amber-400 font-bold'
                    : 'text-foreground'
              )}
              onClick={() => runCall('exam.toggleTimer')}
            >
              {part.remainingMinutesLabel}
            </button>
          )}
        </div>

        {/* Right: Controls */}
        <div className="flex shrink-0 items-center gap-2">
          {showAudioControls && (
            <div className="hidden sm:flex items-center gap-2 mr-1">
              <button
                type="button"
                onClick={toggleMute}
                className="text-muted-foreground hover:text-foreground transition-colors p-1"
                aria-label="Mute or unmute audio"
              >
                {muted || vol === 0 ? (
                  <VolumeX className="h-4 w-4" />
                ) : (
                  <Volume2 className="h-4 w-4" />
                )}
              </button>
              <div className="w-24">
                <Slider
                  min={0}
                  max={100}
                  value={[muted ? 0 : vol]}
                  onValueChange={([v]) => {
                    setVol(v)
                    if (v > 0) setMuted(false)
                  }}
                  onValueCommit={([v]) => runCall('exam.setVolume', { value: v })}
                  aria-label="Audio volume"
                />
              </div>
            </div>
          )}

          <Tooltip>
            <TooltipTrigger asChild>
              <Button
                variant="outline"
                size="icon"
                className="h-9 w-9 rounded-sm"
                onClick={toggleFullscreen}
                aria-label="Full screen"
              >
                <Maximize2 className="h-4 w-4" />
              </Button>
            </TooltipTrigger>
            <TooltipContent>Full screen</TooltipContent>
          </Tooltip>

          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button
                variant="outline"
                size="icon"
                className="h-9 w-9 rounded-sm"
                aria-label="Menu"
              >
                <MenuIcon className="h-4 w-4" />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" className="w-60">
              <DropdownMenuLabel>Volume</DropdownMenuLabel>
              <div className="px-3 py-2">
                <Slider
                  min={0}
                  max={100}
                  value={[vol]}
                  onValueChange={([v]) => { setVol(v); if (v > 0) setMuted(false) }}
                  onValueCommit={([v]) => runCall('exam.setVolume', { value: v })}
                  aria-label="Volume"
                />
              </div>
              <DropdownMenuSeparator />
              <DropdownMenuLabel>Display</DropdownMenuLabel>
              <DropdownMenuItem onSelect={() => runCall('exam.smallerText')}>Smaller text</DropdownMenuItem>
              <DropdownMenuItem onSelect={() => runCall('exam.biggerText')}>Larger text</DropdownMenuItem>
              <DropdownMenuItem onSelect={() => runCall('exam.toggleContrast')}>
                {part.contrastOn ? 'Normal contrast' : 'High contrast'}
              </DropdownMenuItem>
              <DropdownMenuItem onSelect={onToggleExamDark}>
                {examDark ? 'Light theme' : 'Dark theme'}
              </DropdownMenuItem>
              <DropdownMenuSeparator />
              <DropdownMenuLabel>Test Mode</DropdownMenuLabel>
              <DropdownMenuItem onSelect={() => runCall('exam.toggleStrict')}>
                {run.strictMode ? `Strict mode: on (${run.strictLevelLabel})` : 'Strict mode: off'}
              </DropdownMenuItem>
              <DropdownMenuItem onSelect={() => runCall('exam.toggleTimer')}>
                {part.isTimerHidden ? 'Show clock' : 'Hide clock'}
              </DropdownMenuItem>
              <DropdownMenuSeparator />
              <DropdownMenuItem variant="destructive" onSelect={leaveTest}>Leave the test</DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>

          <Button
            variant="outline"
            className="h-9 px-4 rounded-sm text-sm font-medium"
            disabled={busy}
            onClick={() => setConfirmSubmit(true)}
          >
            Submit
          </Button>

          <Tooltip>
            <TooltipTrigger asChild>
              <Button
                variant="outline"
                size="icon"
                className="h-9 w-9 rounded-sm"
                onClick={openNotes}
                aria-label="Notes"
              >
                <Pencil className="h-4 w-4" />
              </Button>
            </TooltipTrigger>
            <TooltipContent>Notes</TooltipContent>
          </Tooltip>
        </div>
      </div>

      {/* Instruction Banner Box - Edge to edge matching IDP IELTS on Computer */}
      <div className="w-full shrink-0 border-b border-border bg-[#eceff1] dark:bg-muted/40 px-6 py-2.5">
        <p className="text-sm sm:text-base font-bold text-foreground">
          {part.instructionHeading || `Part ${part.skillPartNumber || part.index + 1}`}
        </p>
        <p className="mt-0.5 text-xs sm:text-sm text-foreground/80">
          {part.bannerInstruction || (
            part.isReading
              ? 'Read the text and answer questions 1-40.'
              : part.isListening
                ? 'Listen and answer questions 1-40.'
                : part.instructions
          )}
        </p>
      </div>

      {/* Strict mode warning indicator if active */}
      {run.strictMode && (
        <div className="w-full shrink-0 flex flex-wrap items-center gap-3 border-b border-amber-500/30 bg-amber-500/10 px-6 py-2 text-amber-900 dark:text-amber-200">
          <p className="text-xs sm:text-sm leading-relaxed">
            {run.hasViolations ? run.violationLabel : `Strict mode is active (${run.strictLevelLabel}).`}
          </p>
          <Button size="sm" variant="outline" className="h-6 text-xs px-2" onClick={() => call('exam.focusWindow').catch(() => {})}>
            Focus test window
          </Button>
        </div>
      )}

      {/* Main Content Area - Full height independent scrolling */}
      <div className="min-h-0 flex-1 h-full overflow-hidden">
        <PartBody part={part} run={run} busy={busy} runCall={runCall} />
      </div>

      {/* Notes side drawer */}
      {part.notesOpen && <NotesPanel part={part} runCall={runCall} />}

      {/* Bottom Navigation Strip */}
      <ExamBottomBar
        exam={exam}
        busy={busy}
        runCall={runCall}
        onConfirmSubmit={() => setConfirmSubmit(true)}
      />

      <SubmitConfirm
        open={confirmSubmit}
        unanswered={unansweredCount(run)}
        onCancel={() => setConfirmSubmit(false)}
        onConfirm={async () => {
          setConfirmSubmit(false)
          await runCall('exam.submit')
        }}
      />
    </div>
  )
}

function IntroCard({ exam, part, busy, runCall, onLeave }) {
  const run = exam.run
  const skillName = run.introSkill || part?.skill || 'Exam'

  return (
    <div className="w-[580px] max-w-full rounded border border-border bg-card p-8 text-center shadow-sm">
      <Badge variant="secondary" className="bg-primary/10 text-primary text-sm px-3 py-1">
        {skillName} Section
      </Badge>
      <h2 className="mt-3 text-2xl font-bold tracking-tight text-foreground">
        {run.introTitle || `IELTS ${skillName}`}
      </h2>
      <p className="mt-2 text-sm leading-relaxed text-muted-foreground">
        {run.introDetail || `${part?.minutes || 30} minutes`}
      </p>
      <div className="my-5 rounded border border-border/40 bg-muted/40 p-4 text-left">
        <p className="text-xs font-semibold uppercase tracking-wider text-muted-foreground mb-1">
          Instructions
        </p>
        <p className="text-sm leading-relaxed text-foreground/90">
          {run.introHint || (
            skillName === 'Listening'
              ? 'The audio clip plays once. You will have time to read the questions before listening.'
              : skillName === 'Reading'
                ? 'Read the texts and answer the questions. You can navigate freely between passages in this section.'
                : skillName === 'Writing'
                  ? 'Write your responses for each task in the answer areas provided.'
                  : 'Answer each speaking prompt clearly and fluently.'
          )}
        </p>
      </div>
      <div className="mt-6 flex flex-wrap items-center justify-center gap-3">
        <Button
          size="lg"
          className="min-w-44 text-sm font-semibold"
          disabled={busy}
          onClick={() => runCall('exam.startPart')}
        >
          {busy ? 'Starting...' : `Start ${skillName} section`}
        </Button>
        <Button variant="outline" size="lg" disabled={busy} onClick={onLeave}>
          Leave test
        </Button>
      </div>
      <p className="mt-3 text-xs leading-relaxed text-muted-foreground">
        The timer starts when you press Start {skillName} section.
      </p>
    </div>
  )
}

function PartBody({ part, run, busy, runCall }) {
  if (part.isSpeaking) {
    return (
      <div className="h-full min-h-0 overflow-y-auto px-6 py-4">
        <SpeakingPanel part={part} run={run} busy={busy} runCall={runCall} />
      </div>
    )
  }
  if (part.isWriting) {
    return (
      <div className="h-full min-h-0 overflow-y-auto px-6 py-4">
        <WritingPanel part={part} busy={busy} runCall={runCall} />
      </div>
    )
  }
  if (part.isReading && part.hasMaterial) {
    return (
      <SplitPanes
        resetKey={part.index}
        left={<Passage part={part} busy={busy} runCall={runCall} />}
        right={<Questions part={part} run={run} busy={busy} runCall={runCall} />}
      />
    )
  }
  return (
    <div className="h-full min-h-0 overflow-y-auto px-6 py-4">
      <div className="mx-auto flex max-w-4xl flex-col gap-4">
        <Questions part={part} run={run} busy={busy} runCall={runCall} />
      </div>
    </div>
  )
}

function SplitPanes({ left, right, resetKey }) {
  const [split, setSplit] = useState(50)
  const [dragging, setDragging] = useState(false)
  const trackRef = useRef(null)

  useEffect(() => { setSplit(50) }, [resetKey])

  function clamp(pct) {
    return Math.min(72, Math.max(28, pct))
  }

  function pctFromClientX(clientX) {
    const rect = trackRef.current?.getBoundingClientRect()
    if (!rect || rect.width === 0) return split
    return clamp(((clientX - rect.left) / rect.width) * 100)
  }

  function onGripDown(e) {
    if (e.button !== 0) return
    e.preventDefault()
    setDragging(true)
    function move(ev) {
      setSplit(pctFromClientX(ev.clientX))
    }
    function up() {
      setDragging(false)
      window.removeEventListener('mousemove', move)
      window.removeEventListener('mouseup', up)
    }
    window.addEventListener('mousemove', move)
    window.addEventListener('mouseup', up)
  }

  return (
    <div ref={trackRef} className="reading-split grid h-full min-h-0 grid-cols-1 md:grid-cols-[1fr_auto_1fr]" style={{ '--split': split } as React.CSSProperties}>
      <div className="reading-pane h-full min-h-0 overflow-y-auto px-6 py-4 md:border-r md:border-border">{left}</div>
      <button
        type="button"
        className={`reading-split-handle hidden w-3 cursor-col-resize items-center justify-center border-x border-border bg-muted/20 text-muted-foreground transition-colors hover:bg-muted md:flex ${dragging ? 'bg-muted text-foreground' : ''}`}
        onMouseDown={onGripDown}
        role="separator"
        aria-orientation="vertical"
        aria-valuenow={Math.round(split)}
        aria-valuemin={28}
        aria-valuemax={72}
        aria-label="Resize panels"
      >
        <div className="rounded border border-border bg-card p-0.5 shadow-xs">
          <ArrowLeftRight className="h-3 w-3 text-muted-foreground" />
        </div>
      </button>
      <div className="reading-pane h-full min-h-0 overflow-y-auto px-6 py-4">{right}</div>
    </div>
  )
}

function formatPassageTitle(title) {
  if (!title) return ''
  return title.replace(/^(reading\s+passage\s+\d+|passage\s+\d+|reading\s+part\s+\d+|part\s+\d+)\s*[:\-]\s*/i, '').trim()
}

function Passage({ part, busy, runCall }) {
  function highlightSelection() {
    const selected = window.getSelection ? String(window.getSelection()).trim() : ''
    if (selected) runCall('exam.addHighlight', { text: selected })
  }

  const cleanTitle = formatPassageTitle(part.title)

  return (
    <>
      <div className="sticky top-0 z-10 mb-3 flex flex-wrap items-center gap-2 border-b border-border/30 bg-card py-1.5">
        <Button size="sm" variant="outline" className="h-7 text-xs" disabled={busy} onClick={highlightSelection}>
          Highlight
        </Button>
        <Button
          size="sm"
          variant="outline"
          className="h-7 text-xs"
          disabled={busy || (part.highlights ?? []).length === 0}
          onClick={() => runCall('exam.clearHighlights')}
        >
          Clear highlights
        </Button>
        <span className="ml-1 text-xs text-muted-foreground">Select text, then press Highlight.</span>
      </div>

      {cleanTitle && (
        <h2 className="mb-2 mt-4 text-center text-xl font-bold tracking-tight text-foreground">
          {cleanTitle}
        </h2>
      )}

      {part.topic && (
        <p className="mb-6 text-center text-sm italic text-muted-foreground">
          {part.topic}
        </p>
      )}

      <article className="passage-text">
        {highlightParagraphs(part.material ?? '', part.highlights ?? []).map((chunk, i) => (
          <p key={i} className="mb-4 text-sm leading-relaxed text-[length:calc(15px*var(--exam-scale,1))]">
            {chunk.map((piece, j) =>
              piece.mark ? (
                <mark key={j} className="bg-amber-200 px-0.5 dark:bg-amber-700 dark:text-white">
                  {piece.text}
                </mark>
              ) : (
                <span key={j}>{piece.text}</span>
              )
            )}
          </p>
        ))}
      </article>
    </>
  )
}

function highlightParagraphs(text, highlights) {
  const phrases = (highlights || []).filter((h) => h && h.length > 1)
  return text.split(/\n{2,}/).map((para) => {
    let pieces = [{ text: para, mark: false }]
    for (const phrase of phrases) {
      const next = []
      for (const piece of pieces) {
        if (piece.mark || !piece.text.includes(phrase)) {
          next.push(piece)
          continue
        }
        const parts = piece.text.split(phrase)
        parts.forEach((chunk, i) => {
          if (chunk) next.push({ text: chunk, mark: false })
          if (i < parts.length - 1) next.push({ text: phrase, mark: true })
        })
      }
      pieces = next
    }
    return pieces
  })
}

function Questions({ part, run, busy, runCall }) {
  return (
    <>
      {part.isListening && !part.audioPlayedOnce && (
        <div className="mb-3 flex items-center gap-3">
          <span className="text-sm text-muted-foreground">{part.audioStatus || run.listeningPrepLabel}</span>
          <Button size="sm" variant="outline" className="h-7 text-xs" disabled={busy} onClick={() => runCall('exam.skipPrep')}>
            Start now
          </Button>
        </div>
      )}

      {part.isListening && part.noAudioFallback && part.hasMaterial && (
        <div className="mb-4 rounded border border-border bg-muted/50 p-3.5">
          <p className="mb-1 text-sm font-semibold leading-relaxed">Transcript</p>
          <p className="text-sm leading-relaxed">{part.material}</p>
        </div>
      )}

      {part.questionGroupHeading && (
        <h3 className="text-base font-bold text-foreground">
          {part.questionGroupHeading}
        </h3>
      )}

      {part.questionGroupHint && (
        <p className="mb-4 mt-0.5 text-sm leading-relaxed text-foreground/80">
          {part.questionGroupHint}
        </p>
      )}

      {part.isListening && part.topic && (
        <h4 className="mb-4 text-center text-base font-bold text-foreground">
          {part.topic}
        </h4>
      )}

      <div className="flex flex-col gap-3">
        {(part.questions ?? []).map((q, index) => (
          <QuestionBlock
            key={`${part.index}-${q.number}`}
            q={q}
            index={index}
            partIndex={part.index}
            focused={index === part.focusedIndex}
            busy={busy}
            runCall={runCall}
          />
        ))}
      </div>
    </>
  )
}

function NotesPanel({ part, runCall }) {
  const [notes, setNotes, flushNotes] = useDraftText(
    part.notes ?? '',
    part.index,
    (text) => runCall('exam.setNotes', { text })
  )
  return (
    <div className="fixed inset-y-0 right-0 z-40 w-[360px] max-w-[90vw] overflow-y-auto border-l border-border bg-card p-4 shadow-xl">
      <div className="mb-3 flex items-center justify-between">
        <p className="text-sm font-semibold leading-relaxed">Notes</p>
        <Button size="sm" variant="outline" onClick={() => { flushNotes(); runCall('exam.closeNotes') }}>
          Close
        </Button>
      </div>
      <Separator className="mb-3" />
      {part.selectedMaterialWord && (
        <div className="mb-3">
          <p className="text-xs text-muted-foreground">From passage</p>
          <div className="mt-1 rounded border border-border bg-muted/50 p-2.5">
            <p className="text-sm leading-relaxed">{part.selectedMaterialWord}</p>
          </div>
        </div>
      )}
      <textarea
        className="min-h-[240px] w-full rounded border border-input bg-background p-2.5 align-top text-sm leading-relaxed"
        value={notes}
        placeholder="Write your notes here. They stay with this part."
        onChange={(e) => setNotes(e.currentTarget.value)}
        onBlur={flushNotes}
      />
    </div>
  )
}

function unansweredCount(run) {
  let blank = 0
  for (const p of run.parts ?? []) {
    for (const q of p.questions ?? []) {
      const answered = q.isGap
        ? !!q.answer
        : q.isMatch
          ? q.matchRows?.some((r) => r.selected)
          : !!q.selectedKey
      if (!answered) blank++
    }
  }
  return blank
}
