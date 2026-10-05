import { useDraftText } from '@/hooks'

/// Writing Task 1 and Task 2: the prompt and chart on the left,
/// essay answer editor with live word count on the right.
export default function WritingPanel({
  part,
  busy,
  runCall,
  run,
}: {
  part: any
  busy?: boolean
  runCall: (method: string, args?: any) => Promise<any>
  run?: any
}) {
  // Typing only commits after a pause, so long essays never lag the window.
  const [essay, setEssay, flushEssay] = useDraftText(
    part.essay ?? '',
    part.index,
    (text) => runCall('exam.setEssay', { text })
  )

  return (
    <div className="flex flex-col gap-4">
      {/* Instruction block: colour edge separates it from the task itself. */}
      <div className="border border-exam-block-border border-l-4 border-l-exam-instruction-accent bg-exam-instruction p-4 select-none">
        <h2 className="text-base font-bold text-foreground">
          {part.instructionHeading || `Part ${part.skillPartNumber || part.index + 1}`}
        </h2>
        <p className="mt-0.5 text-sm text-foreground/80 leading-relaxed">
          {part.bannerInstruction || part.instructions || 'Write your response for this task in the answer area provided.'}
        </p>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-6 flex-1 min-h-0">
        {/* Left Column: Task Prompt, Chart / Image, Instructions */}
        <div className="overflow-y-auto pr-2 flex flex-col gap-3">
          <h3 className="text-base font-bold text-foreground">{part.title}</h3>
          {part.hasWritingTask1Image && part.hasWritingImageFile && (
            <figure className="mb-2">
              <img src={part.imageUrl} alt="Task 1 chart" className="max-w-full rounded-none border border-exam-block-border" />
              <figcaption className="mt-1 text-xs text-muted-foreground">{part.imageHint}</figcaption>
            </figure>
          )}
          {part.showMissingImageHint && (
            <div className="border border-warning/30 border-l-4 border-l-warning bg-warning/10 p-2.5 text-xs text-warning">
              {part.imageHint}
            </div>
          )}
          {part.material && (
            <div className="whitespace-pre-wrap border border-exam-block-border bg-exam-instruction p-3.5 text-sm leading-relaxed text-foreground">
              {part.material}
            </div>
          )}
          {part.instructions && (
            <p className="text-xs sm:text-sm text-muted-foreground leading-relaxed">{part.instructions}</p>
          )}
        </div>

        {/* Right Column: Writing Area & Live Word Counter */}
        <div className="flex flex-col h-full min-h-0">
          <div className="flex items-center justify-between mb-1.5">
            <span className="text-xs font-bold uppercase tracking-wider text-muted-foreground">Your response</span>
            <span className="text-xs font-semibold text-foreground tabular-nums">{part.wordCountLabel}</span>
          </div>
          <textarea
            className="flex-1 min-h-[360px] w-full resize-none rounded-none border border-input bg-exam-block p-3.5 align-top text-sm leading-relaxed focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
            value={essay}
            placeholder="Type your essay response here..."
            disabled={busy}
            onChange={(e) => setEssay(e.currentTarget.value)}
            onBlur={flushEssay}
          />
          <p className="mt-2 text-xs text-muted-foreground">
            Writing tasks are evaluated by AI against official IELTS band descriptors upon test submission.
          </p>
        </div>
      </div>
    </div>
  )
}
