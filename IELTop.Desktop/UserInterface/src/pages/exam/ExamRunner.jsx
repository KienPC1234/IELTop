import { useState } from 'react'
import {
  Box,
  Group,
  Text,
  Button,
  ActionIcon,
  Menu,
  Slider,
  Paper,
  Badge,
  Divider,
  Stack,
  Center,
  Tooltip,
  ScrollArea,
} from '@mantine/core'
import { Volume2, VolumeX, Maximize2, Menu as MenuIcon, Pencil } from 'lucide-react'
import { call } from '../../bridge.js'
import QuestionBlock from './QuestionBlock.jsx'
import ExamBottomBar from './ExamBottomBar.jsx'
import WritingPanel from './WritingPanel.jsx'
import SpeakingPanel from './SpeakingPanel.jsx'
import SubmitConfirm from './SubmitConfirm.jsx'

/// The running exam window: top bar, instruction band, the part body, the
/// notes panel, and the bottom strip. All state and scoring live in the Core
/// engine, so the same answers score the same way as the old Windows app.
export default function ExamRunner({ exam, onApply, fontScale = 1 }) {
  const [busy, setBusy] = useState(false)
  const [confirmSubmit, setConfirmSubmit] = useState(false)
  const [muted, setMuted] = useState(false)
  const [lastVolume, setLastVolume] = useState(80)
  const run = exam.run
  const part = run.currentPart

  async function runCall(method, args) {
    setBusy(true)
    try {
      onApply(await call(method, args))
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
      setLastVolume(run.volume)
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
    call('exam.closeWindow').catch(() => {})
  }

  if (!part) return null

  const intro = run.isPartIntro
  const lowTime = part.isLowTime
  const critical = part.isCriticalTime

  return (
    <Box
      className={`exam-shell${part.contrastOn ? ' contrast' : ''}`}
      style={{ '--exam-scale': fontScale }}
    >
      {/* Top bar: brand and title, the countdown, then the tools. */}
      <Group
        justify="space-between"
        wrap="nowrap"
        px="lg"
        py="sm"
        className="exam-top"
      >
        <Group gap="sm" wrap="nowrap" style={{ minWidth: 0, flex: 1 }}>
          <span className="exam-logo">IELTop</span>
          <Text fw={700} size="md" truncate>
            {run.title || part.headerLine}
          </Text>
        </Group>

        <Group gap="xs" wrap="nowrap" justify="center">
          {part.isTimerHidden ? (
            <Button variant="default" size="compact-sm" onClick={() => runCall('exam.toggleTimer')}>
              Show time
            </Button>
          ) : (
            <Tooltip label="Hide the clock" withArrow>
              <Text
                fw={600}
                size="md"
                style={{ cursor: 'pointer' }}
                c={critical ? 'red.8' : lowTime ? 'orange.8' : undefined}
                onClick={() => runCall('exam.toggleTimer')}
              >
                {part.remainingMinutesLabel}
              </Text>
            </Tooltip>
          )}
        </Group>

        <Group gap="xs" wrap="nowrap" justify="flex-end" style={{ flex: 1 }}>
          <Tooltip label="Mute or unmute" withArrow>
            <ActionIcon variant="default" size="lg" onClick={toggleMute} aria-label="Mute or unmute">
              {muted || run.volume === 0 ? <VolumeX size={18} /> : <Volume2 size={18} />}
            </ActionIcon>
          </Tooltip>
          <Slider
            w={120}
            min={0}
            max={100}
            value={run.volume}
            onChange={(v) => runCall('exam.setVolume', { value: v })}
            aria-label="Volume"
          />
          <Tooltip label="Full screen" withArrow>
            <ActionIcon variant="default" size="lg" onClick={toggleFullscreen} aria-label="Full screen">
              <Maximize2 size={17} />
            </ActionIcon>
          </Tooltip>

          <Menu shadow="md" position="bottom-end" width={240} withinPortal>
            <Menu.Target>
              <ActionIcon variant="default" size="lg" aria-label="Menu">
                <MenuIcon size={18} />
              </ActionIcon>
            </Menu.Target>
            <Menu.Dropdown>
              <Menu.Label>Reading</Menu.Label>
              <Menu.Item onClick={() => runCall('exam.smallerText')}>Smaller text</Menu.Item>
              <Menu.Item onClick={() => runCall('exam.biggerText')}>Larger text</Menu.Item>
              <Menu.Item onClick={() => runCall('exam.toggleContrast')}>
                {part.contrastOn ? 'Reading: normal' : 'Reading: high contrast'}
              </Menu.Item>
              <Menu.Divider />
              <Menu.Label>Test</Menu.Label>
              <Menu.Item onClick={() => runCall('exam.toggleStrict')}>
                {run.strictMode ? 'Strict mode: on' : 'Strict mode: off'}
              </Menu.Item>
              <Menu.Item onClick={() => runCall('exam.toggleTimer')}>
                {part.isTimerHidden ? 'Show the clock' : 'Hide the clock'}
              </Menu.Item>
              <Menu.Divider />
              <Menu.Item color="red" onClick={leaveTest}>
                Leave the test
              </Menu.Item>
            </Menu.Dropdown>
          </Menu>

          <Button variant="default" disabled={busy} onClick={() => setConfirmSubmit(true)}>
            Submit
          </Button>

          <Tooltip label="Notes" withArrow>
            <ActionIcon variant="default" size="lg" onClick={openNotes} aria-label="Notes">
              <Pencil size={17} />
            </ActionIcon>
          </Tooltip>
        </Group>
      </Group>

      {/* Instruction band, like the real test. */}
      <Paper withBorder radius="sm" mx="lg" mt="md" p="md" bg="gray.0">
        <Text fw={700}>{part.instructionHeading}</Text>
        <Text size="sm" c="dimmed" mt={2}>
          {part.instructions}
        </Text>
      </Paper>

      {run.strictMode && run.hasViolations && (
        <Paper mx="lg" mt="sm" p="xs" radius="sm" bg="yellow.1">
          <Text size="sm" c="yellow.9">
            {run.violationLabel}
          </Text>
        </Paper>
      )}

      {/* The part body. Reading splits the passage and the questions. */}
      <ScrollArea className="exam-body" type="auto">
        <Box px="lg" py="md">
          {intro ? (
            <IntroCard exam={exam} busy={busy} runCall={runCall} />
          ) : (
            <PartBody part={part} run={run} busy={busy} runCall={runCall} />
          )}
        </Box>
      </ScrollArea>

      {part.notesOpen && <NotesPanel part={part} busy={busy} runCall={runCall} />}

      <ExamBottomBar exam={exam} busy={busy} runCall={runCall} />

      <SubmitConfirm
        open={confirmSubmit}
        unanswered={unansweredCount(run)}
        onCancel={() => setConfirmSubmit(false)}
        onConfirm={async () => {
          setConfirmSubmit(false)
          await runCall('exam.submit')
        }}
      />
    </Box>
  )
}

function IntroCard({ exam, busy, runCall }) {
  const run = exam.run
  return (
    <Center mih={380}>
      <Paper withBorder radius="md" p="xl" w={560} ta="center">
        <Badge color="red" variant="light" mb="xs">
          {run.introSkill}
        </Badge>
        <Text fw={700} size="xl">
          {run.introTitle}
        </Text>
        <Text c="dimmed" size="sm" mt="xs">
          {run.introDetail}
        </Text>
        <Text size="sm" c="dimmed" mt="md">
          {run.introHint}
        </Text>
        <Button mt="lg" disabled={busy} onClick={() => runCall('exam.startPart')}>
          Start part
        </Button>
        <Text size="xs" c="dimmed" mt="sm">
          The clock starts when you press Start part.
        </Text>
      </Paper>
    </Center>
  )
}

function PartBody({ part, run, busy, runCall }) {
  if (part.isSpeaking) {
    return <SpeakingPanel part={part} run={run} busy={busy} runCall={runCall} />
  }
  if (part.isWriting) {
    return <WritingPanel part={part} busy={busy} runCall={runCall} />
  }
  if (part.isReading && part.hasMaterial) {
    return (
      <div className="split">
        <div className="passage">
          <Passage part={part} busy={busy} runCall={runCall} />
        </div>
        <div className="questions">
          <Questions part={part} run={run} busy={busy} runCall={runCall} />
        </div>
      </div>
    )
  }
  return (
    <Box className="questions single">
      <Questions part={part} run={run} busy={busy} runCall={runCall} />
    </Box>
  )
}

function Passage({ part, busy, runCall }) {
  function highlightSelection() {
    const selected = window.getSelection ? String(window.getSelection()).trim() : ''
    if (selected) runCall('exam.addHighlight', { text: selected })
  }
  return (
    <>
      <Group gap="xs" mb="sm">
        <Button variant="default" size="compact-sm" disabled={busy} onClick={highlightSelection}>
          Highlight
        </Button>
        <Button
          variant="default"
          size="compact-sm"
          disabled={busy || part.highlights.length === 0}
          onClick={() => runCall('exam.clearHighlights')}
        >
          Clear highlights
        </Button>
        <Text size="xs" c="dimmed">
          Select text, then press Highlight.
        </Text>
      </Group>
      <article className="passage-text">
        {highlightParagraphs(part.material, part.highlights).map((chunk, i) => (
          <p key={i}>
            {chunk.map((piece, j) =>
              piece.mark ? <mark key={j}>{piece.text}</mark> : <span key={j}>{piece.text}</span>
            )}
          </p>
        ))}
      </article>
    </>
  )
}

/// Splits the passage into paragraphs, wrapping any highlighted phrase in a
/// marker so the highlight shows without a rich text editor.
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
      {part.isListening && (
        <Group gap="sm" mb="sm">
          <Text size="sm" c="dimmed">
            {part.audioStatus}
          </Text>
          {!part.audioPlayedOnce && (
            <Button variant="default" size="compact-sm" disabled={busy} onClick={() => runCall('exam.skipPrep')}>
              Start now
            </Button>
          )}
        </Group>
      )}
      {part.isListening && part.noAudioFallback && part.hasMaterial && (
        <Paper withBorder radius="sm" p="sm" mb="md" bg="gray.0">
          <Text fw={600} size="sm" mb={4}>
            Transcript
          </Text>
          <Text size="sm">{part.material}</Text>
        </Paper>
      )}
      <Text fw={700} size="md">
        {part.questionGroupHeading}
      </Text>
      <Text size="sm" mb="md">
        {part.questionGroupHint}
      </Text>
      <Stack gap="lg">
        {part.questions.map((q, index) => (
          <QuestionBlock
            key={q.number}
            q={q}
            index={index}
            focused={index === part.focusedIndex}
            busy={busy}
            runCall={runCall}
          />
        ))}
      </Stack>
    </>
  )
}

function NotesPanel({ part, busy, runCall }) {
  return (
    <Paper className="notes-panel" shadow="lg" p="md">
      <Group justify="space-between" mb="sm">
        <Text fw={700}>Notes</Text>
        <Button variant="default" size="compact-sm" onClick={() => runCall('exam.closeNotes')}>
          Close
        </Button>
      </Group>
      <Divider mb="sm" />
      {part.selectedMaterialWord && (
        <Box mb="sm">
          <Text size="xs" c="dimmed">
            From the passage
          </Text>
          <Paper withBorder p="xs" radius="sm" bg="gray.0" mt={4}>
            <Text size="sm">{part.selectedMaterialWord}</Text>
          </Paper>
        </Box>
      )}
      <textarea
        className="essay"
        rows={12}
        value={part.notes}
        placeholder="Write your notes here. They stay with this part."
        onChange={(e) => runCall('exam.setNotes', { text: e.target.value })}
      />
    </Paper>
  )
}

/// Counts questions with no answer across every part, for the submit warning.
function unansweredCount(run) {
  let blank = 0
  for (const p of run.parts ?? []) {
    for (const q of p.questions ?? []) {
      const answered = q.isGap ? !!q.answer : q.isMatch ? q.matchRows?.some((r) => r.selected) : !!q.selectedKey
      if (!answered) blank++
    }
  }
  return blank
}
