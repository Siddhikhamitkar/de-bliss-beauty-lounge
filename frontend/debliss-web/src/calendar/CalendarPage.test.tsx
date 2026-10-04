import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/client.ts'
import type { Appointment, CalendarData, CalendarStaff } from '../api/contracts.ts'
import { getService, searchServices } from '../api/catalogue.ts'
import { createCustomer, searchCustomers } from '../api/customers.ts'
import {
  createAppointment,
  getAppointment,
  getCalendar,
  setAppointmentStatus,
  updateAppointment,
  updateAppointmentSchedule,
} from '../api/scheduling.ts'
import { checkStaffAvailability } from '../api/staff.ts'
import { dayKeyOf, setMinutesOfDay } from './calendarTime.ts'
import { CalendarPage } from './CalendarPage.tsx'

vi.mock('../api/scheduling.ts', () => ({
  getCalendar: vi.fn(),
  getAppointment: vi.fn(),
  createAppointment: vi.fn(),
  updateAppointment: vi.fn(),
  updateAppointmentSchedule: vi.fn(),
  setAppointmentStatus: vi.fn(),
}))

vi.mock('../api/catalogue.ts', () => ({
  searchServices: vi.fn(),
  getService: vi.fn(),
}))

vi.mock('../api/customers.ts', () => ({
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

vi.mock('../api/staff.ts', () => ({
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

const everyDay = (start: string, end: string): CalendarStaff['workingHours']['days'] => [
  { dayOfWeek: 'Sunday', isWorkingDay: true, intervals: [{ start, end }] },
  { dayOfWeek: 'Monday', isWorkingDay: true, intervals: [{ start, end }] },
  { dayOfWeek: 'Tuesday', isWorkingDay: true, intervals: [{ start, end }] },
  { dayOfWeek: 'Wednesday', isWorkingDay: true, intervals: [{ start, end }] },
  { dayOfWeek: 'Thursday', isWorkingDay: true, intervals: [{ start, end }] },
  { dayOfWeek: 'Friday', isWorkingDay: true, intervals: [{ start, end }] },
  { dayOfWeek: 'Saturday', isWorkingDay: true, intervals: [{ start, end }] },
]

function member(id: string, firstName: string, start: string, end: string): CalendarStaff {
  return {
    id,
    firstName,
    lastName: 'Test',
    jobTitle: 'Stylist',
    calendarColor: '#8a4f56',
    sortOrder: 0,
    workingHours: { days: everyDay(start, end) },
    isActive: true,
  }
}

function todayAt(hours: number, minutes: number): Date {
  const day = new Date()
  day.setHours(hours, minutes, 0, 0)
  return day
}

function booking(
  id: string,
  customerName: string,
  start: Date,
  durationMinutes: number,
  status: string,
  staffId: string,
  serviceName: string,
): Appointment {
  return {
    id,
    customerId: 'c1',
    customerName,
    customerPhone: '+971 50 555 0100',
    startsAt: start.toISOString(),
    endsAt: new Date(start.getTime() + durationMinutes * 60_000).toISOString(),
    status,
    source: 'Phone',
    notes: null,
    cancellationReason: null,
    services: [
      {
        serviceId: 'svc1',
        serviceName,
        durationMinutes,
        unitPrice: 120,
        staffId,
        staffFirstName: staffId === 's1' ? 'Ava' : 'Noah',
        staffLastName: 'Test',
        staffColor: '#8a4f56',
      },
    ],
    totalPrice: 120,
    totalDurationMinutes: durationMinutes,
    createdAt: start.toISOString(),
    updatedAt: null,
  }
}

const calendarData: CalendarData = {
  from: new Date().toISOString(),
  to: new Date().toISOString(),
  staff: [member('s1', 'Ava', '09:00', '18:00'), member('s2', 'Noah', '10:00', '15:00')],
  appointments: [
    booking('a1', 'Lina Mansour', todayAt(10, 0), 60, 'Pending', 's1', 'Signature Cut'),
    booking('a2', 'Omar Farooq', todayAt(14, 0), 30, 'Completed', 's2', 'Beard Trim'),
  ],
  markers: [
    {
      id: 'm1',
      staffId: null,
      startsAt: todayAt(12, 0).toISOString(),
      endsAt: todayAt(13, 0).toISOString(),
      kind: 'Blocked',
      type: 'StaffMeeting',
      label: 'Staff meeting',
    },
  ],
}

/** The staff names rendered as grid column heads (the toolbar also lists them). */
function columnNames(): string[] {
  return [...document.querySelectorAll('.cal-staff-name')].map(
    element => element.textContent ?? '',
  )
}

beforeAll(() => {
  // jsdom has no pointer-capture implementation; the grid only needs the calls
  // to exist so drag bookkeeping can run.
  Element.prototype.setPointerCapture = vi.fn()
  Element.prototype.releasePointerCapture = vi.fn()
  Element.prototype.hasPointerCapture = vi.fn(() => true)
})

beforeEach(() => {
  vi.mocked(getCalendar).mockResolvedValue(calendarData)
  vi.mocked(getAppointment).mockResolvedValue(calendarData.appointments[0])
  vi.mocked(searchServices).mockResolvedValue({
    items: [],
    totalCount: 0,
    page: 1,
    pageSize: 100,
    totalPages: 0,
    hasPreviousPage: false,
    hasNextPage: false,
  })
  vi.mocked(getService).mockRejectedValue(new Error('not used'))
  vi.mocked(searchCustomers).mockResolvedValue({
    items: [],
    totalCount: 0,
    page: 1,
    pageSize: 8,
    totalPages: 0,
    hasPreviousPage: false,
    hasNextPage: false,
  })
  vi.mocked(createCustomer).mockRejectedValue(new Error('not used'))
  vi.mocked(setAppointmentStatus).mockResolvedValue(calendarData.appointments[0])
  vi.mocked(updateAppointmentSchedule).mockResolvedValue(calendarData.appointments[0])
  vi.mocked(createAppointment).mockResolvedValue(calendarData.appointments[0])
  vi.mocked(updateAppointment).mockResolvedValue(calendarData.appointments[0])
  vi.mocked(checkStaffAvailability).mockResolvedValue({
    staffId: 's1',
    start: todayAt(10, 0).toISOString(),
    end: todayAt(11, 0).toISOString(),
    isAvailable: true,
  })
})

describe('CalendarPage', () => {
  it('renders one column per staff member per day with proportional event heights', async () => {
    render(<CalendarPage />)

    await screen.findByRole('button', { name: /Lina Mansour/ })

    // Seven days × two members, day-major, with the working window under each name.
    expect(columnNames().filter(name => name === 'Ava Test')).toHaveLength(7)
    expect(columnNames().filter(name => name === 'Noah Test')).toHaveLength(7)
    expect(screen.getAllByText('Working 09:00–18:00').length).toBeGreaterThan(0)
    expect(screen.getAllByText('Working 10:00–15:00').length).toBeGreaterThan(0)

    // 10:00 → top 720px; 60 minutes → 72px at 1.2 px/min. Height follows duration.
    const lina = screen.getByRole('button', { name: /Lina Mansour/ })
    expect(lina).toHaveStyle({ top: '720px', height: '72px' })
    const omar = screen.getByRole('button', { name: /Omar Farooq/ })
    expect(omar).toHaveStyle({ top: '1008px', height: '36px' })

    // The salon-wide blocked period repeats on every column of its day.
    expect(screen.getAllByText('Staff meeting')).toHaveLength(2)
  })

  it('narrows the grid with the status, staff and search filters', async () => {
    const user = userEvent.setup()
    render(<CalendarPage />)

    await screen.findByRole('button', { name: /Lina Mansour/ })

    await user.selectOptions(screen.getByLabelText('Status'), 'Completed')
    expect(screen.queryByRole('button', { name: /Lina Mansour/ })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Omar Farooq/ })).toBeInTheDocument()

    await user.selectOptions(screen.getByLabelText('Status'), 'All')
    await user.selectOptions(screen.getByLabelText('Staff'), 'Noah Test')
    expect(columnNames().filter(name => name === 'Noah Test')).toHaveLength(7)
    expect(columnNames().filter(name => name === 'Ava Test')).toHaveLength(0)

    await user.selectOptions(screen.getByLabelText('Staff'), 'all')
    await user.type(screen.getByLabelText('Search'), 'omar{Enter}')
    expect(screen.queryByRole('button', { name: /Lina Mansour/ })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Omar Farooq/ })).toBeInTheDocument()
  })

  it('switches to the month overview and drops into a day from there', async () => {
    const user = userEvent.setup()
    render(<CalendarPage />)

    await screen.findByRole('button', { name: /Lina Mansour/ })
    await user.click(screen.getByRole('button', { name: 'Month' }))

    const month = await screen.findByRole('grid', { name: 'Month overview' })
    expect(within(month).getAllByRole('columnheader')).toHaveLength(7)
    expect(within(month).getAllByRole('gridcell')).toHaveLength(42)
    expect(within(month).getByRole('button', { name: /Lina Mansour/ })).toBeInTheDocument()

    // Click the day number inside the cell that carries Lina's chip; the same
    // date number also appears on adjacent-month padding cells.
    const chip = within(month).getByRole('button', { name: /Lina Mansour/ })
    const cell = chip.closest('[role="gridcell"]')
    expect(cell).not.toBeNull()
    await user.click(
      within(cell as HTMLElement).getByRole('button', { name: String(new Date().getDate()) }),
    )

    // Day view keeps only the clicked day: one column per staff member.
    expect(columnNames().filter(name => name === 'Ava Test')).toHaveLength(1)
    expect(columnNames().filter(name => name === 'Noah Test')).toHaveLength(1)
    expect(screen.queryByRole('grid', { name: 'Month overview' })).not.toBeInTheDocument()
  })

  it('opens the details panel from an event and moves the booking along its pipeline', async () => {
    const user = userEvent.setup()
    render(<CalendarPage />)

    await user.click(await screen.findByRole('button', { name: /Lina Mansour/ }))

    const panel = await screen.findByRole('dialog', { name: 'Appointment details' })
    expect(within(panel).getByRole('heading', { name: 'Lina Mansour' })).toBeInTheDocument()
    expect(within(panel).getByText('+971 50 555 0100')).toBeInTheDocument()

    await user.click(within(panel).getByRole('button', { name: 'Confirm' }))

    await waitFor(() =>
      expect(setAppointmentStatus).toHaveBeenCalledWith('a1', {
        status: 'Confirmed',
        reason: null,
      }),
    )
    // The panel asks the calendar to refresh after the action lands.
    await waitFor(() => expect(getCalendar).toHaveBeenCalledTimes(2))
  })

  it('opens the booking dialog from the toolbar without leaving the page', async () => {
    const user = userEvent.setup()
    render(<CalendarPage />)

    await screen.findByRole('button', { name: /Lina Mansour/ })
    await user.click(screen.getByRole('button', { name: '+ New Appointment' }))

    const dialog = await screen.findByRole('dialog', { name: 'New appointment' })
    expect(within(dialog).getByText('Existing customer')).toBeInTheDocument()

    await user.click(within(dialog).getByRole('button', { name: 'Close booking dialog' }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('reverts a rejected drag in place and surfaces the backend ruling', async () => {
    const user = userEvent.setup()
    vi.mocked(updateAppointmentSchedule).mockRejectedValue(
      new ApiError(409, { status: 409, detail: 'That time is no longer available.' }),
    )
    render(<CalendarPage />)

    const eventButton = await screen.findByRole('button', { name: /Lina Mansour/ })
    const todayKey = dayKeyOf(new Date())
    const column = document.querySelector(`.cal-column[data-column-key="${todayKey}|s1"]`)
    expect(column).not.toBeNull()
    document.elementFromPoint = vi.fn().mockReturnValue(column)

    await user.pointer([
      { keys: '[MouseLeft>]', target: eventButton, coords: { clientX: 120, clientY: 120 } },
      { coords: { clientX: 120, clientY: 240 } },
      { keys: '[/MouseLeft]' },
    ])

    // Grab at 120px, drop at 240px: +100 min on the 5-minute grid, measured
    // from the booking's own start — the grip point is preserved, so the
    // column's on-screen geometry never leaks into the proposed time.
    const expectedStart = setMinutesOfDay(new Date(), 700).toISOString()
    await waitFor(() =>
      expect(updateAppointmentSchedule).toHaveBeenCalledWith('a1', { startsAt: expectedStart }),
    )

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'That time is no longer available.',
    )
    // Revert = the grid was never mutated locally and no refresh was asked for.
    expect(eventButton).toHaveStyle({ top: '720px' })
    expect(getCalendar).toHaveBeenCalledTimes(1)
    // The click that follows the pointer-up must not open the details panel.
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('resizes from the bottom handle without the move handler stealing the drag', async () => {
    const user = userEvent.setup()
    render(<CalendarPage />)

    const eventButton = await screen.findByRole('button', { name: /Lina Mansour/ })
    const handle = eventButton.querySelector('.cal-event-resize')
    expect(handle).not.toBeNull()

    await user.pointer([
      { keys: '[MouseLeft>]', target: handle as Element, coords: { clientX: 120, clientY: 120 } },
      { coords: { clientX: 120, clientY: 144 } },
      { keys: '[/MouseLeft]' },
    ])

    // +24px = +20 min on the 60-minute booking: a duration-only patch, the
    // start and the staff stay untouched.
    await waitFor(() =>
      expect(updateAppointmentSchedule).toHaveBeenCalledWith('a1', { durationMinutes: 80 }),
    )
    expect(getCalendar).toHaveBeenCalledTimes(2)
  })

  it('lifts the booking into a following card a few pixels into the drag', async () => {
    const user = userEvent.setup()
    render(<CalendarPage />)

    const eventButton = await screen.findByRole('button', { name: /Lina Mansour/ })
    const todayKey = dayKeyOf(new Date())
    const column = document.querySelector(`.cal-column[data-column-key="${todayKey}|s1"]`)
    expect(column).not.toBeNull()
    document.elementFromPoint = vi.fn().mockReturnValue(column)

    await user.pointer([
      { keys: '[MouseLeft>]', target: eventButton, coords: { clientX: 120, clientY: 120 } },
      { coords: { clientX: 120, clientY: 126 } },
    ])

    // 6px is past the 4px activation threshold: no hold delay, the preview
    // already follows the pointer, shaped and coloured like the booking card.
    const preview = document.querySelector('.cal-drag-card')
    expect(preview).not.toBeNull()
    expect(preview).toHaveClass('status-Pending')
    expect(preview).toHaveTextContent('Lina Mansour')
    expect(preview).toHaveTextContent('10:05')

    await user.pointer([{ keys: '[/MouseLeft]' }])

    await waitFor(() =>
      expect(updateAppointmentSchedule).toHaveBeenCalledWith('a1', {
        startsAt: setMinutesOfDay(new Date(), 605).toISOString(),
      }),
    )
  })
})
