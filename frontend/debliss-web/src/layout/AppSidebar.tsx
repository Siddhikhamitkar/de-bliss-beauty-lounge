import type { ReactNode } from 'react'
import type { AuthUser, RoleName } from '../api/auth.ts'

type IconName =
  | 'dashboard'
  | 'calendar'
  | 'customers'
  | 'services'
  | 'staff'
  | 'billing'
  | 'reminders'
  | 'packages'
  | 'loyalty'
  | 'inventory'
  | 'expenses'
  | 'reports'
  | 'marketing'
  | 'settings'

type NavGroup = 'workspace' | 'catalog' | 'operations' | 'soon'

interface NavItem {
  key: string
  label: string
  icon: IconName
  /** Which visual section of the rail the item belongs to. */
  group: NavGroup
  /** The route a click navigates to. Items without one are not built yet. */
  path?: string
  /** Which roles may see the item at all. The sidebar is a courtesy filter —
   *  the API is the actual gate. */
  roles: readonly RoleName[]
}

/** Sidebar section order. Groups left with no visible items are skipped. */
const NAV_GROUPS: { key: NavGroup; label: string }[] = [
  { key: 'workspace', label: 'Workspace' },
  { key: 'catalog', label: 'Catalog' },
  { key: 'operations', label: 'Operations' },
  { key: 'soon', label: 'Coming soon' },
]

/**
 * The full salon roadmap, grouped in operational order. The receptionist's
 * rail shows only the built operational modules; the owner additionally sees
 * the future modules as disabled "Soon" rows so the product shape reads at a
 * glance. Staff have no modules yet, so their rail is brand plus identity
 * alone.
 */
const NAV_ITEMS: NavItem[] = [
  { key: 'dashboard', label: 'Dashboard', icon: 'dashboard', group: 'workspace', path: '/receptionist/dashboard', roles: ['OWNER', 'RECEPTIONIST'] },
  { key: 'calendar', label: 'Calendar', icon: 'calendar', group: 'workspace', path: '/calendar', roles: ['OWNER', 'RECEPTIONIST'] },
  { key: 'customers', label: 'Customers', icon: 'customers', group: 'workspace', path: '/customers', roles: ['OWNER', 'RECEPTIONIST'] },
  { key: 'services', label: 'Services', icon: 'services', group: 'catalog', path: '/services', roles: ['OWNER', 'RECEPTIONIST'] },
  { key: 'staff', label: 'Staff', icon: 'staff', group: 'catalog', path: '/staff', roles: ['OWNER', 'RECEPTIONIST'] },
  { key: 'billing', label: 'Billing', icon: 'billing', group: 'operations', path: '/billing', roles: ['OWNER', 'RECEPTIONIST'] },
  { key: 'reminders', label: 'Reminders', icon: 'reminders', group: 'operations', path: '/reminders', roles: ['OWNER', 'RECEPTIONIST'] },
  { key: 'packages', label: 'Packages', icon: 'packages', group: 'soon', roles: ['OWNER'] },
  { key: 'loyalty', label: 'Loyalty', icon: 'loyalty', group: 'soon', roles: ['OWNER'] },
  { key: 'inventory', label: 'Inventory', icon: 'inventory', group: 'soon', roles: ['OWNER'] },
  { key: 'expenses', label: 'Expenses', icon: 'expenses', group: 'soon', roles: ['OWNER'] },
  { key: 'reports', label: 'Reports', icon: 'reports', group: 'soon', roles: ['OWNER'] },
  { key: 'marketing', label: 'Marketing', icon: 'marketing', group: 'soon', roles: ['OWNER'] },
  { key: 'settings', label: 'Settings', icon: 'settings', group: 'soon', roles: ['OWNER'] },
]

/** Sub-screens that belong to a rail item even though they live on their own path. */
const ACTIVE_ALIASES: Record<string, string> = {
  '/categories': '/services',
  '/coupons': '/billing',
}

const ICON_PATHS: Record<IconName, ReactNode> = {
  dashboard: (
    <>
      <rect x="4" y="4" width="7" height="7" rx="1.6" />
      <rect x="13" y="4" width="7" height="7" rx="1.6" />
      <rect x="4" y="13" width="7" height="7" rx="1.6" />
      <rect x="13" y="13" width="7" height="7" rx="1.6" />
    </>
  ),
  calendar: (
    <>
      <rect x="4" y="5.5" width="16" height="14" rx="2" />
      <path d="M4 10h16" />
      <path d="M8.5 3.5v4" />
      <path d="M15.5 3.5v4" />
    </>
  ),
  customers: (
    <>
      <circle cx="12" cy="8.5" r="3.2" />
      <path d="M5.5 19c.9-3 3.4-4.6 6.5-4.6s5.6 1.6 6.5 4.6" />
    </>
  ),
  services: (
    <>
      <path d="M4.5 10.8V5.8A1.3 1.3 0 0 1 5.8 4.5h5l8 8a1.3 1.3 0 0 1 0 1.9l-4.4 4.4a1.3 1.3 0 0 1-1.9 0l-8-8Z" />
      <circle cx="8.8" cy="8.8" r="1.3" />
    </>
  ),
  staff: (
    <>
      <circle cx="9" cy="8.5" r="2.8" />
      <path d="M3.8 19c.8-2.7 2.9-4.2 5.2-4.2s4.4 1.5 5.2 4.2" />
      <path d="M15.5 6.2a2.6 2.6 0 0 1 0 4.9" />
      <path d="M17.5 14.9c1.3.6 2.3 1.9 2.8 3.6" />
    </>
  ),
  billing: (
    <>
      <rect x="3.5" y="6" width="17" height="12" rx="2" />
      <path d="M3.5 10h17" />
      <path d="M7 14.5h4" />
    </>
  ),
  reminders: (
    <>
      <path d="M12 4a5.3 5.3 0 0 0-5.3 5.3c0 4.2-1.7 5.6-1.7 5.6h14s-1.7-1.4-1.7-5.6A5.3 5.3 0 0 0 12 4Z" />
      <path d="M10.2 18.6a1.9 1.9 0 0 0 3.6 0" />
    </>
  ),
  packages: (
    <>
      <path d="M4 8.5 12 4l8 4.5v7L12 20l-8-4.5v-7Z" />
      <path d="m4 8.5 8 4.5 8-4.5" />
      <path d="M12 13v7" />
    </>
  ),
  loyalty: (
    <path d="M12 19.5l-6.6-6.4a4.4 4.4 0 0 1 6.2-6.2l.4.4.4-.4a4.4 4.4 0 0 1 6.2 6.2L12 19.5Z" />
  ),
  inventory: (
    <>
      <rect x="3.5" y="4.5" width="17" height="5" rx="1.3" />
      <path d="M5.5 9.5V18a1.5 1.5 0 0 0 1.5 1.5h10a1.5 1.5 0 0 0 1.5-1.5V9.5" />
      <path d="M10 13h4" />
    </>
  ),
  expenses: (
    <>
      <rect x="3" y="6.5" width="18" height="11" rx="2" />
      <circle cx="12" cy="12" r="2.6" />
      <path d="M6.5 12h2" />
      <path d="M15.5 12h2" />
    </>
  ),
  reports: (
    <>
      <path d="M5 19.5V13" />
      <path d="M12 19.5V6" />
      <path d="M19 19.5v-9" />
      <path d="M4 19.5h16" />
    </>
  ),
  marketing: (
    <>
      <path d="M13.5 5.5 6 9H4.5A1.5 1.5 0 0 0 3 10.5v3A1.5 1.5 0 0 0 4.5 15H6l7.5 3.5v-13Z" />
      <path d="M17 9a4 4 0 0 1 0 6" />
      <path d="M19.5 7a7.5 7.5 0 0 1 0 10" />
    </>
  ),
  settings: (
    <>
      <path d="M4 7h16" />
      <path d="M4 12h16" />
      <path d="M4 17h16" />
      <circle cx="9" cy="7" r="2" />
      <circle cx="15" cy="12" r="2" />
      <circle cx="8" cy="17" r="2" />
    </>
  ),
}

function NavIcon({ name }: { name: IconName }) {
  return (
    <svg
      className="side-nav-icon"
      viewBox="0 0 24 24"
      width="18"
      height="18"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.7"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      {ICON_PATHS[name]}
    </svg>
  )
}

/**
 * The application shell's left rail: brand, role-filtered section navigation
 * and the signed-in identity with sign-out. The active item is marked with
 * aria-current so tests and assistive tech read it.
 */
export function AppSidebar({
  currentPath,
  user,
  onNavigate,
  onSignOut,
}: {
  currentPath: string
  user: AuthUser
  onNavigate: (path: string) => void
  onSignOut: () => void
}) {
  const items = NAV_ITEMS.filter(item => item.roles.some(role => user.roles.includes(role)))
  const activePath = ACTIVE_ALIASES[currentPath] ?? currentPath
  const displayName = `${user.firstName} ${user.lastName}`.trim()
  const primaryRole = user.roles[0] ?? 'STAFF'

  return (
    <aside className="app-sidebar" aria-label="Main navigation">
      <div className="side-brand">
        <span className="side-brand-mark" aria-hidden="true">
          DB
        </span>
        <span className="side-brand-text">
          <span className="side-brand-name">De Bliss</span>
          <span className="side-brand-sub">Beauty Lounge</span>
        </span>
      </div>

      <nav className="side-nav">
        {NAV_GROUPS.map(group => {
          const groupItems = items.filter(item => item.group === group.key)
          if (groupItems.length === 0) {
            return null
          }

          return (
            <div key={group.key} className="side-group">
              <span className="side-group-label">{group.label}</span>
              {groupItems.map(item => {
                const isActive = item.path === activePath

                return (
                  <button
                    key={item.key}
                    type="button"
                    className="side-nav-item"
                    aria-current={isActive ? 'page' : undefined}
                    disabled={item.path === undefined}
                    onClick={() => {
                      if (item.path !== undefined) {
                        onNavigate(item.path)
                      }
                    }}
                  >
                    <NavIcon name={item.icon} />
                    <span className="side-nav-label">{item.label}</span>
                    {item.path === undefined && (
                      <>
                        {' '}
                        <span className="side-nav-soon">Soon</span>
                      </>
                    )}
                  </button>
                )
              })}
            </div>
          )
        })}
      </nav>

      <div className="side-user">
        <div className="side-user-text">
          <span className="side-user-name">{displayName}</span>
          <span className="side-user-role">{primaryRole}</span>
        </div>
        <button type="button" className="side-signout" onClick={onSignOut}>
          Sign out
        </button>
      </div>
    </aside>
  )
}
