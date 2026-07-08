import React from 'react'
import { Link, useLocation } from 'react-router-dom'
import {
  LayoutDashboard,
  Stethoscope,
  Settings,
  LogOut,
  Users,
  Database,
  Activity,
} from 'lucide-react'
import { useAuth } from '../contexts/AuthContext'
import type { AppModule } from '../utils/permissions'

interface NavItem {
  path: string
  label: string
  icon: React.ComponentType<{ className?: string }>
  module: AppModule
  children?: { path: string; label: string }[]
}

const NAV_ITEMS: NavItem[] = [
  {
    path: '/dashboard',
    label: 'Dashboard',
    icon: LayoutDashboard,
    module: 'dashboard',
  },
  {
    path: '/clinical/physician-statistics',
    label: 'Clinical',
    icon: Stethoscope,
    module: 'clinical',
    children: [{ path: '/clinical/physician-statistics', label: 'Physician Statistics' }],
  },
  {
    path: '/admin/users',
    label: 'Administration',
    icon: Settings,
    module: 'administration',
    children: [
      { path: '/admin/users', label: 'Users' },
      { path: '/admin/database', label: 'Database' },
      { path: '/admin/health', label: 'System Health' },
    ],
  },
]

export default function Navigation() {
  const location = useLocation()
  const { username, isAdmin, modules, logout } = useAuth()

  if (location.pathname === '/login') return null

  const visibleItems = NAV_ITEMS.filter(
    (item) => isAdmin || modules.includes(item.module)
  )

  const isActive = (path: string) =>
    location.pathname === path || location.pathname.startsWith(path + '/')

  return (
    <nav className="bg-white shadow-sm border-b">
      <div className="container mx-auto px-4">
        <div className="flex items-center justify-between min-h-16 py-2">
          <div className="flex items-center gap-6">
            <Link to="/dashboard" className="flex items-center gap-3 shrink-0">
              <img src="/logo_H1.png" alt="Hearts 1st" className="h-16 w-auto" />
              <span className="hidden sm:block text-[55px] font-semibold text-brand-accent leading-none">H1Stats</span>
            </Link>
          </div>

          <div className="flex gap-1 items-center flex-wrap justify-end">
            {visibleItems.map((item) => {
              const Icon = item.icon
              const active = isActive(item.path)
              return (
                <Link
                  key={item.path}
                  to={item.path}
                  className={`
                    flex items-center gap-2 px-3 py-2 rounded-md text-sm font-medium transition-colors
                    ${
                      active
                        ? 'bg-brand-primary-light text-brand-primary border border-brand-primary-border'
                        : 'text-gray-600 hover:bg-gray-100 hover:text-gray-900'
                    }
                  `}
                >
                  <Icon className="h-4 w-4" />
                  {item.label}
                </Link>
              )
            })}
          </div>
        </div>

        {username && (
          <div className="flex items-center justify-between border-t border-gray-100 py-2 text-sm text-gray-600">
            <span>
              Signed in as <strong className="text-brand-primary">{username}</strong>
              {isAdmin && <span className="ml-2 text-xs bg-brand-primary-light text-brand-primary px-2 py-0.5 rounded">Admin</span>}
            </span>
            <button
              type="button"
              onClick={logout}
              className="flex items-center gap-1 text-gray-500 hover:text-brand-accent transition-colors"
            >
              <LogOut className="h-4 w-4" />
              Sign out
            </button>
          </div>
        )}
      </div>
    </nav>
  )
}

export { Users, Database, Activity }
