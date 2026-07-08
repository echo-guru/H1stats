import { useEffect, useState } from 'react'
import { LayoutDashboard, Activity, Users, PieChart, Clock } from 'lucide-react'
import { WidgetCard, StatWidget } from '../components/widgets/WidgetCard'
import { formatPersonName } from '../lib/utils'

interface DashboardSummary {
  studiesToday: number
  studiesThisWeek: number
  studiesThisMonth: number
  topPhysicians: { name: string; count: number }[]
  studyMix: { name: string; count: number; inpatient: number; outpatient: number }[]
  recentActivity: { description: string; at: string }[]
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
  recentActivity: [],
}

export default function Dashboard() {
  const [data, setData] = useState<DashboardSummary>(PLACEHOLDER)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    fetch('/api/dashboard/summary')
      .then((r) => (r.ok ? r.json() : PLACEHOLDER))
      .then(setData)
      .catch(() => setData(PLACEHOLDER))
      .finally(() => setLoading(false))
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
        <WidgetCard title="Top Reporting Physicians" icon={Users}>
          {data.topPhysicians.length === 0 ? (
            <p className="text-sm text-muted-foreground py-4 text-center">
              Connect SQL Server to populate widget data
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

      <WidgetCard title="Recent Activity" icon={Activity}>
        {data.recentActivity.length === 0 ? (
          <p className="text-sm text-muted-foreground py-2">No recent activity</p>
        ) : (
          <ul className="divide-y divide-brand-primary-border">
            {data.recentActivity.map((a, i) => (
              <li key={i} className="py-2 text-sm flex justify-between">
                <span>{a.description}</span>
                <span className="text-muted-foreground">{a.at}</span>
              </li>
            ))}
          </ul>
        )}
      </WidgetCard>
    </div>
  )
}
