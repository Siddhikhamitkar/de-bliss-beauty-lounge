import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fetchCurrentUser, login, logout } from '../api/auth.ts'
import type { AuthUser } from '../api/auth.ts'
import type { ReceptionistDashboard } from '../api/contracts.ts'
import { getReceptionistDashboard } from '../api/dashboard.ts'
import { getCalendar } from '../api/scheduling.ts'
import App from '../App.tsx'

vi.mock('../api/auth.ts', () => ({
  login: vi.fn(),
  fetchCurrentUser: vi.fn(),
  logout: vi.fn(),
}))

vi.mock('../api/scheduling.ts', () => ({
  getCalendar: vi.fn(),
  getAppointment: vi.fn(),
  createAppointment: vi.fn(),
  updateAppointment: vi.fn(),
  updateAppointmentSchedule: vi.fn(),
  setAppointmentStatus: vi.fn(),
}))

vi.mock('../api/dashboard.ts', () => ({
  getReceptionistDashboard: vi.fn(),
}))

const ownerUser: AuthUser = {
  id: 'u1',
  firstName: 'Omar',
  lastName: 'Owner',
  email: 'owner@debliss.dev',
  roles: ['OWNER'],
  staffId: null,
}

const receptionistUser: AuthUser = {
  id: 'u2',
  firstName: 'Nadia',
  lastName: 'FrontDesk',
  email: 'receptionist@debliss.dev',
  roles: ['RECEPTIONIST'],
  staffId: null,
}

const staffUser: AuthUser = {
  id: 'u3',
  firstName: 'Sara',
  lastName: 'Stylist',
  email: 'staff@debliss.dev',
  roles: ['STAFF'],
  staffId: 's1',
}

const emptyDashboard: ReceptionistDashboard = {
  generatedAt: '2026-10-03T10:00:00+04:00',
  today: {
    date: '2026-10-03',
    total: 0,
    completed: 0,
    inProgress: 0,
    upcoming: 0,
    noShow: 0,
    cancelled: 0,
    walkIns: 0,
  },
  schedule: [],
  upcoming: [],
  staff: [],
  alerts: {
    birthdaysToday: 0,
    birthdayNamesToday: [],
    birthdayNamesLaterThisWeek: [],
    firstVisitsToday: 0,
    recentNotes: [],
  },
  popularServices: [],
}

beforeEach(() => {
  vi.mocked(getReceptionistDashboard).mockResolvedValue(emptyDashboard)
  vi.mocked(getCalendar).mockResolvedValue({
    from: new Date().toISOString(),
    to: new Date().toISOString(),
    staff: [],
    appointments: [],
    markers: [],
  })
  vi.mocked(logout).mockResolvedValue(undefined)
})

describe('auth flow', () => {
  it('redirects an unauthenticated visitor to the login page', async () => {
    vi.mocked(fetchCurrentUser).mockResolvedValue(null)
    window.history.pushState({}, '', '/calendar')
    render(<App />)

    expect(await screen.findByRole('heading', { name: 'Welcome back' })).toBeInTheDocument()
    await waitFor(() => expect(window.location.pathname).toBe('/login'))
    // The protected module never rendered.
    expect(screen.queryByRole('region', { name: 'Appointment calendar' })).not.toBeInTheDocument()
  })

  it('keeps an invalid login on the page with the generic error', async () => {
    vi.mocked(fetchCurrentUser).mockResolvedValue(null)
    vi.mocked(login).mockRejectedValue(new Error('Email or password is incorrect.'))
    window.history.pushState({}, '', '/login')
    const user = userEvent.setup()
    render(<App />)

    await user.type(await screen.findByLabelText(/email/i), 'nobody@debliss.dev')
    await user.type(screen.getByLabelText(/^password$/i), 'wrong')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Email or password is incorrect.',
    )
    expect(window.location.pathname).toBe('/login')
  })

  it('lands the receptionist on the ReceptionistDashboard after login', async () => {
    vi.mocked(fetchCurrentUser).mockResolvedValue(null)
    vi.mocked(login).mockResolvedValue(receptionistUser)
    window.history.pushState({}, '', '/login')
    const user = userEvent.setup()
    render(<App />)

    await user.type(await screen.findByLabelText(/email/i), 'receptionist@debliss.dev')
    await user.type(screen.getByLabelText(/^password$/i), 'DeBliss#Front2026!')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(
      await screen.findByRole('region', { name: 'Receptionist dashboard' }),
    ).toBeInTheDocument()
    expect(window.location.pathname).toBe('/receptionist/dashboard')
  })

  it('lands the owner on the OwnerDashboard placeholder after login', async () => {
    vi.mocked(fetchCurrentUser).mockResolvedValue(null)
    vi.mocked(login).mockResolvedValue(ownerUser)
    window.history.pushState({}, '', '/login')
    const user = userEvent.setup()
    render(<App />)

    await user.type(await screen.findByLabelText(/email/i), 'owner@debliss.dev')
    await user.type(screen.getByLabelText(/^password$/i), 'DeBliss#Owner2026!')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByRole('region', { name: 'Owner dashboard' })).toBeInTheDocument()
    expect(screen.getByText('Your owner workspace is being prepared.')).toBeInTheDocument()
    expect(window.location.pathname).toBe('/owner/dashboard')
  })

  it('lands staff on the StaffDashboard placeholder after login', async () => {
    vi.mocked(fetchCurrentUser).mockResolvedValue(null)
    vi.mocked(login).mockResolvedValue(staffUser)
    window.history.pushState({}, '', '/login')
    const user = userEvent.setup()
    render(<App />)

    await user.type(await screen.findByLabelText(/email/i), 'staff@debliss.dev')
    await user.type(screen.getByLabelText(/^password$/i), 'DeBliss#Team2026!')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByRole('region', { name: 'Staff dashboard' })).toBeInTheDocument()
    expect(screen.getByText('Your staff workspace is being prepared.')).toBeInTheDocument()
    expect(window.location.pathname).toBe('/staff/dashboard')
  })

  it('keeps the receptionist out of the owner workspace path and vice versa shape', async () => {
    // A receptionist bookmarking /owner/dashboard is moved to her own home.
    vi.mocked(fetchCurrentUser).mockResolvedValue(receptionistUser)
    window.history.pushState({}, '', '/owner/dashboard')
    render(<App />)

    expect(
      await screen.findByRole('region', { name: 'Receptionist dashboard' }),
    ).toBeInTheDocument()
    expect(window.location.pathname).toBe('/receptionist/dashboard')
  })

  it('shapes the sidebar by role after login', async () => {
    vi.mocked(fetchCurrentUser).mockResolvedValue(null)
    vi.mocked(login).mockResolvedValue(receptionistUser)
    window.history.pushState({}, '', '/login')
    const user = userEvent.setup()
    render(<App />)

    await user.type(await screen.findByLabelText(/email/i), 'receptionist@debliss.dev')
    await user.type(screen.getByLabelText(/^password$/i), 'x')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    await screen.findByRole('region', { name: 'Receptionist dashboard' })
    // Only the built modules — no owner-only "Soon" clutter.
    expect(screen.getByRole('button', { name: 'Calendar' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Packages/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Settings/ })).not.toBeInTheDocument()
  })

  it('logs out back to the login page and clears the session', async () => {
    vi.mocked(fetchCurrentUser).mockResolvedValue(receptionistUser)
    window.history.pushState({}, '', '/receptionist/dashboard')
    const user = userEvent.setup()
    render(<App />)

    await screen.findByRole('region', { name: 'Receptionist dashboard' })
    await user.click(screen.getByRole('button', { name: 'Sign out' }))

    expect(await screen.findByRole('heading', { name: 'Welcome back' })).toBeInTheDocument()
    expect(vi.mocked(logout)).toHaveBeenCalledTimes(1)
    await waitFor(() => expect(window.location.pathname).toBe('/login'))
  })

  it('moves a signed-in user away from the login page to their home', async () => {
    vi.mocked(fetchCurrentUser).mockResolvedValue(ownerUser)
    window.history.pushState({}, '', '/login')
    render(<App />)

    expect(await screen.findByRole('region', { name: 'Owner dashboard' })).toBeInTheDocument()
    await waitFor(() => expect(window.location.pathname).toBe('/owner/dashboard'))
  })
})
