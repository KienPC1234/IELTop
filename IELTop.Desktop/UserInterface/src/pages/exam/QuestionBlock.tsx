import { Input } from '@/components/ui/input'
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group'
import { Checkbox } from '@/components/ui/checkbox'
import { cn } from '@/lib/utils'
import { useDraftText } from '@/hooks'

export default function QuestionBlock({
  q,
  index,
  partIndex,
  focused,
  busy,
  runCall,
}: {
  q: any
  index: number
  partIndex?: number
  focused?: any
  busy?: boolean
  runCall: (method: string, args?: any) => Promise<any>
}) {
  if (q.isGap) {
    return <GapQuestion q={q} index={index} partIndex={partIndex} focused={focused} busy={busy} runCall={runCall} />
  }

  if (q.isMatch) {
    return <MatchQuestion q={q} index={index} focused={focused} busy={busy} runCall={runCall} />
  }

  const isMultiple = (q.prompt ?? '').toLowerCase().includes('two') ||
                     (q.prompt ?? '').toLowerCase().includes('three') ||
                     q.isMultipleChoice

  if (isMultiple) {
    return <MultipleChoiceQuestion q={q} index={index} focused={focused} busy={busy} runCall={runCall} />
  }

  return <SingleChoiceQuestion q={q} index={index} focused={focused} busy={busy} runCall={runCall} />
}

function SingleChoiceQuestion({
  q,
  index,
  focused,
  busy,
  runCall,
}: {
  q: any
  index: number
  focused?: any
  busy?: boolean
  runCall: (method: string, args?: any) => Promise<any>
}) {
  const options = q.options ?? []

  return (
    <div id={`q-${q.number}`} className="flex items-start gap-4 py-2.5">
      <div className="w-6 shrink-0 pt-0.5 text-sm font-bold text-foreground tabular-nums">
        {q.number}
      </div>
      <div className="min-w-0 flex-1">
        <p className="text-sm font-normal leading-relaxed text-foreground">{q.prompt}</p>

        {options.length > 0 ? (
          <RadioGroup
            value={q.selectedKey ?? ''}
            onValueChange={(v) => runCall('exam.setChoice', { questionIndex: index, key: v })}
            className="mt-3 flex flex-col gap-2.5"
          >
            {options.map((o) => {
              const isSelected = q.selectedKey === o.key
              const label = o.text?.trim() || o.key
              return (
                <label
                  key={o.key}
                  className="flex cursor-pointer items-center gap-3 py-0.5 text-sm text-foreground transition-colors hover:text-foreground/80"
                >
                  <RadioGroupItem value={o.key} disabled={busy} />
                  <span className={cn(isSelected && 'font-semibold')}>{label}</span>
                </label>
              )
            })}
          </RadioGroup>
        ) : (
          <p className="mt-2 text-xs text-muted-foreground">No choices available.</p>
        )}
      </div>
    </div>
  )
}

function MultipleChoiceQuestion({
  q,
  index,
  focused,
  busy,
  runCall,
}: {
  q: any
  index: number
  focused?: any
  busy?: boolean
  runCall: (method: string, args?: any) => Promise<any>
}) {
  const options = q.options ?? []
  const selectedKeys = (q.selectedKey ?? '')
    .split(',')
    .map((s) => s.trim())
    .filter(Boolean)

  function toggleKey(key) {
    let next
    if (selectedKeys.includes(key)) {
      next = selectedKeys.filter((k) => k !== key)
    } else {
      next = [...selectedKeys, key]
    }
    runCall('exam.setChoice', { questionIndex: index, key: next.join(',') })
  }

  return (
    <div id={`q-${q.number}`} className="flex items-start gap-4 py-2.5">
      <div className="w-6 shrink-0 pt-0.5 text-sm font-bold text-foreground tabular-nums">
        {q.number}
      </div>
      <div className="min-w-0 flex-1">
        <p className="text-sm font-normal leading-relaxed text-foreground">{q.prompt}</p>

        {options.length > 0 ? (
          <div className="mt-3 flex flex-col gap-2.5">
            {options.map((o) => {
              const isSelected = selectedKeys.includes(o.key)
              const label = o.text?.trim() || o.key
              return (
                <label
                  key={o.key}
                  className="flex cursor-pointer items-center gap-3 py-0.5 text-sm text-foreground transition-colors hover:text-foreground/80"
                >
                  <Checkbox
                    checked={isSelected}
                    disabled={busy}
                    onCheckedChange={() => toggleKey(o.key)}
                  />
                  <span className={cn(isSelected && 'font-semibold')}>{label}</span>
                </label>
              )
            })}
          </div>
        ) : (
          <p className="mt-2 text-xs text-muted-foreground">No choices available.</p>
        )}
      </div>
    </div>
  )
}

function GapQuestion({
  q,
  index,
  partIndex,
  focused,
  busy,
  runCall,
}: {
  q: any
  index: number
  partIndex?: number
  focused?: any
  busy?: boolean
  runCall: (method: string, args?: any) => Promise<any>
}) {
  const [val, setVal, flush] = useDraftText(
    q.answer ?? '',
    `${partIndex}-${index}`,
    (answer) => runCall('exam.setGap', { questionIndex: index, answer })
  )

  const prompt = q.prompt ?? ''
  const hasInlineGap = prompt.includes('_____') || prompt.includes('___')

  return (
    <div id={`q-${q.number}`} className="flex items-start gap-4 py-2.5">
      <div className="w-6 shrink-0 pt-1 text-sm font-bold text-foreground tabular-nums">
        {q.number}
      </div>
      <div className="min-w-0 flex-1">
        {hasInlineGap ? (
          <InlineGapPrompt
            prompt={prompt}
            number={q.number}
            value={val}
            disabled={busy}
            onChange={setVal}
            onBlur={flush}
          />
        ) : (
          <div>
            <p className="text-sm font-normal leading-relaxed text-foreground">{prompt}</p>
            <div className="mt-2.5 max-w-sm">
              <Input
                value={val}
                placeholder="Type answer here..."
                disabled={busy}
                className="h-9 rounded-sm border-border bg-background text-sm font-medium"
                onChange={(e) => setVal(e.currentTarget.value)}
                onBlur={flush}
              />
            </div>
          </div>
        )}
      </div>
    </div>
  )
}

function InlineGapPrompt({
  prompt,
  number,
  value,
  disabled,
  onChange,
  onBlur,
}: {
  prompt: string
  number: number | string
  value: string
  disabled?: boolean
  onChange: (val: string) => void
  onBlur: () => void
}) {
  const parts = prompt.split(/_{3,}/)
  return (
    <p className="text-sm font-normal leading-loose text-foreground">
      {parts.map((p, i) => (
        <span key={i}>
          {p}
          {i < parts.length - 1 && (
            <span className="mx-1.5 inline-block align-middle">
              <Input
                value={value}
                disabled={disabled}
                placeholder={String(number)}
                className="inline-block h-8 w-36 rounded-sm border-border bg-background px-2.5 text-center text-sm font-medium focus-visible:ring-1"
                onChange={(e) => onChange(e.currentTarget.value)}
                onBlur={onBlur}
              />
            </span>
          )}
        </span>
      ))}
    </p>
  )
}

function MatchQuestion({
  q,
  index,
  focused,
  busy,
  runCall,
}: {
  q: any
  index: number
  focused?: any
  busy?: boolean
  runCall: (method: string, args?: any) => Promise<any>
}) {
  const rows = q.matchRows ?? []
  const bank = q.bank ?? []

  return (
    <div id={`q-${q.number}`} className="flex items-start gap-4 py-2.5">
      <div className="w-6 shrink-0 pt-0.5 text-sm font-bold text-foreground tabular-nums">
        {q.number}
      </div>
      <div className="min-w-0 flex-1">
        <p className="text-sm font-normal leading-relaxed text-foreground">{q.prompt}</p>

        {rows.length > 0 ? (
          <div className="mt-3 flex flex-col gap-2 rounded border border-border p-3">
            {rows.map((r, rowIndex) => (
              <div key={rowIndex} className="flex flex-wrap items-center justify-between gap-3 border-b border-border/50 py-2 last:border-b-0">
                <span className="text-sm font-medium text-foreground">{r.label}</span>
                <select
                  value={r.selected ?? ''}
                  disabled={busy}
                  onChange={(e) =>
                    runCall('exam.setMatchRow', {
                      questionIndex: index,
                      rowIndex,
                      answer: e.currentTarget.value,
                    })
                  }
                  className="h-8 rounded border border-input bg-background px-3 text-xs font-medium text-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
                >
                  <option value="">Select match...</option>
                  {bank.map((item, bIdx) => (
                    <option key={bIdx} value={item}>{item}</option>
                  ))}
                </select>
              </div>
            ))}
          </div>
        ) : (
          <p className="mt-2 text-xs text-muted-foreground">No match rows available.</p>
        )}
      </div>
    </div>
  )
}
