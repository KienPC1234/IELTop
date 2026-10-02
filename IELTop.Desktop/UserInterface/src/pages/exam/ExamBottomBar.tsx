import { ChevronLeft, ChevronRight } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { cn } from '@/lib/utils'

export default function ExamBottomBar({ exam, busy, runCall, onConfirmSubmit }) {
  const run = exam?.run
  const part = run?.currentPart
  if (!part) return null

  const questions = part.questions ?? []
  const atFirst = (part.focusedIndex ?? 0) <= 0
  const atLast = (part.focusedIndex ?? 0) >= questions.length - 1

  const currentSkill = part.skill
  const skillParts = (run.parts ?? []).filter(
    (p) => p.skill?.toLowerCase() === currentSkill?.toLowerCase()
  )
  const displayParts = skillParts.length > 0 ? skillParts : (run.parts ?? [])

  const isLastPartInSkill =
    skillParts.length > 0 && skillParts[skillParts.length - 1].index === part.index
  const hasNextSkill = run.hasNextSkill ?? (run.parts ?? []).some(
    (p) => p.index > part.index && p.skill?.toLowerCase() !== currentSkill?.toLowerCase()
  )
  const nextSkillName =
    run.nextSkillName ||
    (run.parts ?? []).find(
      (p) => p.index > part.index && p.skill?.toLowerCase() !== currentSkill?.toLowerCase()
    )?.skill

  function goBack() {
    runCall('exam.moveQuestion', { delta: -1 })
  }

  function goForward() {
    if (atLast && isLastPartInSkill && hasNextSkill) {
      return runCall('exam.nextSection')
    }
    runCall('exam.moveQuestion', { delta: 1 })
  }

  return (
    <div className="shrink-0 border-t border-border bg-card">
      <div className="flex h-13 items-center justify-between gap-2 px-3 sm:gap-4 sm:px-6">
        <div
          className="flex min-w-0 flex-1 items-center gap-3 overflow-x-auto py-1 sm:gap-6"
          role="tablist"
          aria-label="Skill parts"
        >
          {displayParts.map((p) => {
            const isCurrent = p.index === part.index
            const partNum = p.skillPartNumber || p.index + 1
            const pQuestions = p.questions ?? []

            if (isCurrent) {
              return (
                <div
                  key={p.index}
                  role="tab"
                  aria-selected={true}
                  className="flex shrink-0 items-center gap-2 border-b-2 border-foreground pb-1 sm:gap-3"
                >
                  <span className="text-sm font-bold text-foreground">
                    Part {partNum}
                  </span>
                  {pQuestions.length > 0 && (
                    <div className="flex shrink-0 items-center gap-1 sm:gap-1.5">
                      {pQuestions.map((q, i) => {
                        const isFocused = i === (part.focusedIndex ?? 0)
                        return (
                          <button
                            key={q.number}
                            type="button"
                            aria-label={`Question ${q.number}`}
                            disabled={busy}
                            className={cn(
                              'min-w-[22px] px-1 py-0.5 text-center text-sm tabular-nums transition-colors',
                              isFocused
                                ? 'border-b-2 border-primary font-bold text-foreground'
                                : 'text-muted-foreground hover:text-foreground',
                              q.isAnswered && !isFocused && 'border-b border-foreground/70 font-medium text-foreground',
                              q.isFlagged && 'rounded-sm bg-amber-500/20'
                            )}
                            onClick={() => runCall('exam.goToQuestion', { index: i })}
                          >
                            {q.number}
                          </button>
                        )
                      })}
                    </div>
                  )}
                </div>
              )
            }

            return (
              <button
                key={p.index}
                type="button"
                role="tab"
                aria-selected={false}
                disabled={busy}
                onClick={() => runCall('exam.selectPart', { index: p.index })}
                className="flex shrink-0 items-center gap-1.5 text-sm font-medium text-muted-foreground transition-colors hover:text-foreground sm:gap-2"
              >
                <span>Part {partNum}</span>
                <span className="text-xs tabular-nums opacity-75">
                  {p.answeredCount ?? 0}/{p.scoredCount ?? pQuestions.length}
                </span>
              </button>
            )
          })}
        </div>

        <div className="flex shrink-0 items-center gap-1.5 sm:gap-2">
          {isLastPartInSkill && hasNextSkill && (
            <Button
              variant="default"
              size="sm"
              className="h-9 rounded-sm px-3 text-xs font-semibold sm:px-4 sm:text-sm"
              disabled={busy}
              onClick={() => runCall('exam.nextSection')}
            >
              Next section: {nextSkillName || 'Next'}
            </Button>
          )}

          {isLastPartInSkill && !hasNextSkill && (
            <Button
              variant="default"
              size="sm"
              className="h-9 rounded-sm px-3 text-xs font-semibold sm:px-4 sm:text-sm"
              disabled={busy}
              onClick={() => onConfirmSubmit?.()}
            >
              Submit test
            </Button>
          )}

          <Tooltip>
            <TooltipTrigger asChild>
              <Button
                variant="outline"
                size="icon"
                className="h-9 w-9 rounded-sm"
                disabled={busy || (atFirst && part.index === 0)}
                onClick={goBack}
                aria-label="Previous question"
              >
                <ChevronLeft className="h-4 w-4" />
              </Button>
            </TooltipTrigger>
            <TooltipContent>Previous question</TooltipContent>
          </Tooltip>

          <Tooltip>
            <TooltipTrigger asChild>
              <Button
                variant="outline"
                size="icon"
                className="h-9 w-9 rounded-sm"
                disabled={busy || (atLast && part.index >= (run.parts ?? []).length - 1 && !hasNextSkill)}
                onClick={goForward}
                aria-label="Next question"
              >
                <ChevronRight className="h-4 w-4" />
              </Button>
            </TooltipTrigger>
            <TooltipContent>Next question</TooltipContent>
          </Tooltip>
        </div>
      </div>
    </div>
  )
}
