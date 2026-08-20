import { useCallback, useEffect, useState } from 'react'
import { useAuth } from '../contexts/AuthContext'

function storageKey(username: string) {
  return `h1stats_favorite_reports:${username.trim().toLowerCase()}`
}

function readLocal(username: string): string[] {
  try {
    const raw = localStorage.getItem(storageKey(username))
    if (!raw) return []
    const parsed = JSON.parse(raw)
    return Array.isArray(parsed) ? parsed.filter((p) => typeof p === 'string') : []
  } catch {
    return []
  }
}

function writeLocal(username: string, paths: string[]) {
  localStorage.setItem(storageKey(username), JSON.stringify(paths))
}

export function useReportFavorites() {
  const { username } = useAuth()
  const [favorites, setFavorites] = useState<string[]>(() => (username ? readLocal(username) : []))

  useEffect(() => {
    if (!username) {
      setFavorites([])
      return
    }

    setFavorites(readLocal(username))
    let cancelled = false
    fetch(`/api/auth/favorites?username=${encodeURIComponent(username)}`, { cache: 'no-store' })
      .then((res) => (res.ok ? res.json() : null))
      .then((data) => {
        if (cancelled || !Array.isArray(data)) return
        const paths = data.filter((p: unknown): p is string => typeof p === 'string')
        writeLocal(username, paths)
        setFavorites(paths)
      })
      .catch(() => {})

    return () => {
      cancelled = true
    }
  }, [username])

  const isFavorite = useCallback((path: string) => favorites.includes(path), [favorites])

  const toggleFavorite = useCallback(
    (path: string) => {
      if (!username) return
      setFavorites((prev) => {
        const next = prev.includes(path) ? prev.filter((p) => p !== path) : [...prev, path]
        writeLocal(username, next)
        fetch('/api/auth/favorites', {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ username, paths: next }),
        }).catch(() => {})
        return next
      })
    },
    [username]
  )

  return { favorites, isFavorite, toggleFavorite }
}
