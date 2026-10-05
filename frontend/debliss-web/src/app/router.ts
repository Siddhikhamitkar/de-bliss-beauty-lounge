import { useEffect, useState } from 'react'

/**
 * The smallest router that covers the SPA: paths pushed onto the history stack,
 * a popstate listener so back/forward keep React in sync. No route library —
 * the app has a flat table of paths and role guards, nothing nested to match.
 */
export function navigateTo(path: string): void {
  if (window.location.pathname !== path) {
    window.history.pushState({}, '', path)
  }

  window.dispatchEvent(new PopStateEvent('popstate'))
}

export function usePathname(): string {
  const [path, setPath] = useState(() => window.location.pathname)

  useEffect(() => {
    const sync = () => setPath(window.location.pathname)
    window.addEventListener('popstate', sync)
    return () => window.removeEventListener('popstate', sync)
  }, [])

  return path
}

/** The role a user lands on after login: their own dashboard, never someone else's. */
export function homePathFor(roles: readonly string[]): string {
  if (roles.includes('OWNER')) {
    return '/owner/dashboard'
  }

  if (roles.includes('RECEPTIONIST')) {
    return '/receptionist/dashboard'
  }

  return '/staff/dashboard'
}

/**
 * Which roles each protected path admits. Owner and receptionist share the
 * operational modules; the owner workspace is owner-only; the staff workspace
 * exists as a role landing but carries no modules yet.
 */
const ROUTE_ACCESS: Record<string, readonly string[]> = {
  '/owner/dashboard': ['OWNER'],
  '/receptionist/dashboard': ['OWNER', 'RECEPTIONIST'],
  '/staff/dashboard': ['STAFF'],
  '/calendar': ['OWNER', 'RECEPTIONIST'],
  '/customers': ['OWNER', 'RECEPTIONIST'],
  '/services': ['OWNER', 'RECEPTIONIST'],
  '/categories': ['OWNER', 'RECEPTIONIST'],
  '/staff': ['OWNER', 'RECEPTIONIST'],
  '/billing': ['OWNER', 'RECEPTIONIST'],
  '/coupons': ['OWNER', 'RECEPTIONIST'],
  '/reminders': ['OWNER', 'RECEPTIONIST'],
}

/** A path the routing table knows; anything else falls back to the role home. */
export function isKnownPath(path: string): boolean {
  return path in ROUTE_ACCESS
}

/** Whether one of the user's roles admits the path. */
export function isPathAllowed(path: string, roles: readonly string[]): boolean {
  const allowed = ROUTE_ACCESS[path]
  return allowed !== undefined && roles.some(role => allowed.includes(role))
}
