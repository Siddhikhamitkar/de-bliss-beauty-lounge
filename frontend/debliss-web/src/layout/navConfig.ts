import type { RoleName } from '../api/auth.ts'

export type IconName =
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

export type NavGroup = 'workspace' | 'catalog' | 'operations' | 'soon'

export interface NavItem {
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
export const NAV_GROUPS: { key: NavGroup; label: string }[] = [
  { key: 'workspace', label: 'Workspace' },
  { key: 'catalog', label: 'Catalog' },
  { key: 'operations', label: 'Operations' },
  { key: 'soon', label: 'Coming soon' },
]

/**
 * The full salon roadmap, grouped in operational order. The receptionist's
 * rail shows only the built operational modules; the owner additionally sees
 * the future modules as disabled "Soon" rows so the product shape reads at a
 * glance. Staff have no modules yet, so their rail is brand alone.
 */
export const NAV_ITEMS: NavItem[] = [
  { key: 'dashboard', label: 'Dashboard', icon: 'dashboard', group: 'workspace', path: '/receptionist/dashboard', roles: ['OWNER', 'RECEPTIONIST'] },
  { key: 'calendar', label: 'Calendar', icon: 'calendar', group: 'workspace', path: '/calendar', roles: ['OWNER', 'RECEPTIONIST'] },
  { key: 'customers', label: 'Customers', icon: 'customers', group: 'workspace', path: '/customers', roles: ['OWNER', 'RECEPTIONIST'] },
  { key: 'services', label: 'Services', icon: 'services', group: 'catalog', path: '/services', roles: ['OWNER', 'RECEPTIONIST'] },
  { key: 'staff', label: 'Staff', icon: 'staff', group: 'catalog', path: '/staff', roles: ['OWNER', 'RECEPTIONIST'] },
  { key: 'billing', label: 'Billing', icon: 'billing', group: 'operations', path: '/billing', roles: ['OWNER', 'RECEPTIONIST'] },
  { key: 'reminders', label: 'Reminders', icon: 'reminders', group: 'operations', path: '/reminders', roles: ['OWNER', 'RECEPTIONIST'] },
  { key: 'inventory', label: 'Inventory', icon: 'inventory', group: 'operations', path: '/inventory', roles: ['OWNER', 'RECEPTIONIST'] },
  { key: 'settings', label: 'Settings', icon: 'settings', group: 'operations', path: '/settings', roles: ['OWNER'] },
  { key: 'packages', label: 'Packages', icon: 'packages', group: 'soon', roles: ['OWNER'] },
  { key: 'loyalty', label: 'Loyalty', icon: 'loyalty', group: 'soon', roles: ['OWNER'] },
  { key: 'expenses', label: 'Expenses', icon: 'expenses', group: 'soon', roles: ['OWNER'] },
  { key: 'reports', label: 'Reports', icon: 'reports', group: 'soon', roles: ['OWNER'] },
  { key: 'marketing', label: 'Marketing', icon: 'marketing', group: 'soon', roles: ['OWNER'] },
]

/** Sub-screens that belong to a rail item even though they live on their own path. */
export const ACTIVE_ALIASES: Record<string, string> = {
  '/categories': '/services',
  '/coupons': '/billing',
  '/owner/dashboard': '/receptionist/dashboard',
}

/** The module label a path belongs to, used by the top bar for page context. */
export function moduleLabelFor(path: string): string {
  const active = ACTIVE_ALIASES[path] ?? path
  return NAV_ITEMS.find(item => item.path === active)?.label ?? 'De Bliss'
}
