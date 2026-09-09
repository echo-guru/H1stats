import { Fragment, useCallback, useEffect, useState } from 'react'
import { Download, RefreshCw } from 'lucide-react'
import { Button } from '../components/ui/button'

interface MissingCtcaDocRow {
  appointmentId: string
  patientRid: string
  patientCmId: string
  lastName: string
  firstName: string
  dateOfBirth?: string | null
  cardiologistId?: string | null
  cardiologistName?: string | null
  appointmentDate: string
  appointmentTime?: string | null
  arrived: boolean
  appointmentNote?: string | null
  roomName?: string | null
  siteName?: string | null
  serviceTitle: string
  bookingCount: number
}

interface MissingCtcaDocResult {
  dateFrom: string
  dateTo: string
  cardiologistId?: string | null
  count: number
  rows: MissingCtcaDocRow[]
}

const CARDIOLOGIST_OPTIONS = [
  { id: '', label: 'All cardiologists' },
  { id: 'unassigned', label: 'Unassigned' },
  { id: '282', label: "Dr Julie Ch'ng" },
  { id: '2901', label: 'Dr Shi Yi Goo' },
  { id: '1681', label: 'Dr Vance Manins' },
  { id: '1941', label: 'Dr Justin Morze' },
  { id: '144', label: 'Dr Roess Pascoe' },
  { id: '981', label: 'Dr Jeremy Wright' },
]

function toIso(d: Date): string {
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

function yesterdayIso(): string {
  const d = new Date()
  d.setDate(d.getDate() - 1)
  return toIso(d)
}

function addDaysIso(iso: string, days: number): string {
  const [y, m, d] = iso.split('-').map(Number)
  const dt = new Date(y, m - 1, d)
  dt.setDate(dt.getDate() + days)
  return toIso(dt)
}

function formatDisplayDate(iso?: string | null): string {
  if (!iso) return '—'
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso)
  if (!m) return iso
  return `${m[3]}/${m[2]}/${m[1]}`
}

function csvEscape(value: string): string {
  if (/[",\n\r]/.test(value)) return `"${value.replace(/"/g, '""')}"`
  return value
}

export interface MissingCtcaDocsListProps {
  apiBase?: string
}

export default function MissingCtcaDocsList({
  apiBase = '/api/workllists',
}: MissingCtcaDocsListProps) {
  const [dateFrom, setDateFrom] = useState(() => addDaysIso(yesterdayIso(), -29))
  const [dateTo, setDateTo] = useState(yesterdayIso)
  const [cardiologist, setCardiologist] = useState('')
  const [result, setResult] = useState<MissingCtcaDocResult | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [expandedId, setExpandedId] = useState<string | null>(null)

  const load = useCallback(async () => {
    const from = dateFrom.trim()
    const to = dateTo.trim()
    if (!from || !to) {
      setError('Provide both dates')
      return
    }
    if (from > to) {
      setError('Date from must be on or before date to')
      return
    }

    setLoading(true)
    setError('')
    try {
      const qs = new URLSearchParams()
      qs.set('dateFrom', from)
      qs.set('dateTo', to)
      if (cardiologist) qs.set('cardiologist', cardiologist)
      const res = await fetch(`${apiBase}/missing-ctca-docs?${qs}`)
      const data = await res.json().catch(() => ({}))
      if (!res.ok) throw new Error(data.message || 'Failed to load missing CTCA docs')
      setResult(data as MissingCtcaDocResult)
      setExpandedId(null)
    } catch (e) {
      setResult(null)
      setError(e instanceof Error ? e.message : 'Failed to load missing CTCA docs')
    } finally {
      setLoading(false)
    }
  }, [apiBase, cardiologist, dateFrom, dateTo])

  useEffect(() => {
    void load()
    // Load once when the tab mounts; subsequent loads are via the button / shortcuts.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [apiBase])

  const exportCsv = () => {
    if (!result || result.rows.length === 0) return
    const header = [
      'Lastname',
      'Firstname',
      'PAT ID',
      'DOB',
      'Cardiologist',
      'CTCA date',
      'Time',
      'Arrived',
      'Site',
      'Room',
      'Unmatched bookings',
      'Notes',
    ]
    const lines = [header.join(',')]
    for (const row of result.rows) {
      lines.push(
        [
          csvEscape(row.lastName || ''),
          csvEscape(row.firstName || ''),
          csvEscape(row.patientCmId || ''),
          csvEscape(formatDisplayDate(row.dateOfBirth)),
          csvEscape(row.cardiologistName || ''),
          csvEscape(formatDisplayDate(row.appointmentDate)),
          csvEscape(row.appointmentTime || ''),
          csvEscape(row.arrived ? 'Yes' : 'No'),
          csvEscape(row.siteName || ''),
          csvEscape(row.roomName || ''),
          csvEscape(String(row.bookingCount)),
          csvEscape(row.appointmentNote || ''),
        ].join(',')
      )
    }
    const blob = new Blob([lines.join('\n')], { type: 'text/csv' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `missing-ctca-docs-${new Date().toISOString().slice(0, 10)}.csv`
    a.click()
    URL.revokeObjectURL(url)
  }

  const setLookback = (days: number) => {
    const to = yesterdayIso()
    setDateTo(to)
    setDateFrom(addDaysIso(to, -(days - 1)))
  }

  const cardioLabel =
    CARDIOLOGIST_OPTIONS.find((c) => c.id === (result?.cardiologistId || ''))?.label ||
    'All cardiologists'

  return (
    <section className="space-y-4">
      <div>
        <h3 className="text-lg font-semibold text-brand-primary">Missing CTCA docs</h3>
        <p className="text-sm text-gray-600">
          Past CT Coronary Angiogram diary bookings with no imported test/results CTCA document
          (category 1) dated on or after that booking. One row per patient — future bookings are
          excluded.
        </p>
      </div>

      <div className="bg-white rounded-lg border border-brand-primary-border shadow-sm p-4">
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
          <div>
            <label className="block text-sm font-medium text-brand-primary mb-1">Date From</label>
            <input
              type="date"
              value={dateFrom}
              onChange={(e) => setDateFrom(e.target.value)}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-primary"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-brand-primary mb-1">Date To</label>
            <input
              type="date"
              value={dateTo}
              onChange={(e) => setDateTo(e.target.value)}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-primary"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-brand-primary mb-1">H1 Cardiologist</label>
            <select
              value={cardiologist}
              onChange={(e) => setCardiologist(e.target.value)}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-primary bg-white"
            >
              {CARDIOLOGIST_OPTIONS.map((c) => (
                <option key={c.id || 'all'} value={c.id}>
                  {c.label}
                </option>
              ))}
            </select>
          </div>
        </div>

        <div className="mt-4 flex flex-wrap items-center gap-2">
          <Button type="button" onClick={() => void load()} disabled={loading}>
            <RefreshCw className={`h-4 w-4 mr-2 ${loading ? 'animate-spin' : ''}`} />
            {loading ? 'Loading…' : 'Load worklist'}
          </Button>
          <Button
            type="button"
            variant="outline"
            onClick={exportCsv}
            disabled={!result || result.rows.length === 0}
          >
            <Download className="h-4 w-4 mr-2" />
            Export CSV
          </Button>
          <button
            type="button"
            className="text-sm text-gray-500 hover:text-brand-primary underline-offset-2 hover:underline"
            onClick={() => setLookback(7)}
          >
            Last 7 days
          </button>
          <button
            type="button"
            className="text-sm text-gray-500 hover:text-brand-primary underline-offset-2 hover:underline"
            onClick={() => setLookback(30)}
          >
            Last 30 days
          </button>
          <button
            type="button"
            className="text-sm text-gray-500 hover:text-brand-primary underline-offset-2 hover:underline"
            onClick={() => setLookback(90)}
          >
            Last 90 days
          </button>
        </div>

        {error && (
          <p className="mt-3 text-sm text-red-700 bg-red-50 border border-red-100 rounded-md px-3 py-2">
            {error}
          </p>
        )}
      </div>

      {result && (
        <div className="bg-white rounded-lg border border-brand-primary-border shadow-sm overflow-hidden">
          <div className="px-4 py-3 border-b border-gray-100 text-sm text-gray-600 flex flex-wrap gap-x-4 gap-y-1">
            <span>
              <strong className="text-brand-primary">{result.count}</strong> patient
              {result.count === 1 ? '' : 's'} missing a CTCA result
            </span>
            <span>
              Dates: {formatDisplayDate(result.dateFrom)} – {formatDisplayDate(result.dateTo)}
            </span>
            <span>Cardiologist: {cardioLabel}</span>
          </div>

          <div className="overflow-x-auto">
            <table className="w-full table-fixed text-sm">
              <thead className="bg-brand-primary-light text-brand-primary">
                <tr>
                  <th className="text-left font-semibold px-3 py-2 w-[11%]">Lastname</th>
                  <th className="text-left font-semibold px-3 py-2 w-[11%]">Firstname</th>
                  <th className="text-left font-semibold px-3 py-2 w-[8%]">PAT ID</th>
                  <th className="text-left font-semibold px-3 py-2 w-[8%]">DOB</th>
                  <th className="text-left font-semibold px-3 py-2 w-[13%]">Cardiologist</th>
                  <th className="text-left font-semibold px-3 py-2 w-[8%]">CTCA date</th>
                  <th className="text-left font-semibold px-3 py-2 w-[6%]">Time</th>
                  <th className="text-left font-semibold px-3 py-2 w-[7%]">Arrived</th>
                  <th className="text-left font-semibold px-3 py-2 w-[28%]">Notes</th>
                </tr>
              </thead>
              <tbody>
                {result.rows.length === 0 ? (
                  <tr>
                    <td colSpan={9} className="px-3 py-8 text-center text-gray-500">
                      No past CTCA bookings missing a result document for the selected filters.
                    </td>
                  </tr>
                ) : (
                  result.rows.map((row) => {
                    const expanded = expandedId === row.patientRid
                    const note = row.appointmentNote ?? ''
                    return (
                      <Fragment key={row.patientRid}>
                        <tr
                          onClick={() =>
                            setExpandedId((prev) => (prev === row.patientRid ? null : row.patientRid))
                          }
                          className={`border-t border-gray-100 align-top cursor-pointer ${
                            expanded ? 'bg-brand-primary-light/60' : 'hover:bg-gray-50'
                          }`}
                        >
                          <td className="px-3 py-2 font-medium text-gray-900 truncate">
                            {row.lastName || '—'}
                          </td>
                          <td className="px-3 py-2 text-gray-800 truncate">{row.firstName || '—'}</td>
                          <td className="px-3 py-2 text-gray-800 truncate">{row.patientCmId || '—'}</td>
                          <td className="px-3 py-2 text-gray-700 truncate">
                            {formatDisplayDate(row.dateOfBirth)}
                          </td>
                          <td className="px-3 py-2 text-gray-800 truncate">
                            {row.cardiologistName || (
                              <span className="text-gray-400 italic">Unassigned</span>
                            )}
                          </td>
                          <td className="px-3 py-2 text-gray-800 truncate">
                            {formatDisplayDate(row.appointmentDate)}
                          </td>
                          <td className="px-3 py-2 text-gray-800 truncate">
                            {row.appointmentTime || '—'}
                          </td>
                          <td className="px-3 py-2 text-gray-800 truncate">
                            {row.arrived ? 'Yes' : 'No'}
                          </td>
                          <td className="px-3 py-2 text-gray-700">
                            <div className="truncate">
                              {note || (
                                <span className="text-gray-400 italic">Click row for details</span>
                              )}
                            </div>
                          </td>
                        </tr>
                        {expanded ? (
                          <tr className="bg-brand-primary-light/40 border-b border-brand-primary-border">
                            <td colSpan={9} className="px-4 py-3" onClick={(e) => e.stopPropagation()}>
                              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
                                <div>
                                  <p className="text-xs font-medium text-brand-primary mb-1">
                                    Booking
                                  </p>
                                  <p className="text-sm text-gray-800">
                                    {row.serviceTitle}
                                    {row.bookingCount > 1
                                      ? ` · ${row.bookingCount} unmatched in range`
                                      : ''}
                                  </p>
                                </div>
                                <div>
                                  <p className="text-xs font-medium text-brand-primary mb-1">
                                    Date / time
                                  </p>
                                  <p className="text-sm text-gray-800">
                                    {formatDisplayDate(row.appointmentDate)}
                                    {row.appointmentTime ? ` ${row.appointmentTime}` : ''}
                                    {row.arrived ? ' · arrived' : ' · not arrived'}
                                  </p>
                                </div>
                                <div>
                                  <p className="text-xs font-medium text-brand-primary mb-1">
                                    Room
                                  </p>
                                  <p className="text-sm text-gray-800">
                                    {row.roomName || '—'}
                                    {row.siteName ? ` · ${row.siteName}` : ''}
                                  </p>
                                </div>
                                <div className="sm:col-span-2 lg:col-span-4">
                                  <p className="text-xs font-medium text-brand-primary mb-1">
                                    Booking notes
                                  </p>
                                  <div className="whitespace-pre-wrap text-sm text-gray-800 bg-white border border-gray-200 rounded-md px-3 py-2 min-h-[4.5rem]">
                                    {note || (
                                      <span className="text-gray-400 italic">No note</span>
                                    )}
                                  </div>
                                </div>
                                <p className="sm:col-span-2 lg:col-span-4 text-xs text-gray-500">
                                  Click the row again to collapse.
                                </p>
                              </div>
                            </td>
                          </tr>
                        ) : null}
                      </Fragment>
                    )
                  })
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </section>
  )
}
