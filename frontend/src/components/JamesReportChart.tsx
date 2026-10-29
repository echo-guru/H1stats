import { useId } from 'react'

export interface JamesChartPoint {
  key: string
  yearLabel: string
  quarterLabel: string
  counts: number[]
}

export interface JamesChartSeries {
  label: string
  color: string
}

function niceMax(value: number): number {
  if (value <= 0) return 10
  const exp = Math.floor(Math.log10(value))
  const mag = 10 ** exp
  const n = value / mag
  const nice = n <= 1 ? 1 : n <= 2 ? 2 : n <= 5 ? 5 : 10
  return nice * mag
}

function Legend({ series }: { series: JamesChartSeries[] }) {
  return (
    <div className="flex flex-wrap gap-3 text-xs text-gray-600">
      {series.map((s) => (
        <span key={s.label} className="inline-flex items-center gap-1.5">
          <span className="inline-block h-2.5 w-2.5 rounded-sm" style={{ background: s.color }} />
          {s.label}
        </span>
      ))}
    </div>
  )
}

/** Grouped bar chart: one series per selected test, across quarters. */
export function JamesReportGroupedChart({
  points,
  series,
  title,
}: {
  points: JamesChartPoint[]
  series: JamesChartSeries[]
  title: string
}) {
  const gradId = `james-bar-${useId().replace(/:/g, '')}`
  const seriesCount = Math.max(series.length, 1)
  const width = Math.max(720, points.length * Math.max(36, seriesCount * 10))
  const height = 260
  const pad = { top: 28, right: 16, bottom: 48, left: 48 }
  const innerW = width - pad.left - pad.right
  const innerH = height - pad.top - pad.bottom
  const maxY = niceMax(
    Math.max(0, ...points.flatMap((p) => series.map((_, i) => p.counts[i] ?? 0))),
  )
  const tickCount = 4
  const ticks = Array.from({ length: tickCount + 1 }, (_, i) => (maxY / tickCount) * i)
  const slot = points.length > 0 ? innerW / points.length : innerW
  const groupW = Math.min(slot * 0.78, Math.max(28, seriesCount * 14))
  const barGap = seriesCount > 6 ? 1 : 2
  const barW = Math.max((groupW - barGap * (seriesCount - 1)) / seriesCount, 1)

  return (
    <div>
      <div className="flex flex-wrap items-center justify-between gap-2 mb-2">
        <p className="text-sm font-medium text-brand-primary">{title}</p>
        <Legend series={series} />
      </div>
      <div className="overflow-x-auto">
        <svg
          viewBox={`0 0 ${width} ${height}`}
          className="w-full h-auto min-w-[560px]"
          role="img"
          aria-label={title}
        >
          <defs>
            <linearGradient id={gradId} x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor="#33758F" stopOpacity="0.12" />
              <stop offset="100%" stopColor="#33758F" stopOpacity="0" />
            </linearGradient>
          </defs>
          <rect
            x={pad.left}
            y={pad.top}
            width={innerW}
            height={innerH}
            fill={`url(#${gradId})`}
          />
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
                <text x={pad.left - 8} y={y + 4} textAnchor="end" fill="#6b7280" fontSize="10">
                  {tick.toLocaleString()}
                </text>
              </g>
            )
          })}
          {points.map((p, i) => {
            const groupX = pad.left + slot * i + (slot - groupW) / 2
            const cx = pad.left + slot * i + slot / 2
            return (
              <g key={p.key}>
                {series.map((s, si) => {
                  const v = p.counts[si] ?? 0
                  const h = maxY > 0 ? (v / maxY) * innerH : 0
                  const x = groupX + si * (barW + barGap)
                  const y = pad.top + innerH - h
                  return (
                    <g key={`${s.label}-${si}`}>
                      <title>{`${s.label} · ${p.yearLabel} ${p.quarterLabel}: ${v.toLocaleString()}`}</title>
                      <rect
                        x={x}
                        y={y}
                        width={barW}
                        height={Math.max(h, v > 0 ? 2 : 0)}
                        rx={2}
                        fill={s.color}
                      />
                    </g>
                  )
                })}
                <text x={cx} y={height - 28} textAnchor="middle" fill="#374151" fontSize="11">
                  {p.quarterLabel}
                </text>
                <text x={cx} y={height - 14} textAnchor="middle" fill="#6b7280" fontSize="10">
                  {p.yearLabel}
                </text>
              </g>
            )
          })}
        </svg>
      </div>
    </div>
  )
}

/** Side-by-side year totals across the three campuses. */
export function JamesReportSiteCompareChart({
  sites,
  series,
}: {
  sites: { siteName: string; counts: number[] }[]
  series: JamesChartSeries[]
}) {
  const seriesCount = Math.max(series.length, 1)
  const width = Math.max(720, sites.length * Math.max(120, seriesCount * 28))
  const height = 240
  const pad = { top: 28, right: 16, bottom: 40, left: 48 }
  const innerW = width - pad.left - pad.right
  const innerH = height - pad.top - pad.bottom
  const maxY = niceMax(
    Math.max(0, ...sites.flatMap((s) => series.map((_, i) => s.counts[i] ?? 0))),
  )
  const tickCount = 4
  const ticks = Array.from({ length: tickCount + 1 }, (_, i) => (maxY / tickCount) * i)
  const slot = sites.length > 0 ? innerW / sites.length : innerW
  const groupW = Math.min(slot * 0.7, Math.max(48, seriesCount * 18))
  const barGap = seriesCount > 6 ? 1 : 3
  const barW = Math.max((groupW - barGap * (seriesCount - 1)) / seriesCount, 1)

  return (
    <div>
      <div className="flex flex-wrap items-center justify-between gap-2 mb-2">
        <p className="text-sm font-medium text-brand-primary">Year totals by site</p>
        <Legend series={series} />
      </div>
      <div className="overflow-x-auto">
        <svg
          viewBox={`0 0 ${width} ${height}`}
          className="w-full h-auto min-w-[480px]"
          role="img"
          aria-label="Year totals by site"
        >
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
                <text x={pad.left - 8} y={y + 4} textAnchor="end" fill="#6b7280" fontSize="10">
                  {tick.toLocaleString()}
                </text>
              </g>
            )
          })}
          {sites.map((site, i) => {
            const groupX = pad.left + slot * i + (slot - groupW) / 2
            const cx = pad.left + slot * i + slot / 2
            return (
              <g key={site.siteName}>
                {series.map((s, si) => {
                  const v = site.counts[si] ?? 0
                  const h = maxY > 0 ? (v / maxY) * innerH : 0
                  const x = groupX + si * (barW + barGap)
                  const y = pad.top + innerH - h
                  return (
                    <g key={`${s.label}-${si}`}>
                      <title>{`${site.siteName} · ${s.label}: ${v.toLocaleString()}`}</title>
                      <rect
                        x={x}
                        y={y}
                        width={barW}
                        height={Math.max(h, v > 0 ? 2 : 0)}
                        rx={2}
                        fill={s.color}
                      />
                      {v > 0 && barW >= 10 && (
                        <text
                          x={x + barW / 2}
                          y={y - 6}
                          textAnchor="middle"
                          fill={s.color}
                          fontSize="9"
                          fontFamily="ui-monospace, monospace"
                        >
                          {v.toLocaleString()}
                        </text>
                      )}
                    </g>
                  )
                })}
                <text x={cx} y={height - 14} textAnchor="middle" fill="#374151" fontSize="12">
                  {site.siteName}
                </text>
              </g>
            )
          })}
        </svg>
      </div>
    </div>
  )
}
