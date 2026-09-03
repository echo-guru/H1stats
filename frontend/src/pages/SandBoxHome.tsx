import { useCallback, useEffect, useState } from 'react'
import { AlertTriangle, CheckCircle, FlaskConical, RefreshCw, XCircle } from 'lucide-react'
import { Button } from '../components/ui/button'
import WorkllistsHome from './WorkllistsHome'

interface SandBoxStatus {
  dataset: string
  isDev: boolean
  endpoint: string
  schema: string
  connected: boolean
  message: string
  testedAt?: string | null
}

export default function SandBoxHome() {
  const [status, setStatus] = useState<SandBoxStatus | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const loadStatus = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      const res = await fetch('/api/sandbox/status')
      const data = await res.json().catch(() => ({}))
      if (!res.ok) throw new Error(data.message || 'Failed to check CM2 DEV connection')
      setStatus(data as SandBoxStatus)
    } catch (e) {
      setStatus(null)
      setError(e instanceof Error ? e.message : 'Failed to check CM2 DEV connection')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void loadStatus()
  }, [loadStatus])

  return (
    <div className="space-y-6">
      <div
        role="alert"
        className="rounded-lg border-4 border-amber-500 bg-amber-100 px-6 py-8 text-center shadow-sm"
      >
        <div className="flex flex-col items-center gap-3">
          <AlertTriangle className="h-12 w-12 text-amber-700" aria-hidden />
          <p className="text-3xl sm:text-4xl font-bold tracking-wide text-amber-950 uppercase">
            DEV dataset only
          </p>
          <p className="max-w-2xl text-base sm:text-lg text-amber-900 font-medium">
            SandBox connects exclusively to the CM2 DEV Oracle database (CM2DEV).
            Do not treat data here as production. Changes in SandBox must never target the live CM2 schedule.
          </p>
        </div>
      </div>

      <div className="flex items-center gap-3">
        <FlaskConical className="h-8 w-8 text-brand-primary" />
        <div>
          <h2 className="text-2xl font-semibold text-brand-accent">SandBox</h2>
          <p className="text-sm text-gray-600">Super Admin only — experimental tools against CM2 DEV</p>
        </div>
      </div>

      <div className="bg-white rounded-lg border border-brand-primary-border shadow-sm p-4 space-y-3 max-w-xl">
        <div className="flex items-center justify-between gap-3">
          <h3 className="text-sm font-semibold text-brand-primary">CM2 DEV connection</h3>
          <Button type="button" variant="outline" size="sm" onClick={() => void loadStatus()} disabled={loading}>
            <RefreshCw className={`h-3.5 w-3.5 mr-1.5 ${loading ? 'animate-spin' : ''}`} />
            {loading ? 'Checking…' : 'Retest'}
          </Button>
        </div>

        {error && (
          <p className="text-sm text-red-700 bg-red-50 border border-red-100 rounded-md px-3 py-2">{error}</p>
        )}

        {status && (
          <div
            className={`flex items-start gap-2 text-sm p-3 rounded-lg ${
              status.connected ? 'bg-emerald-50 text-emerald-900' : 'bg-red-50 text-red-900'
            }`}
          >
            {status.connected ? (
              <CheckCircle className="h-4 w-4 shrink-0 mt-0.5" />
            ) : (
              <XCircle className="h-4 w-4 shrink-0 mt-0.5" />
            )}
            <div className="space-y-1">
              <p>
                <strong>{status.endpoint}</strong>
                {status.connected ? ' — connected' : ' — not connected'}
              </p>
              <p className="text-xs opacity-90">
                Dataset {status.dataset} · schema {status.schema}
                {status.message ? ` · ${status.message}` : ''}
              </p>
            </div>
          </div>
        )}
      </div>

      <div className="border-t border-brand-primary-border pt-6">
        <WorkllistsHome
          apiBase="/api/sandbox/workllists"
          sitesApi="/api/sandbox/workllists/sites"
          showPageHeader
          title="Workllists (CM2 DEV)"
          subtitle="Same pink-recall worklist as production, queried only against CM2DEV"
          noteSavedMessage="Note saved to CM2 DEV schedule"
        />
      </div>
    </div>
  )
}
