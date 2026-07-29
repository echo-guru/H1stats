import { Link } from 'react-router-dom'
import { Settings, Users, Database, Activity, ArrowRight } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'

interface AdminAction {
  title: string
  description: string
  path: string
  icon: LucideIcon
}

const ADMIN_ACTIONS: AdminAction[] = [
  {
    title: 'User Management',
    description: 'Create users, assign admin or user roles, and reset passwords.',
    path: '/admin/users',
    icon: Users,
  },
  {
    title: 'Database Configuration',
    description: 'Configure H1PACS (MVF, AcusonDB) and CM2 on separate servers.',
    path: '/admin/database',
    icon: Database,
  },
  {
    title: 'System Health',
    description: 'Check API status and SQL Server connectivity.',
    path: '/admin/health',
    icon: Activity,
  },
]

export default function AdminHome() {
  return (
    <div className="space-y-6">
      <div className="flex items-center gap-3">
        <Settings className="h-8 w-8 text-brand-primary" />
        <div>
          <h2 className="text-2xl font-semibold text-brand-accent">Administration</h2>
          <p className="text-sm text-gray-600">System configuration and user access</p>
        </div>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
        {ADMIN_ACTIONS.map((action) => {
          const Icon = action.icon
          return (
            <Link
              key={action.path}
              to={action.path}
              className="group bg-white rounded-lg border border-brand-primary-border shadow-sm p-5 hover:border-brand-primary hover:shadow-md transition-all"
            >
              <div className="flex items-start justify-between gap-3">
                <div className="rounded-lg bg-brand-primary-light p-2.5">
                  <Icon className="h-6 w-6 text-brand-primary" />
                </div>
                <ArrowRight className="h-5 w-5 text-gray-300 group-hover:text-brand-primary transition-colors shrink-0 mt-1" />
              </div>
              <h3 className="mt-4 text-lg font-semibold text-brand-accent">{action.title}</h3>
              <p className="mt-1 text-sm text-gray-600">{action.description}</p>
            </Link>
          )
        })}
      </div>
    </div>
  )
}
