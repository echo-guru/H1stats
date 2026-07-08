import React, { createContext, useContext, useState, useEffect, useCallback, ReactNode } from 'react'
import type { AppModule } from '../utils/permissions'
import { normalizeModules } from '../utils/permissions'

const AUTH_USERNAME_KEY = 'h1stats_username'
const AUTH_IS_ADMIN_KEY = 'h1stats_is_admin'
const AUTH_MODULES_KEY = 'h1stats_modules'
const AUTH_TOKEN_KEY = 'h1stats_token'

export interface LoginResult {
  success: boolean
  error?: string
  isAdmin?: boolean
  modules?: AppModule[]
}

interface AuthContextType {
  username: string | null
  isAdmin: boolean
  modules: AppModule[]
  login: (username: string, password: string) => Promise<LoginResult>
  logout: () => void
}

const AuthContext = createContext<AuthContextType | undefined>(undefined)

export function useAuth() {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth must be used within AuthProvider')
  return context
}

function readStoredModules(): AppModule[] {
  const raw = sessionStorage.getItem(AUTH_MODULES_KEY)
  if (!raw) return []
  try {
    return normalizeModules(JSON.parse(raw))
  } catch {
    return []
  }
}

export const AuthProvider: React.FC<{ children: ReactNode }> = ({ children }) => {
  const [username, setUsername] = useState<string | null>(() => sessionStorage.getItem(AUTH_USERNAME_KEY))
  const [isAdmin, setIsAdmin] = useState(() => sessionStorage.getItem(AUTH_IS_ADMIN_KEY) === 'true')
  const [modules, setModules] = useState<AppModule[]>(() => readStoredModules())

  useEffect(() => {
    if (username) {
      sessionStorage.setItem(AUTH_USERNAME_KEY, username)
      sessionStorage.setItem(AUTH_IS_ADMIN_KEY, String(isAdmin))
      sessionStorage.setItem(AUTH_MODULES_KEY, JSON.stringify(modules))
    } else {
      sessionStorage.removeItem(AUTH_USERNAME_KEY)
      sessionStorage.removeItem(AUTH_IS_ADMIN_KEY)
      sessionStorage.removeItem(AUTH_MODULES_KEY)
      sessionStorage.removeItem(AUTH_TOKEN_KEY)
    }
  }, [username, isAdmin, modules])

  const login = useCallback(async (user: string, password: string): Promise<LoginResult> => {
    try {
      const res = await fetch('/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username: user, password }),
      })
      if (!res.ok) {
        const data = await res.json().catch(() => ({}))
        return { success: false, error: data.message || data.error || 'Login failed' }
      }
      const data = await res.json()
      setUsername(data.username)
      setIsAdmin(Boolean(data.isAdmin))
      setModules(normalizeModules(data.modules))
      if (data.token) sessionStorage.setItem(AUTH_TOKEN_KEY, data.token)
      return { success: true, isAdmin: data.isAdmin, modules: data.modules }
    } catch {
      // Dev fallback when backend is not running
      if (user === 'tonyf' && password === 'tony') {
        const devModules: AppModule[] = ['dashboard', 'clinical', 'administration']
        setUsername('tonyf')
        setIsAdmin(true)
        setModules(devModules)
        return { success: true, isAdmin: true, modules: devModules }
      }
      if (user === 'admin' && password === 'admin') {
        const devModules: AppModule[] = ['dashboard', 'clinical', 'administration']
        setUsername('admin')
        setIsAdmin(true)
        setModules(devModules)
        return { success: true, isAdmin: true, modules: devModules }
      }
      if (user === 'clinical' && password === 'clinical') {
        const devModules: AppModule[] = ['dashboard', 'clinical']
        setUsername('clinical')
        setIsAdmin(false)
        setModules(devModules)
        return { success: true, isAdmin: false, modules: devModules }
      }
      return { success: false, error: 'Cannot reach server. Use tonyf/tony, admin/admin, or clinical/clinical for offline dev.' }
    }
  }, [])

  const logout = useCallback(() => {
    setUsername(null)
    setIsAdmin(false)
    setModules([])
    fetch('/api/auth/logout', { method: 'POST' }).catch(() => {})
  }, [])

  return (
    <AuthContext.Provider value={{ username, isAdmin, modules, login, logout }}>
      {children}
    </AuthContext.Provider>
  )
}
