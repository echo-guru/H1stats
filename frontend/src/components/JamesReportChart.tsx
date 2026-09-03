import { useId } from 'react'

export interface JamesChartPoint {
  key: string
  yearLabel: string
  quarterLabel: string
  echo: number
  seDse: number
  consults: number
}

const SERIES = [
  { key: 'echo' as const, label: 'Echo', color: '#33758F' },
  { key: 'seDse' as const, label: 'SE / DSE', color: '#69A8BC' },
  { key: 'consults' as const, label: 'Consults', color: '#C4A35A' },
]

function niceMax(value: number): number {
  if (value <= 0) return 10
  const exp = Math.floor(Math.log10(value))
  const mag = 10 ** exp
  const n = value / mag
  const nice = n <= 1 ? 1 : n <= 2 ? 2 : n <= 5 ? 5 : 10
  return nice * mag
}

function valueOf(p: JamesChartPoint, key: (typeof SERIES)[number]['key']): number {
  return p[key]
}

/** Grouped bar chart: Echo, SE/DSE, Consults across quarters (PDF-style timeline). */
export function JamesReportGroupedChart({
  points,
  title,
}: {
  points: JamesChartPoint[]
  title: string
}) {
  const gradId = `james-bar-${useId().replace(/:/g, '')}`
  const width = 720
  const height = 260
  const pad = { top: 28, right: 16, bottom: 48, left: 48 }
  const innerW = width - pad.left - pad.right
  const innerH = height - pad.top - pad.bottom
  const maxY = niceMax(
    Math.max(0, ...points.flatMap((p) => [p.echo, p.seDse, p.consults])),
  )
  const tickCount = 4
  const ticks = Array.from({ length: tickCount + 1 }, (_, i) => (maxY / tickCount) * i)
  const slot = points.length > 0 ? innerW / points.length : innerW
  const groupW = Math.min(slot * 0.78, 72)
  const barW = groupW / SERIES.length - 2

  return (
    <div>
      <div className="flex flex-wrap items-center justify-between gap-2 mb-2">
        <p className="text-sm font-medium text-brand-primary">{title}</p>
        <div className="flex flex-wrap gap-3 text-xs text-gray-600">
          {SERIES.map((s) => (
            <span key={s.key} className="inline-flex items-center gap-1.5">
              <span className="inline-block h-2.5 w-2.5 rounded-sm" style={{ background: s.color }} />
              {s.label}
            </span>
          ))}
        </div>
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
                {SERIES.map((s, si) => {
                  const v = valueOf(p, s.key)
                  const h = maxY > 0 ? (v / maxY) * innerH : 0
                  const x = groupX + si * (barW + 2)
                  const y = pad.top + innerH - h
                  return (
                    <g key={s.key}>
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
}: {
  sites: { siteName: string; echo: number; seDse: number; consults: number }[]
}) {
  const width = 720
  const height = 240
  const pad = { top: 28, right: 16, bottom: 40, left: 48 }
  const innerW = width - pad.left - pad.right
  const innerH = height - pad.top - pad.bottom
  const maxY = niceMax(
    Math.max(0, ...sites.flatMap((s) => [s.echo, s.seDse, s.consults])),
  )
  const tickCount = 4
  const ticks = Array.from({ length: tickCount + 1 }, (_, i) => (maxY / tickCount) * i)
  const slot = sites.length > 0 ? innerW / sites.length : innerW
  const groupW = Math.min(slot * 0.55, 100)
  const barW = groupW / SERIES.length - 3

  return (
    <div>
      <div className="flex flex-wrap items-center justify-between gap-2 mb-2">
        <p className="text-sm font-medium text-brand-primary">Year totals by site</p>
        <div className="flex flex-wrap gap-3 text-xs text-gray-600">
          {SERIES.map((s) => (
            <span key={s.key} className="inline-flex items-center gap-1.5">
              <span className="inline-block h-2.5 w-2.5 rounded-sm" style={{ background: s.color }} />
              {s.label}
            </span>
          ))}
        </div>
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
            const values = [site.echo, site.seDse, site.consults]
            return (
              <g key={site.siteName}>
                {SERIES.map((s, si) => {
                  const v = values[si] ?? 0
                  const h = maxY > 0 ? (v / maxY) * innerH : 0
                  const x = groupX + si * (barW + 3)
                  const y = pad.top + innerH - h
                  return (
                    <g key={s.key}>
                      <title>{`${site.siteName} · ${s.label}: ${v.toLocaleString()}`}</title>
                      <rect
                        x={x}
                        y={y}
                        width={barW}
                        height={Math.max(h, v > 0 ? 2 : 0)}
                        rx={2}
                        fill={s.color}
                      />
                      {v > 0 && (
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
