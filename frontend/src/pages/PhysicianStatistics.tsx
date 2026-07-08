import { useEffect, useState } from 'react'
import { Stethoscope, Download, Search } from 'lucide-react'
import { Button } from '../components/ui/button'
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


const OUTPATIENT_WARDS = ['cleveland', 'toowoomba', 'clev', 'twba']

export function isOutpatientWard(ward: string | null | undefined): boolean {
  if (!ward || ward.trim() === '') return true
  return OUTPATIENT_WARDS.includes(ward.trim().toLowerCase())
}

export default function PhysicianStatistics() {
  const [dateFrom, setDateFrom] = useState(() => {
    const d = new Date()
    d.setDate(1)
    return toLocalDateInputValue(d)
  })
  const [dateTo, setDateTo] = useState(() => toLocalDateInputValue())
  const [physician, setPhysician] = useState('')
  const [physicians, setPhysicians] = useState<string[]>([])
  const [report, setReport] = useState<ReportResult | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    fetch('/api/clinical/physicians')
      .then((r) => (r.ok ? r.json() : []))
      .then((list: string[]) => setPhysicians(Array.isArray(list) ? list : []))
      .catch(() => setPhysicians([]))
  }, [])

  const runReport = async () => {
    const from = dateFrom.trim()
    const to = (dateTo.trim() || toLocalDateInputValue())
    if (!from) {
      setError('Please select a start date')
      return
    }

    setLoading(true)
    setError('')
    try {
      const params = new URLSearchParams({ dateFrom: from, dateTo: to })
      if (physician) params.set('physician', physician)
      const res = await fetch(`/api/clinical/physician-statistics?${params}`)
      if (!res.ok) {
        const err = await res.json().catch(() => ({}))
        throw new Error(err.message || 'Report failed')
      }
      setReport(await res.json())
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Report failed — connect SQL Server backend')
      setReport(null)
    } finally {
      setLoading(false)
    }
  }

  const exportCsv = () => {
    if (!report) return
    const lines = ['Physician,Study Type,Total,Outpatient,Inpatient']
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
    a.download = `physician-statistics-${dateFrom}-${dateTo}.csv`
    a.click()
    URL.revokeObjectURL(url)
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-3">
        <Stethoscope className="h-8 w-8 text-brand-primary" />
        <div>
          <h2 className="text-2xl font-semibold text-brand-accent">Physician Statistics</h2>
          <p className="text-sm text-gray-600">Reporting activity by physician and study type</p>
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
            <label className="block text-sm font-medium text-brand-primary mb-1">Diagnosing Physician</label>
            <select
              value={physician}
              onChange={(e) => setPhysician(e.target.value)}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-primary bg-white"
            >
              <option value="">All Doctors</option>
              {physicians.map((p) => (
                <option key={p} value={p}>{formatPersonName(p)}</option>
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
          {report.groups.map((g) => (
            <div key={g.physician} className="bg-card rounded-lg border border-brand-primary-border overflow-hidden">
              <div className="bg-brand-primary text-white px-4 py-2.5 font-semibold">
                {formatPersonName(g.physician)}
              </div>
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead className="bg-muted/50">
                    <tr>
                      <th className="text-left p-3 font-medium text-brand-primary">Study Type</th>
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
                    <tr className="bg-brand-primary-light font-semibold">
                      <td className="p-3">Subtotal</td>
                      <td className="p-3 text-right font-mono">{g.subtotal.total}</td>
                      <td className="p-3 text-right font-mono">{g.subtotal.outpatient}</td>
                      <td className="p-3 text-right font-mono">{g.subtotal.inpatient}</td>
                    </tr>
                  </tbody>
                </table>
              </div>
            </div>
          ))}

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
