import { Fragment, useCallback, useEffect, useState } from 'react'
import { Download, ExternalLink, RefreshCw } from 'lucide-react'
import { Button } from '../components/ui/button'

interface UnactionedConsultReferralRow {
  documentId: string
  patientRid: string
  patientCmId: string
  lastName: string
  firstName: string
  dateOfBirth?: string | null
  cardiologistId?: string | null
  cardiologistName?: string | null
  documentDate: string
  documentTitle?: string | null
  documentPath?: string | null
  documentCategory: number
  upcomingConsultDate?: string | null
  upcomingConsultTitle?: string | null
  upcomingConsultNote?: string | null
}

interface UnactionedConsultReferralResult {
  dateFrom: string
  dateTo: string
  cardiologistId?: string | null
  count: number
  rows: UnactionedConsultReferralRow[]
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

function todayIso(): string {
  const d = new Date()
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

function addDaysIso(iso: string, days: number): string {
  const [y, m, d] = iso.split('-').map(Number)
  const dt = new Date(y, m - 1, d)
  dt.setDate(dt.getDate() + days)
  const yy = dt.getFullYear()
  const mm = String(dt.getMonth() + 1).padStart(2, '0')
  const dd = String(dt.getDate()).padStart(2, '0')
  return `${yy}-${mm}-${dd}`
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

export interface UnactionedConsultReferralsListProps {
  apiBase?: string
}

export default function UnactionedConsultReferralsList({
  apiBase = '/api/workllists',
}: UnactionedConsultReferralsListProps) {
  const [dateFrom, setDateFrom] = useState(todayIso)
  const [dateTo, setDateTo] = useState(todayIso)
  const [cardiologist, setCardiologist] = useState('')
  const [result, setResult] = useState<UnactionedConsultReferralResult | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [expandedId, setExpandedId] = useState<string | null>(null)
  const [openingId, setOpeningId] = useState<string | null>(null)
  const [openMessage, setOpenMessage] = useState('')

  const load = useCallback(async () => {
    const from = dateFrom.trim()
    const to = dateTo.trim()
    if (!from || !to) {
      setError('Provide both dates, or use Today')
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
      const res = await fetch(`${apiBase}/unactioned-consult-referrals?${qs}`)
      const data = await res.json().catch(() => ({}))
      if (!res.ok) throw new Error(data.message || 'Failed to load unactioned consult referrals')
      setResult(data as UnactionedConsultReferralResult)
      setExpandedId(null)
      setOpenMessage('')
    } catch (e) {
      setResult(null)
      setError(e instanceof Error ? e.message : 'Failed to load unactioned consult referrals')
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
      'Document date',
      'Title',
      'Upcoming consult',
      'Upcoming consult date',
      'Booking notes',
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
          csvEscape(formatDisplayDate(row.documentDate)),
          csvEscape(row.documentTitle || ''),
          csvEscape(row.upcomingConsultTitle || ''),
          csvEscape(formatDisplayDate(row.upcomingConsultDate)),
          csvEscape(row.upcomingConsultNote || ''),
        ].join(',')
      )
    }
    const blob = new Blob([lines.join('\n')], { type: 'text/csv' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `unactioned-consult-referrals-${new Date().toISOString().slice(0, 10)}.csv`
    a.click()
    URL.revokeObjectURL(url)
  }

  const setLookback = (days: number) => {
    const to = todayIso()
    setDateTo(to)
    setDateFrom(days <= 1 ? to : addDaysIso(to, -(days - 1)))
  }

  const openReferral = async (documentId: string) => {
    setOpeningId(documentId)
    setError('')
    setOpenMessage('')
    // Open synchronously so popup blockers still allow the tab after the fetch.
    const preview = window.open('about:blank', '_blank')
    try {
      const res = await fetch(
        `${apiBase}/unactioned-consult-referrals/${encodeURIComponent(documentId)}/file`
      )
      if (!res.ok) {
        const data = await res.json().catch(() => ({}))
        throw new Error(data.message || 'Could not open referral PDF')
      }
      const blob = await res.blob()
      const url = URL.createObjectURL(blob)
      if (preview) {
        preview.location.replace(url)
      } else {
        const a = document.createElement('a')
        a.href = url
        a.target = '_blank'
        a.rel = 'noopener noreferrer'
        a.click()
      }
      window.setTimeout(() => URL.revokeObjectURL(url), 60_000)
    } catch (e) {
      preview?.close()
      setError(e instanceof Error ? e.message : 'Could not open referral PDF')
    } finally {
      setOpeningId(null)
    }
  }

  const cardioLabel =
    CARDIOLOGIST_OPTIONS.find((c) => c.id === (result?.cardiologistId || ''))?.label ||
    'All cardiologists'

  return (
    <section className="space-y-4">
      <div>
        <h3 className="text-lg font-semibold text-brand-primary">Unactioned consult referrals</h3>
        <p className="text-sm text-gray-600">
          Consult referral documents imported onto a patient (CM2 “Cons Ref”) with no
          unarrived Cardiology or Admin recall. Document date is the CM2 import day (no time).
          An upcoming consult booking is shown when one exists, but does not hide the row.
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
            onClick={() => setLookback(1)}
          >
            Today
          </button>
          <button
            type="button"
            className="text-sm text-gray-500 hover:text-brand-primary underline-offset-2 hover:underline"
            onClick={() => setLookback(2)}
          >
            Last 2 days
          </button>
          <button
            type="button"
            className="text-sm text-gray-500 hover:text-brand-primary underline-offset-2 hover:underline"
            onClick={() => setLookback(7)}
          >
            Last 7 days
          </button>
        </div>

        {error && (
          <p className="mt-3 text-sm text-red-700 bg-red-50 border border-red-100 rounded-md px-3 py-2">
            {error}
          </p>
        )}
        {openMessage && !error && (
          <p className="mt-3 text-sm text-emerald-700 bg-emerald-50 border border-emerald-100 rounded-md px-3 py-2">
            {openMessage}
          </p>
        )}
      </div>

      {result && (
        <div className="bg-white rounded-lg border border-brand-primary-border shadow-sm overflow-hidden">
          <div className="px-4 py-3 border-b border-gray-100 text-sm text-gray-600 flex flex-wrap gap-x-4 gap-y-1">
            <span>
              <strong className="text-brand-primary">{result.count}</strong> unactioned consult
              referral{result.count === 1 ? '' : 's'}
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
                  <th className="text-left font-semibold px-3 py-2 w-[12%]">Cardiologist</th>
                  <th className="text-left font-semibold px-3 py-2 w-[8%]">Imported</th>
                  <th className="text-left font-semibold px-3 py-2 w-[26%]">Referral title</th>
                  <th className="text-left font-semibold px-3 py-2 w-[16%]">Upcoming consult</th>
                </tr>
              </thead>
              <tbody>
                {result.rows.length === 0 ? (
                  <tr>
                    <td colSpan={8} className="px-3 py-8 text-center text-gray-500">
                      No unactioned consult referrals for the selected filters.
                    </td>
                  </tr>
                ) : (
                  result.rows.map((row) => {
                    const expanded = expandedId === row.documentId
                    return (
                      <Fragment key={row.documentId}>
                        <tr
                          onClick={() =>
                            setExpandedId((prev) => (prev === row.documentId ? null : row.documentId))
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
                            {formatDisplayDate(row.documentDate)}
                          </td>
                          <td className="px-3 py-2 text-gray-700">
                            {row.documentTitle && row.documentPath ? (
                              <button
                                type="button"
                                title="Open referral PDF"
                                disabled={openingId === row.documentId}
                                onClick={(e) => {
                                  e.stopPropagation()
                                  void openReferral(row.documentId)
                                }}
                                className="truncate max-w-full text-left text-brand-primary hover:underline inline-flex items-center gap-1 disabled:opacity-60"
                              >
                                <ExternalLink className="h-3.5 w-3.5 shrink-0" />
                                <span className="truncate">
                                  {openingId === row.documentId ? 'Opening…' : row.documentTitle}
                                </span>
                              </button>
                            ) : (
                              <div className="truncate">{row.documentTitle || '—'}</div>
                            )}
                          </td>
                          <td className="px-3 py-2 text-gray-700">
                            {row.upcomingConsultDate ? (
                              <div className="truncate">
                                {formatDisplayDate(row.upcomingConsultDate)}
                                {row.upcomingConsultTitle ? ` · ${row.upcomingConsultTitle}` : ''}
                              </div>
                            ) : (
                              <span className="text-gray-400 italic">None</span>
                            )}
                          </td>
                        </tr>
                        {expanded ? (
                          <tr className="bg-brand-primary-light/40 border-b border-brand-primary-border">
                            <td colSpan={8} className="px-4 py-3" onClick={(e) => e.stopPropagation()}>
                              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
                                <div className="sm:col-span-2">
                                  <p className="text-xs font-medium text-brand-primary mb-1">
                                    Referral
                                  </p>
                                  {row.documentTitle && row.documentPath ? (
                                    <button
                                      type="button"
                                      title="Open referral PDF"
                                      disabled={openingId === row.documentId}
                                      onClick={() => void openReferral(row.documentId)}
                                      className="text-sm text-left text-brand-primary hover:underline inline-flex items-center gap-1 disabled:opacity-60"
                                    >
                                      <ExternalLink className="h-3.5 w-3.5 shrink-0" />
                                      {openingId === row.documentId
                                        ? 'Opening…'
                                        : row.documentTitle}
                                    </button>
                                  ) : (
                                    <p className="text-sm text-gray-800 whitespace-pre-wrap">
                                      {row.documentTitle || '—'}
                                    </p>
                                  )}
                                </div>
                                <div className="sm:col-span-2">
                                  <p className="text-xs font-medium text-brand-primary mb-1">
                                    Upcoming consult
                                  </p>
                                  <p className="text-sm text-gray-800">
                                    {row.upcomingConsultDate
                                      ? `${formatDisplayDate(row.upcomingConsultDate)}${
                                          row.upcomingConsultTitle
                                            ? ` · ${row.upcomingConsultTitle}`
                                            : ''
                                        }`
                                      : 'No consult booking from today'}
                                  </p>
                                </div>
                                {row.upcomingConsultDate ? (
                                  <div className="sm:col-span-2 lg:col-span-4">
                                    <p className="text-xs font-medium text-brand-primary mb-1">
                                      Booking notes
                                    </p>
                                    <div className="whitespace-pre-wrap text-sm text-gray-800 bg-white border border-gray-200 rounded-md px-3 py-2 min-h-[4.5rem]">
                                      {row.upcomingConsultNote || (
                                        <span className="text-gray-400 italic">No note</span>
                                      )}
                                    </div>
                                  </div>
                                ) : null}
                                <p className="sm:col-span-2 lg:col-span-4 text-xs text-gray-500">
                                  Click the row again to collapse. Click the referral title to open
                                  the PDF.
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
