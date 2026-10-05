import { ChevronLeft, ChevronRight } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { cn } from '@/lib/utils'

export default function ExamBottomBar({ exam, busy, runCall, onConfirmSubmit }: {
  exam: any
  busy?: boolean
  runCall: (method: string, args?: any) => Promise<any>
  onConfirmSubmit?: () => void
}) {
  const run = exam?.run
  const part = run?.currentPart
  if (!part) return null

  const questions = part.questions ?? []
  const atFirst = (part.focusedIndex ?? 0) <= 0
  const atLast = (part.focusedIndex ?? 0) >= questions.length - 1

  const currentSkill = part.skill
  const skillParts = (run.parts ?? []).filter(
    (p: any) => p.skill?.toLowerCase() === currentSkill?.toLowerCase()
  )
  const displayParts = skillParts.length > 0 ? skillParts : (run.parts ?? [])

  const isLastPartInSkill =
    skillParts.length > 0 && skillParts[skillParts.length - 1].index === part.index
  const hasNextSkill = run.hasNextSkill ?? (run.parts ?? []).some(
    (p: any) => p.index > part.index && p.skill?.toLowerCase() !== currentSkill?.toLowerCase()
  )
  const nextSkillName =
    run.nextSkillName ||
    (run.parts ?? []).find(
      (p: any) => p.index > part.index && p.skill?.toLowerCase() !== currentSkill?.toLowerCase()
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
    <footer className="shrink-0 border-t border-exam-block-border bg-exam-bar text-exam-bar-foreground select-none">
      <div className="flex min-h-14 items-stretch justify-between gap-3 px-4 sm:px-6">
        {/* Parts and questions strip, the way the official test splits a
            section into numbered parts with a live question list. */}
        <div
          className="flex min-w-0 flex-1 items-stretch gap-6 overflow-x-auto sm:gap-10"
          role="tablist"
          aria-label="Skill parts"
        >
          {displayParts.map((p: any) => {
            const isCurrent = p.index === part.index
            const partNum = p.skillPartNumber || p.index + 1
            const pQuestions = p.questions ?? []

            if (isCurrent) {
              return (
                <div
                  key={p.index}
                  role="tab"
                  aria-selected={true}
                  className="relative flex shrink-0 items-center gap-3 border-t-[3px] border-exam-instruction-accent pt-3 pb-2 sm:gap-4"
                >
                  <span className="text-sm font-bold text-foreground shrink-0">
                    Part {partNum}
                  </span>

                  {pQuestions.length > 0 && (
                    <div className="flex shrink-0 items-center gap-1">
                      {pQuestions.map((q: any, i: number) => {
                        const isFocused = i === (part.focusedIndex ?? 0)
                        const answered = q.isAnswered
                        const flagged = q.isFlagged
                        return (
                          <button
                            key={q.number}
                            type="button"
                            aria-label={`Question ${q.number}`}
                            aria-current={isFocused ? 'true' : undefined}
                            disabled={busy}
                            className={cn(
                              'flex h-6 min-w-6 items-center justify-center border-b-2 px-1.5 text-sm tabular-nums transition-colors cursor-pointer',
                              isFocused
                                ? 'border-foreground font-bold text-foreground'
                                : answered
                                  ? 'border-exam-bar-foreground/50 font-medium text-foreground hover:border-foreground'
                                  : 'border-transparent text-muted-foreground hover:text-foreground',
                              flagged && 'bg-warning/25 font-semibold text-warning'
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
                className="flex shrink-0 flex-col items-start justify-center gap-0.5 text-sm font-medium text-muted-foreground transition-colors hover:text-foreground cursor-pointer py-1"
              >
                <span>Part {partNum}</span>
                <span className="text-xs tabular-nums text-muted-foreground">
                  {p.answeredCount ?? 0}/{p.scoredCount ?? pQuestions.length}
                </span>
              </button>
            )
          })}
        </div>

        {/* Section completion actions and question navigation */}
        <div className="flex shrink-0 items-center gap-2 py-2">
          {isLastPartInSkill && hasNextSkill && (
            <Button
              variant="default"
              size="sm"
              className="h-9 rounded-none px-3 text-xs font-semibold sm:px-4 sm:text-sm"
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
              className="h-9 rounded-none px-3 text-xs font-semibold sm:px-4 sm:text-sm"
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
                className="h-9 w-9 rounded-none"
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
                className="h-9 w-9 rounded-none"
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
    </footer>
  )
}
