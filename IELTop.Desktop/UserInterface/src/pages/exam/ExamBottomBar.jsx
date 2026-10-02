import { Group, Box, UnstyledButton, ActionIcon, Text, Tooltip } from '@mantine/core'
import { ArrowLeft, ArrowRight } from 'lucide-react'

/// The bottom strip: a thin progress line, the part tabs (the part in view
/// also lists its question numbers), and the navigation arrows on the right.
export default function ExamBottomBar({ exam, busy, runCall }) {
  const run = exam.run
  const part = run.currentPart
  if (!part) return null

  const atFirst = part.focusedIndex <= 0
  const atLast = part.focusedIndex >= part.questions.length - 1

  // The arrows walk the questions, then step to the neighbouring part, like
  // the real test. A speaking part has no question list, so they step parts.
  function goBack() {
    if (!atFirst) return runCall('exam.moveQuestion', { delta: -1 })
    if (part.index > 0 && run.parts[part.index - 1]?.isPassed)
      return runCall('exam.selectPart', { index: part.index - 1 })
  }

  function goForward() {
    if (!atLast) return runCall('exam.moveQuestion', { delta: 1 })
    if (run.parts[part.index + 1]) return runCall('exam.nextPart')
  }

  return (
    <Box className="exam-bottom">
      <Group gap={2} px="lg" wrap="nowrap">
        {run.parts.map((p) => (
          <Box key={p.index} className="progress-seg" style={{ background: p.progressFill }} />
        ))}
      </Group>

      <Group justify="space-between" wrap="nowrap" gap="md" px="lg" py="sm">
        <Group gap={0} wrap="nowrap" style={{ flex: 1, minWidth: 0 }}>
          {run.parts.map((p) => (
            <Group
              key={p.index}
              gap="xs"
              wrap="nowrap"
              className={`strip-part${p.isCurrent ? ' current' : ''}`}
            >
              <UnstyledButton
                className={`strip-tab${p.isCurrent ? ' current' : ''}`}
                disabled={busy || (!p.isPassed && !p.isCurrent)}
                onClick={() => runCall('exam.selectPart', { index: p.index })}
              >
                <Text span fw={p.isCurrent ? 700 : 400} size="sm" c={p.isCurrent ? undefined : 'dimmed'}>
                  {p.partTabLabel}
                </Text>
                {!p.isCurrent && (
                  <Text span size="xs" c="dimmed" ml={8}>
                    {p.answeredProgressLabel}
                  </Text>
                )}
              </UnstyledButton>
              {p.isCurrent && p.questions.length > 0 && (
                <Group gap={2} wrap="nowrap">
                  {p.questions.map((q, i) => (
                    <UnstyledButton
                      key={q.number}
                      className={`strip-number${i === p.focusedIndex ? ' on' : ''}${q.isFlagged ? ' flagged' : ''}`}
                      onClick={() => runCall('exam.goToQuestion', { index: i })}
                    >
                      {q.number}
                    </UnstyledButton>
                  ))}
                </Group>
              )}
            </Group>
          ))}
        </Group>

        <Group gap="xs" wrap="nowrap">
          <Tooltip label="Previous" withArrow>
            <ActionIcon
              className="nav-square"
              variant="default"
              size="xl"
              radius="sm"
              disabled={busy || (atFirst && part.index === 0)}
              onClick={goBack}
              aria-label="Previous"
            >
              <ArrowLeft size={22} />
            </ActionIcon>
          </Tooltip>
          <Tooltip label="Next" withArrow>
            <ActionIcon
              className="nav-square"
              variant="default"
              size="xl"
              radius="sm"
              disabled={busy || (atLast && part.index >= run.parts.length - 1)}
              onClick={goForward}
              aria-label="Next"
            >
              <ArrowRight size={22} />
            </ActionIcon>
          </Tooltip>
        </Group>
      </Group>
    </Box>
  )
}
