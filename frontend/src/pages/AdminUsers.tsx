import { useCallback, useEffect, useState } from 'react'
import { Plus, Save, Trash2, Users } from 'lucide-react'
import { Button } from '../components/ui/button'
import { AdminBackLink } from '../components/AdminBackLink'
import { useAuth } from '../contexts/AuthContext'

interface UserRow {
  username: string
  isAdmin: boolean
  isDisabled: boolean
  isSuperAdmin: boolean
}

type RoleValue = 'user' | 'admin' | 'superadmin'

interface EditState {
  username: string
  role: RoleValue
  isDisabled: boolean
  password: string
}

const EMPTY_CREATE = { username: '', password: '', role: 'user' as RoleValue }

function roleFromUser(user: Pick<UserRow, 'isAdmin' | 'isSuperAdmin'>): RoleValue {
  if (user.isSuperAdmin) return 'superadmin'
  if (user.isAdmin) return 'admin'
  return 'user'
}

function flagsFromRole(role: RoleValue): { isAdmin: boolean; isSuperAdmin: boolean } {
  if (role === 'superadmin') return { isAdmin: true, isSuperAdmin: true }
  if (role === 'admin') return { isAdmin: true, isSuperAdmin: false }
  return { isAdmin: false, isSuperAdmin: false }
}

function editFromUser(user: UserRow): EditState {
  return {
    username: user.username,
    role: roleFromUser(user),
    isDisabled: user.isDisabled,
    password: '',
  }
}

function editIsDirty(user: UserRow, edit: EditState): boolean {
  return (
    edit.username.trim() !== user.username
    || edit.role !== roleFromUser(user)
    || edit.isDisabled !== user.isDisabled
    || edit.password.trim().length > 0
  )
}

export default function AdminUsers() {
  const { username: actorUsername, isSuperAdmin } = useAuth()
  const [users, setUsers] = useState<UserRow[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const [createForm, setCreateForm] = useState(EMPTY_CREATE)
  const [creating, setCreating] = useState(false)
  const [edits, setEdits] = useState<Record<string, EditState>>({})
  const [saving, setSaving] = useState<string | null>(null)
  const [deleting, setDeleting] = useState<string | null>(null)

  const loadUsers = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      const res = await fetch('/api/admin/users')
      if (!res.ok) throw new Error('Failed to load users')
      const data = await res.json()
      if (!Array.isArray(data)) throw new Error('Failed to load users')
      const rows: UserRow[] = data.map((u: Partial<UserRow>) => ({
        username: String(u.username ?? ''),
        isAdmin: Boolean(u.isAdmin),
        isDisabled: Boolean(u.isDisabled),
        isSuperAdmin: Boolean(u.isSuperAdmin),
      }))
      setUsers(rows)
      setEdits(Object.fromEntries(rows.map((u) => [u.username, editFromUser(u)])))
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load users')
      setUsers([])
      setEdits({})
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    loadUsers()
  }, [loadUsers])

  const createUser = async (e: React.FormEvent) => {
    e.preventDefault()
    setCreating(true)
    setError('')
    setMessage('')
    try {
      const flags = flagsFromRole(createForm.role)
      const res = await fetch('/api/admin/users', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          username: createForm.username.trim(),
          password: createForm.password,
          isAdmin: flags.isAdmin,
          isSuperAdmin: flags.isSuperAdmin,
          actorUsername: actorUsername ?? undefined,
        }),
      })
      const data = await res.json().catch(() => ({}))
      if (!res.ok) throw new Error(data.message || 'Failed to create user')
      setMessage(`Created user ${data.username}`)
      setCreateForm(EMPTY_CREATE)
      await loadUsers()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to create user')
    } finally {
      setCreating(false)
    }
  }

  const saveUser = async (originalUsername: string) => {
    const edit = edits[originalUsername]
    if (!edit) return

    const trimmedUsername = edit.username.trim()
    if (!trimmedUsername) {
      setError('Username cannot be empty')
      return
    }

    setSaving(originalUsername)
    setError('')
    setMessage('')
    try {
      const flags = flagsFromRole(edit.role)
      const body: {
        isAdmin: boolean
        isDisabled: boolean
        isSuperAdmin: boolean
        actorUsername?: string
        password?: string
        newUsername?: string
      } = {
        isAdmin: flags.isAdmin,
        isDisabled: edit.isDisabled,
        isSuperAdmin: flags.isSuperAdmin,
        actorUsername: actorUsername ?? undefined,
      }
      if (edit.password.trim()) body.password = edit.password
      if (trimmedUsername !== originalUsername) body.newUsername = trimmedUsername

      const res = await fetch(`/api/admin/users/${encodeURIComponent(originalUsername)}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      })
      const data = await res.json().catch(() => ({}))
      if (!res.ok) throw new Error(data.message || 'Failed to update user')
      setMessage(`Updated user ${data.username ?? trimmedUsername}`)
      await loadUsers()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to update user')
    } finally {
      setSaving(null)
    }
  }

  const deleteUser = async (username: string) => {
    if (!window.confirm(`Remove user "${username}"? This cannot be undone.`)) return

    setDeleting(username)
    setError('')
    setMessage('')
    try {
      const res = await fetch(`/api/admin/users/${encodeURIComponent(username)}`, {
        method: 'DELETE',
      })
      const data = await res.json().catch(() => ({}))
      if (!res.ok) throw new Error(data.message || 'Failed to delete user')
      setMessage(`Removed user ${username}`)
      await loadUsers()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to delete user')
    } finally {
      setDeleting(null)
    }
  }

  const updateEdit = (username: string, patch: Partial<EditState>) => {
    setEdits((prev) => ({
      ...prev,
      [username]: { ...prev[username], ...patch },
    }))
  }

  const resetEdit = (user: UserRow) => {
    setEdits((prev) => ({
      ...prev,
      [user.username]: editFromUser(user),
    }))
  }

  const roleSelect = (
    value: RoleValue,
    onChange: (role: RoleValue) => void,
    className: string
  ) => (
    <select
      value={value}
      onChange={(e) => onChange(e.target.value as RoleValue)}
      className={className}
    >
      <option value="user">User — dashboard and clinical</option>
      <option value="admin">Admin — full access</option>
      {isSuperAdmin && <option value="superadmin">Super Admin — full access + restricted modules</option>}
      {!isSuperAdmin && value === 'superadmin' && (
        <option value="superadmin">Super Admin</option>
      )}
    </select>
  )

  return (
    <div className="space-y-6">
      <AdminBackLink />
      <div className="flex items-center gap-3">
        <Users className="h-8 w-8 text-brand-primary" />
        <div>
          <h2 className="text-2xl font-semibold text-brand-accent">User Administration</h2>
          <p className="text-sm text-gray-600">
            Create, edit, disable, or remove users. Disabled users cannot sign in.
            {isSuperAdmin
              ? ' As Super Admin you can also grant Super Admin to others.'
              : ''}
          </p>
        </div>
      </div>

      {error && (
        <p className="text-sm text-red-600 bg-red-50 border border-red-100 rounded-lg px-3 py-2">{error}</p>
      )}
      {message && (
        <p className="text-sm text-emerald-700 bg-emerald-50 border border-emerald-100 rounded-lg px-3 py-2">{message}</p>
      )}

      <div className="bg-white rounded-lg border border-brand-primary-border shadow-sm p-6 max-w-xl">
        <h3 className="text-sm font-semibold text-brand-primary mb-4">Add user</h3>
        <form onSubmit={createUser} className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-brand-primary mb-1">Username</label>
            <input
              value={createForm.username}
              onChange={(e) => setCreateForm({ ...createForm, username: e.target.value })}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-brand-primary"
              autoComplete="off"
              required
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-brand-primary mb-1">Password</label>
            <input
              type="password"
              value={createForm.password}
              onChange={(e) => setCreateForm({ ...createForm, password: e.target.value })}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-brand-primary"
              autoComplete="new-password"
              required
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-brand-primary mb-1">Role</label>
            {roleSelect(
              createForm.role,
              (role) => setCreateForm({ ...createForm, role }),
              'w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-brand-primary bg-white'
            )}
          </div>
          <Button type="submit" variant="brand" disabled={creating} className="gap-2">
            <Plus className="h-4 w-4" />
            {creating ? 'Creating…' : 'Create user'}
          </Button>
        </form>
      </div>

      <div className="bg-white rounded-lg border border-brand-primary-border shadow-sm overflow-hidden">
        <div className="px-6 py-4 border-b border-brand-primary-border">
          <h3 className="text-sm font-semibold text-brand-primary">Users</h3>
        </div>

        {loading ? (
          <p className="text-sm text-muted-foreground p-6">Loading users…</p>
        ) : users.length === 0 ? (
          <p className="text-sm text-muted-foreground p-6">No users found</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="bg-muted/50">
                <tr>
                  <th className="text-left p-3 font-medium text-brand-primary">Username</th>
                  <th className="text-left p-3 font-medium text-brand-primary">Role</th>
                  <th className="text-left p-3 font-medium text-brand-primary">Status</th>
                  <th className="text-left p-3 font-medium text-brand-primary">New password</th>
                  <th className="text-right p-3 font-medium text-brand-primary">Actions</th>
                </tr>
              </thead>
              <tbody>
                {users.map((user) => {
                  const edit = edits[user.username] ?? editFromUser(user)
                  const dirty = editIsDirty(user, edit)
                  const roleDisabled = !isSuperAdmin && user.isSuperAdmin
                  return (
                    <tr
                      key={user.username}
                      className={`border-t border-brand-primary-border ${
                        user.isDisabled ? 'bg-muted/20 text-gray-500' : ''
                      }`}
                    >
                      <td className="p-3">
                        <input
                          value={edit.username}
                          onChange={(e) => updateEdit(user.username, { username: e.target.value })}
                          className="w-full min-w-[8rem] border border-gray-300 rounded-lg px-2 py-1.5 text-sm focus:ring-2 focus:ring-brand-primary font-medium"
                          autoComplete="off"
                        />
                      </td>
                      <td className="p-3">
                        {roleDisabled ? (
                          <span className="inline-flex items-center px-2 py-1.5 text-sm text-brand-primary bg-brand-primary-light rounded-lg">
                            Super Admin
                          </span>
                        ) : (
                          roleSelect(
                            edit.role,
                            (role) => updateEdit(user.username, { role }),
                            'border border-gray-300 rounded-lg px-2 py-1.5 text-sm bg-white focus:ring-2 focus:ring-brand-primary'
                          )
                        )}
                      </td>
                      <td className="p-3">
                        <select
                          value={edit.isDisabled ? 'disabled' : 'active'}
                          onChange={(e) =>
                            updateEdit(user.username, { isDisabled: e.target.value === 'disabled' })
                          }
                          className="border border-gray-300 rounded-lg px-2 py-1.5 text-sm bg-white focus:ring-2 focus:ring-brand-primary"
                        >
                          <option value="active">Active</option>
                          <option value="disabled">Disabled</option>
                        </select>
                      </td>
                      <td className="p-3">
                        <input
                          type="password"
                          value={edit.password}
                          onChange={(e) => updateEdit(user.username, { password: e.target.value })}
                          placeholder="Leave blank to keep"
                          className="w-full min-w-[10rem] border border-gray-300 rounded-lg px-2 py-1.5 text-sm focus:ring-2 focus:ring-brand-primary"
                          autoComplete="new-password"
                        />
                      </td>
                      <td className="p-3">
                        <div className="flex items-center justify-end gap-2">
                          {dirty && (
                            <Button
                              variant="ghost"
                              size="sm"
                              onClick={() => resetEdit(user)}
                              disabled={saving === user.username || deleting === user.username}
                            >
                              Reset
                            </Button>
                          )}
                          <Button
                            variant="outline"
                            size="sm"
                            className="gap-1"
                            disabled={!dirty || saving === user.username || deleting === user.username}
                            onClick={() => saveUser(user.username)}
                          >
                            <Save className="h-3.5 w-3.5" />
                            {saving === user.username ? 'Saving…' : 'Save'}
                          </Button>
                          <Button
                            variant="outline"
                            size="sm"
                            className="gap-1 text-red-700 border-red-200 hover:bg-red-50"
                            disabled={saving === user.username || deleting === user.username}
                            onClick={() => deleteUser(user.username)}
                          >
                            <Trash2 className="h-3.5 w-3.5" />
                            {deleting === user.username ? 'Removing…' : 'Remove'}
                          </Button>
                        </div>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  )
}
