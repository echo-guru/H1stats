interface PieSlice {
  key: string
  label: string
  color: string
  value: number
}

interface DoctorPie {
  id: string
  initials: string
  displayName: string
  slices: PieSlice[]
  total: number
}

const SERIES: { key: string; label: string; color: string }[] = [
  { key: 'echo-total', label: 'Echo', color: '#33758F' },
  { key: 'se-total', label: 'SE', color: '#69A8BC' },
  { key: 'dse-total', label: 'DSE', color: '#A63746' },
  { key: 'consults', label: 'Consults', color: '#C4A35A' },
]

function polar(cx: number, cy: number, r: number, angleDeg: number) {
  const a = ((angleDeg - 90) * Math.PI) / 180
  return { x: cx + r * Math.cos(a), y: cy + r * Math.sin(a) }
}

function slicePath(cx: number, cy: number, r: number, startDeg: number, endDeg: number): string {
  const start = polar(cx, cy, r, startDeg)
  const end = polar(cx, cy, r, endDeg)
  const large = endDeg - startDeg > 180 ? 1 : 0
  return `M ${cx} ${cy} L ${start.x} ${start.y} A ${r} ${r} 0 ${large} 1 ${end.x} ${end.y} Z`
}

function DoctorPieChart({ doctor, onSelect }: { doctor: DoctorPie; onSelect?: (id: string) => void }) {
  const cx = 80
  const cy = 80
  const r = 68
  let cursor = 0

  return (
    <div className="bg-white rounded-lg border border-brand-primary-border p-4 flex flex-col items-center">
      <button
        type="button"
        onClick={() => onSelect?.(doctor.id)}
        className="text-sm font-semibold text-brand-accent hover:underline"
        title={`12-month history for ${doctor.displayName}`}
      >
        {doctor.initials}
      </button>
      <button
        type="button"
        onClick={() => onSelect?.(doctor.id)}
        className="text-xs text-gray-500 mb-2 truncate max-w-full hover:text-brand-primary hover:underline"
        title={`12-month history for ${doctor.displayName}`}
      >
        {doctor.displayName}
      </button>
      <svg
        viewBox="0 0 160 160"
        className="w-44 h-44"
        role="img"
        aria-label={`${doctor.displayName} mix of Echo, SE, DSE, and Consults`}
      >
        {doctor.total === 0 ? (
          <circle cx={cx} cy={cy} r={r} fill="#e8f1f5" stroke="#a8c9d9" />
        ) : (
          doctor.slices.map((slice) => {
            if (slice.value <= 0) return null
            const sweep = (slice.value / doctor.total) * 360
            if (sweep >= 359.99) {
              return <circle key={slice.key} cx={cx} cy={cy} r={r} fill={slice.color} />
            }
            const start = cursor
            cursor += sweep
            return (
              <path key={slice.key} d={slicePath(cx, cy, r, start, cursor)} fill={slice.color}>
                <title>{`${slice.label}: ${slice.value.toLocaleString()}`}</title>
              </path>
            )
          })
        )}
      </svg>
      <ul className="mt-2 w-full grid grid-cols-2 gap-x-3 gap-y-1 text-xs">
        {doctor.slices.map((s) => (
          <li key={s.key} className="flex items-center justify-between gap-2">
            <span className="flex items-center gap-1.5 min-w-0">
              <span className="h-2.5 w-2.5 rounded-sm shrink-0" style={{ backgroundColor: s.color }} />
              <span className="text-gray-600 truncate">{s.label}</span>
            </span>
            <span className="font-mono tabular-nums text-brand-primary">{s.value.toLocaleString()}</span>
          </li>
        ))}
      </ul>
    </div>
  )
}

export function buildDoctorPies(
  columns: { id: string; initials: string; displayName: string }[],
  rows: { key: string; counts: number[] }[],
): DoctorPie[] {
  const byKey = new Map(rows.map((r) => [r.key, r.counts]))
  return columns.map((col, i) => {
    const slices = SERIES.map((s) => ({
      ...s,
      value: byKey.get(s.key)?.[i] ?? 0,
    }))
    return {
      id: col.id,
      initials: col.initials,
      displayName: col.displayName,
      slices,
      total: slices.reduce((sum, s) => sum + s.value, 0),
    }
  })
}

export function DrSummaryPieGrid({
  doctors,
  onSelectDoctor,
}: {
  doctors: DoctorPie[]
  onSelectDoctor?: (id: string) => void
}) {
  return (
    <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
      {doctors.map((d) => (
        <DoctorPieChart key={d.id} doctor={d} onSelect={onSelectDoctor} />
      ))}
    </div>
  )
}
