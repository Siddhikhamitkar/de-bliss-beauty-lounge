import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import type { AuthUser } from '../api/auth.ts'
import { AppTopBar } from './AppTopBar.tsx'

const RECEPTIONIST: AuthUser = {
  id: 'u2',
  firstName: 'Nadia',
  lastName: 'FrontDesk',
  email: 'receptionist@debliss.dev',
  roles: ['RECEPTIONIST'],
  staffId: null,
}

describe('AppTopBar', () => {
  it('shows the signed-in identity and fires sign-out', async () => {
    const user = userEvent.setup()
    const onSignOut = vi.fn()
    render(
      <AppTopBar currentPath="/calendar" user={RECEPTIONIST} onSignOut={onSignOut} />,
    )

    expect(screen.getByText('Nadia FrontDesk')).toBeInTheDocument()
    expect(screen.getByText('RECEPTIONIST')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Sign out' }))
    expect(onSignOut).toHaveBeenCalledTimes(1)
  })

  it('labels the page with the module the path belongs to', () => {
    const { unmount } = render(
      <AppTopBar currentPath="/calendar" user={RECEPTIONIST} onSignOut={() => {}} />,
    )
    expect(screen.getByText('Calendar')).toBeInTheDocument()
    unmount()

    render(<AppTopBar currentPath="/categories" user={RECEPTIONIST} onSignOut={() => {}} />)
    expect(screen.getByText('Services')).toBeInTheDocument()
  })

  it('labels the owner landing path as Dashboard', () => {
    const owner: AuthUser = { ...RECEPTIONIST, roles: ['OWNER'] }
    render(<AppTopBar currentPath="/owner/dashboard" user={owner} onSignOut={() => {}} />)

    expect(screen.getByText('Dashboard')).toBeInTheDocument()
  })
})
