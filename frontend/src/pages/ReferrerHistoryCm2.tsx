import { Fragment, useEffect, useRef, useState } from 'react'
import { Building2, CalendarClock, CalendarRange, ChevronDown, ChevronRight, Download, Search, User, X, BarChart3 } from 'lucide-react'
import { Button } from '../components/ui/button'
import { MonthTotalChart } from '../components/MonthTotalChart'

type SearchMode = 'doctor' | 'practice'

interface SearchHit {
  id: string
  displayName: string
  secondaryText?: string | null
}

interface TestRow {
  investigationType: string
  total: number
  outpatient: number
  inpatient: number
}

interface DoctorRow {
  doctorId: string
  displayName: string
  total: number
  outpatient: number
  inpatient: number
  testRows: TestRow[]
}

interface MonthRow {
  yearMonth: string
  label: string
  total: number
  outpatient: number
  inpatient: number
  testRows: TestRow[]
  doctorRows?: DoctorRow[]
}

interface ReportResult {
  mode: SearchMode
  entityId: string
  displayName: string
  suburb?: string | null
  dateFrom: string
  dateTo: string
  combinedSites: string[]
  months: MonthRow[]
  grandTotal: { total: number; outpatient: number; inpatient: number }
  investigationTypeFilter?: string | null
  grain?: 'month' | 'quarter'
}

interface InvestigationTypeOption {
  id: string
  displayName: string
}

function formatIsoDate(iso: string): string {
  const d = new Date(`${iso}T00:00:00`)
  if (Number.isNaN(d.getTime())) return iso
  return d.toLocaleDateString('en-AU', { day: 'numeric', month: 'short', year: 'numeric' })
}

function toLocalDateInputValue(date = new Date()): string {
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

function lastTenYearsRange(): { from: string; to: string } {
  const to = new Date()
  const from = new Date(to.getFullYear() - 10, to.getMonth(), to.getDate())
  from.setMonth(Math.floor(from.getMonth() / 3) * 3, 1)
  return { from: toLocalDateInputValue(from), to: toLocalDateInputValue(to) }
}

function defaultDateFrom(): string {
  const d = new Date()
  d.setDate(1)
  d.setMonth(d.getMonth() - 11)
  return toLocalDateInputValue(d)
}

export default function ReferrerHistoryCm2() {
  const [mode, setMode] = useState<SearchMode>('doctor')
  const [query, setQuery] = useState('')
  const [hits, setHits] = useState<SearchHit[]>([])
  const [searching, setSearching] = useState(false)
  const [open, setOpen] = useState(false)
  const [selected, setSelected] = useState<SearchHit | null>(null)
  const [investigationType, setInvestigationType] = useState('')
  const [investigationTypes, setInvestigationTypes] = useState<InvestigationTypeOption[]>([])
  const [dateFrom, setDateFrom] = useState(defaultDateFrom)
  const [dateTo, setDateTo] = useState(() => toLocalDateInputValue())
  const [report, setReport] = useState<ReportResult | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [expanded, setExpanded] = useState<Record<string, boolean>>({})
  const [showChart, setShowChart] = useState(false)
  const debounceRef = useRef<number | null>(null)
  const boxRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    fetch('/api/clinical/cm2/investigation-types')
      .then((r) => (r.ok ? r.json() : []))
      .then((list) => {
        if (!Array.isArray(list)) {
          setInvestigationTypes([])
          return
        }
        setInvestigationTypes(
          list.map((item: { id: string; displayName: string }) => ({
            id: String(item.id),
            displayName: item.displayName,
          }))
        )
      })
      .catch(() => setInvestigationTypes([]))
  }, [])

  useEffect(() => {
    const onDocClick = (e: MouseEvent) => {
      if (!boxRef.current?.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', onDocClick)
    return () => document.removeEventListener('mousedown', onDocClick)
  }, [])

  useEffect(() => {
    if (debounceRef.current) window.clearTimeout(debounceRef.current)
    const q = query.trim()
    if (selected && q === selected.displayName) {
      setHits([])
      setSearching(false)
      return
    }
    if (q.length < 2) {
      setHits([])
      setSearching(false)
      return
    }

    setSearching(true)
    debounceRef.current = window.setTimeout(async () => {
      const path =
        mode === 'doctor'
          ? `/api/clinical/cm2/referring-doctors/search?q=${encodeURIComponent(q)}`
          : `/api/clinical/cm2/referring-practices/search?q=${encodeURIComponent(q)}`
      try {
        const res = await fetch(path)
        const list = res.ok ? await res.json() : []
        setHits(Array.isArray(list) ? list : [])
        setOpen(true)
      } catch {
        setHits([])
      } finally {
        setSearching(false)
      }
    }, 300)

    return () => {
      if (debounceRef.current) window.clearTimeout(debounceRef.current)
    }
  }, [query, mode, selected])

  const changeMode = (next: SearchMode) => {
    if (next === mode) return
    setMode(next)
    setQuery('')
    setHits([])
    setSelected(null)
    setReport(null)
    setError('')
    setExpanded({})
    setShowChart(false)
  }

  const chooseHit = (hit: SearchHit) => {
    setSelected(hit)
    setQuery(hit.displayName)
    setHits([])
    setOpen(false)
    setError('')
  }

  const clearSelected = () => {
    setSelected(null)
    setQuery('')
    setHits([])
    setReport(null)
    setError('')
    setExpanded({})
    setShowChart(false)
  }

  const runHistory = async (from: string, to: string, grain: 'month' | 'quarter') => {
    if (!selected) {
      setError(mode === 'doctor' ? 'Search and select a referring doctor' : 'Search and select a practice')
      return
    }
    if (!from) {
      setError('Please select a start date')
      return
    }

    setLoading(true)
    setError('')
    try {
      const params = new URLSearchParams({
        mode,
        id: selected.id,
        dateFrom: from,
        dateTo: to,
        grain,
      })
      if (investigationType) params.set('investigationType', investigationType)
      const res = await fetch(`/api/clinical/cm2/referrer-history?${params}`)
      if (!res.ok) {
        const err = await res.json().catch(() => ({}))
        throw new Error(err.message || 'Report failed')
      }
      setReport(await res.json())
      setExpanded({})
      setShowChart(grain === 'quarter')
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Report failed')
      setReport(null)
      setExpanded({})
      setShowChart(false)
    } finally {
      setLoading(false)
    }
  }

  const runReport = async () => {
    const from = dateFrom.trim()
    const to = dateTo.trim() || toLocalDateInputValue()
    await runHistory(from, to, 'month')
  }

  const runQuarterlyTenYears = async () => {
    const range = lastTenYearsRange()
    setDateFrom(range.from)
    setDateTo(range.to)
    await runHistory(range.from, range.to, 'quarter')
  }

  const toggleExpanded = (key: string) => {
    setExpanded((prev) => ({ ...prev, [key]: !prev[key] }))
  }

  const exportCsv = () => {
    if (!report) return
    const periodHeader = report.grain === 'quarter' ? 'Quarter' : 'Month'
    const practiceMode = report.mode === 'practice'
    const lines = practiceMode
      ? [`${periodHeader},Doctor,Investigation Type,Total,Outpatient,Inpatient`]
      : [`${periodHeader},Investigation Type,Total,Outpatient,Inpatient`]
    for (const m of report.months) {
      if (practiceMode) {
        const doctors = m.doctorRows ?? []
        if (doctors.length === 0) {
          lines.push(`"${m.label}","","",${m.total},${m.outpatient},${m.inpatient}`)
          continue
        }
        for (const d of doctors) {
          if (d.testRows.length === 0) {
            lines.push(`"${m.label}","${d.displayName}","",${d.total},${d.outpatient},${d.inpatient}`)
            continue
          }
          for (const r of d.testRows) {
            lines.push(
              `"${m.label}","${d.displayName}","${r.investigationType}",${r.total},${r.outpatient},${r.inpatient}`
            )
          }
        }
        continue
      }
      if (m.testRows.length === 0) {
        lines.push(`"${m.label}","",${m.total},${m.outpatient},${m.inpatient}`)
        continue
      }
      for (const r of m.testRows) {
        lines.push(`"${m.label}","${r.investigationType}",${r.total},${r.outpatient},${r.inpatient}`)
      }
      lines.push(`"${m.label}","${periodHeader} total",${m.total},${m.outpatient},${m.inpatient}`)
    }
    lines.push(
      `"Total","",${report.grandTotal.total},${report.grandTotal.outpatient},${report.grandTotal.inpatient}`
    )
    const blob = new Blob([lines.join('\n')], { type: 'text/csv' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    const who = report.displayName.replace(/\s+/g, '-').toLowerCase()
    const typeSlug = report.investigationTypeFilter
      ? `-${report.investigationTypeFilter.replace(/\s+/g, '-').toLowerCase()}`
      : ''
    a.download = `referrer-history-cm2-${report.mode}-${who}${typeSlug}${report.grain === 'quarter' ? '-quarterly' : ''}-${report.dateFrom}-${report.dateTo}.csv`
    a.click()
    URL.revokeObjectURL(url)
  }

  const selectedTypeLabel =
    report?.investigationTypeFilter ||
    investigationTypes.find((t) => t.id === investigationType)?.displayName ||
    'all tests'

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-3">
        <CalendarRange className="h-8 w-8 text-brand-primary" />
        <div>
          <h2 className="text-2xl font-semibold text-brand-accent">Search by referrer/practice</h2>
          <p className="text-sm text-gray-600">
            Monthly referral history for a doctor or practice (Oracle CM2 / Hearts1st)
          </p>
        </div>
      </div>

      <div className="bg-white rounded-lg border border-brand-primary-border shadow-sm p-4">
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-12 gap-4">
          <div className="lg:col-span-2">
            <label className="block text-sm font-medium text-brand-primary mb-1">Search by</label>
            <div className="flex rounded-lg border border-gray-300 overflow-hidden">
              <button
                type="button"
                onClick={() => changeMode('doctor')}
                className={`flex-1 px-3 py-2 text-sm font-medium flex items-center justify-center gap-1.5 ${
                  mode === 'doctor'
                    ? 'bg-brand-primary text-white'
                    : 'bg-white text-gray-700 hover:bg-gray-50'
                }`}
              >
                <User className="h-4 w-4" />
                Doctor
              </button>
              <button
                type="button"
                onClick={() => changeMode('practice')}
                className={`flex-1 px-3 py-2 text-sm font-medium flex items-center justify-center gap-1.5 border-l border-gray-300 ${
                  mode === 'practice'
                    ? 'bg-brand-primary text-white'
                    : 'bg-white text-gray-700 hover:bg-gray-50'
                }`}
              >
                <Building2 className="h-4 w-4" />
                Practice
              </button>
            </div>
          </div>

          <div className="lg:col-span-2">
            <label className="block text-sm font-medium text-brand-primary mb-1">Date From</label>
            <input
              type="date"
              value={dateFrom}
              onChange={(e) => setDateFrom(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') void runReport()
              }}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-primary"
            />
          </div>
          <div className="lg:col-span-2">
            <label className="block text-sm font-medium text-brand-primary mb-1">Date To</label>
            <input
              type="date"
              value={dateTo}
              onChange={(e) => setDateTo(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') void runReport()
              }}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-primary"
            />
          </div>

          <div className="lg:col-span-6" ref={boxRef}>
            <label className="block text-sm font-medium text-brand-primary mb-1">
              {mode === 'doctor' ? 'Referring doctor' : 'Referring practice'}
            </label>
            <div className="relative">
              <input
                type="text"
                value={query}
                onChange={(e) => {
                  setQuery(e.target.value)
                  if (selected) setSelected(null)
                  setOpen(true)
                }}
                onFocus={() => hits.length > 0 && setOpen(true)}
                placeholder={mode === 'doctor' ? 'Type a doctor name…' : 'Type a practice or suburb…'}
                className="w-full border border-gray-300 rounded-lg px-3 py-2 pr-9 text-sm focus:outline-none focus:ring-2 focus:ring-brand-primary"
                autoComplete="off"
              />
              {query && (
                <button
                  type="button"
                  onClick={clearSelected}
                  className="absolute right-2 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-700"
                  aria-label="Clear search"
                >
                  <X className="h-4 w-4" />
                </button>
              )}
              {open && (hits.length > 0 || searching) && (
                <div className="absolute z-20 mt-1 w-full bg-white border border-gray-200 rounded-lg shadow-lg max-h-72 overflow-y-auto">
                  {searching && hits.length === 0 && (
                    <p className="px-3 py-2 text-sm text-gray-500">Searching…</p>
                  )}
                  {hits.map((hit) => (
                    <button
                      key={hit.id}
                      type="button"
                      onClick={() => chooseHit(hit)}
                      className="w-full text-left px-3 py-2 hover:bg-brand-primary-light border-b border-gray-100 last:border-0"
                    >
                      <div className="text-sm font-medium text-brand-accent">{hit.displayName}</div>
                      {hit.secondaryText && (
                        <div className="text-xs text-gray-500">{hit.secondaryText}</div>
                      )}
                    </button>
                  ))}
                  {!searching && hits.length === 0 && query.trim().length >= 2 && (
                    <p className="px-3 py-2 text-sm text-gray-500">No matches</p>
                  )}
                </div>
              )}
            </div>
            {mode === 'doctor' && (
              <p className="mt-1 text-xs text-gray-500">
                Doctor totals combine every practice location that person referred from.
              </p>
            )}
            {mode === 'practice' && (
              <p className="mt-1 text-xs text-gray-500">
                Expand each month to see referring doctors, then expand a doctor for test types.
              </p>
            )}
          </div>

          <div className="lg:col-span-6">
            <label className="block text-sm font-medium text-brand-primary mb-1">Investigation Type</label>
            <select
              value={investigationType}
              onChange={(e) => setInvestigationType(e.target.value)}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-primary bg-white"
            >
              <option value="">All tests</option>
              {investigationTypes.map((t) => (
                <option key={t.id} value={t.id}>{t.displayName}</option>
              ))}
            </select>
          </div>

          <div className="lg:col-span-6 flex items-end gap-2 flex-wrap">
            <Button variant="brand" onClick={runReport} disabled={loading} className="gap-2">
              <Search className="h-4 w-4" />
              {loading ? 'Running…' : 'Run Report'}
            </Button>
            <Button
              variant="outline"
              onClick={() => void runQuarterlyTenYears()}
              disabled={loading}
              className="gap-2"
              title="Quarterly referral history for the last 10 years"
            >
              <CalendarClock className="h-4 w-4" />
              10-year quarterly
            </Button>
            {report && (
              <Button variant="outline" onClick={exportCsv} className="gap-2">
                <Download className="h-4 w-4" />
                CSV
              </Button>
            )}
          </div>
        </div>
        {error && <p className="text-sm text-red-600 mt-3">{error}</p>}
      </div>

      {report && (
        <div className="space-y-3">
          <div className="flex items-start justify-between gap-3">
            <div>
              <h3 className="text-lg font-semibold text-brand-accent">
                {report.displayName}
                {report.suburb ? <span className="text-gray-500 font-normal"> — {report.suburb}</span> : null}
              </h3>
              <p className="text-sm text-gray-600">
                {formatIsoDate(report.dateFrom)} to {formatIsoDate(report.dateTo)}
                {' · '}
                {report.grain === 'quarter' ? 'quarterly' : 'monthly'}
                {' · '}
                {selectedTypeLabel}
                {report.mode === 'doctor' ? ' · all Hearts 1st sites combined' : ''}
              </p>
              {report.mode === 'doctor' && report.combinedSites.length > 0 && (
                <p className="text-sm text-gray-600 mt-1">
                  Combined from {report.combinedSites.length} referring{' '}
                  {report.combinedSites.length === 1 ? 'practice' : 'practices'}:{' '}
                  {report.combinedSites.join(', ')}
                </p>
              )}
            </div>
            <Button
              variant={showChart ? 'brand' : 'outline'}
              onClick={() => setShowChart((v) => !v)}
              className="px-3 shrink-0"
              title={showChart ? 'Hide chart' : report.grain === 'quarter' ? 'Show quarterly chart' : 'Show monthly chart'}
              aria-label={showChart ? 'Hide chart' : report.grain === 'quarter' ? 'Show quarterly chart' : 'Show monthly chart'}
              aria-pressed={showChart}
            >
              <BarChart3 className="h-5 w-5" />
            </Button>
          </div>

          {showChart && (
            <MonthTotalChart
              months={report.months}
              title={report.grain === 'quarter' ? 'Referrals by quarter' : 'Referrals by month'}
            />
          )}

          <div className="bg-card rounded-lg border border-brand-primary-border overflow-hidden">
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead className="bg-brand-primary text-white">
                  <tr>
                    <th className="text-left p-3 font-medium">{report.grain === 'quarter' ? 'Quarter' : 'Month'}</th>
                    <th className="text-right p-3 font-medium">Total</th>
                    <th className="text-right p-3 font-medium">Outpatient</th>
                    <th className="text-right p-3 font-medium">Inpatient</th>
                  </tr>
                </thead>
                <tbody>
                  {report.months.map((m) => {
                    const doctors = m.doctorRows ?? []
                    const nestDoctors = report.mode === 'practice' && doctors.length > 0
                    const canExpand = nestDoctors || m.testRows.length > 0
                    const isOpen = Boolean(expanded[m.yearMonth])
                    const Chevron = isOpen ? ChevronDown : ChevronRight
                    return (
                      <Fragment key={m.yearMonth}>
                        <tr
                          className={`border-b border-brand-primary-border/50 ${
                            canExpand ? 'hover:bg-muted/30' : ''
                          }`}
                        >
                          <td className="p-0">
                            {canExpand ? (
                              <button
                                type="button"
                                onClick={() => toggleExpanded(m.yearMonth)}
                                aria-expanded={isOpen}
                                className="w-full text-left px-3 py-3 flex items-center gap-2"
                              >
                                <Chevron className="h-4 w-4 text-brand-primary shrink-0" />
                                <span>{m.label}</span>
                              </button>
                            ) : (
                              <span className="block px-3 py-3 pl-9 text-gray-500">{m.label}</span>
                            )}
                          </td>
                          <td className="p-3 text-right font-mono">{m.total.toLocaleString()}</td>
                          <td className="p-3 text-right font-mono">{m.outpatient.toLocaleString()}</td>
                          <td className="p-3 text-right font-mono">{m.inpatient.toLocaleString()}</td>
                        </tr>
                        {isOpen &&
                          nestDoctors &&
                          doctors.map((d) => {
                            const doctorKey = `${m.yearMonth}::${d.doctorId}`
                            const doctorOpen = Boolean(expanded[doctorKey])
                            const DoctorChevron = doctorOpen ? ChevronDown : ChevronRight
                            return (
                              <Fragment key={doctorKey}>
                                <tr className="border-b border-brand-primary-border/40 bg-muted/20 hover:bg-muted/30">
                                  <td className="p-0">
                                    <button
                                      type="button"
                                      onClick={() => toggleExpanded(doctorKey)}
                                      aria-expanded={doctorOpen}
                                      className="w-full text-left px-3 py-2.5 pl-9 flex items-center gap-2"
                                    >
                                      <DoctorChevron className="h-4 w-4 text-brand-primary shrink-0" />
                                      <span className="text-gray-800">{d.displayName}</span>
                                    </button>
                                  </td>
                                  <td className="p-3 text-right font-mono">{d.total.toLocaleString()}</td>
                                  <td className="p-3 text-right font-mono">{d.outpatient.toLocaleString()}</td>
                                  <td className="p-3 text-right font-mono">{d.inpatient.toLocaleString()}</td>
                                </tr>
                                {doctorOpen &&
                                  d.testRows.map((r) => (
                                    <tr
                                      key={`${doctorKey}-${r.investigationType}`}
                                      className="border-b border-brand-primary-border/30 bg-muted/10"
                                    >
                                      <td className="p-3 pl-16 text-gray-700">{r.investigationType}</td>
                                      <td className="p-3 text-right font-mono">{r.total.toLocaleString()}</td>
                                      <td className="p-3 text-right font-mono">{r.outpatient.toLocaleString()}</td>
                                      <td className="p-3 text-right font-mono">{r.inpatient.toLocaleString()}</td>
                                    </tr>
                                  ))}
                              </Fragment>
                            )
                          })}
                        {isOpen &&
                          !nestDoctors &&
                          m.testRows.map((r) => (
                            <tr
                              key={`${m.yearMonth}-${r.investigationType}`}
                              className="border-b border-brand-primary-border/40 bg-muted/20"
                            >
                              <td className="p-3 pl-12 text-gray-700">{r.investigationType}</td>
                              <td className="p-3 text-right font-mono">{r.total.toLocaleString()}</td>
                              <td className="p-3 text-right font-mono">{r.outpatient.toLocaleString()}</td>
                              <td className="p-3 text-right font-mono">{r.inpatient.toLocaleString()}</td>
                            </tr>
                          ))}
                      </Fragment>
                    )
                  })}
                  <tr className="bg-brand-accent/10 font-bold">
                    <td className="p-3">Total</td>
                    <td className="p-3 text-right font-mono">{report.grandTotal.total.toLocaleString()}</td>
                    <td className="p-3 text-right font-mono">{report.grandTotal.outpatient.toLocaleString()}</td>
                    <td className="p-3 text-right font-mono">{report.grandTotal.inpatient.toLocaleString()}</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
          <p className="text-xs text-gray-500">
            {report.mode === 'practice'
              ? `Click a ${report.grain === 'quarter' ? 'quarter' : 'month'} to see referring doctors, then expand a doctor for investigation types. `
              : `Click a ${report.grain === 'quarter' ? 'quarter' : 'month'} to expand investigation types. `}
            {report.grain === 'quarter' ? 'Quarters' : 'Months'} with no referrals stay at zero.
          </p>
        </div>
      )}
    </div>
  )
}
