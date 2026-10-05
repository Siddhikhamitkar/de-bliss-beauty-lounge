import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { setCustomerWhatsAppOptIn } from '../api/customers.ts'
import type { CustomerProfile } from '../api/contracts.ts'
import { CustomerWhatsAppOptIn } from './CustomerWhatsAppOptIn.tsx'

vi.mock('../api/customers.ts', () => ({
  searchCustomers: vi.fn(),
  getCustomer: vi.fn(),
  createCustomer: vi.fn(),
  updateCustomer: vi.fn(),
  setCustomerStatus: vi.fn(),
  setCustomerWhatsAppOptIn: vi.fn(),
  listCustomerNotes: vi.fn(),
  createCustomerNote: vi.fn(),
  updateCustomerNote: vi.fn(),
  listCustomerAppointments: vi.fn(),
}))

function makeProfile(overrides: Partial<CustomerProfile> = {}): CustomerProfile {
  return {
    id: 'c1',
    firstName: 'Lina',
    lastName: 'Mansour',
    email: null,
    phoneNumber: '+971 50 555 0100',
    dateOfBirth: null,
    gender: null,
    address: null,
    isActive: true,
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
    createdAt: '2026-10-03T00:00:00Z',
    updatedAt: null,
    ...overrides,
  }
}

function renderCard(overrides: Partial<CustomerProfile> = {}) {
  const onChanged = vi.fn()
  render(<CustomerWhatsAppOptIn profile={makeProfile(overrides)} onChanged={onChanged} />)
  return onChanged
}

describe('CustomerWhatsAppOptIn', () => {
  beforeEach(() => {
    vi.mocked(setCustomerWhatsAppOptIn).mockResolvedValue(makeProfile())
  })

  it('opens with nothing assumed and offers only the verbs that add a record', () => {
    renderCard()

    const card = screen.getByRole('region', { name: 'WhatsApp consent' })
    expect(within(card).getByText('Not recorded')).toBeInTheDocument()
    expect(within(card).getByRole('button', { name: 'Record opt-in' })).toBeInTheDocument()
    expect(within(card).getByRole('button', { name: 'Record opt-out' })).toBeInTheDocument()
    // Nothing to clear while there is no record.
    expect(within(card).queryByRole('button', { name: 'Clear record' })).not.toBeInTheDocument()
  })

  it('shows a recorded opt-in with its date and source, and no duplicate verb', () => {
    renderCard({
      whatsAppOptInStatus: 'OptedIn',
      whatsAppOptInDate: '2026-10-03T10:00:00Z',
      whatsAppOptInSource: 'Reception',
    })

    const card = screen.getByRole('region', { name: 'WhatsApp consent' })
    expect(card).toHaveTextContent(/Opted in on .+ · recorded at Reception/)
    expect(within(card).queryByRole('button', { name: 'Record opt-in' })).not.toBeInTheDocument()
    expect(within(card).getByRole('button', { name: 'Record opt-out' })).toBeInTheDocument()
    expect(within(card).getByRole('button', { name: 'Clear record' })).toBeInTheDocument()
  })

  it('shows a recorded opt-out with its date and no duplicate verb', () => {
    renderCard({ whatsAppOptInStatus: 'OptedOut', whatsAppOptOutDate: '2026-10-03T11:00:00Z' })

    const card = screen.getByRole('region', { name: 'WhatsApp consent' })
    expect(card).toHaveTextContent(/Opted out on /)
    expect(within(card).getByRole('button', { name: 'Record opt-in' })).toBeInTheDocument()
    expect(within(card).queryByRole('button', { name: 'Record opt-out' })).not.toBeInTheDocument()
  })

  it('records an opt-in only after the confirmation and then asks for a reload', async () => {
    const user = userEvent.setup()
    const onChanged = renderCard()

    await user.click(screen.getByRole('button', { name: 'Record opt-in' }))

    // Nothing is recorded until the dialog is explicitly confirmed.
    expect(setCustomerWhatsAppOptIn).not.toHaveBeenCalled()
    const dialog = screen.getByRole('dialog', { name: 'Record WhatsApp consent' })
    expect(within(dialog).getByText(/Lina's consent to receive appointment messages/)).toBeInTheDocument()

    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }))
    expect(setCustomerWhatsAppOptIn).not.toHaveBeenCalled()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Record opt-in' }))
    await user.click(
      within(screen.getByRole('dialog')).getByRole('button', { name: 'Record opt-in' }),
    )

    await waitFor(() => expect(setCustomerWhatsAppOptIn).toHaveBeenCalledWith('c1', 'OptedIn'))
    await waitFor(() => expect(onChanged).toHaveBeenCalledTimes(1))
  })

  it('records an opt-out through its own confirmation', async () => {
    const user = userEvent.setup()
    const onChanged = renderCard()

    await user.click(screen.getByRole('button', { name: 'Record opt-out' }))
    await user.click(
      within(screen.getByRole('dialog', { name: 'Record a WhatsApp opt-out' })).getByRole(
        'button',
        { name: 'Record opt-out' },
      ),
    )

    await waitFor(() => expect(setCustomerWhatsAppOptIn).toHaveBeenCalledWith('c1', 'OptedOut'))
    await waitFor(() => expect(onChanged).toHaveBeenCalledTimes(1))
  })

  it('surfaces a failed recording as a banner without asking for a reload', async () => {
    const user = userEvent.setup()
    const onChanged = renderCard()
    vi.mocked(setCustomerWhatsAppOptIn).mockRejectedValue(new Error('consent write failed'))

    await user.click(screen.getByRole('button', { name: 'Record opt-in' }))
    await user.click(
      within(screen.getByRole('dialog')).getByRole('button', { name: 'Record opt-in' }),
    )

    expect(await screen.findByRole('alert')).toHaveTextContent('consent write failed')
    expect(onChanged).not.toHaveBeenCalled()
  })
})
