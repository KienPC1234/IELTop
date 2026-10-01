// A small band trend line: one point per finished test, drawn as an SVG so
// there is no chart dependency to ship.
export default function BandTrend({ points }) {
  const width = 640
  const height = 170
  const padX = 36
  const padY = 22

  const values = points.flatMap((p) => [p.low, p.high])
  const min = Math.min(...values, 4)
  const max = Math.max(...values, 9)

  const innerW = width - padX * 2
  const innerH = height - padY * 2

  const x = (i) =>
    points.length === 1
      ? width / 2
      : padX + (i / (points.length - 1)) * innerW
  const y = (v) => padY + innerH - ((v - min) / (max - min || 1)) * innerH

  const mid = points.map((p) => (p.low + p.high) / 2)
  const line = mid.map((v, i) => `${i === 0 ? 'M' : 'L'} ${x(i)} ${y(v)}`).join(' ')

  // Shade the band range as a soft area between low and high.
  const top = points.map((p, i) => `${i === 0 ? 'M' : 'L'} ${x(i)} ${y(p.high)}`).join(' ')
  const bottom = [...points]
    .reverse()
    .map((p, i) => `L ${x(points.length - 1 - i)} ${y(p.low)}`)
    .join(' ')
  const area = `${top} ${bottom} Z`

  const gridLines = [min, (min + max) / 2, max]

  return (
    <svg className="trend" viewBox={`0 0 ${width} ${height}`} preserveAspectRatio="xMidYMid meet">
      {gridLines.map((v, i) => (
        <g key={i}>
          <line
            x1={padX}
            x2={width - padX}
            y1={y(v)}
            y2={y(v)}
            stroke="#e3e6ea"
            strokeWidth="1"
          />
          <text x={6} y={y(v) + 4} fontSize="11" fill="#6b7280">
            {v.toFixed(1)}
          </text>
        </g>
      ))}

      <path d={area} fill="#b91c1c" opacity="0.12" />
      <path d={line} fill="none" stroke="#b91c1c" strokeWidth="2.5" />

      {points.map((p, i) => (
        <g key={i}>
          <circle cx={x(i)} cy={y((p.low + p.high) / 2)} r="3.5" fill="#b91c1c" />
          <text x={x(i)} y={height - 4} fontSize="11" fill="#6b7280" textAnchor="middle">
            {p.label}
          </text>
        </g>
      ))}
    </svg>
  )
}
