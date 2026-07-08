import React, { useState } from 'react'
import { useNavigate, useLocation } from 'react-router-dom'
import { LogIn, BarChart3 } from 'lucide-react'
import { Button } from '../components/ui/button'
import { useAuth } from '../contexts/AuthContext'
import { canAccessPath, defaultPathForUser } from '../utils/permissions'

export default function Login() {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const { login, username: currentUser, isAdmin, modules } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()

  const resolveRedirect = (nextIsAdmin: boolean, nextModules: typeof modules) => {
    const perms = { username: username.trim(), isAdmin: nextIsAdmin, modules: nextModules }
    const from = (location.state as { from?: { pathname: string } })?.from?.pathname
    if (from && from !== '/' && canAccessPath(from, perms)) return from
    return defaultPathForUser(perms)
  }

  React.useEffect(() => {
    if (currentUser) {
      navigate(resolveRedirect(isAdmin, modules), { replace: true })
    }
  }, [currentUser, isAdmin, modules, navigate])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError('')
    setLoading(true)
    const result = await login(username.trim(), password)
    setLoading(false)
    if (result.success) {
      navigate(resolveRedirect(Boolean(result.isAdmin), result.modules ?? []), { replace: true })
    } else {
      setError(result.error || 'Login failed')
    }
  }

  return (
    <div className="max-w-sm mx-auto mt-16">
      <div className="bg-white rounded-lg border border-brand-primary-border shadow-sm p-6">
        <div className="flex items-center gap-3 mb-2">
          <BarChart3 className="h-8 w-8 text-brand-primary" />
          <h1 className="text-xl font-semibold text-brand-accent">H1Stats</h1>
        </div>
        <p className="text-gray-600 text-sm mb-6">
          Operational intelligence for Hearts 1st
        </p>
        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-brand-primary mb-1">Username</label>
            <input
              type="text"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              className="w-full border border-gray-300 rounded-md px-3 py-2 focus:outline-none focus:ring-2 focus:ring-brand-primary focus:border-brand-primary"
              autoComplete="username"
              autoFocus
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-brand-primary mb-1">Password</label>
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className="w-full border border-gray-300 rounded-md px-3 py-2 focus:outline-none focus:ring-2 focus:ring-brand-primary focus:border-brand-primary"
              autoComplete="current-password"
            />
          </div>
          {error && <p className="text-sm text-red-600">{error}</p>}
          <Button
            type="submit"
            variant="brand"
            className="w-full gap-2"
            disabled={loading}
          >
            <LogIn className="h-4 w-4" />
            {loading ? 'Signing in…' : 'Sign in'}
          </Button>
        </form>
      </div>
    </div>
  )
}
