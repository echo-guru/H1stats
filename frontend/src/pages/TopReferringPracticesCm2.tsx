import { useState } from 'react'
import { Building2, ChevronDown, ChevronRight, Download, Search } from 'lucide-react'
import { Button } from '../components/ui/button'

interface TestRow {
  investigationType: string
  total: number
  outpatient: number
  inpatient: number
}

interface PracticeDoctor {
  providerKey: string
  displayName: string
  providerNumber?: string | null
  rows: TestRow[]
  subtotal: { total: number; outpatient: number; inpatient: number }
}

interface PracticeGroup {
  rank: number
  practiceId: string
  practiceName: string
  suburb?: string | null
  doctors: PracticeDoctor[]
  subtotal: { total: number; outpatient: number; inpatient: number }
}

interface ReportResult {
  groups: PracticeGroup[]
  grandTotal: { total: number; outpatient: number; inpatient: number }
  topN: number
}

function toLocalDateInputValue(date = new Date()): string {
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

export default function TopReferringPracticesCm2() {
  const [dateFrom, setDateFrom] = useState(() => {
    const d = new Date()
    d.setDate(1)
    return toLocalDateInputValue(d)
  })
  const [dateTo, setDateTo] = useState(() => toLocalDateInputValue())
  const [topN, setTopN] = useState(50)
  const [report, setReport] = useState<ReportResult | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [expandedPractices, setExpandedPractices] = useState<Record<string, boolean>>({})
  const [expandedDoctors, setExpandedDoctors] = useState<Record<string, boolean>>({})

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
      const res = await fetch(`/api/clinical/cm2/top-referring-practices?${params}`)
      if (!res.ok) {
        const err = await res.json().catch(() => ({}))
        throw new Error(err.message || 'Report failed')
      }
      setReport(await res.json())
      setExpandedPractices({})
      setExpandedDoctors({})
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Report failed')
      setReport(null)
      setExpandedPractices({})
      setExpandedDoctors({})
    } finally {
      setLoading(false)
    }
  }

  const togglePractice = (id: string) => {
    setExpandedPractices((prev) => ({ ...prev, [id]: !prev[id] }))
  }

  const toggleDoctor = (practiceId: string, providerKey: string) => {
    const key = `${practiceId}::${providerKey}`
    setExpandedDoctors((prev) => ({ ...prev, [key]: !prev[key] }))
  }

  const exportCsv = () => {
    if (!report) return
    const lines = [
      'Rank,Practice,Suburb,Referring Doctor,Provider Number,Investigation Type,Total,Outpatient,Inpatient',
    ]
    for (const p of report.groups) {
      for (const d of p.doctors) {
        for (const r of d.rows) {
          lines.push(
            `${p.rank},"${p.practiceName}","${p.suburb ?? ''}","${d.displayName}","${d.providerNumber ?? ''}",` +
              `"${r.investigationType}",${r.total},${r.outpatient},${r.inpatient}`
          )
        }
      }
    }
    lines.push(
      `,"Grand Total","","","","",${report.grandTotal.total},${report.grandTotal.outpatient},${report.grandTotal.inpatient}`
    )
    const blob = new Blob([lines.join('\n')], { type: 'text/csv' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `top-referring-practices-cm2-${dateFrom}-${dateTo}-top${report.topN}.csv`
    a.click()
    URL.revokeObjectURL(url)
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-3">
        <Building2 className="h-8 w-8 text-brand-primary" />
        <div>
          <h2 className="text-2xl font-semibold text-brand-accent">Top Referring Practices - CM2</h2>
          <p className="text-sm text-gray-600">
            Referral activity by practice, then referring doctor (provider number), then investigation type
          </p>
        </div>
      </div>

      <div className="bg-white rounded-lg border border-brand-primary-border shadow-sm p-4">
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
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
            Showing top {report.topN} referring practices ({report.groups.length} returned).
            Expand a practice to see referring doctors, then expand a doctor for investigation-type breakdown.
          </p>

          {report.groups.map((p) => {
            const practiceOpen = Boolean(expandedPractices[p.practiceId])
            const PracticeChevron = practiceOpen ? ChevronDown : ChevronRight
            const practiceLabel = p.suburb
              ? `${p.practiceName} (${p.suburb})`
              : p.practiceName

            return (
              <div key={p.practiceId} className="bg-card rounded-lg border border-brand-primary-border overflow-hidden">
                <button
                  type="button"
                  onClick={() => togglePractice(p.practiceId)}
                  aria-expanded={practiceOpen}
                  className="w-full bg-brand-primary text-white px-4 py-2.5 font-semibold flex items-center gap-3 text-left hover:bg-brand-primary/90 transition-colors"
                >
                  <PracticeChevron className="h-4 w-4 shrink-0 opacity-90" />
                  <span className="inline-flex items-center justify-center min-w-8 h-8 rounded bg-white/15 text-sm font-mono shrink-0">
                    #{p.rank}
                  </span>
                  <span className="flex-1 min-w-0 truncate">{practiceLabel}</span>
                  <span
                    className="shrink-0 rounded-md bg-white/20 px-3 py-1 text-base sm:text-lg font-mono font-bold tracking-wide tabular-nums"
                    title="Total studies"
                  >
                    {p.subtotal.total.toLocaleString()}
                  </span>
                </button>

                {practiceOpen && (
                  <div className="divide-y divide-brand-primary-border/40 bg-white">
                    {p.doctors.map((d) => {
                      const doctorKey = `${p.practiceId}::${d.providerKey}`
                      const doctorOpen = Boolean(expandedDoctors[doctorKey])
                      const DoctorChevron = doctorOpen ? ChevronDown : ChevronRight
                      const doctorLabel = d.providerNumber
                        ? `${d.displayName} · ${d.providerNumber}`
                        : d.displayName

                      return (
                        <div key={d.providerKey}>
                          <button
                            type="button"
                            onClick={() => toggleDoctor(p.practiceId, d.providerKey)}
                            aria-expanded={doctorOpen}
                            className="w-full px-4 py-2.5 flex items-center gap-3 text-left hover:bg-muted/40 transition-colors"
                          >
                            <DoctorChevron className="h-4 w-4 shrink-0 text-brand-primary" />
                            <span className="flex-1 min-w-0 truncate text-sm font-medium text-brand-accent">
                              {doctorLabel}
                            </span>
                            <span
                              className="shrink-0 rounded-md bg-brand-primary-light text-brand-primary px-2.5 py-0.5 text-sm font-mono font-bold tabular-nums"
                              title="Total studies"
                            >
                              {d.subtotal.total.toLocaleString()}
                            </span>
                          </button>

                          {doctorOpen && (
                            <div className="overflow-x-auto border-t border-brand-primary-border/30 bg-muted/20">
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
                                  {d.rows.map((r) => (
                                    <tr
                                      key={`${d.providerKey}-${r.investigationType}`}
                                      className="border-b border-brand-primary-border/40 hover:bg-muted/30"
                                    >
                                      <td className="p-3 pl-10">{r.investigationType}</td>
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
