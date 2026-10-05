import { createContext, useCallback, useContext, useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { fetchCurrentUser, login as loginRequest, logout as logoutRequest } from '../api/auth.ts'
import type { AuthUser } from '../api/auth.ts'

interface AuthContextValue {
  /** null while the current session is being checked, null afterwards when signed out. */
  user: AuthUser | null
  loading: boolean
  login: (email: string, password: string) => Promise<AuthUser>
  logout: () => Promise<void>
}

const AuthContext = createContext<AuthContextValue | null>(null)

/**
 * Holds the signed-in user for the whole SPA. The session itself lives in the
 * API's HttpOnly cookie — nothing credential-shaped is kept in this context,
 * localStorage, or a log line.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let ignore = false

    fetchCurrentUser()
      .then(found => {
        if (!ignore) {
          setUser(found)
          setLoading(false)
        }
      })
      .catch(() => {
        if (!ignore) {
          setLoading(false)
        }
      })

    return () => {
      ignore = true
    }
  }, [])

  // Any module call that comes back 401 means the session expired or was
  // revoked server-side; drop the user so the router returns to the login page.
  useEffect(() => {
    const onUnauthorized = () => setUser(null)
    window.addEventListener('debliss:unauthorized', onUnauthorized)
    return () => window.removeEventListener('debliss:unauthorized', onUnauthorized)
  }, [])

  const login = useCallback(async (email: string, password: string) => {
    const authenticated = await loginRequest(email, password)
    setUser(authenticated)
    return authenticated
  }, [])

  const logout = useCallback(async () => {
    await logoutRequest()
    setUser(null)
  }, [])

  return (
    <AuthContext.Provider value={{ user, loading, login, logout }}>
      {children}
    </AuthContext.Provider>
  )
}

export function useAuth(): AuthContextValue {
  const value = useContext(AuthContext)

  if (value === null) {
    throw new Error('useAuth must be used inside <AuthProvider>.')
  }

  return value
}
