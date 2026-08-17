interface MonthPoint {
  yearMonth: string
  label: string
  total: number
}

interface MonthTotalChartProps {
  months: MonthPoint[]
}

function niceMax(value: number): number {
  if (value <= 0) return 10
  const exp = Math.floor(Math.log10(value))
  const mag = 10 ** exp
  const n = value / mag
  const nice = n <= 1 ? 1 : n <= 2 ? 2 : n <= 5 ? 5 : 10
  return nice * mag
}

function shortMonthLabel(label: string): { month: string; year: string } {
  const parts = label.trim().split(/\s+/)
  return {
    month: parts[0] ?? label,
    year: parts[1] ? parts[1].slice(-2) : '',
  }
}

export function MonthTotalChart({ months }: MonthTotalChartProps) {
  const width = 760
  const height = 300
  const pad = { top: 28, right: 20, bottom: 52, left: 52 }
  const innerW = width - pad.left - pad.right
  const innerH = height - pad.top - pad.bottom
  const maxY = niceMax(Math.max(0, ...months.map((m) => m.total)))
  const tickCount = 4
  const ticks = Array.from({ length: tickCount + 1 }, (_, i) => (maxY / tickCount) * i)
  const slot = months.length > 0 ? innerW / months.length : innerW
  const barW = Math.min(36, slot * 0.55)

  return (
    <div className="bg-white rounded-lg border border-brand-primary-border p-4">
      <p className="text-sm font-medium text-brand-primary mb-2">Referrals by month</p>
      <div className="overflow-x-auto">
        <svg
          viewBox={`0 0 ${width} ${height}`}
          className="w-full min-w-[640px] h-auto"
          role="img"
          aria-label="Total referrals for each of the past 12 months"
        >
          <text
            x={14}
            y={pad.top + innerH / 2}
            textAnchor="middle"
            fill="#33758F"
            fontSize="12"
            transform={`rotate(-90 14 ${pad.top + innerH / 2})`}
          >
            Total
          </text>

          {ticks.map((tick) => {
            const y = pad.top + innerH - (tick / maxY) * innerH
            return (
              <g key={tick}>
                <line
                  x1={pad.left}
                  x2={pad.left + innerW}
                  y1={y}
                  y2={y}
                  stroke={tick === 0 ? '#a8c9d9' : '#e8f1f5'}
                  strokeWidth={tick === 0 ? 1.5 : 1}
                />
                <text x={pad.left - 8} y={y + 4} textAnchor="end" fill="#6b7280" fontSize="11">
                  {tick.toLocaleString()}
                </text>
              </g>
            )
          })}

          {months.map((m, i) => {
            const x = pad.left + slot * i + (slot - barW) / 2
            const h = maxY > 0 ? (m.total / maxY) * innerH : 0
            const y = pad.top + innerH - h
            const { month, year } = shortMonthLabel(m.label)
            const cx = x + barW / 2
            return (
              <g key={m.yearMonth}>
                <title>{`${m.label}: ${m.total.toLocaleString()}`}</title>
                <rect
                  x={x}
                  y={y}
                  width={barW}
                  height={Math.max(h, m.total > 0 ? 2 : 0)}
                  rx={3}
                  fill="#33758F"
                />
                {m.total > 0 && (
                  <text
                    x={cx}
                    y={y - 6}
                    textAnchor="middle"
                    fill="#33758F"
                    fontSize="11"
                    fontFamily="ui-monospace, monospace"
                  >
                    {m.total.toLocaleString()}
                  </text>
                )}
                <text x={cx} y={height - 28} textAnchor="middle" fill="#374151" fontSize="11">
                  {month}
                </text>
                <text x={cx} y={height - 14} textAnchor="middle" fill="#6b7280" fontSize="10">
                  {year}
                </text>
              </g>
            )
          })}
        </svg>
      </div>
    </div>
  )
}
