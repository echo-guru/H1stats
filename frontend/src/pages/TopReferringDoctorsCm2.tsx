import { useEffect, useState } from 'react'
import { ChevronDown, ChevronRight, Download, Search, Users } from 'lucide-react'
import { Button } from '../components/ui/button'

interface ReferringDoctorStatRow {
  personId: string
  displayName: string
  investigationType: string
  total: number
  outpatient: number
  inpatient: number
}

interface ReferringDoctorGroup {
  rank: number
  personId: string
  displayName: string
  rows: ReferringDoctorStatRow[]
  subtotal: { total: number; outpatient: number; inpatient: number }
}

interface ReportResult {
  groups: ReferringDoctorGroup[]
  grandTotal: { total: number; outpatient: number; inpatient: number }
  topN: number
  investigationTypeFilter?: string | null
}

interface InvestigationTypeOption {
  id: string
  displayName: string
}

function toLocalDateInputValue(date = new Date()): string {
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

export default function TopReferringDoctorsCm2() {
  const [dateFrom, setDateFrom] = useState(() => {
    const d = new Date()
    d.setDate(1)
    return toLocalDateInputValue(d)
  })
  const [dateTo, setDateTo] = useState(() => toLocalDateInputValue())
  const [topN, setTopN] = useState(50)
  const [investigationType, setInvestigationType] = useState('')
  const [investigationTypes, setInvestigationTypes] = useState<InvestigationTypeOption[]>([])
  const [report, setReport] = useState<ReportResult | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [expanded, setExpanded] = useState<Record<string, boolean>>({})

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

  const runReport = async () => {
    const from = dateFrom.trim()
    const to = dateTo.trim() || toLocalDateInputValue()
    if (!from) {
      setError('Please select a start date')
      return
    }

    const n = Number(topN)
    if (!Number.isFinite(n) || n < 1) {
      setError('Top N must be a number of at least 1')
      return
    }

    setLoading(true)
    setError('')
    try {
      const params = new URLSearchParams({
        dateFrom: from,
        dateTo: to,
        top: String(Math.min(500, Math.max(1, Math.floor(n)))),
      })
      if (investigationType) params.set('investigationType', investigationType)
      const res = await fetch(`/api/clinical/cm2/top-referring-doctors?${params}`)
      if (!res.ok) {
        const err = await res.json().catch(() => ({}))
        throw new Error(err.message || 'Report failed')
      }
      setReport(await res.json())
      setExpanded({})
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Report failed')
      setReport(null)
      setExpanded({})
    } finally {
      setLoading(false)
    }
  }

  const toggleExpanded = (key: string) => {
    setExpanded((prev) => ({ ...prev, [key]: !prev[key] }))
  }

  const exportCsv = () => {
    if (!report) return
    const lines = ['Rank,Referring Doctor,Investigation Type,Total,Outpatient,Inpatient']
    for (const g of report.groups) {
      for (const r of g.rows) {
        lines.push(
          `${g.rank},"${g.displayName}","${r.investigationType}",${r.total},${r.outpatient},${r.inpatient}`
        )
      }
      lines.push(
        `${g.rank},"${g.displayName}","Subtotal",${g.subtotal.total},${g.subtotal.outpatient},${g.subtotal.inpatient}`
      )
    }
    lines.push(
      `,"Grand Total","",${report.grandTotal.total},${report.grandTotal.outpatient},${report.grandTotal.inpatient}`
    )
    const blob = new Blob([lines.join('\n')], { type: 'text/csv' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    const typeSlug = report.investigationTypeFilter
      ? `-${report.investigationTypeFilter.replace(/\s+/g, '-').toLowerCase()}`
      : ''
    a.download = `top-referring-doctors-cm2-${dateFrom}-${dateTo}-top${report.topN}${typeSlug}.csv`
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
        <Users className="h-8 w-8 text-brand-primary" />
        <div>
          <h2 className="text-2xl font-semibold text-brand-accent">Top Referring Doctors - CM2</h2>
          <p className="text-sm text-gray-600">
            Referral activity by doctor person and investigation type (Oracle CM2 / Hearts1st)
          </p>
        </div>
      </div>

      <div className="bg-white rounded-lg border border-brand-primary-border shadow-sm p-4">
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-5 gap-4">
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
          <div>
            <label className="block text-sm font-medium text-brand-primary mb-1">Top N</label>
            <input
              type="number"
              min={1}
              max={500}
              value={topN}
              onChange={(e) => setTopN(Number(e.target.value))}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-primary"
            />
          </div>
          <div className="flex items-end gap-2">
            <Button variant="brand" onClick={runReport} disabled={loading} className="gap-2 flex-1">
              <Search className="h-4 w-4" />
              {loading ? 'Running…' : 'Run Report'}
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
        <div className="space-y-4">
          <p className="text-sm text-gray-600">
            Showing top {report.topN} referring doctors for {selectedTypeLabel}
            ({report.groups.length} returned). Click a doctor to expand investigation types.
          </p>
          {report.groups.map((g) => {
            const isOpen = Boolean(expanded[g.personId])
            const Chevron = isOpen ? ChevronDown : ChevronRight

            return (
              <div key={g.personId} className="bg-card rounded-lg border border-brand-primary-border overflow-hidden">
                <button
                  type="button"
                  onClick={() => toggleExpanded(g.personId)}
                  aria-expanded={isOpen}
                  className="w-full bg-brand-primary text-white px-4 py-2.5 font-semibold flex items-center gap-3 text-left hover:bg-brand-primary/90 transition-colors"
                >
                  <Chevron className="h-4 w-4 shrink-0 opacity-90" />
                  <span className="inline-flex items-center justify-center min-w-8 h-8 rounded bg-white/15 text-sm font-mono shrink-0">
                    #{g.rank}
                  </span>
                  <span className="flex-1 min-w-0 truncate">{g.displayName}</span>
                  <span
                    className="shrink-0 rounded-md bg-white/20 px-3 py-1 text-base sm:text-lg font-mono font-bold tracking-wide tabular-nums"
                    title="Total studies"
                  >
                    {g.subtotal.total.toLocaleString()}
                  </span>
                </button>

                {isOpen && (
                  <div className="overflow-x-auto">
                    <table className="w-full text-sm">
                      <thead className="bg-muted/50">
                        <tr>
                          <th className="text-left p-3 font-medium text-brand-primary">Investigation Type</th>
                          <th className="text-right p-3 font-medium text-brand-primary">Total</th>
                          <th className="text-right p-3 font-medium text-brand-primary">Outpatient</th>
                          <th className="text-right p-3 font-medium text-brand-primary">Inpatient</th>
                        </tr>
                      </thead>
                      <tbody>
                        {g.rows.map((r) => (
                          <tr
                            key={`${g.personId}-${r.investigationType}`}
                            className="border-b border-brand-primary-border/50 hover:bg-muted/30"
                          >
                            <td className="p-3">{r.investigationType}</td>
                            <td className="p-3 text-right font-mono">{r.total}</td>
                            <td className="p-3 text-right font-mono">{r.outpatient}</td>
                            <td className="p-3 text-right font-mono">{r.inpatient}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </div>
            )
          })}

          <div className="bg-card rounded-lg border border-brand-primary-border overflow-hidden">
            <table className="w-full text-sm">
              <tbody>
                <tr className="bg-brand-accent/10 font-bold">
                  <td className="p-3">Grand Total (top {report.topN})</td>
                  <td className="p-3 text-right font-mono w-24">{report.grandTotal.total}</td>
                  <td className="p-3 text-right font-mono w-28">{report.grandTotal.outpatient}</td>
                  <td className="p-3 text-right font-mono w-24">{report.grandTotal.inpatient}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  )
}
