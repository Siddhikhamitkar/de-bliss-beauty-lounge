import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import type { AuthUser } from '../api/auth.ts'
import { AppSidebar } from './AppSidebar.tsx'

const RECEPTIONIST: AuthUser = {
  id: 'u2',
  firstName: 'Nadia',
  lastName: 'FrontDesk',
  email: 'receptionist@debliss.dev',
  roles: ['RECEPTIONIST'],
  staffId: null,
}

const OWNER: AuthUser = {
  id: 'u1',
  firstName: 'Omar',
  lastName: 'Owner',
  email: 'owner@debliss.dev',
  roles: ['OWNER'],
  staffId: null,
}

const STAFF: AuthUser = {
  id: 'u3',
  firstName: 'Sara',
  lastName: 'Stylist',
  email: 'staff@debliss.dev',
  roles: ['STAFF'],
  staffId: 's1',
}

describe('AppSidebar', () => {
  it('gives the receptionist only the built operational modules', () => {
    render(
      <AppSidebar currentPath="/calendar" user={RECEPTIONIST} onNavigate={() => {}} />,
    )

    expect(screen.getByRole('complementary', { name: 'Main navigation' })).toBeInTheDocument()

    for (const label of ['Dashboard', 'Calendar', 'Customers', 'Services', 'Staff', 'Billing', 'Inventory']) {
      expect(screen.getByRole('button', { name: new RegExp(`^${label}`) })).toBeInTheDocument()
    }

    // Future modules must not clutter the receptionist rail.
    for (const label of ['Packages', 'Loyalty', 'Expenses', 'Reports', 'Marketing', 'Settings']) {
      expect(screen.queryByRole('button', { name: new RegExp(`^${label}`) })).not.toBeInTheDocument()
    }
  })

  it('gives the owner the full roadmap with future modules visible but disabled', () => {
    render(
      <AppSidebar currentPath="/owner/dashboard" user={OWNER} onNavigate={() => {}} />,
    )

    // Inventory is built now: enabled, like the other operational modules.
    expect(screen.getByRole('button', { name: 'Inventory' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Loyalty' })).toBeEnabled()

    for (const label of ['Packages', 'Expenses', 'Reports', 'Marketing']) {
      expect(screen.getByRole('button', { name: new RegExp(`^${label}`) })).toBeDisabled()
    }
  })

  it('gives the owner the tax settings rail item and navigates to it', async () => {
    const user = userEvent.setup()
    const onNavigate = vi.fn()
    render(
      <AppSidebar currentPath="/settings" user={OWNER} onNavigate={onNavigate} />,
    )

    expect(screen.getByRole('button', { name: 'Settings' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Settings' })).toHaveAttribute('aria-current', 'page')

    await user.click(screen.getByRole('button', { name: 'Settings' }))
    expect(onNavigate).toHaveBeenCalledExactlyOnceWith('/settings')
  })

  it('gives staff an empty rail — their modules are a later task', () => {
    render(
      <AppSidebar currentPath="/staff/dashboard" user={STAFF} onNavigate={() => {}} />,
    )

    // No module entries at all — the rail is brand plus navigation alone;
    // identity and sign-out live in the top bar.
    expect(screen.queryByRole('button', { name: 'Dashboard' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Calendar' })).not.toBeInTheDocument()
    expect(screen.queryAllByRole('button')).toHaveLength(0)
  })

  it('marks the current path active and navigates by path', async () => {
    const user = userEvent.setup()
    const onNavigate = vi.fn()
    render(
      <AppSidebar currentPath="/calendar" user={RECEPTIONIST} onNavigate={onNavigate} />,
    )

    expect(screen.getByRole('button', { name: 'Calendar' })).toHaveAttribute('aria-current', 'page')
    expect(screen.getByRole('button', { name: 'Dashboard' })).not.toHaveAttribute('aria-current')

    await user.click(screen.getByRole('button', { name: 'Customers' }))
    await user.click(screen.getByRole('button', { name: 'Staff' }))

    expect(onNavigate).toHaveBeenNthCalledWith(1, '/customers')
    expect(onNavigate).toHaveBeenNthCalledWith(2, '/staff')
  })

  it('keeps Services active on its categories screen and Billing on coupons', () => {
    const { unmount } = render(
      <AppSidebar currentPath="/categories" user={RECEPTIONIST} onNavigate={() => {}} />,
    )
    expect(screen.getByRole('button', { name: 'Services' })).toHaveAttribute('aria-current', 'page')
    unmount()

    render(
      <AppSidebar currentPath="/coupons" user={RECEPTIONIST} onNavigate={() => {}} />,
    )
    expect(screen.getByRole('button', { name: 'Billing' })).toHaveAttribute('aria-current', 'page')
  })

  it('marks the dashboard item active on the owner landing path too', () => {
    render(<AppSidebar currentPath="/owner/dashboard" user={OWNER} onNavigate={() => {}} />)

    expect(screen.getByRole('button', { name: 'Dashboard' })).toHaveAttribute('aria-current', 'page')
  })
})
