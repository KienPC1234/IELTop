// A sleek band trend chart: draws points and confidence range as SVG.
// Colors follow theme tokens for light and dark support.
export default function BandTrend({ points = [] }) {
  const width = 640
  const height = 180
  const padX = 40
  const padY = 24

  const values = points.flatMap((p) => [p.low, p.high])
  const min = Math.min(...values, 4.0)
  const max = Math.max(...values, 9.0)

  const innerW = width - padX * 2
  const innerH = height - padY * 2

  const x = (i) =>
    points.length === 1 ? width / 2 : padX + (i / (points.length - 1)) * innerW
  const y = (v) => padY + innerH - ((v - min) / (max - min || 1)) * innerH

  const mid = points.map((p) => (p.low + p.high) / 2)
  const line = mid.map((v, i) => `${i === 0 ? 'M' : 'L'} ${x(i)} ${y(v)}`).join(' ')

  const top = points.map((p, i) => `${i === 0 ? 'M' : 'L'} ${x(i)} ${y(p.high)}`).join(' ')
  const bottom = [...points]
    .reverse()
    .map((p, i) => `L ${x(points.length - 1 - i)} ${y(p.low)}`)
    .join(' ')
  const area = `${top} ${bottom} Z`

  const gridSteps = [4.0, 5.0, 6.0, 7.0, 8.0, 9.0].filter((v) => v >= min - 0.5 && v <= max + 0.5)

  return (
    <div className="flex flex-col gap-2">
      <svg className="h-[180px] w-full select-none" viewBox={`0 0 ${width} ${height}`} preserveAspectRatio="xMidYMid meet">
        <defs>
          <linearGradient id="bandGradient" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="var(--primary)" stopOpacity="0.25" />
            <stop offset="100%" stopColor="var(--primary)" stopOpacity="0.02" />
          </linearGradient>
        </defs>

        {/* Horizontal grid lines */}
        {gridSteps.map((v) => (
          <g key={v}>
            <line
              x1={padX}
              x2={width - padX}
              y1={y(v)}
              y2={y(v)}
              stroke="currentColor"
              strokeOpacity="0.1"
              strokeDasharray="4 4"
            />
            <text x={padX - 8} y={y(v) + 4} fontSize="11" fill="currentColor" fillOpacity="0.5" textAnchor="end" className="tabular-nums">
              {v.toFixed(1)}
            </text>
          </g>
        ))}

        {/* Confidence band shading */}
        <path d={area} fill="url(#bandGradient)" />

        {/* Trend line */}
        <path
          d={line}
          fill="none"
          stroke="var(--primary)"
          strokeWidth="2.5"
          strokeLinecap="round"
          strokeLinejoin="round"
        />

        {/* Data points */}
        {points.map((p, i) => {
          const cy = y((p.low + p.high) / 2)
          const cx = x(i)
          return (
            <g key={i} className="group">
              <circle
                cx={cx}
                cy={cy}
                r="5"
                fill="var(--background)"
                stroke="var(--primary)"
                strokeWidth="2.5"
              />
              <circle
                cx={cx}
                cy={cy}
                r="2"
                fill="var(--primary)"
              />
              <text
                x={cx}
                y={height - 4}
                fontSize="11"
                fill="currentColor"
                fillOpacity="0.65"
                textAnchor="middle"
                className="font-medium"
              >
                {p.label}
              </text>
            </g>
          )
        })}
      </svg>
      <div className="flex items-center justify-between border-t border-border/50 pt-2 text-xs text-muted-foreground">
        <span>Target score: Band 7.5+</span>
        <span>Latest band estimate: <strong className="text-foreground font-semibold">{points[points.length - 1]?.label || 'None'}</strong></span>
      </div>
    </div>
  )
}

