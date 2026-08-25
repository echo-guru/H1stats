import { useId } from 'react'

interface MonthPoint {
  yearMonth: string
  label: string
  total: number
}

interface MonthTotalChartProps {
  months: MonthPoint[]
  title?: string
  yAxisLabel?: string
  compact?: boolean
  framed?: boolean
  variant?: 'bar' | 'line'
  selectedKey?: string | null
  onSelect?: (yearMonth: string) => void
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

export function MonthTotalChart({
  months,
  title = 'Referrals by month',
  yAxisLabel = 'Total',
  compact = false,
  framed = true,
  variant = 'bar',
  selectedKey = null,
  onSelect,
}: MonthTotalChartProps) {
  const gradId = `trend-fill-${useId().replace(/:/g, '')}`
  const width = 760
  const height = compact ? 216 : 300
  const pad = compact
    ? { top: onSelect ? 28 : 22, right: 16, bottom: 44, left: 44 }
    : { top: onSelect ? 34 : 28, right: 20, bottom: 52, left: 52 }
  const innerW = width - pad.left - pad.right
  const innerH = height - pad.top - pad.bottom
  const maxY = niceMax(Math.max(0, ...months.map((m) => m.total)))
  const tickCount = 4
  const ticks = Array.from({ length: tickCount + 1 }, (_, i) => (maxY / tickCount) * i)
  const slot = months.length > 0 ? innerW / months.length : innerW
  const barW = Math.min(36, slot * 0.55)
  const points = months.map((m, i) => {
    const x = pad.left + slot * i + slot / 2
    const y = pad.top + innerH - (maxY > 0 ? (m.total / maxY) * innerH : 0)
    return { x, y, m }
  })
  const linePath = points.map((p, i) => `${i === 0 ? 'M' : 'L'} ${p.x.toFixed(1)} ${p.y.toFixed(1)}`).join(' ')
  const areaPath =
    points.length === 0
      ? ''
      : `${linePath} L ${points[points.length - 1].x.toFixed(1)} ${pad.top + innerH} L ${points[0].x.toFixed(1)} ${pad.top + innerH} Z`

  const chart = (
    <>
      {title ? <p className={`font-medium text-brand-primary ${compact ? 'text-xs mb-1' : 'text-sm mb-2'}`}>{title}</p> : null}
      <div className="overflow-x-auto">
        <svg
          viewBox={`0 0 ${width} ${height}`}
          className={`w-full h-auto ${compact ? 'min-w-[520px]' : 'min-w-[640px]'}`}
          role={onSelect ? 'group' : 'img'}
          aria-label={title || 'Monthly totals'}
        >
          <defs>
            <linearGradient id={gradId} x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor="#33758F" stopOpacity="0.28" />
              <stop offset="100%" stopColor="#33758F" stopOpacity="0.02" />
            </linearGradient>
          </defs>
          <text
            x={compact ? 12 : 14}
            y={pad.top + innerH / 2}
            textAnchor="middle"
            fill="#33758F"
            fontSize={compact ? '11' : '12'}
            transform={`rotate(-90 ${compact ? 12 : 14} ${pad.top + innerH / 2})`}
          >
            {yAxisLabel}
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
                <text x={pad.left - 8} y={y + 4} textAnchor="end" fill="#6b7280" fontSize={compact ? '10' : '11'}>
                  {tick.toLocaleString()}
                </text>
              </g>
            )
          })}

          {variant === 'line' && points.length > 0 && (
            <>
              <path d={areaPath} fill={`url(#${gradId})`} />
              <path
                d={linePath}
                fill="none"
                stroke="#33758F"
                strokeWidth={2.4}
                strokeLinejoin="round"
                strokeLinecap="round"
              />
            </>
          )}

          {variant === 'bar'
            ? months.map((m, i) => {
                const x = pad.left + slot * i + (slot - barW) / 2
                const h = maxY > 0 ? (m.total / maxY) * innerH : 0
                const y = pad.top + innerH - h
                const { month, year } = shortMonthLabel(m.label)
                const cx = x + barW / 2
                const selected = selectedKey === m.yearMonth
                const clickable = Boolean(onSelect) && m.total > 0
                return (
                  <g
                    key={m.yearMonth}
                    onClick={clickable ? () => onSelect?.(m.yearMonth) : undefined}
                    style={clickable ? { cursor: 'pointer' } : undefined}
                    role={clickable ? 'button' : undefined}
                    tabIndex={clickable ? 0 : undefined}
                    onKeyDown={
                      clickable
                        ? (e) => {
                            if (e.key === 'Enter' || e.key === ' ') {
                              e.preventDefault()
                              onSelect?.(m.yearMonth)
                            }
                          }
                        : undefined
                    }
                  >
                    <title>
                      {clickable
                        ? `${m.label}: ${m.total.toLocaleString()} — click to show tests`
                        : `${m.label}: ${m.total.toLocaleString()}`}
                    </title>
                    {clickable && (
                      <rect
                        x={pad.left + slot * i}
                        y={pad.top}
                        width={slot}
                        height={innerH}
                        fill={selected ? '#33758F' : 'transparent'}
                        opacity={selected ? 0.08 : 1}
                      />
                    )}
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
                        textDecoration={clickable ? 'underline' : undefined}
                        fontWeight={selected ? 700 : undefined}
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
              })
            : points.map((p, i) => {
                const { month, year } = shortMonthLabel(p.m.label)
                const selected = selectedKey === p.m.yearMonth
                const clickable = Boolean(onSelect) && p.m.total > 0
                return (
                  <g
                    key={p.m.yearMonth}
                    onClick={clickable ? () => onSelect?.(p.m.yearMonth) : undefined}
                    style={clickable ? { cursor: 'pointer' } : undefined}
                    role={clickable ? 'button' : undefined}
                    tabIndex={clickable ? 0 : undefined}
                    onKeyDown={
                      clickable
                        ? (e) => {
                            if (e.key === 'Enter' || e.key === ' ') {
                              e.preventDefault()
                              onSelect?.(p.m.yearMonth)
                            }
                          }
                        : undefined
                    }
                  >
                    <title>
                      {clickable
                        ? `${p.m.label}: ${p.m.total.toLocaleString()} — click to show tests`
                        : `${p.m.label}: ${p.m.total.toLocaleString()}`}
                    </title>
                    {clickable && (
                      <rect
                        x={pad.left + slot * i}
                        y={pad.top}
                        width={slot}
                        height={innerH}
                        fill={selected ? '#33758F' : 'transparent'}
                        opacity={selected ? 0.08 : 1}
                      />
                    )}
                    <circle
                      cx={p.x}
                      cy={p.y}
                      r={selected ? 5.4 : 4.2}
                      fill={selected ? '#33758F' : '#fff'}
                      stroke="#33758F"
                      strokeWidth={2}
                    />
                    {p.m.total > 0 && (
                      <text
                        x={p.x}
                        y={p.y - 10}
                        textAnchor="middle"
                        fill="#33758F"
                        fontSize={compact ? '10' : '11'}
                        fontFamily="ui-monospace, monospace"
                        textDecoration={clickable ? 'underline' : undefined}
                        fontWeight={selected ? 700 : undefined}
                      >
                        {p.m.total.toLocaleString()}
                      </text>
                    )}
                    <text x={p.x} y={height - 26} textAnchor="middle" fill="#374151" fontSize={compact ? '10' : '11'}>
                      {month}
                    </text>
                    <text x={p.x} y={height - 12} textAnchor="middle" fill="#6b7280" fontSize="10">
                      {year}
                    </text>
                  </g>
                )
              })}
        </svg>
      </div>
    </>
  )

  if (!framed) return <div>{chart}</div>
  return <div className="bg-white rounded-lg border border-brand-primary-border p-4">{chart}</div>
}
