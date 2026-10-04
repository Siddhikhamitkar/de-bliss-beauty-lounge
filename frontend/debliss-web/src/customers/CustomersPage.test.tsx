import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  getCustomer,
  listCustomerAppointments,
  listCustomerNotes,
  searchCustomers,
  setCustomerStatus,
} from '../api/customers.ts'
import type { CustomerProfile, CustomerSummary, PagedResult } from '../api/contracts.ts'
import { CustomersPage } from './CustomersPage.tsx'

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

function paged(items: CustomerSummary[], overrides: Partial<PagedResult<CustomerSummary>> = {}): PagedResult<CustomerSummary> {
  return {
    items,
    totalCount: items.length,
    page: 1,
    pageSize: 50,
    totalPages: 1,
    hasPreviousPage: false,
    hasNextPage: false,
    ...overrides,
  }
}

function customer(overrides: Partial<CustomerSummary> = {}): CustomerSummary {
  return {
    id: 'c1',
    firstName: 'Lina',
    lastName: 'Mansour',
    phoneNumber: '+971 50 555 0100',
    email: 'lina@debliss.example',
    isActive: true,
    createdAt: '2026-10-03T00:00:00Z',
    updatedAt: null,
    ...overrides,
  }
}

const profile: CustomerProfile = {
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

function renderPage() {
  const onBookAppointment = vi.fn()
  render(<CustomersPage onBookAppointment={onBookAppointment} />)
  return onBookAppointment
}

describe('CustomersPage', () => {
  beforeEach(() => {
    vi.mocked(searchCustomers).mockResolvedValue(paged([customer()]))
    vi.mocked(setCustomerStatus).mockResolvedValue(profile)
    vi.mocked(getCustomer).mockResolvedValue(profile)
    vi.mocked(listCustomerNotes).mockResolvedValue([])
    vi.mocked(listCustomerAppointments).mockResolvedValue([])
  })

  it('shows a loading state before the first answer', () => {
    vi.mocked(searchCustomers).mockReturnValue(new Promise(() => {}))

    renderPage()

    expect(screen.getByRole('status')).toHaveTextContent('Loading customers…')
  })

  it('lists the customers the API returns', async () => {
    renderPage()

    expect(await screen.findByText('Lina Mansour')).toBeInTheDocument()
    expect(screen.getByText('+971 50 555 0100')).toBeInTheDocument()
    expect(screen.getByText('lina@debliss.example')).toBeInTheDocument()
    expect(screen.getByText('Active', { selector: 'span.pill' })).toBeInTheDocument()
  })

  it('shows the empty state when nothing matches the filters', async () => {
    vi.mocked(searchCustomers).mockResolvedValue(paged([]))

    renderPage()

    expect(
      await screen.findByText('No customers match these filters.'),
    ).toBeInTheDocument()
  })

  it('shows the load failure with a working retry', async () => {
    vi.mocked(searchCustomers).mockRejectedValue(new Error('boom'))

    renderPage()

    expect(await screen.findByRole('alert')).toHaveTextContent('boom')

    vi.mocked(searchCustomers).mockResolvedValue(paged([customer()]))
    await userEvent.setup().click(screen.getByRole('button', { name: 'Try again' }))

    expect(await screen.findByText('Lina Mansour')).toBeInTheDocument()
    expect(searchCustomers).toHaveBeenCalledTimes(2)
  })

  it('applies the typed search, trimmed, as a fresh query', async () => {
    const user = userEvent.setup()
    renderPage()

    await screen.findByText('Lina Mansour')
    await user.type(screen.getByLabelText('Search'), '  lina ')
    await user.click(screen.getByRole('button', { name: 'Apply' }))

    await waitFor(() =>
      expect(searchCustomers).toHaveBeenLastCalledWith({
        search: 'lina',
        status: undefined,
        page: undefined,
      }),
    )
  })

  it('narrows the list when the status filter changes', async () => {
    const user = userEvent.setup()
    renderPage()

    await screen.findByText('Lina Mansour')
    await user.selectOptions(screen.getByLabelText('Status'), 'Inactive')

    await waitFor(() =>
      expect(searchCustomers).toHaveBeenLastCalledWith({
        search: undefined,
        status: 'Inactive',
        page: undefined,
      }),
    )
  })

  it('requests the next page when the user pages forward', async () => {
    const user = userEvent.setup()
    vi.mocked(searchCustomers)
      .mockResolvedValueOnce(
        paged([customer()], {
          totalCount: 51,
          totalPages: 2,
          hasNextPage: true,
        }),
      )
      .mockResolvedValueOnce(paged([customer({ id: 'c2', firstName: 'Noor', lastName: 'Haddad' })]))

    renderPage()

    await screen.findByText('Lina Mansour')
    await user.click(screen.getByRole('button', { name: 'Next' }))

    await waitFor(() =>
      expect(searchCustomers).toHaveBeenLastCalledWith({
        search: undefined,
        status: undefined,
        page: 2,
      }),
    )
    expect(await screen.findByText('Noor Haddad')).toBeInTheDocument()
  })

  it('opens the record drawer on a row click, leaving the list mounted', async () => {
    const user = userEvent.setup()
    renderPage()

    await screen.findByText('Lina Mansour')
    await user.click(screen.getByText('Lina Mansour'))

    expect(
      await screen.findByRole('dialog', { name: 'Customer Lina Mansour' }),
    ).toBeInTheDocument()
    // The list and its controls are still there behind the drawer.
    expect(screen.getByRole('heading', { name: 'Customers' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'New customer' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Apply' })).toBeInTheDocument()
  })

  it('toggles status from the row action menu without opening the drawer', async () => {
    const user = userEvent.setup()
    renderPage()

    await screen.findByText('Lina Mansour')
    await user.click(screen.getByRole('button', { name: 'Actions for Lina Mansour' }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()

    await user.click(screen.getByRole('menuitem', { name: 'Deactivate' }))

    await waitFor(() => expect(setCustomerStatus).toHaveBeenCalledWith('c1', false))
    await waitFor(() => expect(searchCustomers).toHaveBeenCalledTimes(2))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('shows a failed toggle as a banner instead of reloading', async () => {
    const user = userEvent.setup()
    vi.mocked(setCustomerStatus).mockRejectedValue(new Error('boom'))
    renderPage()

    await screen.findByText('Lina Mansour')
    await user.click(screen.getByRole('button', { name: 'Actions for Lina Mansour' }))
    await user.click(screen.getByRole('menuitem', { name: 'Deactivate' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('boom')
    expect(searchCustomers).toHaveBeenCalledTimes(1)
  })

  it('opens the editor from the drawer', async () => {
    const user = userEvent.setup()
    renderPage()

    await screen.findByText('Lina Mansour')
    await user.click(screen.getByText('Lina Mansour'))
    await screen.findByRole('dialog', { name: 'Customer Lina Mansour' })
    await user.click(screen.getByRole('button', { name: 'Edit' }))

    expect(await screen.findByRole('heading', { name: 'Edit Lina Mansour' })).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('books an appointment for the customer from the drawer', async () => {
    const user = userEvent.setup()
    const onBookAppointment = renderPage()

    await screen.findByText('Lina Mansour')
    await user.click(screen.getByText('Lina Mansour'))
    await screen.findByRole('dialog', { name: 'Customer Lina Mansour' })
    await user.click(screen.getByRole('button', { name: 'Book appointment' }))

    expect(onBookAppointment).toHaveBeenCalledWith(expect.objectContaining({ id: 'c1' }))
  })

  it('keeps search and filter state when the drawer closes', async () => {
    const user = userEvent.setup()
    renderPage()

    await screen.findByText('Lina Mansour')
    await user.type(screen.getByLabelText('Search'), 'lina')
    await user.click(screen.getByRole('button', { name: 'Apply' }))
    await user.selectOptions(screen.getByLabelText('Status'), 'Inactive')
    await waitFor(() =>
      expect(searchCustomers).toHaveBeenLastCalledWith({
        search: 'lina',
        status: 'Inactive',
        page: undefined,
      }),
    )

    await user.click(screen.getByText('Lina Mansour'))
    const drawer = await screen.findByRole('dialog', { name: 'Customer Lina Mansour' })
    await user.click(within(drawer).getByRole('button', { name: 'Close' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(screen.getByLabelText('Search')).toHaveValue('lina')
    expect(screen.getByLabelText('Status')).toHaveValue('Inactive')
    // Closing the drawer reloads nothing: the list keeps its three queries.
    expect(searchCustomers).toHaveBeenCalledTimes(3)
  })
})
