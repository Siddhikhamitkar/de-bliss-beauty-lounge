import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { searchCustomers, setCustomerStatus } from '../api/customers.ts'
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
    lastVisitAt: null,
  },
  createdAt: '2026-10-03T00:00:00Z',
  updatedAt: null,
}

describe('CustomersPage', () => {
  beforeEach(() => {
    vi.mocked(searchCustomers).mockResolvedValue(paged([customer()]))
    vi.mocked(setCustomerStatus).mockResolvedValue(profile)
  })

  it('shows a loading state before the first answer', () => {
    vi.mocked(searchCustomers).mockReturnValue(new Promise(() => {}))

    render(<CustomersPage />)

    expect(screen.getByRole('status')).toHaveTextContent('Loading customers…')
  })

  it('lists the customers the API returns', async () => {
    render(<CustomersPage />)

    expect(await screen.findByText('Lina Mansour')).toBeInTheDocument()
    expect(screen.getByText('+971 50 555 0100')).toBeInTheDocument()
    expect(screen.getByText('lina@debliss.example')).toBeInTheDocument()
    expect(screen.getByText('Active', { selector: 'span.pill' })).toBeInTheDocument()
  })

  it('shows the empty state when nothing matches the filters', async () => {
    vi.mocked(searchCustomers).mockResolvedValue(paged([]))

    render(<CustomersPage />)

    expect(
      await screen.findByText('No customers match these filters.'),
    ).toBeInTheDocument()
  })

  it('shows the load failure with a working retry', async () => {
    vi.mocked(searchCustomers).mockRejectedValue(new Error('boom'))

    render(<CustomersPage />)

    expect(await screen.findByRole('alert')).toHaveTextContent('boom')

    vi.mocked(searchCustomers).mockResolvedValue(paged([customer()]))
    await userEvent.setup().click(screen.getByRole('button', { name: 'Try again' }))

    expect(await screen.findByText('Lina Mansour')).toBeInTheDocument()
    expect(searchCustomers).toHaveBeenCalledTimes(2)
  })

  it('applies the typed search, trimmed, as a fresh query', async () => {
    const user = userEvent.setup()
    render(<CustomersPage />)

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
    render(<CustomersPage />)

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

    render(<CustomersPage />)

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

  it('deactivates a customer through the API and reloads', async () => {
    const user = userEvent.setup()
    render(<CustomersPage />)

    await screen.findByText('Lina Mansour')
    await user.click(screen.getByRole('button', { name: 'Deactivate' }))

    await waitFor(() => expect(setCustomerStatus).toHaveBeenCalledWith('c1', false))
    await waitFor(() => expect(searchCustomers).toHaveBeenCalledTimes(2))
  })

  it('shows a failed toggle as a banner instead of reloading', async () => {
    const user = userEvent.setup()
    vi.mocked(setCustomerStatus).mockRejectedValue(new Error('boom'))
    render(<CustomersPage />)

    await screen.findByText('Lina Mansour')
    await user.click(screen.getByRole('button', { name: 'Deactivate' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('boom')
    expect(searchCustomers).toHaveBeenCalledTimes(1)
  })
})
