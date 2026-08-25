import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../contexts/AuthContext'
import { canAccessPath, defaultPathForUser } from '../utils/permissions'

export default function RequireAuth() {
  const { username, isAdmin, isSuperAdmin, modules } = useAuth()
  const location = useLocation()

  if (!username) {
    return <Navigate to="/login" state={{ from: location }} replace />
  }

  const perms = { username, isAdmin, isSuperAdmin, modules }
  if (!canAccessPath(location.pathname, perms)) {
    return <Navigate to={defaultPathForUser(perms)} replace />
  }

  return <Outlet />
}
