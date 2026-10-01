export default function ActivityBars({ days }) {
  const max = Math.max(1, ...days.map((d) => d.count))

  return (
    <div className="bars">
      {days.map((day, i) => {
        const height = day.count === 0 ? 4 : Math.round((day.count / max) * 110) + 8
        return (
          <div className="bar-col" key={i}>
            <div className="bar-count">{day.count > 0 ? day.count : ''}</div>
            <div
              className={`bar${day.count === 0 ? ' zero' : ''}`}
              style={{ height }}
            />
            <div className="bar-label">{day.label}</div>
          </div>
        )
      })}
    </div>
  )
}
