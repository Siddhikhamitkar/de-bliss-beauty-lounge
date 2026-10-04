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
import type { Appointment, CustomerProfile, CustomerSummary, ReceptionistDashboard } from './api/contracts.ts'
import { getReceptionistDashboard } from './api/dashboard.ts'
import { startInvoice, searchInvoices } from './api/billing.ts'
import { getAppointment, getCalendar } from './api/scheduling.ts'
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
  searchStaff: vi.fn(),
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

vi.mock('./api/billing.ts', () => ({
  searchInvoices: vi.fn(),
  getInvoice: vi.fn(),
  startInvoice: vi.fn(),
  checkoutInvoice: vi.fn(),
  getReceipt: vi.fn(),
  validateCoupon: vi.fn(),
  searchCoupons: vi.fn(),
  getCoupon: vi.fn(),
  createCoupon: vi.fn(),
  updateCoupon: vi.fn(),
  setCouponStatus: vi.fn(),
}))

vi.mock('./api/dashboard.ts', () => ({
  getReceptionistDashboard: vi.fn(),
}))

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

const saraAppointment: Appointment = {
  id: 'appt-1',
  customerId: 'c1',
  customerName: 'Lina Mansour',
  customerPhone: '+971 50 555 0100',
  startsAt: '2026-10-03T12:00:00+04:00',
  endsAt: '2026-10-03T12:30:00+04:00',
  status: 'Confirmed',
  source: 'Phone',
  notes: null,
  cancellationReason: null,
  services: [],
  totalPrice: 100,
  totalDurationMinutes: 30,
  createdAt: '2026-10-03T00:00:00Z',
  updatedAt: null,
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
  it('opens on the dashboard with the sidebar marking it active', async () => {
    render(<App />)

    expect(
      await screen.findByRole('region', { name: 'Receptionist dashboard' }),
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Dashboard' })).toHaveAttribute(
      'aria-current',
      'page',
    )
    expect(screen.getByRole('button', { name: 'Calendar' })).not.toHaveAttribute('aria-current')
  })

  it('switches modules from the sidebar', async () => {
    const user = userEvent.setup()
    render(<App />)

    await screen.findByRole('region', { name: 'Receptionist dashboard' })
    await user.click(screen.getByRole('button', { name: 'Calendar' }))

    expect(await screen.findByRole('region', { name: 'Appointment calendar' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Calendar' })).toHaveAttribute(
      'aria-current',
      'page',
    )
    expect(screen.getByRole('button', { name: 'Dashboard' })).not.toHaveAttribute('aria-current')
  })

  it('hands a schedule row to the calendar details workflow', async () => {
    const user = userEvent.setup()
    vi.mocked(getReceptionistDashboard).mockResolvedValue({
      ...emptyDashboard,
      schedule: [
        {
          id: 'appt-1',
          customerId: 'c1',
          customerName: 'Lina Mansour',
          startsAt: '2026-10-03T12:00:00+04:00',
          endsAt: '2026-10-03T12:30:00+04:00',
          status: 'Confirmed',
          servicesLabel: 'Haircut',
          staffName: null,
          staffColor: null,
          canCheckout: false,
        },
      ],
    })
    vi.mocked(getAppointment).mockResolvedValue(saraAppointment)
    render(<App />)

    await user.click(
      await screen.findByRole('button', { name: /Lina Mansour Haircut/ }),
    )

    // The calendar takes over with the appointment's details panel open.
    expect(await screen.findByRole('dialog', { name: 'Appointment details' })).toBeInTheDocument()
    expect(vi.mocked(getAppointment)).toHaveBeenCalledWith('appt-1')
    expect(screen.getByRole('button', { name: 'Calendar' })).toHaveAttribute('aria-current', 'page')
  })

  it('opens the booking dialog from the dashboard quick action', async () => {
    const user = userEvent.setup()
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

    await user.click(await screen.findByRole('button', { name: /^New appointment/ }))

    expect(await screen.findByRole('dialog', { name: 'New appointment' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Calendar' })).toHaveAttribute('aria-current', 'page')
  })

  it('opens the customer form from the dashboard quick action', async () => {
    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByRole('button', { name: /^Add customer/ }))

    expect(await screen.findByRole('heading', { name: 'New customer' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Customers' })).toHaveAttribute(
      'aria-current',
      'page',
    )
  })

  it('lands on the Customers list from Find customer', async () => {
    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByRole('button', { name: /^Find customer/ }))

    expect(await screen.findByRole('heading', { name: 'Customers' })).toBeInTheDocument()
    await waitFor(() => expect(searchCustomers).toHaveBeenCalled())
  })

  it('hands a completed visit checkout to the existing overlay', async () => {
    const user = userEvent.setup()
    vi.mocked(startInvoice).mockResolvedValue(undefined as never)
    vi.mocked(getReceptionistDashboard).mockResolvedValue({
      ...emptyDashboard,
      schedule: [
        {
          id: 'appt-9',
          customerId: 'c1',
          customerName: 'Lina Mansour',
          startsAt: '2026-10-03T08:00:00+04:00',
          endsAt: '2026-10-03T08:30:00+04:00',
          status: 'Completed',
          servicesLabel: 'Haircut',
          staffName: null,
          staffColor: null,
          canCheckout: true,
        },
      ],
    })
    render(<App />)

    await user.click(await screen.findByRole('button', { name: 'Checkout' }))

    // CheckoutPage mounts over the dashboard (it fetches its own state).
    expect(await screen.findByRole('dialog', { name: 'Checkout' })).toBeInTheDocument()
  })

  it('navigates to Billing from the quick action', async () => {
    const user = userEvent.setup()
    vi.mocked(searchInvoices).mockResolvedValue({
      items: [],
      totalCount: 0,
      page: 1,
      pageSize: 50,
      totalPages: 0,
      hasPreviousPage: false,
      hasNextPage: false,
    })
    render(<App />)

    await user.click(await screen.findByRole('button', { name: /^Billing & checkout/ }))

    expect(await screen.findByRole('heading', { name: 'Billing' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Billing' })).toHaveAttribute('aria-current', 'page')
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

    await screen.findByRole('region', { name: 'Receptionist dashboard' })
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
