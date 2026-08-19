import { Fragment, useState } from 'react'
import { ChevronLeft, ChevronRight, ClipboardList, Download, PieChart, Search } from 'lucide-react'
import { Button } from '../components/ui/button'
import { DrSummaryPieGrid, buildDoctorPies } from '../components/DrSummaryPieGrid'
import { MonthTotalChart } from '../components/MonthTotalChart'

interface SummaryColumn {
  id: string
  initials: string
  displayName: string
}

interface SummaryRow {
  key: string
  label: string
  kind: 'item' | 'total' | 'subtotal' | 'spacer'
  counts: number[]
}

interface ReportResult {
  year: number
  month: number
  label: string
  dateFrom: string
  dateToExclusive: string
  columns: SummaryColumn[]
  rows: SummaryRow[]
  unassignedDccs?: number
}

function currentYearMonth(asOf = new Date()): string {
  const y = asOf.getFullYear()
  const m = String(asOf.getMonth() + 1).padStart(2, '0')
  return `${y}-${m}`
}

function formatCount(kind: SummaryRow['kind'], n: number): string {
  if (kind === 'spacer') return ''
  return n.toLocaleString()
}

function rowClass(kind: SummaryRow['kind']): string {
  if (kind === 'total') return 'bg-brand-primary-light font-semibold'
  if (kind === 'subtotal') return 'bg-brand-accent/10 font-bold'
  if (kind === 'spacer') return 'h-3 bg-white'
  return 'hover:bg-muted/30'
}

export default function DrSummaryCm2() {
  const [yearMonth, setYearMonth] = useState(currentYearMonth)
  const [report, setReport] = useState<ReportResult | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [chartView, setChartView] = useState(false)
  const [history, setHistory] = useState<ReportResult | null>(null)
  const [historyDoctor, setHistoryDoctor] = useState<SummaryColumn | null>(null)
  const [historyLoading, setHistoryLoading] = useState(false)
  const [expandedTrend, setExpandedTrend] = useState<string | null>(null)

  const runReport = async () => {
    const [year, month] = yearMonth.split('-').map(Number)
    if (!year || !month) {
      setError('Please select a month')
      return
    }

    setLoading(true)
    setError('')
    try {
      const params = new URLSearchParams({ year: String(year), month: String(month) })
      const res = await fetch(`/api/clinical/cm2/dr-summary?${params}`)
      if (!res.ok) {
        const err = await res.json().catch(() => ({}))
        throw new Error(err.message || 'Report failed')
      }
      setReport(await res.json())
      setHistory(null)
      setHistoryDoctor(null)
      setExpandedTrend(null)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Report failed')
      setReport(null)
    } finally {
      setLoading(false)
    }
  }

  const openDoctorHistory = async (doctor: SummaryColumn) => {
    const [year, month] = yearMonth.split('-').map(Number)
    if (!year || !month) {
      setError('Please select a month')
      return
    }

    setHistoryLoading(true)
    setError('')
    try {
      const params = new URLSearchParams({
        physician: doctor.id,
        year: String(year),
        month: String(month),
      })
      const res = await fetch(`/api/clinical/cm2/dr-summary/history?${params}`)
      if (!res.ok) {
        const err = await res.json().catch(() => ({}))
        throw new Error(err.message || 'History failed')
      }
      setHistory(await res.json())
      setHistoryDoctor(doctor)
      setChartView(false)
      setExpandedTrend(null)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'History failed')
    } finally {
      setHistoryLoading(false)
    }
  }

  const closeHistory = () => {
    setHistory(null)
    setHistoryDoctor(null)
    setExpandedTrend(null)
  }

  const toggleTrend = (rowKey: string) => {
    setExpandedTrend((current) => (current === rowKey ? null : rowKey))
  }

  const activeReport = history ?? report

  const exportCsv = () => {
    const source = activeReport
    if (!source) return
    const header = ['OOS', ...source.columns.map((c) => c.initials)]
    const lines = [header.join(',')]
    for (const row of source.rows) {
      if (row.kind === 'spacer') {
        lines.push('')
        continue
      }
      lines.push([`"${row.label}"`, ...row.counts.map(String)].join(','))
    }
    const blob = new Blob([lines.join('\n')], { type: 'text/csv' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    const slug = historyDoctor
      ? `dr-summary-cm2-${historyDoctor.initials}-${yearMonth}-12m`
      : `dr-summary-cm2-${yearMonth}`
    a.download = `${slug}.csv`
    a.click()
    URL.revokeObjectURL(url)
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-3">
        <ClipboardList className="h-8 w-8 text-brand-primary" />
        <div>
          <h2 className="text-2xl font-semibold text-brand-accent">Dr Summary - CM2</h2>
          <p className="text-sm text-gray-600">
            Monthly reporting activity by cardiologist and occasion of service (Oracle CM2 / Hearts1st)
          </p>
        </div>
      </div>

      <div className="bg-white rounded-lg border border-brand-primary-border shadow-sm p-4">
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
          <div>
            <label className="block text-sm font-medium text-brand-primary mb-1">Month</label>
            <input
              type="month"
              value={yearMonth}
              onChange={(e) => setYearMonth(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') runReport()
              }}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-primary"
            />
          </div>
          <div className="flex items-end gap-2 sm:col-span-1 lg:col-span-3">
            <Button variant="brand" onClick={runReport} disabled={loading} className="gap-2">
              <Search className="h-4 w-4" />
              {loading ? 'Running…' : 'Run Report'}
            </Button>
            {report && (
              <>
                <Button variant="outline" onClick={exportCsv} className="gap-2">
                  <Download className="h-4 w-4" />
                  CSV
                </Button>
                {!history && (
                  <Button
                    variant={chartView ? 'brand' : 'outline'}
                    onClick={() => setChartView((v) => !v)}
                    className="px-3 shrink-0"
                    title={chartView ? 'Show table' : 'Show pie charts'}
                    aria-label={chartView ? 'Show table' : 'Show pie charts'}
                    aria-pressed={chartView}
                  >
                    <PieChart className="h-5 w-5" />
                  </Button>
                )}
              </>
            )}
          </div>
        </div>
        {error && <p className="text-sm text-red-600 mt-3">{error}</p>}
      </div>

      {activeReport && (
        <div className="space-y-3">
          {historyDoctor ? (
            <div className="flex items-center gap-3">
              <Button variant="outline" onClick={closeHistory} className="gap-1 px-3">
                <ChevronLeft className="h-4 w-4" />
                All doctors
              </Button>
              <p className="text-sm text-gray-600">
                <span className="font-semibold text-brand-accent">{historyDoctor.displayName}</span>
                {' · '}
                {activeReport.label}. Click a test type to chart the 12-month trend; click again to hide.
              </p>
            </div>
          ) : (
            <p className="text-sm text-gray-600">
              {activeReport.label}. Click a cardiologist for a 12-month view. Echo / DSE / SE / TEE from
              study requested (<code>TEST_REQUIRED</code>); consults combine initial, follow-up, and
              derivatives.
            </p>
          )}
          {historyLoading && <p className="text-sm text-brand-primary">Loading 12-month history…</p>}
          {!history && activeReport.unassignedDccs ? (
            <p className="text-sm text-amber-800">
              {activeReport.unassignedDccs} DCCS appointment{activeReport.unassignedDccs === 1 ? '' : 's'} could
              not be matched to a reporting cardiologist.
            </p>
          ) : null}

          {chartView && !history ? (
            <div className="space-y-3">
              <p className="text-sm text-gray-600">Echo, SE, DSE, and Consults totals for each cardiologist. Click a name for 12-month history.</p>
              <DrSummaryPieGrid
                doctors={buildDoctorPies(activeReport.columns, activeReport.rows)}
                onSelectDoctor={(id) => {
                  const col = report?.columns.find((c) => c.id === id)
                  if (col) void openDoctorHistory(col)
                }}
              />
            </div>
          ) : (
            <div className="bg-card rounded-lg border border-brand-primary-border overflow-hidden">
              <div className="overflow-x-auto">
                <table className="w-full text-sm border-collapse">
                  <thead>
                    <tr className="bg-brand-primary text-white">
                      <th className="text-left p-2.5 font-semibold sticky left-0 bg-brand-primary min-w-[8.5rem]">
                        OOS
                      </th>
                      {activeReport.columns.map((c) => (
                        <th
                          key={c.id}
                          className="text-right p-2.5 font-semibold min-w-[3.5rem] whitespace-nowrap"
                          title={c.displayName}
                        >
                          {history ? (
                            c.initials
                          ) : (
                            <button
                              type="button"
                              onClick={() => openDoctorHistory(c)}
                              className="hover:underline underline-offset-2"
                            >
                              {c.initials}
                            </button>
                          )}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {activeReport.rows.map((row) => {
                      if (row.kind === 'spacer') {
                        return (
                          <tr key={row.key} className="h-3 bg-white">
                            <td colSpan={activeReport.columns.length + 1} className="p-0" />
                          </tr>
                        )
                      }

                      const trendOpen = Boolean(history) && expandedTrend === row.key

                      return (
                        <Fragment key={row.key}>
                          <tr
                            className={`border-b border-brand-primary-border/60 ${rowClass(row.kind)} ${
                              trendOpen ? 'bg-brand-primary-light' : ''
                            } ${history ? 'cursor-pointer' : ''}`}
                            onClick={history ? () => toggleTrend(row.key) : undefined}
                          >
                            <td className="p-2.5 sticky left-0 bg-inherit whitespace-nowrap">
                              {history ? (
                                <button
                                  type="button"
                                  onClick={(e) => {
                                    e.stopPropagation()
                                    toggleTrend(row.key)
                                  }}
                                  aria-expanded={trendOpen}
                                  className="inline-flex items-center gap-1.5 text-left hover:underline underline-offset-2"
                                  title={trendOpen ? `Hide ${row.label} trend` : `Show ${row.label} over 12 months`}
                                >
                                  <ChevronRight
                                    className={`h-3.5 w-3.5 shrink-0 text-brand-primary transition-transform duration-300 ${
                                      trendOpen ? 'rotate-90' : ''
                                    }`}
                                  />
                                  {row.label}
                                </button>
                              ) : (
                                row.label
                              )}
                            </td>
                            {row.counts.map((n, i) => (
                              <td key={`${row.key}-${i}`} className="p-2.5 text-right font-mono tabular-nums">
                                {formatCount(row.kind, n)}
                              </td>
                            ))}
                          </tr>
                          {history ? (
                            <tr className={trendOpen ? 'border-b border-brand-primary-border/60' : ''}>
                              <td colSpan={activeReport.columns.length + 1} className="p-0">
                                <div
                                  className={`grid transition-[grid-template-rows] duration-300 ease-out ${
                                    trendOpen ? 'grid-rows-[1fr]' : 'grid-rows-[0fr]'
                                  }`}
                                >
                                  <div className="overflow-hidden min-h-0">
                                    <div className="px-4 py-3 bg-gradient-to-b from-brand-primary-light/80 to-white">
                                      <MonthTotalChart
                                        months={activeReport.columns.map((c, i) => ({
                                          yearMonth: c.id,
                                          label: c.displayName || c.initials,
                                          total: row.counts[i] ?? 0,
                                        }))}
                                        title={`${row.label} · ${historyDoctor?.displayName ?? ''}`}
                                        compact
                                        framed={false}
                                        variant="line"
                                      />
                                    </div>
                                  </div>
                                </div>
                              </td>
                            </tr>
                          ) : null}
                        </Fragment>
                      )
                    })}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  )
}
