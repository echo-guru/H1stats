import { Fragment, useEffect, useState } from 'react'
import { BarChart3, Download, FileDown, Search } from 'lucide-react'
import { Button } from '../components/ui/button'
import {
  JamesReportGroupedChart,
  JamesReportSiteCompareChart,
  type JamesChartPoint,
  type JamesChartSeries,
} from '../components/JamesReportChart'
import { downloadJamesReportPdf } from '../lib/jamesReportPdf'

const SERIES_COLORS = [
  '#33758F',
  '#69A8BC',
  '#C4A35A',
  '#C47B6B',
  '#5B8A72',
  '#6B5B95',
  '#D4A017',
  '#4A6FA5',
  '#A35D6A',
  '#7A8B4A',
  '#8B5E3C',
  '#3D7A6A',
  '#5C6B73',
]

/** Echo, exercise stress echo, dobutamine stress echo, and consults. */
const DEFAULT_TESTS = ['1', '2', '3', 'consult-initial']

const KNOWN_TESTS: InvestigationTypeOption[] = [
  { id: '1', displayName: 'Transthoracic Echo' },
  { id: '2', displayName: 'Exercise Stress Echo' },
  { id: '3', displayName: 'Dobutamine Stress Echo' },
  { id: '4', displayName: 'Transoesophageal Echo' },
  { id: '5', displayName: 'Resting ECG' },
  { id: '6', displayName: 'Exercise ECG' },
  { id: '7', displayName: 'Holter Monitor' },
  { id: '8', displayName: 'Seven-Day Event Monitor' },
  { id: '9', displayName: 'Blood Pressure Monitor' },
  { id: '10', displayName: 'Coronary Angiography' },
  { id: '11', displayName: 'CT Coronary Angiography' },
  { id: '12', displayName: 'Cardiac MRI' },
  { id: 'consult-initial', displayName: 'Consultation' },
]

interface TestColumn {
  code: string
  displayName: string
  shortName: string
}

interface QuarterRow {
  year: number
  quarter: number
  label: string
  counts: number[]
}

interface YearBlock {
  year: number
  partial: boolean
  quarters: QuarterRow[]
  totals: number[]
}

interface SiteSection {
  siteId: string
  siteName: string
  years: YearBlock[]
}

interface JamesReportResult {
  dateFrom: string
  dateTo: string
  tests: TestColumn[]
  sites: SiteSection[]
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

function defaultDateFrom(): string {
  return `${new Date().getFullYear() - 2}-01-01`
}

function formatCount(n: number): string {
  return n.toLocaleString()
}

function seriesFor(tests: TestColumn[]): JamesChartSeries[] {
  return tests.map((t, i) => ({
    label: t.shortName,
    color: SERIES_COLORS[i % SERIES_COLORS.length]!,
  }))
}

function chartPointsForSite(site: SiteSection): JamesChartPoint[] {
  return site.years.flatMap((y) =>
    y.quarters.map((q) => ({
      key: `${y.year}-Q${q.quarter}`,
      yearLabel: String(y.year),
      quarterLabel: q.label,
      counts: q.counts,
    })),
  )
}

function selectedYearTotals(sites: SiteSection[], year: number) {
  return sites.map((site) => {
    const block = site.years.find((y) => y.year === year)
    return {
      siteName: site.siteName,
      counts: block?.totals ?? [],
    }
  })
}

function orderedSelection(selected: string[], catalog: InvestigationTypeOption[]): string[] {
  const known = catalog.filter((t) => selected.includes(t.id)).map((t) => t.id)
  const extras = selected.filter((id) => !catalog.some((t) => t.id === id))
  return [...known, ...extras]
}

function downloadCsv(report: JamesReportResult) {
  const headers = ['Site', 'Year', 'Period', ...report.tests.map((t) => t.shortName)]
  const lines = [headers.map((h) => `"${h.replace(/"/g, '""')}"`).join(',')]
  for (const site of report.sites) {
    for (const year of site.years) {
      for (const q of year.quarters) {
        lines.push(
          [
            `"${site.siteName}"`,
            year.year,
            `"${q.label}"`,
            ...report.tests.map((_, i) => q.counts[i] ?? 0),
          ].join(','),
        )
      }
      const totalLabel = year.partial ? `${year.year} Total (partial)` : `${year.year} Total`
      lines.push(
        [
          `"${site.siteName}"`,
          year.year,
          `"${totalLabel}"`,
          ...report.tests.map((_, i) => year.totals[i] ?? 0),
        ].join(','),
      )
    }
  }
  const blob = new Blob([lines.join('\n')], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `james-report-${report.dateFrom.slice(0, 10)}-${report.dateTo.slice(0, 10)}.csv`
  a.click()
  URL.revokeObjectURL(url)
}

function SiteTable({ site, tests }: { site: SiteSection; tests: TestColumn[] }) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm border-collapse">
        <thead>
          <tr className="border-b border-brand-primary-border text-left text-xs uppercase tracking-wide text-gray-500">
            <th className="py-2 pr-3 font-medium">Period</th>
            {tests.map((t) => (
              <th key={t.code} className="py-2 px-2 font-medium text-right" title={t.displayName}>
                {t.shortName}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {site.years.map((year) => (
            <Fragment key={year.year}>
              <tr>
                <td
                  colSpan={1 + tests.length}
                  className="pt-3 pb-1 text-xs font-semibold text-brand-accent tracking-wide"
                >
                  {year.year}
                  {year.partial ? ' · partial year' : ''}
                </td>
              </tr>
              {year.quarters.map((q) => (
                <tr key={`${year.year}-${q.quarter}`} className="border-b border-gray-100 hover:bg-muted/30">
                  <td className="py-1.5 pr-3 text-gray-700">{q.label}</td>
                  {tests.map((t, i) => (
                    <td key={t.code} className="py-1.5 px-2 text-right tabular-nums">
                      {formatCount(q.counts[i] ?? 0)}
                    </td>
                  ))}
                </tr>
              ))}
              <tr className="bg-brand-primary-light/60 font-semibold border-b border-brand-primary-border">
                <td className="py-2 pr-3">
                  {year.year} Total{year.partial ? ' (partial)' : ''}
                </td>
                {tests.map((t, i) => (
                  <td key={t.code} className="py-2 px-2 text-right tabular-nums">
                    {formatCount(year.totals[i] ?? 0)}
                  </td>
                ))}
              </tr>
            </Fragment>
          ))}
        </tbody>
      </table>
    </div>
  )
}

export default function JamesReportCm2() {
  const [dateFrom, setDateFrom] = useState(defaultDateFrom)
  const [dateTo, setDateTo] = useState(() => toLocalDateInputValue())
  const [investigationTypes, setInvestigationTypes] = useState<InvestigationTypeOption[]>(KNOWN_TESTS)
  const [selectedTypes, setSelectedTypes] = useState<string[]>(DEFAULT_TESTS)
  const [report, setReport] = useState<JamesReportResult | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [compareYear, setCompareYear] = useState<number | null>(null)

  const run = async (fromValue: string, toValue: string, types: string[]) => {
    const from = fromValue.trim()
    const to = toValue.trim() || toLocalDateInputValue()
    if (!from) {
      setError('Please select a start date')
      setLoading(false)
      return
    }
    if (types.length === 0) {
      setError('Select at least one test')
      setLoading(false)
      return
    }

    setLoading(true)
    setError('')
    try {
      const params = new URLSearchParams({ dateFrom: from, dateTo: to })
      for (const typeId of types) params.append('investigationType', typeId)
      const res = await fetch(`/api/clinical/cm2/james-report?${params}`)
      if (!res.ok) {
        const err = await res.json().catch(() => ({}))
        throw new Error(err.message || 'Report failed')
      }
      const data = (await res.json()) as JamesReportResult
      setReport(data)
      const years = data.sites[0]?.years.map((y) => y.year) ?? []
      setCompareYear((current) => (current != null && years.includes(current) ? current : years[years.length - 1] ?? null))
    } catch (e) {
      setReport(null)
      setError(e instanceof Error ? e.message : 'Report failed')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    let cancelled = false
    ;(async () => {
      try {
        const res = await fetch('/api/clinical/cm2/investigation-types?includeAll=true')
        if (res.ok) {
          const list = await res.json()
          if (!cancelled && Array.isArray(list)) {
            setInvestigationTypes(
              list.map((item: { id: string; displayName: string }) => ({
                id: String(item.id),
                displayName: item.displayName,
              })),
            )
          }
        }
      } catch {
        // Defaults still run if the type list cannot be loaded.
      }
      if (!cancelled) await run(defaultDateFrom(), toLocalDateInputValue(), DEFAULT_TESTS)
    })()
    return () => {
      cancelled = true
    }
    // Initial load only.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const toggleType = (id: string) => {
    setSelectedTypes((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]))
  }

  const catalog = investigationTypes
  const selectedOrdered = orderedSelection(selectedTypes, catalog)
  const yearsAvailable = report?.sites[0]?.years.map((y) => y.year) ?? []
  const yearMeta = new Map(report?.sites[0]?.years.map((y) => [y.year, y.partial]) ?? [])
  const series = report ? seriesFor(report.tests) : []
  const activeCompare = compareYear ?? yearsAvailable[yearsAvailable.length - 1] ?? null

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="flex items-start gap-3">
          <BarChart3 className="h-8 w-8 text-brand-primary shrink-0 mt-0.5" />
          <div>
            <h2 className="text-2xl font-semibold text-brand-accent">James Report</h2>
            <p className="text-sm text-gray-600 mt-1 max-w-2xl">
              Greenslopes, Cleveland and Toowoomba — site volumes by quarter for the date range and
              tests you select.
            </p>
          </div>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button
            type="button"
            variant="outline"
            disabled={!report}
            onClick={() => report && downloadCsv(report)}
          >
            <Download className="h-4 w-4 mr-2" />
            CSV
          </Button>
          <Button
            type="button"
            variant="brand"
            disabled={!report}
            onClick={() => report && downloadJamesReportPdf(report, activeCompare ?? undefined)}
          >
            <FileDown className="h-4 w-4 mr-2" />
            PDF
          </Button>
        </div>
      </div>

      <div className="bg-white rounded-lg border border-brand-primary-border shadow-sm p-4">
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4 items-end">
          <div>
            <label className="block text-sm font-medium text-brand-primary mb-1" htmlFor="james-from">
              Date from
            </label>
            <input
              id="james-from"
              type="date"
              value={dateFrom}
              onChange={(e) => setDateFrom(e.target.value)}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-primary"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-brand-primary mb-1" htmlFor="james-to">
              Date to
            </label>
            <input
              id="james-to"
              type="date"
              value={dateTo}
              onChange={(e) => setDateTo(e.target.value)}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-primary"
            />
          </div>
          <div className="sm:col-span-2">
            <Button
              type="button"
              variant="brand"
              onClick={() => void run(dateFrom, dateTo, selectedOrdered.length > 0 ? selectedOrdered : selectedTypes)}
              disabled={loading}
              className="gap-2"
            >
              <Search className={`h-4 w-4 ${loading ? 'animate-pulse' : ''}`} />
              {loading ? 'Running…' : 'Run report'}
            </Button>
          </div>
        </div>

        <fieldset className="mt-4">
          <legend className="text-sm font-medium text-brand-primary mb-2">Tests to include</legend>
          <div className="flex flex-wrap items-center justify-between gap-2 mb-2">
            <p className="text-xs text-gray-500 max-w-3xl">
              Tick one or more tests. Echo, exercise stress echo, dobutamine stress echo, and consults
              are selected by default. Counts stay as site volumes per quarter inside the dates above.
            </p>
            <div className="flex gap-2">
              <button
                type="button"
                className="text-xs text-brand-primary hover:underline"
                onClick={() => setSelectedTypes(DEFAULT_TESTS)}
              >
                Defaults
              </button>
              <button
                type="button"
                className="text-xs text-brand-primary hover:underline"
                onClick={() => setSelectedTypes(investigationTypes.map((t) => t.id))}
              >
                Select all
              </button>
              <button
                type="button"
                className="text-xs text-brand-primary hover:underline"
                onClick={() => setSelectedTypes([])}
              >
                Clear
              </button>
            </div>
          </div>
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-x-4 gap-y-1.5">
            {investigationTypes.map((t) => (
              <label key={t.id} className="flex items-center gap-2 text-sm text-gray-800">
                <input
                  type="checkbox"
                  checked={selectedTypes.includes(t.id)}
                  onChange={() => toggleType(t.id)}
                  className="rounded border-gray-300 text-brand-primary focus:ring-brand-primary"
                />
                {t.displayName}
              </label>
            ))}
          </div>
        </fieldset>
        {error && <p className="text-sm text-red-600 mt-3">{error}</p>}
      </div>

      {loading && !report && (
        <div className="bg-white rounded-lg border border-brand-primary-border px-6 py-16 text-center text-gray-500">
          Loading James Report…
        </div>
      )}

      {report && (
        <>
          <p className="text-xs text-gray-500">
            {report.dateFrom.slice(0, 10)} → {report.dateTo.slice(0, 10)} (inclusive).{' '}
            {report.tests.map((t) => t.displayName).join(', ')}. Studies from completed CM2 tests;
            consults from diary appointments by campus room. A quarter is listed when it overlaps the
            range; the count only covers the selected days.
          </p>

          <section className={report.tests.length > 4 ? 'grid grid-cols-1 gap-4' : 'grid grid-cols-1 xl:grid-cols-3 gap-4'}>
            {report.sites.map((site) => (
              <div
                key={site.siteId}
                className="bg-white rounded-lg border border-brand-primary-border shadow-sm p-4"
              >
                <h3 className="text-lg font-semibold text-brand-primary mb-3">{site.siteName}</h3>
                <SiteTable site={site} tests={report.tests} />
              </div>
            ))}
          </section>

          <section className="space-y-4">
            <h3 className="text-lg font-semibold text-brand-accent">Quarterly trends</h3>
            <div className="grid grid-cols-1 gap-4">
              {report.sites.map((site) => (
                <div
                  key={`chart-${site.siteId}`}
                  className="bg-white rounded-lg border border-brand-primary-border p-4"
                >
                  <JamesReportGroupedChart
                    title={site.siteName}
                    points={chartPointsForSite(site)}
                    series={series}
                  />
                </div>
              ))}
            </div>
          </section>

          {activeCompare != null && (
            <section className="bg-white rounded-lg border border-brand-primary-border p-4 space-y-3">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <h3 className="text-lg font-semibold text-brand-accent">Site comparison</h3>
                <label className="text-sm text-gray-600 flex items-center gap-2">
                  Year
                  <select
                    className="rounded-md border border-brand-primary-border bg-white px-2 py-1 text-sm"
                    value={activeCompare}
                    onChange={(e) => setCompareYear(Number(e.target.value))}
                  >
                    {yearsAvailable.map((y) => (
                      <option key={y} value={y}>
                        {y}
                        {yearMeta.get(y) ? ' (partial)' : ''}
                      </option>
                    ))}
                  </select>
                </label>
              </div>
              <JamesReportSiteCompareChart
                sites={selectedYearTotals(report.sites, activeCompare)}
                series={series}
              />
            </section>
          )}
        </>
      )}
    </div>
  )
}
