import React, { useEffect, useRef, useState } from 'react'
import { useNavigate, useLocation } from 'react-router-dom'
import { LogIn, BarChart3 } from 'lucide-react'
import { Button } from '../components/ui/button'
import { useAuth } from '../contexts/AuthContext'
import { canAccessPath, defaultPathForUser } from '../utils/permissions'

const LAST_USERNAME_KEY = 'h1stats_last_username'

async function fetchLoginUsernames(): Promise<string[]> {
  const res = await fetch('/api/auth/usernames', { cache: 'no-store' })
  if (!res.ok) throw new Error('Failed to load usernames')
  const list = await res.json()
  if (!Array.isArray(list)) throw new Error('Invalid usernames response')
  return list.map((item: unknown) => String(item)).filter(Boolean)
}

export default function Login() {
  const [usernames, setUsernames] = useState<string[]>([])
  const [usernamesLoading, setUsernamesLoading] = useState(true)
  const [username, setUsername] = useState(() => localStorage.getItem(LAST_USERNAME_KEY) ?? '')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const { login, username: currentUser, isAdmin, modules } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const passwordRef = useRef<HTMLInputElement>(null)

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

  const loadUsernames = React.useCallback(async () => {
    setUsernamesLoading(true)
    try {
      const names = await fetchLoginUsernames()
      setUsernames(names)
    } catch {
      setUsernames((prev) => prev)
    } finally {
      setUsernamesLoading(false)
    }
  }, [])

  useEffect(() => {
    void loadUsernames()
  }, [loadUsernames])

  useEffect(() => {
    const refresh = () => {
      if (document.visibilityState === 'visible') void loadUsernames()
    }
    window.addEventListener('focus', refresh)
    document.addEventListener('visibilitychange', refresh)
    return () => {
      window.removeEventListener('focus', refresh)
      document.removeEventListener('visibilitychange', refresh)
    }
  }, [loadUsernames])

  useEffect(() => {
    if (usernames.length === 0) return
    if (username && usernames.includes(username)) return
    const last = localStorage.getItem(LAST_USERNAME_KEY)
    setUsername(last && usernames.includes(last) ? last : usernames[0])
  }, [usernames, username])

  const handleUserChange = (value: string) => {
    setUsername(value)
    setPassword('')
    passwordRef.current?.focus()
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError('')
    setLoading(true)
    const result = await login(username.trim(), password)
    setLoading(false)
    if (result.success) {
      localStorage.setItem(LAST_USERNAME_KEY, username.trim())
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
            <select
              value={username}
              onChange={(e) => handleUserChange(e.target.value)}
              className="w-full border border-gray-300 rounded-md px-3 py-2 bg-white focus:outline-none focus:ring-2 focus:ring-brand-primary focus:border-brand-primary"
              autoComplete="username"
              autoFocus
              required
            >
              {usernamesLoading && usernames.length === 0 && <option value="">Loading users…</option>}
              {!usernamesLoading && usernames.length === 0 && (
                <option value="">No users available — check the API is running</option>
              )}
              {usernames.map((name) => (
                <option key={name} value={name}>{name}</option>
              ))}
            </select>
          </div>
          <div>
            <label className="block text-sm font-medium text-brand-primary mb-1">Password</label>
            <input
              ref={passwordRef}
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
            disabled={loading || !username}
          >
            <LogIn className="h-4 w-4" />
            {loading ? 'Signing in…' : 'Sign in'}
          </Button>
        </form>
      </div>
    </div>
  )
}
