import { cn } from '@/lib/utils'

export default function ActivityBars({ days = [] }) {
  const max = Math.max(1, ...days.map((d) => d.count))

  return (
    <div className="flex flex-col gap-3" role="img" aria-label="Study activity for the last 7 days">
      <div className="flex h-36 items-end gap-3 pt-4">
        {days.map((day, i) => {
          const ratio = day.count / max
          const heightPercent = day.count === 0 ? 6 : Math.max(14, Math.round(ratio * 88))
          const isHighest = day.count > 0 && day.count === max

          return (
            <div key={i} className="group relative flex flex-1 flex-col items-center justify-end h-full">
              {/* Count tooltip on hover */}
              <div
                className={cn(
                  'mb-1.5 min-h-[18px] text-xs font-semibold tabular-nums transition-opacity duration-150',
                  day.count > 0 ? 'text-foreground' : 'text-transparent group-hover:text-muted-foreground'
                )}
              >
                {day.count > 0 ? day.count : '0'}
              </div>

              {/* Bar track and fill */}
              <div className="relative flex w-full max-w-[36px] flex-col justify-end overflow-hidden rounded-t-md bg-muted/60 h-28">
                <div
                  className={cn(
                    'w-full rounded-t-md transition-all duration-300',
                    day.count === 0
                      ? 'bg-muted-foreground/20'
                      : isHighest
                        ? 'bg-primary shadow-sm'
                        : 'bg-primary/75 group-hover:bg-primary'
                  )}
                  style={{ height: `${heightPercent}%` }}
                />
              </div>

              {/* Day label */}
              <span className="mt-2 text-xs font-medium text-muted-foreground transition-colors group-hover:text-foreground">
                {day.label}
              </span>
            </div>
          )
        })}
      </div>
      <div className="flex items-center justify-between border-t border-border/50 pt-2 text-xs text-muted-foreground">
        <span>Active days count toward your study streak</span>
        <span className="font-medium text-foreground">
          {days.reduce((acc, d) => acc + (d.count > 0 ? 1 : 0), 0)} of 7 days active
        </span>
      </div>
    </div>
  )
}

