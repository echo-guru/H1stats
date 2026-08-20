import { useId } from 'react'

export interface Cm2VolumeMonth {
  yearMonth: string
  label: string
  echo: number
  stress: number
  tee: number
  consults: number
}

export const CM2_VOLUME_SERIES = [
  { key: 'echo', label: 'Echo', color: '#33758F' },
  { key: 'stress', label: 'Stress echo / Dob', color: '#69A8BC' },
  { key: 'tee', label: 'TEE / IOE', color: '#A63746' },
  { key: 'consults', label: 'Consults', color: '#C4A35A' },
] as const

type SeriesKey = (typeof CM2_VOLUME_SERIES)[number]['key']

function seriesValue(month: Cm2VolumeMonth, key: SeriesKey): number {
  return month[key]
}

export function monthTotal(month: Cm2VolumeMonth): number {
  return month.echo + month.stress + month.tee + month.consults
}

export function volumeMonthsFromDrSummary(
  columns: { id: string; initials: string; displayName: string }[],
  rows: { key: string; counts: number[] }[],
): Cm2VolumeMonth[] {
  const byKey = new Map(rows.map((row) => [row.key, row.counts]))
  return columns.map((column, i) => {
    const echo = byKey.get('echo-total')?.[i] ?? 0
    const stress = (byKey.get('se-total')?.[i] ?? 0) + (byKey.get('dse-total')?.[i] ?? 0)
    const tee = (byKey.get('tee')?.[i] ?? 0) + (byKey.get('ioe')?.[i] ?? 0)
    const consults = byKey.get('consults')?.[i] ?? 0
    return {
      yearMonth: column.id,
      label: column.displayName || column.initials,
      echo,
      stress,
      tee,
      consults,
    }
  })
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

function LinePanel({
  title,
  color,
  values,
  months,
  wide,
}: {
  title: string
  color: string
  values: number[]
  months: Cm2VolumeMonth[]
  wide?: boolean
}) {
  const gradId = `cm2-line-${useId().replace(/:/g, '')}`
  const width = 760
  const height = wide ? 240 : 200
  const pad = { top: 22, right: 16, bottom: 44, left: 44 }
  const innerW = width - pad.left - pad.right
  const innerH = height - pad.top - pad.bottom
  const maxY = niceMax(Math.max(0, ...values))
  const tickCount = 4
  const ticks = Array.from({ length: tickCount + 1 }, (_, i) => (maxY / tickCount) * i)
  const slot = months.length > 0 ? innerW / months.length : innerW
  const points = months.map((m, i) => {
    const total = values[i] ?? 0
    const x = pad.left + slot * i + slot / 2
    const y = pad.top + innerH - (maxY > 0 ? (total / maxY) * innerH : 0)
    return { x, y, total, m }
  })
  const linePath = points.map((p, i) => `${i === 0 ? 'M' : 'L'} ${p.x.toFixed(1)} ${p.y.toFixed(1)}`).join(' ')
  const areaPath =
    points.length === 0
      ? ''
      : `${linePath} L ${points[points.length - 1].x.toFixed(1)} ${pad.top + innerH} L ${points[0].x.toFixed(1)} ${pad.top + innerH} Z`

  return (
    <div>
      <p className="text-xs font-medium mb-1" style={{ color }}>
        {title}
      </p>
      <div className="overflow-x-auto">
        <svg
          viewBox={`0 0 ${width} ${height}`}
          className="w-full h-auto min-w-0"
          role="img"
          aria-label={`${title} over 12 months`}
        >
          <defs>
            <linearGradient id={gradId} x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor={color} stopOpacity="0.28" />
              <stop offset="100%" stopColor={color} stopOpacity="0.02" />
            </linearGradient>
          </defs>
          <text
            x="12"
            y={pad.top + innerH / 2}
            textAnchor="middle"
            fill={color}
            fontSize="11"
            transform={`rotate(-90 12 ${pad.top + innerH / 2})`}
          >
            Tests
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
                <text x={pad.left - 8} y={y + 4} textAnchor="end" fill="#6b7280" fontSize="10">
                  {tick.toLocaleString()}
                </text>
              </g>
            )
          })}
          {points.length > 0 && (
            <>
              <path d={areaPath} fill={`url(#${gradId})`} />
              <path
                d={linePath}
                fill="none"
                stroke={color}
                strokeWidth={2.4}
                strokeLinejoin="round"
                strokeLinecap="round"
              />
            </>
          )}
          {points.map((p) => {
            const { month, year } = shortMonthLabel(p.m.label)
            return (
              <g key={p.m.yearMonth}>
                <title>{`${title} · ${p.m.label}: ${p.total.toLocaleString()}`}</title>
                <circle cx={p.x} cy={p.y} r={4} fill="#fff" stroke={color} strokeWidth={2} />
                {p.total > 0 && (
                  <text
                    x={p.x}
                    y={p.y - 10}
                    textAnchor="middle"
                    fill={color}
                    fontSize="10"
                    fontFamily="ui-monospace, monospace"
                  >
                    {p.total.toLocaleString()}
                  </text>
                )}
                <text x={p.x} y={height - 26} textAnchor="middle" fill="#374151" fontSize="10">
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
    </div>
  )
}

export function Cm2VolumeTrendChart({ months }: { months: Cm2VolumeMonth[] }) {
  return (
    <div className="space-y-4">
      <LinePanel
        title="Total"
        color="#1f4f61"
        wide
        months={months}
        values={months.map(monthTotal)}
      />
      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {CM2_VOLUME_SERIES.map((series) => (
          <div key={series.key} className="rounded-lg border border-brand-primary-border/70 p-3">
            <LinePanel
              title={series.label}
              color={series.color}
              months={months}
              values={months.map((m) => seriesValue(m, series.key))}
            />
          </div>
        ))}
      </div>
    </div>
  )
}
