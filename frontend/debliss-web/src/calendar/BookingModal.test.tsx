import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/client.ts'
import type {
  Appointment,
  CustomerProfile,
  CustomerSummary,
  EligibleStaff,
  PagedResult,
  Service,
  ServiceSummary,
} from '../api/contracts.ts'
import { getService, searchServices } from '../api/catalogue.ts'
import { createCustomer, searchCustomers } from '../api/customers.ts'
import { createAppointment, updateAppointment } from '../api/scheduling.ts'
import { checkStaffAvailability } from '../api/staff.ts'
import { BookingModal } from './BookingModal.tsx'
import type { BookingState } from './CalendarPage.tsx'

vi.mock('../api/catalogue.ts', () => ({
  searchServices: vi.fn(),
  getService: vi.fn(),
}))

vi.mock('../api/customers.ts', () => ({
  searchCustomers: vi.fn(),
  createCustomer: vi.fn(),
}))

vi.mock('../api/staff.ts', () => ({
  checkStaffAvailability: vi.fn(),
}))

vi.mock('../api/scheduling.ts', () => ({
  createAppointment: vi.fn(),
  updateAppointment: vi.fn(),
}))

function page<T>(items: T[]): PagedResult<T> {
  return {
    items,
    totalCount: items.length,
    page: 1,
    pageSize: items.length,
    totalPages: 1,
    hasPreviousPage: false,
    hasNextPage: false,
  }
}

function eligible(id: string, firstName: string): EligibleStaff {
  return { staffId: id, firstName, lastName: 'Test', jobTitle: 'Stylist', isActive: true }
}

function menuService(id: string, name: string, durationMinutes: number): ServiceSummary {
  return {
    id,
    name,
    categoryId: 'cat1',
    categoryName: 'Hair',
    durationMinutes,
    price: 120,
    isActive: true,
    isOnlineBookable: true,
    isBookable: true,
    eligibleStaffCount: 2,
    sortOrder: 0,
    createdAt: new Date().toISOString(),
    updatedAt: null,
  }
}

function fullService(id: string, name: string, staff: EligibleStaff[]): Service {
  return {
    ...menuService(id, name, 60),
    description: null,
    isCategoryActive: true,
    eligibleStaff: staff,
  }
}

function customer(id: string, firstName: string, lastName: string, phone: string): CustomerSummary {
  return {
    id,
    firstName,
    lastName,
    phoneNumber: phone,
    email: null,
    isActive: true,
    createdAt: new Date().toISOString(),
    updatedAt: null,
  }
}

function customerProfile(summary: CustomerSummary): CustomerProfile {
  return {
    ...summary,
    dateOfBirth: null,
    gender: null,
    address: null,
    appointmentSummary: {
      totalAppointments: 0,
      completedAppointments: 0,
      cancelledAppointments: 0,
      lastVisitAt: null,
    },
  }
}

const menu = [menuService('svc1', 'Signature Cut', 60)]
const avaThenNoah = [eligible('s1', 'Ava'), eligible('s2', 'Noah')]
const lina = customer('c1', 'Lina', 'Mansour', '+971 50 555 0101')

function savedAppointment(): Appointment {
  const now = new Date().toISOString()
  return {
    id: 'a9',
    customerId: 'c1',
    customerName: 'Lina Mansour',
    customerPhone: '+971 50 555 0101',
    startsAt: now,
    endsAt: now,
    status: 'Pending',
    source: 'Phone',
    notes: null,
    cancellationReason: null,
    services: [],
    totalPrice: 0,
    totalDurationMinutes: 0,
    createdAt: now,
    updatedAt: null,
  }
}

function createBooking(staffId: string | null, startMinutes: number | null): Exclude<BookingState, null> {
  return { kind: 'create', day: new Date(), staffId, startMinutes }
}

beforeEach(() => {
  vi.mocked(searchServices).mockResolvedValue(page(menu))
  vi.mocked(getService).mockResolvedValue(fullService('svc1', 'Signature Cut', avaThenNoah))
  vi.mocked(searchCustomers).mockResolvedValue(page([lina]))
  vi.mocked(createCustomer).mockRejectedValue(new Error('not used'))
  vi.mocked(createAppointment).mockResolvedValue(savedAppointment())
  vi.mocked(updateAppointment).mockResolvedValue(savedAppointment())
  vi.mocked(checkStaffAvailability).mockResolvedValue({
    staffId: 's1',
    start: new Date().toISOString(),
    end: new Date().toISOString(),
    isAvailable: true,
  })
})

describe('BookingModal', () => {
  it('books a walk-in: column staff preselected, availability hint, submit payload', async () => {
    const user = userEvent.setup()
    const onSaved = vi.fn()
    render(
      <BookingModal booking={createBooking('s1', 10 * 60)} onClose={() => {}} onSaved={onSaved} />,
    )

    await user.click(screen.getByRole('button', { name: 'Walk-in' }))
    await user.type(screen.getByPlaceholderText('Walk-in guest'), 'Guest Maya')
    await user.type(screen.getByLabelText('Phone (optional)'), '+971 50 555 0199')
    await user.click(await screen.findByRole('button', { name: /Signature Cut/ }))

    // The column that was clicked performs the service when eligible.
    const staffSelect = await screen.findByLabelText('Staff')
    await waitFor(() => expect(staffSelect).toHaveValue('s1'))
    expect(
      await screen.findByText('This staff member is available for the selected window.'),
    ).toBeInTheDocument()

    await user.selectOptions(staffSelect, 's2')
    await user.type(screen.getByLabelText('Notes'), 'Prefers quiet corner')
    await user.click(screen.getByRole('button', { name: 'Book appointment' }))

    const expectedStart = new Date()
    expectedStart.setHours(10, 0, 0, 0)
    await waitFor(() =>
      expect(createAppointment).toHaveBeenCalledWith({
        customerId: null,
        walkInCustomerName: 'Guest Maya',
        walkInCustomerPhone: '+971 50 555 0199',
        startsAt: expectedStart.toISOString(),
        services: [{ serviceId: 'svc1', staffId: 's2' }],
        notes: 'Prefers quiet corner',
      }),
    )
    await waitFor(() => expect(onSaved).toHaveBeenCalled())
  })

  it('books an existing customer found through search', async () => {
    const user = userEvent.setup()
    const onSaved = vi.fn()
    render(
      <BookingModal booking={createBooking(null, null)} onClose={() => {}} onSaved={onSaved} />,
    )

    await user.type(screen.getByLabelText('Search customers'), 'Lina')
    await user.click(screen.getByRole('button', { name: 'Search' }))
    await user.click(await screen.findByRole('button', { name: /Lina Mansour/ }))
    expect(screen.getByText(/Booking for/)).toHaveTextContent('Lina Mansour · +971 50 555 0101')

    await user.click(await screen.findByRole('button', { name: /Signature Cut/ }))
    await screen.findByLabelText('Staff')
    await user.click(screen.getByRole('button', { name: 'Book appointment' }))

    const expectedStart = new Date()
    expectedStart.setHours(9, 0, 0, 0)
    await waitFor(() =>
      expect(createAppointment).toHaveBeenCalledWith({
        customerId: 'c1',
        walkInCustomerName: null,
        walkInCustomerPhone: null,
        startsAt: expectedStart.toISOString(),
        services: [{ serviceId: 'svc1', staffId: 's1' }],
        notes: null,
      }),
    )
    await waitFor(() => expect(onSaved).toHaveBeenCalled())
  })

  it('parks a duplicate-phone conflict and re-saves with allowDuplicatePhone', async () => {
    const user = userEvent.setup()
    const onSaved = vi.fn()
    vi.mocked(createCustomer)
      .mockRejectedValueOnce(
        new ApiError(409, {
          status: 409,
          detail: 'A customer with this phone number already exists (Lina Mansour).',
        }),
      )
      .mockResolvedValueOnce(customerProfile(lina))
    render(
      <BookingModal booking={createBooking('s1', 10 * 60)} onClose={() => {}} onSaved={onSaved} />,
    )

    await user.click(screen.getByRole('button', { name: 'New customer' }))
    await user.type(screen.getByLabelText('First name'), 'Lina')
    await user.type(screen.getByLabelText('Last name'), 'Mansour')
    await user.type(screen.getByLabelText('Mobile'), '+971 50 555 0101')
    await user.click(await screen.findByRole('button', { name: /Signature Cut/ }))
    await screen.findByLabelText('Staff')
    await user.click(screen.getByRole('button', { name: 'Book appointment' }))

    const warning = await screen.findByRole('dialog', { name: 'Possible duplicate customer' })
    expect(within(warning).getByText(/already exists/)).toBeInTheDocument()
    await user.click(within(warning).getByRole('button', { name: 'Save anyway' }))

    await waitFor(() =>
      expect(createCustomer).toHaveBeenLastCalledWith(
        expect.objectContaining({ allowDuplicatePhone: true }),
      ),
    )
    await waitFor(() =>
      expect(createAppointment).toHaveBeenCalledWith(
        expect.objectContaining({ customerId: 'c1', walkInCustomerName: null }),
      ),
    )
    await waitFor(() => expect(onSaved).toHaveBeenCalled())
  })

  it('refuses to book with no customer picked and says why', async () => {
    const user = userEvent.setup()
    render(<BookingModal booking={createBooking(null, null)} onClose={() => {}} onSaved={() => {}} />)

    await user.click(screen.getByRole('button', { name: 'Book appointment' }))

    expect(
      await screen.findByText('Choose a customer, or switch to New customer / Walk-in.'),
    ).toBeInTheDocument()
    expect(createAppointment).not.toHaveBeenCalled()
  })

  it('refuses to book a service with no staff chosen for it', async () => {
    const user = userEvent.setup()
    vi.mocked(getService).mockResolvedValue(fullService('svc1', 'Signature Cut', []))
    render(
      <BookingModal booking={createBooking(null, null)} onClose={() => {}} onSaved={() => {}} />,
    )

    await user.click(screen.getByRole('button', { name: 'Walk-in' }))
    await user.click(await screen.findByRole('button', { name: /Signature Cut/ }))
    await user.click(screen.getByRole('button', { name: 'Book appointment' }))

    expect(
      await screen.findByText('Choose a staff member for Signature Cut.'),
    ).toBeInTheDocument()
    expect(createAppointment).not.toHaveBeenCalled()
  })
})
