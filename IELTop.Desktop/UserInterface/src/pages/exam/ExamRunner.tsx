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
          'exam-idp flex h-screen w-full flex-col overflow-hidden bg-exam-surface text-foreground',
          part.contrastOn && 'exam-contrast'
        )}
        style={{ '--exam-scale': fontScale } as React.CSSProperties}
      >
        <ErrorBar message={error} onDismiss={() => setError('')} />

        <header className="flex h-14 shrink-0 items-center justify-between gap-4 border-b border-black/20 bg-exam-header px-4 text-exam-header-foreground sm:px-6">
          <div className="flex min-w-0 items-center gap-3">
            <img src="./assets/IELTop-red-symbol.svg" alt="" className="h-7 w-7 shrink-0 rounded" />
            <span className="truncate text-base font-bold">
              {run.title || part.paperName || 'IELTop Test'}
            </span>
          </div>
          <Button
            variant="outline"
            size="sm"
            className="h-8 rounded-none border-exam-header-muted/40 bg-transparent text-exam-header-foreground hover:bg-white/10 hover:text-exam-header-foreground"
            onClick={leaveTest}
          >
            Leave test
          </Button>
        </header>

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
        'exam-idp flex h-screen w-full flex-col overflow-hidden bg-exam-surface text-foreground',
        part.contrastOn && 'exam-contrast'
      )}
      style={{ '--exam-scale': fontScale } as React.CSSProperties}
    >
      <ErrorBar message={error} onDismiss={() => setError('')} />

      {/* Top Navigation Bar. A three column grid keeps the clock on the true
          centre of the window whatever the title and the controls weigh. The
          side columns are minmax(0,1fr) so a long title cannot widen one side
          and push the clock off centre. */}
      <header className="grid h-14 shrink-0 grid-cols-[minmax(0,1fr)_auto_minmax(0,1fr)] items-center gap-3 border-b border-black/20 bg-exam-header px-4 text-exam-header-foreground sm:px-6">
        {/* Left: Test paper title */}
        <div className="flex min-w-0 items-center gap-3">
          <img src="./assets/IELTop-red-symbol.svg" alt="" className="hidden h-7 w-7 shrink-0 rounded sm:block" />
          <div className="flex min-w-0 flex-col leading-tight">
            <span className="truncate text-sm font-bold sm:text-base">
              {run.title || part.paperName || 'IELTop Test'}
            </span>
            {part.skill && (
              <span className="truncate text-xs text-exam-header-muted">
                {part.skill}{part.skillPartNumber ? `, Part ${part.skillPartNumber}` : ''}
              </span>
            )}
          </div>
        </div>

        {/* Center: Countdown timer, the middle grid column is always centred. */}
        <div className="text-center">
          {part.isTimerHidden ? (
            <button
              type="button"
              className="text-sm font-medium text-exam-header-muted underline underline-offset-4 hover:text-exam-header-foreground"
              onClick={() => runCall('exam.toggleTimer')}
            >
              Show time
            </button>
          ) : (
            <button
              type="button"
              title="Click to hide or show clock"
              className={cn(
                'block text-base font-bold tabular-nums transition-colors hover:opacity-80 sm:text-lg',
                part.isCriticalTime
                  ? 'text-destructive'
                  : part.isLowTime
                    ? 'text-warning'
                    : 'text-exam-header-foreground'
              )}
              onClick={() => runCall('exam.toggleTimer')}
            >
              {part.remainingMinutesLabel}
            </button>
          )}
        </div>

        {/* Right: Controls */}
        <div className="flex items-center justify-end gap-2">
          {showAudioControls && (
            <div className="hidden sm:flex items-center gap-2 mr-1">
              <button
                type="button"
                onClick={toggleMute}
                className="text-exam-header-muted hover:text-exam-header-foreground transition-colors p-1"
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
                variant="ghost"
                size="icon"
                className="h-9 w-9 rounded-none text-exam-header-foreground hover:bg-white/10 hover:text-exam-header-foreground"
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
                variant="ghost"
                size="icon"
                className="h-9 w-9 rounded-none text-exam-header-foreground hover:bg-white/10 hover:text-exam-header-foreground"
                aria-label="Menu"
              >
                <MenuIcon className="h-4 w-4" />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" className="w-60 rounded-none">
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
            className="h-9 rounded-none border-exam-header-muted/40 bg-transparent px-3 text-sm font-semibold text-exam-header-foreground hover:bg-white/10 hover:text-exam-header-foreground sm:px-4"
            disabled={busy}
            onClick={() => setConfirmSubmit(true)}
          >
            Submit
          </Button>

          <Tooltip>
            <TooltipTrigger asChild>
              <Button
                variant="ghost"
                size="icon"
                className="h-9 w-9 rounded-none text-exam-header-foreground hover:bg-white/10 hover:text-exam-header-foreground"
                onClick={openNotes}
                aria-label="Notes"
              >
                <Pencil className="h-4 w-4" />
              </Button>
            </TooltipTrigger>
            <TooltipContent>Notes</TooltipContent>
          </Tooltip>
        </div>
      </header>


      {/* Strict mode warning indicator if active */}
      {run.strictMode && (
        <div className="w-full shrink-0 flex flex-wrap items-center gap-3 border-b border-warning/30 bg-warning/10 px-6 py-2 text-warning">
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
    <div className="w-[680px] max-w-full overflow-hidden rounded-none border border-exam-block-border bg-exam-block text-center">
      {/* Coloured skill strip so the section is obvious at a glance. */}
      <div className="flex items-center justify-center gap-2 bg-primary px-6 py-3 text-primary-foreground">
        <span className="text-sm font-bold uppercase tracking-widest">{skillName} Section</span>
      </div>

      <div className="px-8 py-7">
        <h2 className="text-2xl font-bold tracking-tight text-foreground">
          {run.introTitle || `IELTS ${skillName}`}
        </h2>
        <p className="mt-2 text-sm leading-relaxed text-muted-foreground">
          {run.introDetail || `${part?.minutes || 30} minutes`}
        </p>

        <div className="my-5 border-l-4 border-l-exam-instruction-accent bg-exam-instruction p-5 text-left">
          <p className="mb-1.5 text-xs font-bold uppercase tracking-wider text-muted-foreground">
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
            className="min-w-48 rounded-none text-sm font-semibold"
            disabled={busy}
            onClick={() => runCall('exam.startPart')}
          >
            {busy ? 'Starting...' : `Start ${skillName} section`}
          </Button>
          <Button variant="outline" size="lg" className="rounded-none" disabled={busy} onClick={onLeave}>
            Leave test
          </Button>
        </div>
        <p className="mt-3 text-xs leading-relaxed text-muted-foreground">
          The timer starts when you press Start {skillName} section.
        </p>
      </div>
    </div>
  )
}

function PartBody({ part, run, busy, runCall }) {
  if (part.isSpeaking) {
    return (
      <div className="h-full min-h-0 overflow-y-auto px-4 py-6 sm:px-8">
        <div className="mx-auto w-full max-w-5xl border border-exam-block-border bg-exam-block p-6 sm:p-8">
          <SpeakingPanel part={part} run={run} busy={busy} runCall={runCall} />
        </div>
      </div>
    )
  }
  if (part.isWriting) {
    return (
      <div className="h-full min-h-0 overflow-y-auto px-4 py-6 sm:px-8">
        <div className="mx-auto flex h-full min-h-0 w-full max-w-7xl border border-exam-block-border bg-exam-block p-6 sm:p-8">
          <WritingPanel part={part} busy={busy} runCall={runCall} />
        </div>
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
    <div className="h-full min-h-0 overflow-y-auto px-4 py-6 sm:px-8">
      <div className="mx-auto w-full max-w-6xl">
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
      <div className="reading-pane h-full min-h-0 overflow-y-auto bg-exam-block px-6 py-6 sm:px-10 md:border-r md:border-exam-block-border">{left}</div>
      <button
        type="button"
        className={`reading-split-handle hidden w-3 cursor-col-resize items-center justify-center border-x border-exam-block-border bg-exam-surface text-muted-foreground transition-colors hover:bg-muted md:flex ${dragging ? 'bg-muted text-foreground' : ''}`}
        onMouseDown={onGripDown}
        role="separator"
        aria-orientation="vertical"
        aria-valuenow={Math.round(split)}
        aria-valuemin={28}
        aria-valuemax={72}
        aria-label="Resize panels"
      >
        <div className="rounded-none border border-exam-block-border bg-exam-block p-0.5">
          <ArrowLeftRight className="h-3 w-3 text-muted-foreground" />
        </div>
      </button>
      <div className="reading-pane h-full min-h-0 overflow-y-auto bg-exam-block px-6 py-6 sm:px-10">{right}</div>
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
      <div className="sticky top-0 z-10 mb-3 flex flex-wrap items-center gap-2 border-b border-exam-block-border bg-exam-block py-1.5">
        <Button size="sm" variant="outline" className="h-7 text-xs rounded-none" disabled={busy} onClick={highlightSelection}>
          Highlight
        </Button>
        <Button
          size="sm"
          variant="outline"
          className="h-7 text-xs rounded-none"
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
                <mark key={j} className="bg-warning/25 px-0.5 text-foreground">
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
    <div className="flex flex-col gap-4">
      {/* Instruction banner: its own block with a colour edge so it reads as a
          heading, the way the official test separates instructions from
          questions. */}
      <div className="border border-exam-block-border border-l-4 border-l-exam-instruction-accent bg-exam-instruction p-4 select-none">
        <h2 className="text-base font-bold text-foreground">
          {part.instructionHeading || `Part ${part.skillPartNumber || part.index + 1}`}
        </h2>
        <p className="mt-0.5 text-sm text-foreground/80 leading-relaxed">
          {part.bannerInstruction || (
            part.isReading
              ? 'Read the text and answer questions 1-40.'
              : part.isListening
                ? 'Listen and answer questions 1-40.'
                : part.instructions
          )}
        </p>
      </div>

      {part.isListening && !part.audioPlayedOnce && (
        <div className="flex items-center gap-3">
          <span className="text-sm text-muted-foreground">{part.audioStatus || run.listeningPrepLabel}</span>
          <Button size="sm" variant="outline" className="h-7 text-xs rounded-none" disabled={busy} onClick={() => runCall('exam.skipPrep')}>
            Start now
          </Button>
        </div>
      )}

      {part.isListening && part.noAudioFallback && part.hasMaterial && (
        <div className="border border-exam-block-border border-l-4 border-l-warning bg-warning/10 p-3.5">
          <p className="mb-1 text-sm font-semibold leading-relaxed">Transcript</p>
          <p className="text-sm leading-relaxed">{part.material}</p>
        </div>
      )}

      {/* Question block: white sheet with a rule between questions. */}
      <div className="border border-exam-block-border bg-exam-block p-4 sm:p-5">
        {part.questionGroupHeading && (
          <div className="mb-1 border-b border-exam-block-border pb-3">
            <h3 className="text-base font-bold text-foreground">
              {part.questionGroupHeading}
            </h3>
            {part.questionGroupHint && (
              <p className="mt-1 text-sm leading-relaxed text-foreground/85">
                {part.questionGroupHint}
              </p>
            )}
          </div>
        )}

        {part.isListening && part.topic && (
          <h4 className="my-3 text-center text-base font-bold tracking-tight text-foreground">
            {part.topic}
          </h4>
        )}

        <div className="divide-y divide-exam-block-border">
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
      </div>
    </div>
  )
}

function NotesPanel({ part, runCall }) {
  const [notes, setNotes, flushNotes] = useDraftText(
    part.notes ?? '',
    part.index,
    (text) => runCall('exam.setNotes', { text })
  )
  return (
    <div className="fixed inset-y-0 right-0 z-40 w-[360px] max-w-[90vw] overflow-y-auto border-l border-exam-block-border bg-exam-block p-4 shadow-xl">
      <div className="mb-3 flex items-center justify-between">
        <p className="text-sm font-semibold leading-relaxed">Notes</p>
        <Button size="sm" variant="outline" className="rounded-none" onClick={() => { flushNotes(); runCall('exam.closeNotes') }}>
          Close
        </Button>
      </div>
      <Separator className="mb-3" />
      {part.selectedMaterialWord && (
        <div className="mb-3">
          <p className="text-xs text-muted-foreground">From passage</p>
          <div className="mt-1 rounded-none border border-exam-block-border bg-exam-instruction p-2.5">
            <p className="text-sm leading-relaxed">{part.selectedMaterialWord}</p>
          </div>
        </div>
      )}
      <textarea
        className="min-h-[240px] w-full rounded-none border border-input bg-exam-block p-2.5 align-top text-sm leading-relaxed"
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
