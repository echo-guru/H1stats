import { useEffect, useState } from 'react'
import { Database, CheckCircle, XCircle, RefreshCw } from 'lucide-react'
import { Button } from '../components/ui/button'
import { AdminBackLink } from '../components/AdminBackLink'

interface DbConfig {
  server: string
  cm2Server: string
  cm2Port: string
  cm2Sid: string
  mvfDatabase: string
  acusonDatabase: string
  username: string
  password?: string
}

interface DatabaseTestResult {
  database: string
  connected: boolean
  message: string
}

interface ConnectionStatus {
  connected: boolean
  results: DatabaseTestResult[]
}

export default function AdminDatabase() {
  const [config, setConfig] = useState<DbConfig>({
    server: '192.168.12.205',
    cm2Server: '',
    cm2Port: '1521',
    cm2Sid: 'CM19',
    mvfDatabase: 'mvf',
    acusonDatabase: 'AcusonDB',
    username: 'h1stats',
    password: '',
  })
  const [status, setStatus] = useState<ConnectionStatus | null>(null)
  const [testing, setTesting] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')

  useEffect(() => {
    fetch('/api/admin/database')
      .then((r) => (r.ok ? r.json() : null))
      .then((data) => {
        if (data) setConfig((c) => ({ ...c, ...data, password: '' }))
      })
      .catch(() => {})
  }, [])

  const testConnection = async () => {
    setTesting(true)
    setError('')
    setMessage('')
    try {
      const res = await fetch('/api/admin/database/test', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(config),
      })
      const data = await res.json()
      setStatus({
        connected: Boolean(data.connected),
        results: Array.isArray(data.results) ? data.results : [],
      })
    } catch {
      setStatus({
        connected: false,
        results: [{ database: 'API', connected: false, message: 'Cannot reach API — start backend' }],
      })
    } finally {
      setTesting(false)
    }
  }

  const saveConfig = async () => {
    setError('')
    setMessage('')
    try {
      const res = await fetch('/api/admin/database', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(config),
      })
      if (!res.ok) throw new Error('Failed to save database settings')
      setMessage('Database settings saved')
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to save database settings')
    }
  }

  return (
    <div className="space-y-6">
      <AdminBackLink />
      <div className="flex items-center gap-3">
        <Database className="h-8 w-8 text-brand-primary" />
        <div>
          <h2 className="text-2xl font-semibold text-brand-accent">Database Configuration</h2>
          <p className="text-sm text-gray-600">Read-only SQL Server connections for H1PACS and CM2</p>
        </div>
      </div>

      {error && (
        <p className="text-sm text-red-600 bg-red-50 border border-red-100 rounded-lg px-3 py-2 max-w-xl">{error}</p>
      )}
      {message && (
        <p className="text-sm text-emerald-700 bg-emerald-50 border border-emerald-100 rounded-lg px-3 py-2 max-w-xl">{message}</p>
      )}

      <div className="bg-white rounded-lg border border-brand-primary-border shadow-sm p-6 space-y-6 max-w-xl">
        <div className="space-y-4">
          <h3 className="text-sm font-semibold text-brand-primary">H1PACS (MVF &amp; AcusonDB)</h3>
          <div>
            <label className="block text-sm font-medium text-brand-primary mb-1">Server</label>
            <input
              value={config.server}
              onChange={(e) => setConfig({ ...config, server: e.target.value })}
              placeholder="e.g. 192.168.12.205"
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-brand-primary"
            />
          </div>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-brand-primary mb-1">MVF database</label>
              <input
                value={config.mvfDatabase}
                onChange={(e) => setConfig({ ...config, mvfDatabase: e.target.value })}
                className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-brand-primary"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-brand-primary mb-1">AcusonDB</label>
              <input
                value={config.acusonDatabase}
                onChange={(e) => setConfig({ ...config, acusonDatabase: e.target.value })}
                className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-brand-primary"
              />
            </div>
          </div>
        </div>

        <div className="space-y-4 border-t border-brand-primary-border pt-4">
          <h3 className="text-sm font-semibold text-brand-primary">CM2 (Oracle)</h3>
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
            <div className="sm:col-span-2">
              <label className="block text-sm font-medium text-brand-primary mb-1">Server</label>
              <input
                value={config.cm2Server}
                onChange={(e) => setConfig({ ...config, cm2Server: e.target.value })}
                placeholder="e.g. 192.168.12.214"
                className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-brand-primary"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-brand-primary mb-1">Port</label>
              <input
                value={config.cm2Port}
                onChange={(e) => setConfig({ ...config, cm2Port: e.target.value })}
                placeholder="1521"
                className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-brand-primary"
              />
            </div>
          </div>
          <div>
            <label className="block text-sm font-medium text-brand-primary mb-1">Oracle SID</label>
            <input
              value={config.cm2Sid}
              onChange={(e) => setConfig({ ...config, cm2Sid: e.target.value })}
              placeholder="CM19"
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-brand-primary"
            />
          </div>
          <p className="text-xs text-muted-foreground">
            CM2 is an Oracle database on a separate server from H1PACS SQL Server.
          </p>
        </div>

        <div className="space-y-4 border-t border-brand-primary-border pt-4">
          <h3 className="text-sm font-semibold text-brand-primary">Credentials</h3>
          <div>
            <label className="block text-sm font-medium text-brand-primary mb-1">Username</label>
            <input
              value={config.username}
              onChange={(e) => setConfig({ ...config, username: e.target.value })}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-brand-primary"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-brand-primary mb-1">Password</label>
            <input
              type="password"
              value={config.password}
              onChange={(e) => setConfig({ ...config, password: e.target.value })}
              placeholder="Leave blank to keep saved password"
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-brand-primary"
            />
          </div>
        </div>

        <div className="flex gap-2 border-t border-brand-primary-border pt-4">
          <Button variant="brand" onClick={testConnection} disabled={testing} className="gap-2">
            <RefreshCw className={`h-4 w-4 ${testing ? 'animate-spin' : ''}`} />
            Test Connections
          </Button>
          <Button variant="outline" onClick={saveConfig}>Save</Button>
        </div>

        {status && (
          <div className="space-y-2">
            {status.results.map((result) => (
              <div
                key={result.database}
                className={`flex items-center gap-2 text-sm p-3 rounded-lg ${
                  result.connected ? 'bg-emerald-50 text-emerald-800' : 'bg-red-50 text-red-800'
                }`}
              >
                {result.connected ? <CheckCircle className="h-4 w-4 shrink-0" /> : <XCircle className="h-4 w-4 shrink-0" />}
                <span>
                  <strong>{result.database}:</strong> {result.message}
                </span>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
