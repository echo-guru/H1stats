import { useEffect, useState } from 'react'
import { Activity, CheckCircle, XCircle } from 'lucide-react'
import { AdminBackLink } from '../components/AdminBackLink'

interface HealthStatus {
  status: string
  version: string
  sqlServer: { connected: boolean; message: string }
  uptime: string
}

export default function SystemHealth() {
  const [health, setHealth] = useState<HealthStatus | null>(null)

  useEffect(() => {
    fetch('/api/health')
      .then((r) => (r.ok ? r.json() : null))
      .then(setHealth)
      .catch(() => setHealth(null))
  }, [])

  return (
    <div className="space-y-6">
      <AdminBackLink />
      <div className="flex items-center gap-3">
        <Activity className="h-8 w-8 text-brand-primary" />
        <div>
          <h2 className="text-2xl font-semibold text-brand-accent">System Health</h2>
          <p className="text-sm text-gray-600">API and database connection status</p>
        </div>
      </div>

      <div className="bg-white rounded-lg border border-brand-primary-border shadow-sm p-6 space-y-4 max-w-lg">
        {!health ? (
          <p className="text-sm text-muted-foreground">Backend not running — start ASP.NET API on port 5002</p>
        ) : (
          <>
            <div className="flex justify-between text-sm">
              <span className="text-brand-primary font-medium">API Status</span>
              <span className="flex items-center gap-1 text-emerald-700">
                <CheckCircle className="h-4 w-4" /> {health.status}
              </span>
            </div>
            <div className="flex justify-between text-sm">
              <span className="text-brand-primary font-medium">Version</span>
              <span>{health.version}</span>
            </div>
            <div className="flex justify-between text-sm">
              <span className="text-brand-primary font-medium">Uptime</span>
              <span>{health.uptime}</span>
            </div>
            <div className="flex justify-between text-sm">
              <span className="text-brand-primary font-medium">SQL Server</span>
              <span className={`flex items-center gap-1 ${health.sqlServer.connected ? 'text-emerald-700' : 'text-red-700'}`}>
                {health.sqlServer.connected ? <CheckCircle className="h-4 w-4" /> : <XCircle className="h-4 w-4" />}
                {health.sqlServer.message}
              </span>
            </div>
          </>
        )}
      </div>
    </div>
  )
}
