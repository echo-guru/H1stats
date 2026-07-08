import { useEffect, useState } from 'react'
import { Database, CheckCircle, XCircle, RefreshCw } from 'lucide-react'
import { Button } from '../components/ui/button'

interface DbConfig {
  server: string
  mvfDatabase: string
  acusonDatabase: string
  username: string
  password?: string
}

interface ConnectionStatus {
  connected: boolean
  message: string
  testedAt?: string
}

export default function AdminDatabase() {
  const [config, setConfig] = useState<DbConfig>({
    server: '192.168.12.205',
    mvfDatabase: 'mvf',
    acusonDatabase: 'AcusonDB',
    username: 'h1stats',
    password: '',
  })
  const [status, setStatus] = useState<ConnectionStatus | null>(null)
  const [testing, setTesting] = useState(false)

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
    try {
      const res = await fetch('/api/admin/database/test', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(config),
      })
      const data = await res.json()
      setStatus(data)
    } catch {
      setStatus({ connected: false, message: 'Cannot reach API — install .NET SDK and start backend' })
    } finally {
      setTesting(false)
    }
  }

  const saveConfig = async () => {
    await fetch('/api/admin/database', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(config),
    })
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-3">
        <Database className="h-8 w-8 text-brand-primary" />
        <div>
          <h2 className="text-2xl font-semibold text-brand-accent">Database Configuration</h2>
          <p className="text-sm text-gray-600">Read-only SQL Server connection (H1PACS)</p>
        </div>
      </div>

      <div className="bg-white rounded-lg border border-brand-primary-border shadow-sm p-6 space-y-4 max-w-xl">
        <div>
          <label className="block text-sm font-medium text-brand-primary mb-1">Server</label>
          <input
            value={config.server}
            onChange={(e) => setConfig({ ...config, server: e.target.value })}
            className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-brand-primary"
          />
        </div>
        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="block text-sm font-medium text-brand-primary mb-1">MVF Database</label>
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
            className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-brand-primary"
          />
        </div>
        <div className="flex gap-2">
          <Button variant="brand" onClick={testConnection} disabled={testing} className="gap-2">
            <RefreshCw className={`h-4 w-4 ${testing ? 'animate-spin' : ''}`} />
            Test Connection
          </Button>
          <Button variant="outline" onClick={saveConfig}>Save</Button>
        </div>

        {status && (
          <div className={`flex items-center gap-2 text-sm p-3 rounded-lg ${status.connected ? 'bg-emerald-50 text-emerald-800' : 'bg-red-50 text-red-800'}`}>
            {status.connected ? <CheckCircle className="h-4 w-4" /> : <XCircle className="h-4 w-4" />}
            {status.message}
          </div>
        )}
      </div>
    </div>
  )
}
