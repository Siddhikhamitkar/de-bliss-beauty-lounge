import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../App.tsx'
import { fetchCurrentUser } from '../api/auth.ts'
import type { RoleName } from '../api/auth.ts'
import { getLoyaltySettings } from '../api/loyalty.ts'
import { isKnownPath, isPathAllowed } from './router.ts'

vi.mock('../api/auth.ts', () => ({ fetchCurrentUser: vi.fn(), logout: vi.fn() }))
vi.mock('../api/loyalty.ts', () => ({ getLoyaltySettings: vi.fn(), updateLoyaltySettings: vi.fn() }))
vi.mock('../dashboard/DashboardPage.tsx', () => ({ DashboardPage: () => <h2>Reception workspace</h2> }))
vi.mock('../dashboard/OwnerDashboardPage.tsx', () => ({ OwnerDashboardPage: () => <h2>Owner workspace</h2> }))
vi.mock('../dashboard/StaffDashboardPage.tsx', () => ({ StaffDashboardPage: () => <h2>Staff workspace</h2> }))

function authenticate(role: RoleName) {
  vi.mocked(fetchCurrentUser).mockResolvedValue({ id: 'u1', firstName: 'Test', lastName: 'User', email: 'test@example.test', roles: [role], staffId: null })
}
beforeEach(() => {
  window.history.pushState({}, '', '/loyalty')
  vi.mocked(getLoyaltySettings).mockResolvedValue({ isEnabled: false, earningPercentage: 5, pointValueAed: 1, minimumRedemptionPoints: 0, maximumRedemptionPercentage: 50, allowPartialRedemption: true, minimumInvoiceAmount: null })
})

describe('owner-only loyalty workspace', () => {
  it('guards the route by the exact OWNER role', () => {
    expect(isKnownPath('/loyalty')).toBe(true)
    expect(isPathAllowed('/loyalty', ['OWNER'])).toBe(true)
    for (const role of ['RECEPTIONIST', 'STAFF', 'ADMIN']) expect(isPathAllowed('/loyalty', [role])).toBe(false)
  })
  it('activates the owner navigation and loads the real settings page', async () => {
    authenticate('OWNER')
    window.history.pushState({}, '', '/owner/dashboard')
    render(<App />)
    await userEvent.setup().click(await screen.findByRole('button', { name: 'Loyalty' }))
    expect(await screen.findByLabelText('Enable loyalty program')).toBeInTheDocument()
    expect(window.location.pathname).toBe('/loyalty')
    expect(screen.getByRole('button', { name: 'Loyalty' })).toHaveAttribute('aria-current', 'page')
  })
  it.each(['RECEPTIONIST', 'STAFF'] satisfies RoleName[])('redirects direct URLs without fetching settings for %s', async role => {
    authenticate(role)
    render(<App />)
    await waitFor(() => expect(window.location.pathname).toBe(role === 'STAFF' ? '/staff/dashboard' : '/receptionist/dashboard'))
    expect(screen.queryByRole('button', { name: 'Loyalty' })).not.toBeInTheDocument()
    expect(getLoyaltySettings).not.toHaveBeenCalled()
  })
})
