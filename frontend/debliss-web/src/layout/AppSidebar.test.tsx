import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { AppSidebar } from './AppSidebar.tsx'

const ROADMAP = [
  'Dashboard',
  'Calendar',
  'Customers',
  'Services',
  'Staff',
  'Billing',
  'Packages',
  'Loyalty',
  'Inventory',
  'Expenses',
  'Reports',
  'Marketing',
  'Settings',
]

describe('AppSidebar', () => {
  it('lists the full salon roadmap with only the built modules actionable', () => {
    render(<AppSidebar current="calendar" onNavigate={() => {}} />)

    expect(screen.getByRole('complementary', { name: 'Main navigation' })).toBeInTheDocument()

    for (const label of ROADMAP) {
      expect(screen.getByRole('button', { name: new RegExp(`^${label}`) })).toBeInTheDocument()
    }

    expect(screen.getByRole('button', { name: 'Calendar' })).toHaveAttribute(
      'aria-current',
      'page',
    )
    // Modules that are not built yet are visible but not actionable.
    expect(screen.getByRole('button', { name: /^Packages/ })).toBeDisabled()
    expect(screen.getByRole('button', { name: /^Settings/ })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Staff' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Billing' })).toBeEnabled()
  })

  it('navigates to the implemented sections', async () => {
    const user = userEvent.setup()
    const onNavigate = vi.fn()
    render(<AppSidebar current="calendar" onNavigate={onNavigate} />)

    await user.click(screen.getByRole('button', { name: 'Customers' }))
    await user.click(screen.getByRole('button', { name: 'Staff' }))

    expect(onNavigate).toHaveBeenNthCalledWith(1, 'customers')
    expect(onNavigate).toHaveBeenNthCalledWith(2, 'staff')
  })

  it('marks Services as the active section while its categories screen is open', () => {
    render(<AppSidebar current="categories" onNavigate={() => {}} />)

    expect(screen.getByRole('button', { name: 'Services' })).toHaveAttribute(
      'aria-current',
      'page',
    )
    expect(screen.getByRole('button', { name: 'Calendar' })).not.toHaveAttribute('aria-current')
  })

  it('marks Billing as the active section while its coupons screen is open', () => {
    render(<AppSidebar current="coupons" onNavigate={() => {}} />)

    expect(screen.getByRole('button', { name: 'Billing' })).toHaveAttribute('aria-current', 'page')
    expect(screen.getByRole('button', { name: 'Calendar' })).not.toHaveAttribute('aria-current')
  })

  it('enables Dashboard and navigates to it', async () => {
    const user = userEvent.setup()
    const onNavigate = vi.fn()
    render(<AppSidebar current="calendar" onNavigate={onNavigate} />)

    const dashboard = screen.getByRole('button', { name: 'Dashboard' })
    expect(dashboard).toBeEnabled()
    expect(dashboard).not.toHaveAttribute('aria-current')

    await user.click(dashboard)
    expect(onNavigate).toHaveBeenCalledWith('dashboard')
  })
})
