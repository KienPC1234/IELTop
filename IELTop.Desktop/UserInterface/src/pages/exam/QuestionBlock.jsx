import { Text, Radio, Table, Button, Box, Paper } from '@mantine/core'

/// One question: a gap sentence with an inline box, a matching table, or a
/// list of choices. Numbers sit in a left gutter, like the real test. A gap
/// keeps its number inside the box, so there is no gutter number for it.
export default function QuestionBlock({ q, index, focused, busy, runCall }) {
  if (q.isGap) {
    return (
      <Box id={`q-${q.number}`} className={`q-row gap${focused ? ' focused' : ''}`}>
        <div className="q-main">
          <p className="gap-sentence">
            {q.gapBefore}{' '}
            <span className="gap-slot">
              <input
                className="gap-box"
                value={q.answer}
                placeholder={q.gapBoxNumber}
                maxLength={60}
                onChange={(e) => runCall('exam.setGap', { questionIndex: index, text: e.currentTarget.value })}
              />
            </span>
            {q.gapAfter ? ` ${q.gapAfter}` : ''}
          </p>
          <FlagButton q={q} index={index} busy={busy} runCall={runCall} />
        </div>
      </Box>
    )
  }

  if (q.isMatch) {
    return (
      <Box id={`q-${q.number}`} className={`q-row${focused ? ' focused' : ''}`}>
        <div className="q-number">{q.number}</div>
        <div className="q-main">
          <Text mb="xs">{q.prompt}</Text>
          {q.bank.length > 0 ? (
            <Table className="match-table" withTableBorder withColumnBorders>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th />
                  {q.bank.map((b) => (
                    <Table.Th key={b} ta="center">
                      {b}
                    </Table.Th>
                  ))}
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {q.matchRows.map((row, rowIndex) => (
                  <Table.Tr key={row.label}>
                    <Table.Td fw={600}>{row.label}</Table.Td>
                    {q.bank.map((b) => (
                      <Table.Td key={b} ta="center">
                        <Radio
                          checked={row.selected === b}
                          onChange={() => runCall('exam.setMatch', { questionIndex: index, rowIndex, value: b })}
                          aria-label={`${row.label} ${b}`}
                        />
                      </Table.Td>
                    ))}
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          ) : (
            <Text size="sm" c="dimmed">
              No choices were given for this matching question.
            </Text>
          )}
          <FlagButton q={q} index={index} busy={busy} runCall={runCall} />
        </div>
      </Box>
    )
  }

  return (
    <Box id={`q-${q.number}`} className={`q-row${focused ? ' focused' : ''}`}>
      <div className="q-number">{q.number}</div>
      <div className="q-main">
        <Text mb="xs">{q.prompt}</Text>
        <Radio.Group value={q.selectedKey} onChange={(v) => runCall('exam.setChoice', { questionIndex: index, key: v })}>
          <div className="options">
            {q.options.map((o) => (
              <Paper
                key={o.key}
                withBorder
                radius="sm"
                p="xs"
                className={`option${o.isSelected ? ' on' : ''}`}
              >
                <Radio value={o.key} label={`${o.key}. ${o.text}`} />
              </Paper>
            ))}
          </div>
        </Radio.Group>
        <FlagButton q={q} index={index} busy={busy} runCall={runCall} />
      </div>
    </Box>
  )
}

function FlagButton({ q, index, busy, runCall }) {
  return (
    <Button
      variant={q.isFlagged ? 'light' : 'subtle'}
      color={q.isFlagged ? 'yellow' : 'gray'}
      size="compact-xs"
      mt="xs"
      disabled={busy}
      onClick={() => runCall('exam.toggleFlag', { questionIndex: index })}
    >
      {q.isFlagged ? 'Marked for review' : 'Review'}
    </Button>
  )
}
