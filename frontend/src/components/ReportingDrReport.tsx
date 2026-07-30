import { useEffect, useState } from 'react'
import { ChevronDown, ChevronRight, Download, Search, type LucideIcon } from 'lucide-react'
import { Button } from './ui/button'
import { formatPersonName } from '../lib/utils'

interface PhysicianStatRow {
  physician: string
  studyType: string
  total: number
  outpatient: number
  inpatient: number
}

interface PhysicianGroup {
  physician: string
  rows: PhysicianStatRow[]
  subtotal: { total: number; outpatient: number; inpatient: number }
}

interface ReportResult {
  groups: PhysicianGroup[]
  grandTotal: { total: number; outpatient: number; inpatient: number }
}

function toLocalDateInputValue(date = new Date()): string {
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

export interface ReportingDrReportProps {
  title: string
  subtitle: string
  icon: LucideIcon
  physiciansUrl: string
  reportUrl: string
  csvPrefix: string
  /** When true, physicians API returns { id, displayName } objects (CM2). */
  physicianOptions?: boolean
  /** How to format physician names in the UI. Defaults to HL7/Syngo formatting. */
  formatPhysicianName?: (name: string) => string
  /** Column / CSV label for study/investigation type. Defaults to "Study Type". */
  studyTypeLabel?: string
  /** Filter dropdown label. Defaults to "Diagnosing Physician". */
  physicianFilterLabel?: string
  /**
   * When true, doctor headers show totals inline and investigation-type
   * breakdown is collapsed until the header is clicked.
   */
  collapsibleGroups?: boolean
  /** Optional notice shown above the results table. */
  resultsNotice?: string
}

export default function ReportingDrReport({
  title,
  subtitle,
  icon: Icon,
  physiciansUrl,
  reportUrl,
  csvPrefix,
  physicianOptions = false,
  formatPhysicianName = formatPersonName,
  studyTypeLabel = 'Study Type',
  physicianFilterLabel = 'Diagnosing Physician',
  collapsibleGroups = false,
  resultsNotice,
}: ReportingDrReportProps) {
  const [dateFrom, setDateFrom] = useState(() => {
    const d = new Date()
    d.setDate(1)
    return toLocalDateInputValue(d)
  })
  const [dateTo, setDateTo] = useState(() => toLocalDateInputValue())
  const [physician, setPhysician] = useState('')
  const [physicians, setPhysicians] = useState<string[]>([])
  const [physicianChoices, setPhysicianChoices] = useState<{ id: string; displayName: string }[]>([])
  const [report, setReport] = useState<ReportResult | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [expanded, setExpanded] = useState<Record<string, boolean>>({})

  useEffect(() => {
    fetch(physiciansUrl)
      .then((r) => (r.ok ? r.json() : []))
      .then((list) => {
        if (physicianOptions && Array.isArray(list)) {
          setPhysicianChoices(
            list.map((item: { id: string; displayName: string }) => ({
              id: String(item.id),
              displayName: item.displayName,
            }))
          )
          setPhysicians([])
        } else {
          setPhysicians(Array.isArray(list) ? list : [])
          setPhysicianChoices([])
        }
      })
      .catch(() => {
        setPhysicians([])
        setPhysicianChoices([])
      })
  }, [physiciansUrl, physicianOptions])

  const runReport = async () => {
    const from = dateFrom.trim()
    const to = dateTo.trim() || toLocalDateInputValue()
    if (!from) {
      setError('Please select a start date')
      return
    }

    setLoading(true)
    setError('')
    try {
      const params = new URLSearchParams({ dateFrom: from, dateTo: to })
      if (physician) params.set('physician', physician)
      const res = await fetch(`${reportUrl}?${params}`)
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
    const lines = [`Physician,${studyTypeLabel},Total,Outpatient,Inpatient`]
    for (const g of report.groups) {
      for (const r of g.rows) {
        lines.push(`"${r.physician}","${r.studyType}",${r.total},${r.outpatient},${r.inpatient}`)
      }
      lines.push(`"${g.physician}","Subtotal",${g.subtotal.total},${g.subtotal.outpatient},${g.subtotal.inpatient}`)
    }
    lines.push(`"Grand Total","",${report.grandTotal.total},${report.grandTotal.outpatient},${report.grandTotal.inpatient}`)
    const blob = new Blob([lines.join('\n')], { type: 'text/csv' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `${csvPrefix}-${dateFrom}-${dateTo}.csv`
    a.click()
    URL.revokeObjectURL(url)
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-3">
        <Icon className="h-8 w-8 text-brand-primary" />
        <div>
          <h2 className="text-2xl font-semibold text-brand-accent">{title}</h2>
          <p className="text-sm text-gray-600">{subtitle}</p>
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
            <label className="block text-sm font-medium text-brand-primary mb-1">{physicianFilterLabel}</label>
            <select
              value={physician}
              onChange={(e) => setPhysician(e.target.value)}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-primary bg-white"
            >
              <option value="">All Doctors</option>
              {physicianOptions
                ? physicianChoices.map((p) => (
                    <option key={p.id} value={p.id}>{formatPhysicianName(p.displayName)}</option>
                  ))
                : physicians.map((p) => (
                    <option key={p} value={p}>{formatPhysicianName(p)}</option>
                  ))}
            </select>
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
          {resultsNotice && (
            <div className="rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-950">
              {resultsNotice}
            </div>
          )}
          {report.groups.map((g) => {
            const isOpen = collapsibleGroups ? Boolean(expanded[g.physician]) : true
            const Chevron = isOpen ? ChevronDown : ChevronRight

            return (
              <div key={g.physician} className="bg-card rounded-lg border border-brand-primary-border overflow-hidden">
                {collapsibleGroups ? (
                  <button
                    type="button"
                    onClick={() => toggleExpanded(g.physician)}
                    aria-expanded={isOpen}
                    className="w-full bg-brand-primary text-white px-4 py-2.5 font-semibold flex items-center gap-3 text-left hover:bg-brand-primary/90 transition-colors"
                  >
                    <Chevron className="h-4 w-4 shrink-0 opacity-90" />
                    <span className="flex-1 min-w-0 truncate">{formatPhysicianName(g.physician)}</span>
                    <span
                      className="shrink-0 rounded-md bg-white/20 px-3 py-1 text-base sm:text-lg font-mono font-bold tracking-wide tabular-nums"
                      title="Total studies"
                    >
                      {g.subtotal.total.toLocaleString()}
                    </span>
                  </button>
                ) : (
                  <div className="bg-brand-primary text-white px-4 py-2.5 font-semibold">
                    {formatPhysicianName(g.physician)}
                  </div>
                )}

                {isOpen && (
                  <div className="overflow-x-auto">
                    <table className="w-full text-sm">
                      <thead className="bg-muted/50">
                        <tr>
                          <th className="text-left p-3 font-medium text-brand-primary">{studyTypeLabel}</th>
                          <th className="text-right p-3 font-medium text-brand-primary">Total</th>
                          <th className="text-right p-3 font-medium text-brand-primary">Outpatient</th>
                          <th className="text-right p-3 font-medium text-brand-primary">Inpatient</th>
                        </tr>
                      </thead>
                      <tbody>
                        {g.rows.map((r) => (
                          <tr key={`${g.physician}-${r.studyType}`} className="border-b border-brand-primary-border/50 hover:bg-muted/30">
                            <td className="p-3">{r.studyType}</td>
                            <td className="p-3 text-right font-mono">{r.total}</td>
                            <td className="p-3 text-right font-mono">{r.outpatient}</td>
                            <td className="p-3 text-right font-mono">{r.inpatient}</td>
                          </tr>
                        ))}
                        {!collapsibleGroups && (
                          <tr className="bg-brand-primary-light font-semibold">
                            <td className="p-3">Subtotal</td>
                            <td className="p-3 text-right font-mono">{g.subtotal.total}</td>
                            <td className="p-3 text-right font-mono">{g.subtotal.outpatient}</td>
                            <td className="p-3 text-right font-mono">{g.subtotal.inpatient}</td>
                          </tr>
                        )}
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
                  <td className="p-3">Grand Total</td>
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
