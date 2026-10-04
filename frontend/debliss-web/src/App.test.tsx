import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { searchServices } from './api/catalogue.ts'
import {
  getCustomer,
  listCustomerAppointments,
  listCustomerNotes,
  searchCustomers,
} from './api/customers.ts'
import type { CustomerProfile, CustomerSummary } from './api/contracts.ts'
import { getCalendar } from './api/scheduling.ts'
import App from './App.tsx'

vi.mock('./api/scheduling.ts', () => ({
  getCalendar: vi.fn(),
  getAppointment: vi.fn(),
  createAppointment: vi.fn(),
  updateAppointment: vi.fn(),
  updateAppointmentSchedule: vi.fn(),
  setAppointmentStatus: vi.fn(),
}))

vi.mock('./api/catalogue.ts', () => ({
  listCategories: vi.fn(),
  searchServices: vi.fn(),
  getService: vi.fn(),
  setServiceStatus: vi.fn(),
}))

vi.mock('./api/customers.ts', () => ({
  searchCustomers: vi.fn(),
  getCustomer: vi.fn(),
  createCustomer: vi.fn(),
  updateCustomer: vi.fn(),
  setCustomerStatus: vi.fn(),
  listCustomerNotes: vi.fn(),
  createCustomerNote: vi.fn(),
  updateCustomerNote: vi.fn(),
  listCustomerAppointments: vi.fn(),
}))

vi.mock('./api/staff.ts', () => ({
  checkStaffAvailability: vi.fn(),
  listStaff: vi.fn(),
  getStaff: vi.fn(),
  createStaff: vi.fn(),
  updateStaff: vi.fn(),
  setStaffStatus: vi.fn(),
  listTimeOff: vi.fn(),
  createTimeOff: vi.fn(),
  cancelTimeOff: vi.fn(),
  listBlockedPeriods: vi.fn(),
  createBlockedPeriod: vi.fn(),
  cancelBlockedPeriod: vi.fn(),
}))

const linaSummary: CustomerSummary = {
  id: 'c1',
  firstName: 'Lina',
  lastName: 'Mansour',
  phoneNumber: '+971 50 555 0100',
  email: 'lina@debliss.example',
  isActive: true,
  createdAt: '2026-10-03T00:00:00Z',
  updatedAt: null,
}

const linaProfile: CustomerProfile = {
  id: 'c1',
  firstName: 'Lina',
  lastName: 'Mansour',
  email: 'lina@debliss.example',
  phoneNumber: '+971 50 555 0100',
  dateOfBirth: null,
  gender: null,
  address: null,
  isActive: true,
  appointmentSummary: {
    totalAppointments: 0,
    completedAppointments: 0,
    cancelledAppointments: 0,
    noShowAppointments: 0,
    lastVisitAt: null,
  },
  createdAt: '2026-10-03T00:00:00Z',
  updatedAt: null,
}

beforeEach(() => {
  vi.mocked(getCalendar).mockResolvedValue({
    from: new Date().toISOString(),
    to: new Date().toISOString(),
    staff: [],
    appointments: [],
    markers: [],
  })
  vi.mocked(searchCustomers).mockResolvedValue({
    items: [],
    totalCount: 0,
    page: 1,
    pageSize: 50,
    totalPages: 0,
    hasPreviousPage: false,
    hasNextPage: false,
  })
})

describe('App', () => {
  it('opens on the calendar with the sidebar marking it active', async () => {
    render(<App />)

    expect(
      await screen.findByRole('region', { name: 'Appointment calendar' }),
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Calendar' })).toHaveAttribute(
      'aria-current',
      'page',
    )
    expect(screen.getByRole('button', { name: 'Customers' })).not.toHaveAttribute('aria-current')
  })

  it('switches modules from the sidebar', async () => {
    const user = userEvent.setup()
    render(<App />)

    await screen.findByRole('region', { name: 'Appointment calendar' })
    await user.click(screen.getByRole('button', { name: 'Customers' }))

    expect(await screen.findByRole('heading', { name: 'Customers' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Customers' })).toHaveAttribute(
      'aria-current',
      'page',
    )
    expect(screen.getByRole('button', { name: 'Calendar' })).not.toHaveAttribute('aria-current')
    await waitFor(() => expect(searchCustomers).toHaveBeenCalled())
  })

  it('starts a booking for a customer chosen in the Customers section', async () => {
    const user = userEvent.setup()
    vi.mocked(searchCustomers).mockResolvedValue({
      items: [linaSummary],
      totalCount: 1,
      page: 1,
      pageSize: 50,
      totalPages: 1,
      hasPreviousPage: false,
      hasNextPage: false,
    })
    vi.mocked(getCustomer).mockResolvedValue(linaProfile)
    vi.mocked(listCustomerNotes).mockResolvedValue([])
    vi.mocked(listCustomerAppointments).mockResolvedValue([])
    vi.mocked(searchServices).mockResolvedValue({
      items: [],
      totalCount: 0,
      page: 1,
      pageSize: 100,
      totalPages: 0,
      hasPreviousPage: false,
      hasNextPage: false,
    })

    render(<App />)

    await screen.findByRole('region', { name: 'Appointment calendar' })
    await user.click(screen.getByRole('button', { name: 'Customers' }))
    await user.click(await screen.findByText('Lina Mansour'))

    await screen.findByRole('dialog', { name: 'Customer Lina Mansour' })
    await user.click(screen.getByRole('button', { name: 'Book appointment' }))

    // Back on the calendar, with the new-booking dialog already tied to her.
    expect(await screen.findByRole('dialog', { name: 'New appointment' })).toBeInTheDocument()
    expect(screen.getByText('Lina Mansour', { selector: 'strong' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Calendar' })).toHaveAttribute('aria-current', 'page')
  })
})
