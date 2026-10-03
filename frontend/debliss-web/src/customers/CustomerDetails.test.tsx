import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
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
} from '../api/contracts.ts'
import { CustomerDetails } from './CustomerDetails.tsx'

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

const appointment: CustomerAppointmentItem = {
  id: 'a1',
  startsAt: '2026-09-15T12:00:00Z',
  endsAt: '2026-09-15T13:00:00Z',
  status: 'Completed',
  serviceNames: ['Haircut'],
  staffNames: ['Ana Bianchi'],
}

describe('CustomerDetails', () => {
  beforeEach(() => {
    vi.mocked(getCustomer).mockResolvedValue(profile)
    vi.mocked(setCustomerStatus).mockResolvedValue(profile)
    vi.mocked(listCustomerNotes).mockResolvedValue([note])
    vi.mocked(listCustomerAppointments).mockResolvedValue([appointment])
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

  it('renders the contact details, summary, notes, and appointment history', async () => {
    render(<CustomerDetails customerId="c1" onBack={() => {}} onEdit={() => {}} />)

    expect(await screen.findByRole('heading', { name: 'Lina Mansour' })).toBeInTheDocument()
    expect(screen.getByText('+971 50 555 0100')).toBeInTheDocument()
    expect(screen.getByText('lina@debliss.example')).toBeInTheDocument()
    expect(screen.getByText('Female')).toBeInTheDocument()
    expect(screen.getByText('Marina Walk 4')).toBeInTheDocument()
    expect(screen.getByText('Active', { selector: 'span.pill' })).toBeInTheDocument()

    // Customer Summary section, straight from the appointment counts.
    expect(screen.getByText('Total appointments')).toBeInTheDocument()
    expect(screen.getByText('3')).toBeInTheDocument()
    expect(screen.getByText('2')).toBeInTheDocument()
    expect(screen.getByText('1')).toBeInTheDocument()

    // Notes and history sections load through their own APIs.
    expect(await screen.findByText('Allergic to henna.')).toBeInTheDocument()
    expect(await screen.findByText('Haircut')).toBeInTheDocument()
    expect(screen.getByText('Ana Bianchi')).toBeInTheDocument()
    // "Completed" appears in both the summary grid and the history row.
    expect(screen.getAllByText('Completed')).toHaveLength(2)
  })

  it('shows the empty states when the customer has no notes or appointments', async () => {
    vi.mocked(listCustomerNotes).mockResolvedValue([])
    vi.mocked(listCustomerAppointments).mockResolvedValue([])

    render(<CustomerDetails customerId="c1" onBack={() => {}} onEdit={() => {}} />)

    expect(await screen.findByText('No notes yet.')).toBeInTheDocument()
    expect(await screen.findByText('No appointments yet.')).toBeInTheDocument()
  })

  it('shows the load failure and offers a way back', async () => {
    vi.mocked(getCustomer).mockRejectedValue(new Error('boom'))
    const onBack = vi.fn()
    const user = userEvent.setup()
    render(<CustomerDetails customerId="c1" onBack={onBack} onEdit={() => {}} />)

    expect(await screen.findByRole('alert')).toHaveTextContent('boom')

    await user.click(screen.getByRole('button', { name: 'Back to customers' }))
    expect(onBack).toHaveBeenCalledOnce()
  })

  it('deactivates the customer through the API and reloads', async () => {
    const user = userEvent.setup()
    render(<CustomerDetails customerId="c1" onBack={() => {}} onEdit={() => {}} />)

    await screen.findByRole('heading', { name: 'Lina Mansour' })
    await user.click(screen.getByRole('button', { name: 'Deactivate' }))

    await waitFor(() => expect(setCustomerStatus).toHaveBeenCalledWith('c1', false))
    await waitFor(() => expect(getCustomer).toHaveBeenCalledTimes(2))
  })

  it('adds a note through the form and reloads the list', async () => {
    const user = userEvent.setup()
    render(<CustomerDetails customerId="c1" onBack={() => {}} onEdit={() => {}} />)

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
    render(<CustomerDetails customerId="c1" onBack={() => {}} onEdit={() => {}} />)

    await screen.findByText('Allergic to henna.')
    await user.click(screen.getByRole('button', { name: 'Edit' }))

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

  it('opens the editor for the customer', async () => {
    const user = userEvent.setup()
    const onEdit = vi.fn()
    render(<CustomerDetails customerId="c1" onBack={() => {}} onEdit={onEdit} />)

    await screen.findByRole('heading', { name: 'Lina Mansour' })
    await user.click(screen.getByRole('button', { name: 'Edit customer' }))

    expect(onEdit).toHaveBeenCalledWith('c1')
  })
})
