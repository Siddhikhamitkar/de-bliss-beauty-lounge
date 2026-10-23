import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../App.tsx'
import { fetchCurrentUser } from '../api/auth.ts'
import type { RoleName } from '../api/auth.ts'
import { isKnownPath, isPathAllowed } from './router.ts'

vi.mock('../api/auth.ts', () => ({ fetchCurrentUser: vi.fn(), logout: vi.fn() }))
vi.mock('../staff/StaffPage.tsx', () => ({ StaffPage: ({ canManageSalary }: { canManageSalary?: boolean }) => <p>{canManageSalary ? 'Owner salary editor' : 'Operational staff editor'}</p> }))
vi.mock('../salary/SalarySlipsPage.tsx', () => ({ SalarySlipsPage: () => <h2>Protected salary workspace</h2> }))
vi.mock('../dashboard/DashboardPage.tsx', () => ({ DashboardPage: () => <h2>Reception workspace</h2> }))
vi.mock('../dashboard/OwnerDashboardPage.tsx', () => ({ OwnerDashboardPage: () => <h2>Owner workspace</h2> }))
vi.mock('../dashboard/StaffDashboardPage.tsx', () => ({ StaffDashboardPage: () => <h2>Staff workspace</h2> }))

function authenticate(role: RoleName) {
  vi.mocked(fetchCurrentUser).mockResolvedValue({ id: 'u1', firstName: 'Test', lastName: 'User', email: 'test@example.test', roles: [role], staffId: null })
}

beforeEach(() => { vi.resetAllMocks(); window.history.pushState({}, '', '/staff') })

describe('salary owner shell access', () => {
  it('recognizes salary slips only for OWNER, not an invented Admin role', () => {
    expect(isKnownPath('/salary-slips')).toBe(true)
    expect(isPathAllowed('/salary-slips', ['OWNER'])).toBe(true)
    for (const role of ['RECEPTIONIST', 'STAFF', 'ADMIN']) expect(isPathAllowed('/salary-slips', [role])).toBe(false)
  })

  it('passes salary editing only in the owner shell and exposes Operations navigation', async () => {
    authenticate('OWNER')
    render(<App />)
    expect(await screen.findByText('Owner salary editor')).toBeInTheDocument()
    await userEvent.setup().click(screen.getByRole('button', { name: 'Salary slips' }))
    expect(await screen.findByRole('heading', { name: 'Protected salary workspace' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Salary slips' })).toHaveAttribute('aria-current', 'page')
    expect(window.location.pathname).toBe('/salary-slips')
  })

  it('keeps receptionist staff editing operational and hides the salary nav item', async () => {
    authenticate('RECEPTIONIST')
    render(<App />)
    expect(await screen.findByText('Operational staff editor')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Salary slips' })).not.toBeInTheDocument()
  })

  it.each(['RECEPTIONIST', 'STAFF'] satisfies RoleName[])('redirects direct salary URLs for %s', async role => {
    authenticate(role)
    window.history.pushState({}, '', '/salary-slips')
    render(<App />)
    await waitFor(() => expect(window.location.pathname).toBe(role === 'STAFF' ? '/staff/dashboard' : '/receptionist/dashboard'))
    expect(screen.queryByRole('heading', { name: 'Protected salary workspace' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Salary slips' })).not.toBeInTheDocument()
  })
})
