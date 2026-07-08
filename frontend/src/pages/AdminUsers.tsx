import { useCallback, useEffect, useState } from 'react'
import { Plus, Save, Users } from 'lucide-react'
import { Button } from '../components/ui/button'

interface UserRow {
  username: string
  isAdmin: boolean
  isDisabled: boolean
}

interface EditState {
  isAdmin: boolean
  password: string
}

const EMPTY_CREATE = { username: '', password: '', isAdmin: false }

export default function AdminUsers() {
  const [users, setUsers] = useState<UserRow[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const [createForm, setCreateForm] = useState(EMPTY_CREATE)
  const [creating, setCreating] = useState(false)
  const [edits, setEdits] = useState<Record<string, EditState>>({})
  const [saving, setSaving] = useState<string | null>(null)

  const loadUsers = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      const res = await fetch('/api/admin/users')
      if (!res.ok) throw new Error('Failed to load users')
      const data: UserRow[] = await res.json()
      setUsers(data)
      setEdits(
        Object.fromEntries(
          data.map((u) => [u.username, { isAdmin: u.isAdmin, password: '' }])
        )
      )
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load users')
      setUsers([])
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
      const res = await fetch('/api/admin/users', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          username: createForm.username.trim(),
          password: createForm.password,
          isAdmin: createForm.isAdmin,
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

  const saveUser = async (username: string) => {
    const edit = edits[username]
    if (!edit) return

    setSaving(username)
    setError('')
    setMessage('')
    try {
      const body: { isAdmin: boolean; password?: string } = { isAdmin: edit.isAdmin }
      if (edit.password.trim()) body.password = edit.password

      const res = await fetch(`/api/admin/users/${encodeURIComponent(username)}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      })
      const data = await res.json().catch(() => ({}))
      if (!res.ok) throw new Error(data.message || 'Failed to update user')
      setMessage(`Updated user ${username}`)
      setEdits((prev) => ({
        ...prev,
        [username]: { ...prev[username], password: '' },
      }))
      await loadUsers()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to update user')
    } finally {
      setSaving(null)
    }
  }

  const updateEdit = (username: string, patch: Partial<EditState>) => {
    setEdits((prev) => ({
      ...prev,
      [username]: { ...prev[username], ...patch },
    }))
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-3">
        <Users className="h-8 w-8 text-brand-primary" />
        <div>
          <h2 className="text-2xl font-semibold text-brand-accent">User Administration</h2>
          <p className="text-sm text-gray-600">Create users and assign Admin or User access</p>
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
            <select
              value={createForm.isAdmin ? 'admin' : 'user'}
              onChange={(e) => setCreateForm({ ...createForm, isAdmin: e.target.value === 'admin' })}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-brand-primary bg-white"
            >
              <option value="user">User — dashboard and clinical</option>
              <option value="admin">Admin — full access</option>
            </select>
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
                  <th className="text-left p-3 font-medium text-brand-primary">New password</th>
                  <th className="text-right p-3 font-medium text-brand-primary">Actions</th>
                </tr>
              </thead>
              <tbody>
                {users.map((user) => {
                  const edit = edits[user.username] ?? { isAdmin: user.isAdmin, password: '' }
                  return (
                    <tr key={user.username} className="border-t border-brand-primary-border">
                      <td className="p-3 font-medium text-gray-800">{user.username}</td>
                      <td className="p-3">
                        <select
                          value={edit.isAdmin ? 'admin' : 'user'}
                          onChange={(e) =>
                            updateEdit(user.username, { isAdmin: e.target.value === 'admin' })
                          }
                          className="border border-gray-300 rounded-lg px-2 py-1.5 text-sm bg-white focus:ring-2 focus:ring-brand-primary"
                        >
                          <option value="user">User</option>
                          <option value="admin">Admin</option>
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
                      <td className="p-3 text-right">
                        <Button
                          variant="outline"
                          size="sm"
                          className="gap-1"
                          disabled={saving === user.username}
                          onClick={() => saveUser(user.username)}
                        >
                          <Save className="h-3.5 w-3.5" />
                          {saving === user.username ? 'Saving…' : 'Save'}
                        </Button>
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
