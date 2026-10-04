import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ComponentProps } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  createCustomerNote,
  getCustomer,
  listCustomerAppointments,
  listCustomerNotes,
  setCustomerStatus,
  updateCustomerNote,
} from '../api/customers.ts'
import type {
  CustomerAppointmentItem,
  CustomerNote,
  CustomerProfile,
  CustomerSummary,
} from '../api/contracts.ts'
import { formatDate, formatDateTime } from '../format.ts'
import { CustomerDrawer } from './CustomerDrawer.tsx'

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

const dayMs = 86_400_000
const nowMs = Date.now()

const summary: CustomerSummary = {
  id: 'c1',
  firstName: 'Lina',
  lastName: 'Mansour',
  phoneNumber: '+971 50 555 0100',
  email: 'lina@debliss.example',
  isActive: true,
  createdAt: '2026-06-01T00:00:00Z',
  updatedAt: null,
}

const profile: CustomerProfile = {
  id: 'c1',
  firstName: 'Lina',
  lastName: 'Mansour',
  email: 'lina@debliss.example',
  phoneNumber: '+971 50 555 0100',
  dateOfBirth: '1990-04-12',
  gender: 'Female',
  address: 'Marina Walk 4',
  isActive: true,
  appointmentSummary: {
    totalAppointments: 3,
    completedAppointments: 2,
    cancelledAppointments: 1,
    noShowAppointments: 0,
    lastVisitAt: '2026-09-15T12:00:00Z',
  },
  createdAt: '2026-06-01T00:00:00Z',
  updatedAt: null,
}

const note: CustomerNote = {
  id: 'n1',
  customerId: 'c1',
  note: 'Allergic to henna.',
  createdAt: '2026-09-01T10:00:00Z',
  updatedAt: null,
}

// Dates relative to the run clock so the Upcoming/History split stays stable.
const pastAppointment: CustomerAppointmentItem = {
  id: 'a1',
  startsAt: new Date(nowMs - dayMs).toISOString(),
  endsAt: new Date(nowMs - dayMs + 3_600_000).toISOString(),
  status: 'Completed',
  serviceNames: ['Haircut'],
  staffNames: ['Ana Bianchi'],
}

const futureAppointment: CustomerAppointmentItem = {
  id: 'a2',
  startsAt: new Date(nowMs + dayMs).toISOString(),
  endsAt: new Date(nowMs + dayMs + 3_600_000).toISOString(),
  status: 'Confirmed',
  serviceNames: ['Facial'],
  staffNames: ['Ana Bianchi'],
}

/** Rebuilds the "9:00 AM – 10:00 AM" line the appointment cards render. */
function timeRange(appointment: CustomerAppointmentItem): string {
  const time = (iso: string) => new Date(iso).toLocaleTimeString(undefined, { timeStyle: 'short' })
  return `${time(appointment.startsAt)} – ${time(appointment.endsAt)}`
}

function renderDrawer(overrides: Partial<ComponentProps<typeof CustomerDrawer>> = {}) {
  const props = {
    customer: summary,
    onClose: vi.fn(),
    onEdit: vi.fn(),
    onBookAppointment: vi.fn(),
    onChanged: vi.fn(),
    ...overrides,
  }
  render(<CustomerDrawer {...props} />)
  return props
}

describe('CustomerDrawer', () => {
  beforeEach(() => {
    vi.mocked(getCustomer).mockResolvedValue(profile)
    vi.mocked(setCustomerStatus).mockResolvedValue(profile)
    vi.mocked(listCustomerNotes).mockResolvedValue([note])
    vi.mocked(listCustomerAppointments).mockResolvedValue([pastAppointment])
    vi.mocked(createCustomerNote).mockResolvedValue({
      id: 'n2',
      customerId: 'c1',
      note: 'Prefers evening appointments.',
      createdAt: '2026-10-03T10:00:00Z',
      updatedAt: null,
    })
    vi.mocked(updateCustomerNote).mockResolvedValue({
      ...note,
      note: 'Allergic to henna-based dyes.',
      updatedAt: '2026-10-03T11:00:00Z',
    })
  })

  it('renders the header once and opens on the Overview tab', async () => {
    renderDrawer()

    expect(screen.getByRole('dialog', { name: 'Customer Lina Mansour' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Lina Mansour' })).toBeInTheDocument()
    // Identity lives in the header only: mobile and status show up exactly once.
    expect(screen.getByText('+971 50 555 0100')).toBeInTheDocument()
    expect(screen.getAllByText('Active', { selector: 'span.pill' })).toHaveLength(1)

    const tabs = within(screen.getByRole('tablist', { name: 'Customer sections' }))
      .getAllByRole('tab')
      .map(tab => tab.textContent)
    expect(tabs).toEqual(['Overview', 'Appointments', 'Sales', 'Customer details', 'Notes'])
    expect(screen.getByRole('tab', { name: 'Overview' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('tabpanel')).toHaveAttribute(
      'aria-labelledby',
      'customer-tab-overview',
    )

    const info = await screen.findByRole('region', { name: 'Customer information' })
    expect(within(info).getByText('Customer since')).toBeInTheDocument()
    expect(within(info).getByText(formatDate(profile.createdAt))).toBeInTheDocument()
    expect(within(info).getByText('lina@debliss.example')).toBeInTheDocument()

    const summaryRegion = screen.getByRole('region', { name: 'Customer summary' })
    for (const value of ['3', '2', '1', '0']) {
      expect(within(summaryRegion).getByText(value)).toBeInTheDocument()
    }
    expect(within(summaryRegion).getByText('No-shows')).toBeInTheDocument()
    expect(
      within(summaryRegion).getByText(formatDateTime('2026-09-15T12:00:00Z')),
    ).toBeInTheDocument()
  })

  it('renders only the selected section when another tab is chosen', async () => {
    const user = userEvent.setup()
    renderDrawer()

    await screen.findByText('lina@debliss.example')
    await user.click(screen.getByRole('tab', { name: 'Appointments' }))

    expect(await screen.findByRole('region', { name: 'Upcoming appointments' })).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Past appointments' })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Appointments' })).toHaveAttribute(
      'aria-selected',
      'true',
    )
    expect(screen.getByRole('tab', { name: 'Overview' })).toHaveAttribute('aria-selected', 'false')
    expect(screen.getByRole('tabpanel')).toHaveAttribute(
      'aria-labelledby',
      'customer-tab-appointments',
    )
    // The Overview sections are gone, not just scrolled past.
    expect(screen.queryByRole('region', { name: 'Customer information' })).not.toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Customer summary' })).not.toBeInTheDocument()
  })

  it('moves between tabs with the arrow keys', async () => {
    const user = userEvent.setup()
    renderDrawer()

    const overview = screen.getByRole('tab', { name: 'Overview' })
    overview.focus()
    await user.keyboard('{ArrowRight}')

    const appointments = screen.getByRole('tab', { name: 'Appointments' })
    expect(appointments).toHaveAttribute('aria-selected', 'true')
    expect(document.activeElement).toBe(appointments)

    await user.keyboard('{ArrowRight}')
    await user.keyboard('{ArrowRight}')
    await user.keyboard('{ArrowRight}')
    expect(screen.getByRole('tab', { name: 'Notes' })).toHaveAttribute('aria-selected', 'true')

    await user.keyboard('{ArrowRight}')
    expect(screen.getByRole('tab', { name: 'Overview' })).toHaveAttribute('aria-selected', 'true')

    await user.keyboard('{ArrowLeft}')
    expect(screen.getByRole('tab', { name: 'Notes' })).toHaveAttribute('aria-selected', 'true')
  })

  it('splits the history into upcoming and past cards with their full line-up', async () => {
    vi.mocked(listCustomerAppointments).mockResolvedValue([pastAppointment, futureAppointment])
    const user = userEvent.setup()
    renderDrawer()

    await user.click(screen.getByRole('tab', { name: 'Appointments' }))

    const upcoming = await screen.findByRole('region', { name: 'Upcoming appointments' })
    expect(within(upcoming).getByText('Facial')).toBeInTheDocument()
    expect(within(upcoming).getByText('Confirmed')).toBeInTheDocument()
    expect(within(upcoming).getByText(formatDate(futureAppointment.startsAt))).toBeInTheDocument()
    expect(within(upcoming).getByText(timeRange(futureAppointment))).toBeInTheDocument()

    const past = screen.getByRole('region', { name: 'Past appointments' })
    expect(within(past).getByText('Haircut')).toBeInTheDocument()
    expect(within(past).getByText('Completed')).toBeInTheDocument()
    expect(within(past).getByText('Ana Bianchi')).toBeInTheDocument()

    expect(screen.queryByText('Nothing scheduled.')).not.toBeInTheDocument()
    expect(screen.queryByText('No past appointments yet.')).not.toBeInTheDocument()
  })

  it('keeps a quiet empty half when only one side has appointments', async () => {
    vi.mocked(listCustomerAppointments).mockResolvedValue([futureAppointment])
    const user = userEvent.setup()
    renderDrawer()

    await user.click(screen.getByRole('tab', { name: 'Appointments' }))

    const upcoming = await screen.findByRole('region', { name: 'Upcoming appointments' })
    expect(within(upcoming).getByText('Facial')).toBeInTheDocument()
    expect(await screen.findByText('No past appointments yet.')).toBeInTheDocument()
    expect(screen.queryByText('Nothing scheduled.')).not.toBeInTheDocument()
  })

  it('shows a single honest empty state when there are no appointments at all', async () => {
    vi.mocked(listCustomerAppointments).mockResolvedValue([])
    const user = userEvent.setup()
    renderDrawer()

    await user.click(screen.getByRole('tab', { name: 'Appointments' }))

    const region = await screen.findByRole('region', { name: 'Appointments' })
    expect(within(region).getByText('No appointments yet.')).toBeInTheDocument()
  })

  it('shows the Sales tab as an empty state without inventing numbers', async () => {
    const user = userEvent.setup()
    renderDrawer()

    await user.click(screen.getByRole('tab', { name: 'Sales' }))

    const sales = screen.getByRole('region', { name: 'Sales' })
    expect(within(sales).getByText('No sales yet.')).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Customer summary' })).not.toBeInTheDocument()
  })

  it('lays out the full record in the Customer details tab', async () => {
    const user = userEvent.setup()
    const props = renderDrawer()

    await user.click(screen.getByRole('tab', { name: 'Customer details' }))

    const details = await screen.findByRole('region', { name: 'Customer details' })
    for (const label of [
      'First name',
      'Last name',
      'Mobile',
      'Email',
      'Date of birth',
      'Gender',
      'Address',
      'Status',
      'Customer since',
    ]) {
      expect(within(details).getByText(label)).toBeInTheDocument()
    }
    expect(within(details).getByText('Lina')).toBeInTheDocument()
    expect(within(details).getByText('Mansour')).toBeInTheDocument()
    expect(within(details).getByText('+971 50 555 0100')).toBeInTheDocument()
    expect(within(details).getByText('lina@debliss.example')).toBeInTheDocument()
    expect(within(details).getByText(formatDate('1990-04-12'))).toBeInTheDocument()
    expect(within(details).getByText('Female')).toBeInTheDocument()
    expect(within(details).getByText('Marina Walk 4')).toBeInTheDocument()
    expect(within(details).getByText('Active', { selector: 'span.pill' })).toBeInTheDocument()

    await user.click(within(details).getByRole('button', { name: 'Edit' }))
    expect(props.onEdit).toHaveBeenCalledWith('c1')
  })

  it('opens the editor from the header Edit action', async () => {
    const user = userEvent.setup()
    const props = renderDrawer()

    await user.click(screen.getByRole('button', { name: 'Edit' }))

    expect(props.onEdit).toHaveBeenCalledWith('c1')
  })

  it('starts a booking for this customer', async () => {
    const user = userEvent.setup()
    const props = renderDrawer()

    await user.click(screen.getByRole('button', { name: 'Book appointment' }))

    expect(props.onBookAppointment).toHaveBeenCalledWith(summary)
  })

  it('deactivates through the action menu and reloads the record', async () => {
    const user = userEvent.setup()
    const props = renderDrawer()

    await screen.findByText('lina@debliss.example')
    await user.click(screen.getByRole('button', { name: 'More customer actions' }))
    await user.click(screen.getByRole('menuitem', { name: 'Deactivate' }))

    await waitFor(() => expect(setCustomerStatus).toHaveBeenCalledWith('c1', false))
    await waitFor(() => expect(getCustomer).toHaveBeenCalledTimes(2))
    expect(props.onChanged).toHaveBeenCalledTimes(1)
  })

  it('shows a failed deactivation as a banner without reloading', async () => {
    const user = userEvent.setup()
    vi.mocked(setCustomerStatus).mockRejectedValue(new Error('boom'))
    const props = renderDrawer()

    await screen.findByText('lina@debliss.example')
    await user.click(screen.getByRole('button', { name: 'More customer actions' }))
    await user.click(screen.getByRole('menuitem', { name: 'Deactivate' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('boom')
    expect(getCustomer).toHaveBeenCalledTimes(1)
    expect(props.onChanged).not.toHaveBeenCalled()
  })

  it('lists notes as cards in the Notes tab', async () => {
    const user = userEvent.setup()
    renderDrawer()

    await user.click(screen.getByRole('tab', { name: 'Notes' }))

    const notes = await screen.findByRole('region', { name: 'Customer notes' })
    expect(within(notes).getByText('Allergic to henna.')).toBeInTheDocument()
    expect(within(notes).getByText(formatDateTime(note.createdAt))).toBeInTheDocument()
    expect(within(notes).getByRole('button', { name: 'Add note' })).toBeInTheDocument()
    // Notes are their own tab; the Overview cards do not also render.
    expect(screen.queryByRole('region', { name: 'Customer summary' })).not.toBeInTheDocument()
  })

  it('loads appointments and notes on demand, not with the drawer', async () => {
    const user = userEvent.setup()
    renderDrawer()

    await screen.findByText('lina@debliss.example')
    expect(listCustomerAppointments).not.toHaveBeenCalled()
    expect(listCustomerNotes).not.toHaveBeenCalled()

    await user.click(screen.getByRole('tab', { name: 'Appointments' }))
    await waitFor(() => expect(listCustomerAppointments).toHaveBeenCalledTimes(1))
    expect(listCustomerNotes).not.toHaveBeenCalled()

    await user.click(screen.getByRole('tab', { name: 'Notes' }))
    await waitFor(() => expect(listCustomerNotes).toHaveBeenCalledTimes(1))
    expect(listCustomerAppointments).toHaveBeenCalledTimes(1)
  })

  it('adds a note through the form and reloads the list', async () => {
    const user = userEvent.setup()
    renderDrawer()

    await user.click(screen.getByRole('tab', { name: 'Notes' }))
    await screen.findByText('Allergic to henna.')
    await user.click(screen.getByRole('button', { name: 'Add note' }))
    await user.type(screen.getByLabelText('Add a note'), 'Prefers evening appointments.')
    await user.click(screen.getByRole('button', { name: 'Add note' }))

    await waitFor(() =>
      expect(createCustomerNote).toHaveBeenCalledWith('c1', {
        note: 'Prefers evening appointments.',
      }),
    )
    await waitFor(() => expect(listCustomerNotes).toHaveBeenCalledTimes(2))
  })

  it('edits a note in place and reloads the list', async () => {
    const user = userEvent.setup()
    renderDrawer()

    await user.click(screen.getByRole('tab', { name: 'Notes' }))
    await screen.findByText('Allergic to henna.')
    const notes = screen.getByRole('region', { name: 'Customer notes' })
    await user.click(within(notes).getByRole('button', { name: 'Edit' }))

    const box = screen.getByLabelText('Edit note')
    await user.clear(box)
    await user.type(box, 'Allergic to henna-based dyes.')
    await user.click(screen.getByRole('button', { name: 'Save note' }))

    await waitFor(() =>
      expect(updateCustomerNote).toHaveBeenCalledWith('c1', 'n1', {
        note: 'Allergic to henna-based dyes.',
      }),
    )
    await waitFor(() => expect(listCustomerNotes).toHaveBeenCalledTimes(2))
  })

  it('shows the profile failure in every profile-backed tab with one retry', async () => {
    vi.mocked(getCustomer).mockRejectedValueOnce(new Error('boom'))
    const user = userEvent.setup()
    renderDrawer()

    expect(await screen.findByRole('alert')).toHaveTextContent('boom')
    expect(screen.getByRole('heading', { name: 'Lina Mansour' })).toBeInTheDocument()

    // The same profile state backs the details tab.
    await user.click(screen.getByRole('tab', { name: 'Customer details' }))
    expect(screen.getByRole('alert')).toHaveTextContent('boom')

    await user.click(screen.getByRole('button', { name: 'Try again' }))

    expect(await screen.findByRole('region', { name: 'Customer details' })).toBeInTheDocument()
    expect(getCustomer).toHaveBeenCalledTimes(2)
  })

  it('keeps the header up while the profile is still loading', () => {
    vi.mocked(getCustomer).mockReturnValue(new Promise(() => {}))

    renderDrawer()

    expect(screen.getByRole('heading', { name: 'Lina Mansour' })).toBeInTheDocument()
    expect(screen.getByText('+971 50 555 0100')).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('Loading profile…')
  })

  it('closes from the close button and from Escape', async () => {
    const user = userEvent.setup()
    const props = renderDrawer()

    await user.click(screen.getByRole('button', { name: 'Close' }))
    expect(props.onClose).toHaveBeenCalledTimes(1)

    await user.keyboard('{Escape}')
    expect(props.onClose).toHaveBeenCalledTimes(2)
  })
})
