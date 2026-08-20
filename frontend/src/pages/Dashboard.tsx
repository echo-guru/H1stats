import { useEffect, useState } from 'react'
import { LayoutDashboard, Activity, Users, PieChart, Clock, LineChart } from 'lucide-react'
import { WidgetCard, StatWidget } from '../components/widgets/WidgetCard'
import {
  Cm2VolumeTrendChart,
  monthTotal,
  volumeMonthsFromDrSummary,
  type Cm2VolumeMonth,
} from '../components/Cm2VolumeTrendChart'
import { formatPersonName } from '../lib/utils'

interface DashboardSummary {
  studiesToday: number
  studiesThisWeek: number
  studiesThisMonth: number
  topPhysicians: { name: string; count: number }[]
  studyMix: { name: string; count: number; inpatient: number; outpatient: number }[]
}

function formatInOutRatio(inpatient: number, outpatient: number): string {
  if (inpatient === 0 && outpatient === 0) return '—'
  const gcd = (a: number, b: number): number => (b === 0 ? a : gcd(b, a % b))
  const divisor = gcd(inpatient, outpatient) || 1
  return `${inpatient / divisor}:${outpatient / divisor}`
}

const PLACEHOLDER: DashboardSummary = {
  studiesToday: 0,
  studiesThisWeek: 0,
  studiesThisMonth: 0,
  topPhysicians: [],
  studyMix: [],
}

const ALL_DRS_TOTAL_ID = 'all-drs-total'
const HEARTS1ST_IDS = ['282', '2901', '1681', '1941', '144', '981']

interface DrSummaryHistory {
  columns: { id: string; initials: string; displayName: string }[]
  rows: { key: string; counts: number[] }[]
}

async function fetchDrSummaryHistory(physician: string, year: number, month: number): Promise<DrSummaryHistory> {
  const params = new URLSearchParams({
    physician,
    year: String(year),
    month: String(month),
  })
  const res = await fetch(`/api/clinical/cm2/dr-summary/history?${params}`)
  if (!res.ok) {
    const err = await res.json().catch(() => ({}))
    throw new Error(err.message || 'CM2 history failed')
  }
  return res.json()
}

function sumDrSummaryHistories(histories: DrSummaryHistory[]): DrSummaryHistory {
  const base = histories[0]
  if (!base) throw new Error('CM2 history failed')
  return {
    columns: base.columns,
    rows: base.rows.map((row, rowIndex) => ({
      ...row,
      counts: row.counts.map((_, monthIndex) =>
        histories.reduce((sum, history) => sum + (history.rows[rowIndex]?.counts[monthIndex] ?? 0), 0),
      ),
    })),
  }
}

async function loadCm2VolumeTrend(): Promise<Cm2VolumeMonth[]> {
  const now = new Date()
  const year = now.getFullYear()
  const month = now.getMonth() + 1
  const combined = await fetchDrSummaryHistory(ALL_DRS_TOTAL_ID, year, month)
  const fromCombined = volumeMonthsFromDrSummary(combined.columns, combined.rows)
  if (fromCombined.some((m) => monthTotal(m) > 0)) return fromCombined

  const histories = await Promise.all(HEARTS1ST_IDS.map((id) => fetchDrSummaryHistory(id, year, month)))
  const summed = sumDrSummaryHistories(histories)
  return volumeMonthsFromDrSummary(summed.columns, summed.rows)
}

export default function Dashboard() {
  const [data, setData] = useState<DashboardSummary>(PLACEHOLDER)
  const [loading, setLoading] = useState(true)
  const [cm2Trend, setCm2Trend] = useState<Cm2VolumeMonth[]>([])
  const [cm2TrendLoading, setCm2TrendLoading] = useState(true)
  const [cm2TrendError, setCm2TrendError] = useState('')

  useEffect(() => {
    fetch('/api/dashboard/summary')
      .then((r) => (r.ok ? r.json() : PLACEHOLDER))
      .then(setData)
      .catch(() => setData(PLACEHOLDER))
      .finally(() => setLoading(false))

    loadCm2VolumeTrend()
      .then(setCm2Trend)
      .catch((e) => setCm2TrendError(e instanceof Error ? e.message : 'CM2 trend failed'))
      .finally(() => setCm2TrendLoading(false))
  }, [])

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-3">
        <LayoutDashboard className="h-8 w-8 text-brand-primary" />
        <div>
          <h2 className="text-2xl font-semibold text-brand-accent">Dashboard</h2>
          <p className="text-sm text-gray-600">What happened yesterday, what is happening today</p>
        </div>
      </div>

      {loading && (
        <p className="text-sm text-muted-foreground">Loading dashboard…</p>
      )}

      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
        <WidgetCard title="Studies Today" icon={Activity}>
          <StatWidget label="Studies" value={data.studiesToday} sublabel="Today" />
        </WidgetCard>
        <WidgetCard title="Studies This Week" icon={Clock}>
          <StatWidget label="Studies" value={data.studiesThisWeek} sublabel="Mon–Sun" />
        </WidgetCard>
        <WidgetCard title="Studies This Month" icon={Clock}>
          <StatWidget label="Studies" value={data.studiesThisMonth} sublabel="Calendar month" />
        </WidgetCard>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <WidgetCard title="Top reporting Drs (CM2, this month)" icon={Users}>
          {data.topPhysicians.length === 0 ? (
            <p className="text-sm text-muted-foreground py-4 text-center">
              Hearts 1st reporting cardiologist volumes will appear here
            </p>
          ) : (
            <ul className="space-y-2">
              {data.topPhysicians.map((p) => (
                <li key={p.name} className="flex justify-between text-sm">
                  <span className="text-gray-700">{formatPersonName(p.name)}</span>
                  <span className="font-semibold text-brand-primary">{p.count}</span>
                </li>
              ))}
            </ul>
          )}
        </WidgetCard>

        <WidgetCard title="Study Mix" icon={PieChart}>
          {data.studyMix.length === 0 ? (
            <p className="text-sm text-muted-foreground py-4 text-center">
              Study type breakdown will appear here
            </p>
          ) : (
            <div className="space-y-2">
              <div className="grid grid-cols-[1fr_auto_auto] gap-4 text-xs font-medium text-brand-primary px-1">
                <span>Study type</span>
                <span className="text-right">IP:OP</span>
                <span className="text-right w-10">Total</span>
              </div>
              <ul className="space-y-2">
                {data.studyMix.map((s) => (
                  <li key={s.name} className="grid grid-cols-[1fr_auto_auto] gap-4 text-sm items-center">
                    <span className="text-gray-700">{s.name}</span>
                    <span className="font-mono text-muted-foreground text-right">
                      {formatInOutRatio(s.inpatient, s.outpatient)}
                    </span>
                    <span className="font-semibold text-brand-primary text-right w-10">{s.count}</span>
                  </li>
                ))}
              </ul>
            </div>
          )}
        </WidgetCard>
      </div>

      <WidgetCard title="Hearts 1st tests (CM2, 12 months)" icon={LineChart}>
        {cm2TrendLoading ? (
          <p className="text-sm text-muted-foreground py-4 text-center">Loading 12-month CM2 volume…</p>
        ) : cm2TrendError ? (
          <p className="text-sm text-red-600 py-4 text-center">{cm2TrendError}</p>
        ) : cm2Trend.length === 0 || cm2Trend.every((m) => monthTotal(m) === 0) ? (
          <p className="text-sm text-muted-foreground py-4 text-center">
            Combined Dr Summary totals will appear here
          </p>
        ) : (
          <div className="space-y-1">
            <p className="text-xs text-gray-500">
              All Hearts 1st reporting cardiologists. Total, then Echo, stress echo/Dob (SE + DSE), TEE / IOE, and
              consults. Each graph uses its own scale.
            </p>
            <Cm2VolumeTrendChart months={cm2Trend} />
          </div>
        )}
      </WidgetCard>
    </div>
  )
}
