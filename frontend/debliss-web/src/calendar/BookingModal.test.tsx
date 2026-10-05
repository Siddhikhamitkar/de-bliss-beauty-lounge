import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/client.ts'
import type {
  Appointment,
  AppointmentServiceLine,
  Bundle,
  CustomerProfile,
  CustomerSummary,
  EligibleStaff,
  PagedResult,
  Service,
  ServiceSummary,
} from '../api/contracts.ts'
import { getService, listBookableBundles, searchIndividualServices } from '../api/catalogue.ts'
import { createCustomer, searchCustomers } from '../api/customers.ts'
import { createAppointment, updateAppointment } from '../api/scheduling.ts'
import { checkStaffAvailability } from '../api/staff.ts'
import { BookingModal } from './BookingModal.tsx'
import type { BookingState } from './CalendarPage.tsx'

vi.mock('../api/catalogue.ts', () => ({
  searchIndividualServices: vi.fn(),
  listBookableBundles: vi.fn(),
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
    itemKind: 'Service',
    serviceCount: null,
    standardTotalPrice: null,
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
    whatsAppOptInStatus: 'Unknown',
    whatsAppOptInDate: null,
    whatsAppOptOutDate: null,
    whatsAppOptInSource: null,
    appointmentSummary: {
      totalAppointments: 0,
      completedAppointments: 0,
      cancelledAppointments: 0,
      noShowAppointments: 0,
      lastVisitAt: null,
    },
  }
}

const menu = [menuService('svc1', 'Signature Cut', 60)]
const avaThenNoah = [eligible('s1', 'Ava'), eligible('s2', 'Noah')]
const lina = customer('c1', 'Lina', 'Mansour', '+971 50 555 0101')

/**
 * The colour goes on first even though the cut is the headline service, so the
 * delivery order (displayOrder) is not the order the components come back in.
 */
const makeover: Bundle = {
  id: 'b1',
  name: 'Makeover',
  description: 'Colour, then the cut.',
  categoryId: 'cat1',
  categoryName: 'Hair',
  isCategoryActive: true,
  durationMinutes: 105,
  price: 100,
  standardTotalPrice: 120,
  savingsAmount: 20,
  isActive: true,
  isBookable: true,
  sortOrder: 3,
  items: [
    { serviceId: 'svc1', name: 'Signature Cut', durationMinutes: 45, price: 40, displayOrder: 1, isActive: true },
    { serviceId: 'svc2', name: 'Gloss Colour', durationMinutes: 60, price: 80, displayOrder: 0, isActive: true },
  ],
  createdAt: '2026-09-02T00:00:00Z',
  updatedAt: null,
}

function appointmentLine(
  serviceId: string,
  serviceName: string,
  durationMinutes: number,
  unitPrice: number,
): AppointmentServiceLine {
  return {
    serviceId,
    serviceName,
    durationMinutes,
    unitPrice,
    staffId: 's1',
    staffFirstName: 'Ava',
    staffLastName: 'Test',
    staffColor: null,
  }
}

/** A booked bundle as the calendar hands it back to the edit dialog. */
function bookedBundle(): Appointment {
  const start = new Date()
  start.setHours(11, 0, 0, 0)
  return {
    ...savedAppointment(),
    startsAt: start.toISOString(),
    endsAt: new Date(start.getTime() + 105 * 60_000).toISOString(),
    status: 'Confirmed',
    services: [
      appointmentLine('svc2', 'Gloss Colour', 60, 80),
      appointmentLine('svc1', 'Signature Cut', 45, 40),
    ],
    totalPrice: 120,
    totalDurationMinutes: 105,
    serviceBundleId: 'b1',
    bundleName: 'Makeover',
    bundlePrice: 100,
  }
}

/** Both of the bundle's components are bookable by the same two stylists. */
function stubBundleComponents(): void {
  vi.mocked(getService).mockImplementation(async serviceId =>
    fullService(serviceId, serviceId === 'svc2' ? 'Gloss Colour' : 'Signature Cut', avaThenNoah),
  )
}

/** The chosen service lines, top to bottom — the order they will be performed in. */
function bookingLines(): HTMLElement[] {
  return screen
    .queryAllByRole('listitem')
    .filter((node): node is HTMLElement => node.classList.contains('booking-line'))
}

function lineNames(): string[] {
  return bookingLines().map(node => node.querySelector('.booking-service-name')?.textContent ?? '')
}

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
  vi.mocked(searchIndividualServices).mockResolvedValue(page(menu))
  vi.mocked(listBookableBundles).mockResolvedValue([])
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
        serviceBundleId: null,
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
        serviceBundleId: null,
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

  it('books a bundle as one appointment whose lines follow the delivery order', async () => {
    const user = userEvent.setup()
    const onSaved = vi.fn()
    vi.mocked(listBookableBundles).mockResolvedValue([makeover])
    stubBundleComponents()
    render(
      <BookingModal booking={createBooking(null, null)} onClose={() => {}} onSaved={onSaved} />,
    )

    await user.click(screen.getByRole('button', { name: 'Walk-in' }))
    await user.type(screen.getByPlaceholderText('Walk-in guest'), 'Guest Maya')

    const bundleButton = await screen.findByRole('button', { name: /Makeover/ })
    expect(bundleButton).toHaveTextContent('2 services · 1 hr 45 min · 100.00 · save 20.00')
    await user.click(bundleButton)

    // The colour comes first, the cut second — the bundle's own sequence.
    await waitFor(() => expect(lineNames()).toEqual(['Gloss Colour', 'Signature Cut']))
    // A bundle books as a whole: no individual picker, no per-line removal.
    expect(screen.queryByLabelText('Search services')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Remove bundle' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Remove Gloss Colour' })).not.toBeInTheDocument()

    const staffSelects = await screen.findAllByLabelText('Staff')
    expect(staffSelects).toHaveLength(2)
    await waitFor(() => expect(staffSelects[0]).toHaveValue('s1'))
    expect(staffSelects[1]).toHaveValue('s1')

    const total = await screen.findByText(/Bundle price/)
    expect(total).toHaveTextContent('Bundle price 100.00 · 1 hr 45 min')
    expect(total).toHaveTextContent('saves 20.00')

    await user.click(screen.getByRole('button', { name: 'Book appointment' }))

    const expectedStart = new Date()
    expectedStart.setHours(9, 0, 0, 0)
    await waitFor(() =>
      expect(createAppointment).toHaveBeenCalledWith({
        customerId: null,
        walkInCustomerName: 'Guest Maya',
        walkInCustomerPhone: null,
        startsAt: expectedStart.toISOString(),
        services: [
          { serviceId: 'svc2', staffId: 's1' },
          { serviceId: 'svc1', staffId: 's1' },
        ],
        serviceBundleId: 'b1',
        notes: null,
      }),
    )
    await waitFor(() => expect(onSaved).toHaveBeenCalled())
  })

  it('drops the bundle again and books a single service with no bundle id', async () => {
    const user = userEvent.setup()
    vi.mocked(listBookableBundles).mockResolvedValue([makeover])
    stubBundleComponents()
    render(
      <BookingModal booking={createBooking(null, null)} onClose={() => {}} onSaved={() => {}} />,
    )

    await user.click(screen.getByRole('button', { name: 'Walk-in' }))
    await user.type(screen.getByPlaceholderText('Walk-in guest'), 'Guest Maya')
    await user.click(await screen.findByRole('button', { name: /Makeover/ }))
    await waitFor(() => expect(lineNames()).toEqual(['Gloss Colour', 'Signature Cut']))

    await user.click(screen.getByRole('button', { name: 'Remove bundle' }))

    // The individual menu comes back with it, and nothing remembers the bundle.
    expect(screen.queryByText('Remove bundle')).not.toBeInTheDocument()
    expect(screen.getByLabelText('Search services')).toBeInTheDocument()
    expect(lineNames()).toEqual([])

    await user.click(await screen.findByRole('button', { name: /Signature Cut/ }))
    const staffSelect = await screen.findByLabelText('Staff')
    await waitFor(() => expect(staffSelect).toHaveValue('s1'))
    await user.click(screen.getByRole('button', { name: 'Book appointment' }))

    await waitFor(() =>
      expect(createAppointment).toHaveBeenCalledWith(
        expect.objectContaining({
          services: [{ serviceId: 'svc1', staffId: 's1' }],
          serviceBundleId: null,
        }),
      ),
    )
  })

  it('shows the server objection to a bundle booking beside the fields', async () => {
    const user = userEvent.setup()
    const onSaved = vi.fn()
    vi.mocked(listBookableBundles).mockResolvedValue([makeover])
    stubBundleComponents()
    vi.mocked(createAppointment).mockRejectedValue(
      new ApiError(400, {
        status: 400,
        errors: {
          serviceBundleId: ["'Makeover' is no longer bookable."],
          services: [
            "Bundle 'Makeover' contains: Gloss Colour, Signature Cut. Submit exactly those services.",
          ],
        },
      }),
    )
    render(
      <BookingModal booking={createBooking(null, null)} onClose={() => {}} onSaved={onSaved} />,
    )

    await user.click(screen.getByRole('button', { name: 'Walk-in' }))
    await user.click(await screen.findByRole('button', { name: /Makeover/ }))
    await waitFor(() => expect(screen.getAllByLabelText('Staff')).toHaveLength(2))
    await user.click(screen.getByRole('button', { name: 'Book appointment' }))

    expect(await screen.findByText("'Makeover' is no longer bookable.")).toBeInTheDocument()
    expect(
      await screen.findByText(
        "Bundle 'Makeover' contains: Gloss Colour, Signature Cut. Submit exactly those services.",
      ),
    ).toBeInTheDocument()
    expect(onSaved).not.toHaveBeenCalled()
  })

  it('keeps a booked bundle editable after it stops being offered', async () => {
    const user = userEvent.setup()
    const onSaved = vi.fn()
    stubBundleComponents()
    // The bundle has gone unbookable since, so the picker no longer lists it.
    render(
      <BookingModal
        booking={{ kind: 'edit', appointment: bookedBundle() }}
        onClose={() => {}}
        onSaved={onSaved}
      />,
    )

    // The header still tells the truth from the appointment's own snapshots.
    expect(await screen.findByText('Makeover')).toBeInTheDocument()
    expect(screen.getByText('2 services · 1 hr 45 min · 100.00')).toBeInTheDocument()
    await waitFor(() => expect(lineNames()).toEqual(['Gloss Colour', 'Signature Cut']))
    await waitFor(() => expect(screen.getAllByLabelText('Staff')).toHaveLength(2))

    await user.click(screen.getByRole('button', { name: 'Save appointment' }))

    const expectedStart = new Date()
    expectedStart.setHours(11, 0, 0, 0)
    await waitFor(() =>
      expect(updateAppointment).toHaveBeenCalledWith('a9', {
        customerId: 'c1',
        walkInCustomerName: null,
        walkInCustomerPhone: null,
        startsAt: expectedStart.toISOString(),
        services: [
          { serviceId: 'svc2', staffId: 's1' },
          { serviceId: 'svc1', staffId: 's1' },
        ],
        serviceBundleId: 'b1',
        notes: null,
      }),
    )
    await waitFor(() => expect(onSaved).toHaveBeenCalled())
  })
})
