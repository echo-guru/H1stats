/** Application modules — extensible without navigation changes */
export type AppModule =
  | 'dashboard'
  | 'clinical'
  | 'administration'
  | 'operations'
  | 'quality'
  | 'research'
  | 'finance'

export const ALL_MODULES: AppModule[] = [
  'dashboard',
  'clinical',
  'administration',
  'operations',
  'quality',
  'research',
  'finance',
]

export const MODULE_LABELS: Record<AppModule, string> = {
  dashboard: 'Dashboard',
  clinical: 'Clinical',
  administration: 'Administration',
  operations: 'Operations',
  quality: 'Quality',
  research: 'Research',
  finance: 'Finance',
}

export interface UserPermissions {
  username: string
  isAdmin: boolean
  modules: AppModule[]
}

const PATH_MODULE_MAP: { prefix: string; module: AppModule }[] = [
  { prefix: '/admin', module: 'administration' },
  { prefix: '/clinical', module: 'clinical' },
  { prefix: '/dashboard', module: 'dashboard' },
  { prefix: '/', module: 'dashboard' },
]

export function canAccessPath(path: string, perms: UserPermissions): boolean {
  if (perms.isAdmin) return true
  const entry = PATH_MODULE_MAP.find((m) => path === m.prefix || path.startsWith(m.prefix + '/'))
  if (!entry) return true
  return perms.modules.includes(entry.module)
}

export function defaultPathForUser(perms: UserPermissions): string {
  if (perms.modules.includes('dashboard') || perms.isAdmin) return '/dashboard'
  if (perms.modules.includes('clinical')) return '/clinical'
  if (perms.modules.includes('administration')) return '/admin'
  return '/dashboard'
}

export function normalizeModules(raw: unknown): AppModule[] {
  if (!Array.isArray(raw)) return []
  return raw.filter((m): m is AppModule => ALL_MODULES.includes(m as AppModule))
}
