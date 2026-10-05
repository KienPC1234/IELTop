import { useState } from 'react'
import { Flag, X } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group'
import { Checkbox } from '@/components/ui/checkbox'
import { cn } from '@/lib/utils'
import { useDraftText } from '@/hooks'

/// Marks a question for review, the same flag the question strip at the bottom
/// of the test screen already colours. Every question type renders one, so the
/// flag can be set from choice, multiple choice, gap and match alike.
function FlagButton({ q, busy, onToggle }) {
  const flagged = !!q.isFlagged

  return (
    <Button
      type="button"
      variant="ghost"
      size="sm"
      disabled={busy}
      onClick={onToggle}
      aria-pressed={flagged}
      aria-label={flagged ? 'Remove review flag' : 'Mark for review'}
      title={flagged ? 'Remove review flag' : 'Mark for review'}
      className={cn(
        'ml-auto shrink-0 self-center rounded-none px-1.5 text-xs',
        flagged ? 'text-warning hover:opacity-80' : 'text-muted-foreground hover:text-foreground'
      )}
    >
      <Flag className={cn('h-4 w-4', flagged && 'fill-current')} />
    </Button>
  )
}

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

  const isMultiple = /\b(two|three)\b/i.test(q.prompt ?? '') || q.isMultipleChoice

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
    <div id={`q-${q.number}`} className={cn("px-3 py-4 transition-colors", focused && "bg-primary/5")}>
      <div className="flex items-baseline gap-3">
        <span className="text-base font-bold text-foreground tabular-nums select-none shrink-0">
          {q.number}
        </span>
        <span className="text-sm font-normal leading-relaxed text-foreground">
          {q.prompt}
        </span>
        <FlagButton
          q={q}
          busy={busy}
          onToggle={() => runCall('exam.toggleFlag', { questionIndex: index })}
        />
      </div>

      {options.length > 0 ? (
        <RadioGroup
          value={q.selectedKey ?? ''}
          onValueChange={(v) => runCall('exam.setChoice', { questionIndex: index, key: v })}
          className="mt-3.5 pl-7 flex flex-col gap-3"
        >
          {options.map((o) => {
            const isSelected = q.selectedKey === o.key
            const label = o.text?.trim() || o.key
            return (
              <label
                key={o.key}
                className="flex cursor-pointer items-center gap-3 py-0.5 text-sm text-foreground transition-colors hover:text-foreground/80"
              >
                <RadioGroupItem
                  value={o.key}
                  disabled={busy}
                  className="size-4.5 border-input"
                />
                <span className={cn(isSelected ? 'font-medium text-foreground' : 'text-foreground/90')}>
                  {label}
                </span>
              </label>
            )
          })}
        </RadioGroup>
      ) : (
        <p className="mt-2 pl-7 text-xs text-muted-foreground">No choices available.</p>
      )}
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
    .map((s: string) => s.trim())
    .filter(Boolean)

  function toggleKey(key: string) {
    let next: string[]
    if (selectedKeys.includes(key)) {
      next = selectedKeys.filter((k) => k !== key)
    } else {
      next = [...selectedKeys, key]
    }
    runCall('exam.setChoice', { questionIndex: index, key: next.join(',') })
  }

  return (
    <div id={`q-${q.number}`} className={cn("px-3 py-4 transition-colors", focused && "bg-primary/5")}>
      <div className="flex items-baseline gap-3">
        <span className="text-base font-bold text-foreground tabular-nums select-none shrink-0">
          {q.number}
        </span>
        <span className="text-sm font-normal leading-relaxed text-foreground">
          {q.prompt}
        </span>
        <FlagButton q={q} busy={busy} onToggle={() => runCall('exam.toggleFlag', { questionIndex: index })} />
      </div>

      {options.length > 0 ? (
        <div className="mt-3.5 pl-7 flex flex-col gap-3">
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
                  className="size-4.5 rounded-none border-input"
                />
                <span className={cn(isSelected ? 'font-medium text-foreground' : 'text-foreground/90')}>
                  {label}
                </span>
              </label>
            )
          })}
        </div>
      ) : (
        <p className="mt-2 pl-7 text-xs text-muted-foreground">No choices available.</p>
      )}
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
    (answer) => runCall('exam.setGap', { questionIndex: index, text: answer })
  )

  const prompt = q.prompt ?? ''
  const hasInlineGap = /_{2,}|\.{3,}|\[gap\]|\[\d+\]/i.test(prompt) || (q.gapBefore && q.gapBefore.length > 0)

  if (hasInlineGap) {
    return (
      <div id={`q-${q.number}`} className={cn("px-3 py-4 transition-colors", focused && "bg-primary/5")}>
        <div className="flex items-start gap-2">
          <div className="grow">
            <InlineGapPrompt
              prompt={prompt}
              number={q.number}
              value={val}
              disabled={busy}
              onChange={setVal}
              onBlur={flush}
            />
          </div>
          <FlagButton q={q} busy={busy} onToggle={() => runCall('exam.toggleFlag', { questionIndex: index })} />
        </div>
      </div>
    )
  }

  return (
    <div id={`q-${q.number}`} className={cn("px-3 py-4 transition-colors", focused && "bg-primary/5")}>
      <div className="flex items-baseline gap-3">
        <span className="text-base font-bold text-foreground tabular-nums select-none shrink-0">
          {q.number}
        </span>
        <span className="text-sm font-normal leading-relaxed text-foreground">
          {prompt}
        </span>
        <FlagButton q={q} busy={busy} onToggle={() => runCall('exam.toggleFlag', { questionIndex: index })} />
      </div>
      <div className="mt-2.5 pl-7 max-w-xs">
        <Input
          value={val}
          placeholder={String(q.number)}
          disabled={busy}
          className="h-8.5 w-40 rounded-none border-input bg-exam-block text-center text-sm font-medium shadow-none placeholder:text-center placeholder:font-bold placeholder:text-foreground/60"
          onChange={(e) => setVal(e.currentTarget.value)}
          onBlur={flush}
        />
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
  let cleanText = prompt
  let bulletType: 'none' | 'bullet' | 'subbullet' = 'none'

  if (/^\s*[•*]\s+/.test(cleanText)) {
    bulletType = 'bullet'
    cleanText = cleanText.replace(/^\s*[•*]\s+/, '')
  } else if (/^\s*[–—]\s+/.test(cleanText)) {
    bulletType = 'subbullet'
    cleanText = cleanText.replace(/^\s*[–—]\s+/, '')
  } else if (/^\s*-\s+/.test(cleanText)) {
    bulletType = 'bullet'
    cleanText = cleanText.replace(/^\s*-\s+/, '')
  }

  const parts = cleanText.split(/_{2,}|\.{3,}|\[gap\]|\[\d+\]/i)

  const content = (
    <span className="text-sm font-normal text-foreground leading-loose">
      {parts.map((p, i) => (
        <span key={i}>
          {p}
          {i < parts.length - 1 && (
            <span className="inline-block align-middle mx-1.5 my-0.5">
              <input
                type="text"
                value={value}
                disabled={disabled}
                placeholder={String(number)}
                className="h-8.5 w-36 sm:w-44 rounded-none border border-input bg-exam-block px-2.5 text-center text-sm font-medium text-foreground placeholder:text-center placeholder:font-bold placeholder:text-foreground/60 focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring shadow-none transition-colors"
                onChange={(e) => onChange(e.currentTarget.value)}
                onBlur={onBlur}
              />
            </span>
          )}
        </span>
      ))}
    </span>
  )

  if (bulletType === 'subbullet') {
    return (
      <div className="flex items-start gap-2.5 pl-6 sm:pl-8 py-1 leading-loose">
        <span className="text-foreground select-none font-bold text-sm leading-loose">-</span>
        <div className="flex-1 min-w-0 leading-loose">{content}</div>
      </div>
    )
  }

  if (bulletType === 'bullet') {
    return (
      <div className="flex items-start gap-2.5 pl-2 sm:pl-4 py-1 leading-loose">
        <span className="text-foreground select-none font-bold text-base leading-loose">•</span>
        <div className="flex-1 min-w-0 leading-loose">{content}</div>
      </div>
    )
  }

  return (
    <div className="py-1 leading-loose">
      {content}
    </div>
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
  const [selectedBankItem, setSelectedBankItem] = useState<string | null>(null)

  function setRowAnswer(rowIndex: number, value: string) {
    runCall('exam.setMatch', {
      questionIndex: index,
      rowIndex,
      value,
      answer: value,
    })
  }

  return (
    <div id={`q-${q.number}`} className={cn("px-3 py-4 transition-colors", focused && "bg-primary/5")}>
      <div className="flex items-start gap-3">
        {q.prompt && (
          <p className="text-sm font-normal leading-relaxed text-foreground mb-4 grow">
            {q.prompt}
          </p>
        )}
        <FlagButton q={q} busy={busy} onToggle={() => runCall('exam.toggleFlag', { questionIndex: index })} />
      </div>

      {rows.length > 0 ? (
        <div className="grid grid-cols-1 md:grid-cols-[1.3fr_1fr] gap-8 items-start mt-2">
          {/* Left Column: Target items / situations */}
          <div className="flex flex-col gap-3">
            <h4 className="text-sm font-bold text-foreground">
              {q.leftHeader || 'Questions'}
            </h4>
            <div className="flex flex-col gap-3">
              {rows.map((r: any, rowIndex: number) => {
                const targetNum = q.number + rowIndex
                const isTargetFilled = Boolean(r.selected && r.selected.trim())
                return (
                  <div
                    key={rowIndex}
                    className="flex flex-wrap items-center justify-between gap-3 py-1.5 border-b border-exam-block-border last:border-b-0"
                  >
                    <span className="text-sm font-normal text-foreground leading-relaxed">
                      {r.label}
                    </span>
                    <div
                      onDragOver={(e) => {
                        e.preventDefault()
                        e.dataTransfer.dropEffect = 'copy'
                      }}
                      onDrop={(e) => {
                        e.preventDefault()
                        const dropped = e.dataTransfer.getData('text/plain')
                        if (dropped) setRowAnswer(rowIndex, dropped)
                      }}
                      onClick={() => {
                        if (selectedBankItem) {
                          setRowAnswer(rowIndex, selectedBankItem)
                          setSelectedBankItem(null)
                        } else if (isTargetFilled) {
                          setRowAnswer(rowIndex, '')
                        }
                      }}
                      className={cn(
                        "min-w-[140px] max-w-[200px] h-9 px-3 rounded-none flex items-center justify-center text-sm transition-all select-none cursor-pointer",
                        isTargetFilled
                          ? "border border-input bg-exam-block text-foreground font-medium"
                          : "border border-dashed border-input text-muted-foreground bg-exam-surface hover:border-ring"
                      )}
                      title={isTargetFilled ? "Click to clear answer" : selectedBankItem ? `Click to place "${selectedBankItem}"` : "Drag answer here or click option"}
                    >
                      {isTargetFilled ? (
                        <div className="flex items-center justify-between w-full gap-2">
                          <span className="truncate">{r.selected}</span>
                          <X className="h-3.5 w-3.5 text-muted-foreground hover:text-foreground shrink-0" />
                        </div>
                      ) : (
                        <span>{targetNum}</span>
                      )}
                    </div>
                  </div>
                )
              })}
            </div>
          </div>

          {/* Right Column: Choices / Bank */}
          {bank.length > 0 && (
            <div className="flex flex-col gap-3">
              <div className="flex items-center justify-between">
                <h4 className="text-sm font-bold text-foreground">
                  {q.rightHeader || 'Options'}
                </h4>
                {selectedBankItem && (
                  <span className="text-xs text-primary font-medium">Click a box on left to place</span>
                )}
              </div>
              <div className="flex flex-col gap-2">
                {bank.map((item: string, bIdx: number) => {
                  const isSelected = selectedBankItem === item
                  const isUsed = rows.some((r: any) => r.selected === item)
                  return (
                    <div
                      key={bIdx}
                      draggable
                      onDragStart={(e) => {
                        e.dataTransfer.setData('text/plain', item)
                      }}
                      onClick={() => {
                        setSelectedBankItem(isSelected ? null : item)
                      }}
                      className={cn(
                        "rounded-none border px-3 py-1.5 text-sm font-medium transition-all select-none cursor-grab active:cursor-grabbing",
                        isSelected
                          ? "border-primary ring-2 ring-primary/40 bg-primary/10 text-foreground font-semibold"
                          : isUsed
                            ? "border-exam-block-border bg-exam-instruction text-muted-foreground opacity-60 hover:opacity-100"
                            : "border-input bg-exam-block text-foreground hover:bg-muted"
                      )}
                    >
                      {item}
                    </div>
                  )
                })}
              </div>
            </div>
          )}
        </div>
      ) : (
        <p className="mt-2 text-xs text-muted-foreground">No match rows available.</p>
      )}
    </div>
  )
}
