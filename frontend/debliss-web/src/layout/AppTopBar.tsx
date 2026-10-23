import type { AuthUser } from '../api/auth.ts'
import { moduleLabelFor } from './navConfig.ts'

/**
 * The shell's compact top bar: the current module as page context on the
 * left, the signed-in identity and sign-out on the right. It stays pinned
 * while the page below scrolls.
 */
export function AppTopBar({
  currentPath,
  user,
  onSignOut,
}: {
  currentPath: string
  user: AuthUser
  onSignOut: () => void
}) {
  const displayName = `${user.firstName} ${user.lastName}`.trim()
  const primaryRole = user.roles[0] ?? 'STAFF'

  return (
    <header className="app-topbar">
      <span className="app-topbar-title">{moduleLabelFor(currentPath)}</span>
      <div className="app-topbar-user">
        <span className="app-topbar-id">
          <span className="app-topbar-name">{displayName}</span>
          <span className="app-topbar-role">{primaryRole}</span>
        </span>
        <button type="button" className="app-topbar-signout" onClick={onSignOut}>
          Sign out
        </button>
      </div>
    </header>
  )
}
