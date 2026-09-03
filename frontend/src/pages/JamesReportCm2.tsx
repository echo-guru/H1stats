import { Fragment, useEffect, useState } from 'react'
import { BarChart3, Download, FileDown, RefreshCw } from 'lucide-react'
import { Button } from '../components/ui/button'
import {
  JamesReportGroupedChart,
  JamesReportSiteCompareChart,
  type JamesChartPoint,
} from '../components/JamesReportChart'
import { downloadJamesReportPdf } from '../lib/jamesReportPdf'

interface QuarterRow {
  year: number
  quarter: number
  label: string
  echo: number
  seDse: number
  consults: number
}

interface YearBlock {
  year: number
  partial: boolean
  quarters: QuarterRow[]
  echoTotal: number
  seDseTotal: number
  consultsTotal: number
}

interface SiteSection {
  siteId: string
  siteName: string
  years: YearBlock[]
}

interface JamesReportResult {
  dateFrom: string
  dateToExclusive: string
  currentYear: number
  sites: SiteSection[]
}

function formatCount(n: number): string {
  return n.toLocaleString()
}

function chartPointsForSite(site: SiteSection): JamesChartPoint[] {
  return site.years.flatMap((y) =>
    y.quarters.map((q) => ({
      key: `${y.year}-Q${q.quarter}`,
      yearLabel: String(y.year),
      quarterLabel: q.label,
      echo: q.echo,
      seDse: q.seDse,
      consults: q.consults,
    })),
  )
}

function selectedYearTotals(sites: SiteSection[], year: number) {
  return sites.map((site) => {
    const block = site.years.find((y) => y.year === year)
    return {
      siteName: site.siteName,
      echo: block?.echoTotal ?? 0,
      seDse: block?.seDseTotal ?? 0,
      consults: block?.consultsTotal ?? 0,
    }
  })
}

function downloadCsv(report: JamesReportResult) {
  const lines = ['Site,Year,Period,Echo,SE/DSE,Consults']
  for (const site of report.sites) {
    for (const year of site.years) {
      for (const q of year.quarters) {
        lines.push(
          `"${site.siteName}",${year.year},"${q.label}",${q.echo},${q.seDse},${q.consults}`,
        )
      }
      const totalLabel = year.partial ? `${year.year} Total (YTD)` : `${year.year} Total`
      lines.push(
        `"${site.siteName}",${year.year},"${totalLabel}",${year.echoTotal},${year.seDseTotal},${year.consultsTotal}`,
      )
    }
  }
  const blob = new Blob([lines.join('\n')], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `james-report-${report.currentYear}.csv`
  a.click()
  URL.revokeObjectURL(url)
}

function SiteTable({ site }: { site: SiteSection }) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm border-collapse">
        <thead>
          <tr className="border-b border-brand-primary-border text-left text-xs uppercase tracking-wide text-gray-500">
            <th className="py-2 pr-3 font-medium">Period</th>
            <th className="py-2 px-2 font-medium text-right">Echo</th>
            <th className="py-2 px-2 font-medium text-right">SE / DSE</th>
            <th className="py-2 pl-2 font-medium text-right">Consults</th>
          </tr>
        </thead>
        <tbody>
          {site.years.map((year) => (
            <Fragment key={year.year}>
              <tr>
                <td
                  colSpan={4}
                  className="pt-3 pb-1 text-xs font-semibold text-brand-accent tracking-wide"
                >
                  {year.year}
                  {year.partial ? ' · year to date' : ''}
                </td>
              </tr>
              {year.quarters.map((q) => (
                <tr key={`${year.year}-${q.quarter}`} className="border-b border-gray-100 hover:bg-muted/30">
                  <td className="py-1.5 pr-3 text-gray-700">{q.label}</td>
                  <td className="py-1.5 px-2 text-right tabular-nums">{formatCount(q.echo)}</td>
                  <td className="py-1.5 px-2 text-right tabular-nums">{formatCount(q.seDse)}</td>
                  <td className="py-1.5 pl-2 text-right tabular-nums">{formatCount(q.consults)}</td>
                </tr>
              ))}
              <tr className="bg-brand-primary-light/60 font-semibold border-b border-brand-primary-border">
                <td className="py-2 pr-3">
                  {year.year} Total{year.partial ? ' (YTD)' : ''}
                </td>
                <td className="py-2 px-2 text-right tabular-nums">{formatCount(year.echoTotal)}</td>
                <td className="py-2 px-2 text-right tabular-nums">{formatCount(year.seDseTotal)}</td>
                <td className="py-2 pl-2 text-right tabular-nums">{formatCount(year.consultsTotal)}</td>
              </tr>
            </Fragment>
          ))}
        </tbody>
      </table>
    </div>
  )
}

export default function JamesReportCm2() {
  const [report, setReport] = useState<JamesReportResult | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [compareYear, setCompareYear] = useState<number | null>(null)

  const load = async () => {
    setLoading(true)
    setError('')
    try {
      const res = await fetch('/api/clinical/cm2/james-report')
      if (!res.ok) {
        const err = await res.json().catch(() => ({}))
        throw new Error(err.message || 'Report failed')
      }
      const data = (await res.json()) as JamesReportResult
      setReport(data)
      setCompareYear(data.currentYear)
    } catch (e) {
      setReport(null)
      setError(e instanceof Error ? e.message : 'Report failed')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void load()
  }, [])

  const yearsAvailable =
    report?.sites[0]?.years.map((y) => y.year) ??
    (compareYear != null ? [compareYear] : [])

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="flex items-start gap-3">
          <BarChart3 className="h-8 w-8 text-brand-primary shrink-0 mt-0.5" />
          <div>
            <h2 className="text-2xl font-semibold text-brand-accent">James Report</h2>
            <p className="text-sm text-gray-600 mt-1 max-w-2xl">
              Greenslopes, Cleveland and Toowoomba — Echo (TTE), SE/DSE and consult diary bookings
              by quarter for the current year (partial) and the prior two years.
            </p>
            {report && (
              <p className="text-xs text-gray-500 mt-1">
                {report.dateFrom.slice(0, 10)} → {report.dateToExclusive.slice(0, 10)} (end exclusive).
                Echo / SE·DSE from completed CM2 studies; consults from diary appointments by campus
                room.
              </p>
            )}
          </div>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button type="button" variant="outline" onClick={() => void load()} disabled={loading}>
            <RefreshCw className={`h-4 w-4 mr-2 ${loading ? 'animate-spin' : ''}`} />
            Refresh
          </Button>
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
            onClick={() =>
              report && downloadJamesReportPdf(report, compareYear ?? report.currentYear)
            }
          >
            <FileDown className="h-4 w-4 mr-2" />
            PDF
          </Button>
        </div>
      </div>

      {error && (
        <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800">
          {error}
        </div>
      )}

      {loading && !report && (
        <div className="bg-white rounded-lg border border-brand-primary-border px-6 py-16 text-center text-gray-500">
          Loading James Report…
        </div>
      )}

      {report && (
        <>
          <section className="grid grid-cols-1 xl:grid-cols-3 gap-4">
            {report.sites.map((site) => (
              <div
                key={site.siteId}
                className="bg-white rounded-lg border border-brand-primary-border shadow-sm p-4"
              >
                <h3 className="text-lg font-semibold text-brand-primary mb-3">{site.siteName}</h3>
                <SiteTable site={site} />
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
                  />
                </div>
              ))}
            </div>
          </section>

          <section className="bg-white rounded-lg border border-brand-primary-border p-4 space-y-3">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <h3 className="text-lg font-semibold text-brand-accent">Site comparison</h3>
              <label className="text-sm text-gray-600 flex items-center gap-2">
                Year
                <select
                  className="rounded-md border border-brand-primary-border bg-white px-2 py-1 text-sm"
                  value={compareYear ?? report.currentYear}
                  onChange={(e) => setCompareYear(Number(e.target.value))}
                >
                  {yearsAvailable.map((y) => (
                    <option key={y} value={y}>
                      {y}
                      {y === report.currentYear ? ' (YTD)' : ''}
                    </option>
                  ))}
                </select>
              </label>
            </div>
            <JamesReportSiteCompareChart
              sites={selectedYearTotals(report.sites, compareYear ?? report.currentYear)}
            />
          </section>
        </>
      )}
    </div>
  )
}
