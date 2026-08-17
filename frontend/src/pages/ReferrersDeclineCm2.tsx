import { useEffect, useState, Fragment } from 'react'
import { ChevronDown, ChevronRight, Download, Search, TrendingDown } from 'lucide-react'
import { Button } from '../components/ui/button'

interface GrowthTestRow {
  investigationType: string
  previousTotal: number
  currentTotal: number
  growth: number
  growthPercent: number | null
}

interface GrowthRow {
  rank: number
  personId: string
  displayName: string
  previousTotal: number
  currentTotal: number
  growth: number
  growthPercent: number | null
  testRows: GrowthTestRow[]
}

interface ReportResult {
  currentQuarterLabel: string
  previousQuarterLabel: string
  currentFrom: string
  currentTo: string
  previousFrom: string
  previousTo: string
  rows: GrowthRow[]
  topN: number
  investigationTypeFilter?: string | null
  sortBy?: string
  siteFilter?: string | null
}

interface InvestigationTypeOption {
  id: string
  displayName: string
}

interface SiteOption {
  id: string
  displayName: string
}

function lastCompletedQuarter(asOf = new Date()): { year: number; quarter: number } {
  const month = asOf.getMonth()
  let quarter = Math.floor(month / 3)
  let year = asOf.getFullYear()
  quarter -= 1
  if (quarter < 0) {
    quarter = 3
    year -= 1
  }
  return { year, quarter: quarter + 1 }
}

function formatGrowthPct(pct: number | null, previousTotal: number, currentTotal: number): string {
  if (previousTotal === 0 && currentTotal > 0) return 'New'
  if (pct === null || pct === undefined) return '—'
  const sign = pct > 0 ? '+' : ''
  return `${sign}${pct.toFixed(1)}%`
}

function formatGrowth(n: number): string {
  const sign = n > 0 ? '+' : ''
  return `${sign}${n.toLocaleString()}`
}

function growthClass(n: number): string {
  if (n > 0) return 'text-emerald-700'
  if (n < 0) return 'text-red-700'
  return 'text-gray-600'
}

export default function ReferrersDeclineCm2() {
  const defaults = lastCompletedQuarter()
  const [year, setYear] = useState(defaults.year)
  const [quarter, setQuarter] = useState(defaults.quarter)
  const [topN, setTopN] = useState(50)
  const [sortBy, setSortBy] = useState<'growth' | 'percent'>('growth')
  const [investigationType, setInvestigationType] = useState('')
  const [investigationTypes, setInvestigationTypes] = useState<InvestigationTypeOption[]>([])
  const [site, setSite] = useState('')
  const [sites, setSites] = useState<SiteOption[]>([])
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

    fetch('/api/clinical/cm2/sites')
      .then((r) => (r.ok ? r.json() : []))
      .then((list) => {
        if (!Array.isArray(list)) {
          setSites([])
          return
        }
        setSites(
          list.map((item: { id: string; displayName: string }) => ({
            id: String(item.id),
            displayName: item.displayName,
          }))
        )
      })
      .catch(() => setSites([]))
  }, [])

  const runReport = async () => {
    const n = Number(topN)
    if (!Number.isFinite(n) || n < 1) {
      setError('Top N must be a number of at least 1')
      return
    }
    if (quarter < 1 || quarter > 4) {
      setError('Quarter must be 1–4')
      return
    }

    setLoading(true)
    setError('')
    try {
      const params = new URLSearchParams({
        year: String(year),
        quarter: String(quarter),
        top: String(Math.min(500, Math.max(1, Math.floor(n)))),
        sortBy,
        mode: 'decline',
      })
      if (investigationType) params.set('investigationType', investigationType)
      if (site) params.set('site', site)
      const res = await fetch(`/api/clinical/cm2/referrers-growth?${params}`)
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

  const toggleExpanded = (personId: string) => {
    setExpanded((prev) => ({ ...prev, [personId]: !prev[personId] }))
  }

  const exportCsv = () => {
    if (!report) return
    const lines = [
      `Rank,Referring Doctor,Investigation Type,${report.previousQuarterLabel},${report.currentQuarterLabel},Drop,Drop %`,
    ]
    for (const r of report.rows) {
      const pct =
        r.previousTotal === 0 && r.currentTotal > 0
          ? 'New'
          : r.growthPercent === null || r.growthPercent === undefined
            ? ''
            : String(r.growthPercent)
      lines.push(
        `${r.rank},"${r.displayName}","(All)",${r.previousTotal},${r.currentTotal},${r.growth},${pct}`
      )
      for (const t of r.testRows ?? []) {
        const tpct =
          t.previousTotal === 0 && t.currentTotal > 0
            ? 'New'
            : t.growthPercent === null || t.growthPercent === undefined
              ? ''
              : String(t.growthPercent)
        lines.push(
          `${r.rank},"${r.displayName}","${t.investigationType}",${t.previousTotal},${t.currentTotal},${t.growth},${tpct}`
        )
      }
    }
    const blob = new Blob([lines.join('\n')], { type: 'text/csv' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    const siteSlug = report.siteFilter
      ? `-${report.siteFilter.replace(/\s+/g, '-').toLowerCase()}`
      : ''
    a.download = `referrers-decline-cm2-${report.currentQuarterLabel.replace(/\s+/g, '-')}-top${report.topN}${siteSlug}.csv`
    a.click()
    URL.revokeObjectURL(url)
  }

  const yearOptions = Array.from({ length: 8 }, (_, i) => defaults.year - i)

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-3">
        <TrendingDown className="h-8 w-8 text-brand-primary" />
        <div>
          <h2 className="text-2xl font-semibold text-brand-accent">Referrers Decline by Quarter - CM2</h2>
          <p className="text-sm text-gray-600">
            Identify referring doctors whose referrals dropped versus the previous quarter
          </p>
        </div>
      </div>

      <div className="bg-white rounded-lg border border-brand-primary-border shadow-sm p-4">
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
          <div>
            <label className="block text-sm font-medium text-brand-primary mb-1">Year</label>
            <select
              value={year}
              onChange={(e) => setYear(Number(e.target.value))}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-primary bg-white"
            >
              {yearOptions.map((y) => (
                <option key={y} value={y}>{y}</option>
              ))}
            </select>
          </div>
          <div>
            <label className="block text-sm font-medium text-brand-primary mb-1">Quarter</label>
            <select
              value={quarter}
              onChange={(e) => setQuarter(Number(e.target.value))}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-primary bg-white"
            >
              <option value={1}>Q1 (Jan–Mar)</option>
              <option value={2}>Q2 (Apr–Jun)</option>
              <option value={3}>Q3 (Jul–Sep)</option>
              <option value={4}>Q4 (Oct–Dec)</option>
            </select>
          </div>
          <div>
            <label className="block text-sm font-medium text-brand-primary mb-1">Site</label>
            <select
              value={site}
              onChange={(e) => setSite(e.target.value)}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-primary bg-white"
            >
              <option value="">All sites</option>
              {sites.map((s) => (
                <option key={s.id} value={s.id}>{s.displayName}</option>
              ))}
            </select>
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
            <label className="block text-sm font-medium text-brand-primary mb-1">Sort by</label>
            <select
              value={sortBy}
              onChange={(e) => setSortBy(e.target.value as 'growth' | 'percent')}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-primary bg-white"
            >
              <option value="growth">Drop (count)</option>
              <option value="percent">Drop %</option>
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
            Ranking referrers who declined in <strong>{report.currentQuarterLabel}</strong>
            {' '}({report.currentFrom} – {report.currentTo}) vs{' '}
            <strong>{report.previousQuarterLabel}</strong>
            {' '}({report.previousFrom} – {report.previousTo})
            {report.investigationTypeFilter ? ` · ${report.investigationTypeFilter}` : ''}
            {report.siteFilter ? ` · ${report.siteFilter}` : ' · all sites'}
            . Only doctors with prior-quarter referrals and a negative change are shown.
            Sorted by {report.sortBy === 'percent' ? 'largest % drop' : 'largest absolute drop'}.
            Click a doctor to expand investigation types.
          </p>

          <div className="bg-card rounded-lg border border-brand-primary-border overflow-hidden">
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead className="bg-brand-primary text-white">
                  <tr>
                    <th className="text-left p-3 font-medium w-10" />
                    <th className="text-left p-3 font-medium w-16">Rank</th>
                    <th className="text-left p-3 font-medium">Referring Doctor</th>
                    <th className="text-right p-3 font-medium">{report.previousQuarterLabel}</th>
                    <th className="text-right p-3 font-medium">{report.currentQuarterLabel}</th>
                    <th className="text-right p-3 font-medium">Drop</th>
                    <th className="text-right p-3 font-medium">Drop %</th>
                  </tr>
                </thead>
                <tbody>
                  {report.rows.map((r) => {
                    const isOpen = Boolean(expanded[r.personId])
                    const Chevron = isOpen ? ChevronDown : ChevronRight
                    return (
                      <Fragment key={r.personId}>
                        <tr
                          className="border-b border-brand-primary-border/50 hover:bg-muted/30 cursor-pointer"
                          onClick={() => toggleExpanded(r.personId)}
                        >
                          <td className="p-3 text-brand-primary">
                            <Chevron className="h-4 w-4" />
                          </td>
                          <td className="p-3 font-mono text-brand-primary">#{r.rank}</td>
                          <td className="p-3 font-medium text-brand-accent">{r.displayName}</td>
                          <td className="p-3 text-right font-mono">{r.previousTotal.toLocaleString()}</td>
                          <td className="p-3 text-right font-mono font-semibold">{r.currentTotal.toLocaleString()}</td>
                          <td className={`p-3 text-right font-mono font-bold ${growthClass(r.growth)}`}>
                            {formatGrowth(r.growth)}
                          </td>
                          <td className={`p-3 text-right font-mono ${growthClass(r.growth)}`}>
                            {formatGrowthPct(r.growthPercent, r.previousTotal, r.currentTotal)}
                          </td>
                        </tr>
                        {isOpen && (r.testRows?.length ?? 0) > 0 && (
                          <tr className="bg-muted/20">
                            <td colSpan={7} className="p-0">
                              <table className="w-full text-sm">
                                <thead className="bg-muted/50">
                                  <tr>
                                    <th className="text-left p-3 pl-12 font-medium text-brand-primary">Investigation Type</th>
                                    <th className="text-right p-3 font-medium text-brand-primary">{report.previousQuarterLabel}</th>
                                    <th className="text-right p-3 font-medium text-brand-primary">{report.currentQuarterLabel}</th>
                                    <th className="text-right p-3 font-medium text-brand-primary">Drop</th>
                                    <th className="text-right p-3 font-medium text-brand-primary">Drop %</th>
                                  </tr>
                                </thead>
                                <tbody>
                                  {r.testRows.map((t) => (
                                    <tr
                                      key={`${r.personId}-${t.investigationType}`}
                                      className="border-b border-brand-primary-border/40"
                                    >
                                      <td className="p-3 pl-12">{t.investigationType}</td>
                                      <td className="p-3 text-right font-mono">{t.previousTotal.toLocaleString()}</td>
                                      <td className="p-3 text-right font-mono">{t.currentTotal.toLocaleString()}</td>
                                      <td className={`p-3 text-right font-mono font-semibold ${growthClass(t.growth)}`}>
                                        {formatGrowth(t.growth)}
                                      </td>
                                      <td className={`p-3 text-right font-mono ${growthClass(t.growth)}`}>
                                        {formatGrowthPct(t.growthPercent, t.previousTotal, t.currentTotal)}
                                      </td>
                                    </tr>
                                  ))}
                                </tbody>
                              </table>
                            </td>
                          </tr>
                        )}
                      </Fragment>
                    )
                  })}
                  {report.rows.length === 0 && (
                    <tr>
                      <td colSpan={7} className="p-6 text-center text-gray-500">
                        No referring doctors with a referral drop found for this quarter.
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
