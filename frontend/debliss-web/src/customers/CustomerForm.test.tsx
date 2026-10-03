import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/client.ts'
import { createCustomer, createCustomerNote, getCustomer, updateCustomer } from '../api/customers.ts'
import type { CustomerProfile } from '../api/contracts.ts'
import { CustomerEditor } from './CustomerForm.tsx'

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
    totalAppointments: 0,
    completedAppointments: 0,
    cancelledAppointments: 0,
    lastVisitAt: null,
  },
  createdAt: '2026-10-03T00:00:00Z',
  updatedAt: null,
}

async function fillRequiredFields(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText('First name'), 'Lina')
  await user.type(screen.getByLabelText('Last name'), 'Mansour')
  await user.type(screen.getByLabelText('Mobile'), '+971 50 555 0100')
}

describe('CustomerEditor', () => {
  beforeEach(() => {
    vi.mocked(getCustomer).mockResolvedValue(profile)
    vi.mocked(createCustomer).mockResolvedValue(profile)
    vi.mocked(updateCustomer).mockResolvedValue(profile)
    vi.mocked(createCustomerNote).mockResolvedValue({
      id: 'n1',
      customerId: 'c1',
      note: 'Prefers evening appointments.',
      createdAt: '2026-10-03T10:00:00Z',
      updatedAt: null,
    })
  })

  it('creates a customer from the form fields', async () => {
    const user = userEvent.setup()
    const onSaved = vi.fn()
    render(<CustomerEditor customerId={null} onSaved={onSaved} onCancel={() => {}} />)

    await screen.findByRole('heading', { name: 'New customer' })
    await fillRequiredFields(user)
    await user.type(screen.getByLabelText('Email'), 'lina@debliss.example')
    await user.type(screen.getByLabelText('Date of birth'), '1990-04-12')
    await user.type(screen.getByLabelText('Gender'), 'Female')
    await user.type(screen.getByLabelText('Address'), 'Marina Walk 4')
    await user.click(screen.getByRole('button', { name: 'Save customer' }))

    await waitFor(() => expect(onSaved).toHaveBeenCalledOnce())
    expect(createCustomer).toHaveBeenCalledWith({
      firstName: 'Lina',
      lastName: 'Mansour',
      phoneNumber: '+971 50 555 0100',
      email: 'lina@debliss.example',
      dateOfBirth: '1990-04-12',
      gender: 'Female',
      address: 'Marina Walk 4',
      isActive: true,
      allowDuplicatePhone: false,
    })
    expect(createCustomerNote).not.toHaveBeenCalled()
    expect(getCustomer).not.toHaveBeenCalled()
  })

  it('posts the create-form note as the customer\'s first note', async () => {
    const user = userEvent.setup()
    const onSaved = vi.fn()
    render(<CustomerEditor customerId={null} onSaved={onSaved} onCancel={() => {}} />)

    await screen.findByRole('heading', { name: 'New customer' })
    await fillRequiredFields(user)
    await user.type(screen.getByLabelText('Notes'), 'Prefers evening appointments.')
    await user.click(screen.getByRole('button', { name: 'Save customer' }))

    await waitFor(() => expect(onSaved).toHaveBeenCalledOnce())
    expect(createCustomerNote).toHaveBeenCalledWith('c1', {
      note: 'Prefers evening appointments.',
    })
  })

  it('shows each server field error beside its own input', async () => {
    const user = userEvent.setup()
    vi.mocked(createCustomer).mockRejectedValue(
      new ApiError(400, {
        status: 400,
        errors: {
          firstName: ['First name is required.'],
          email: ['Enter a valid email address.'],
        },
      }),
    )
    render(<CustomerEditor customerId={null} onSaved={() => {}} onCancel={() => {}} />)

    await screen.findByRole('heading', { name: 'New customer' })
    await user.click(screen.getByRole('button', { name: 'Save customer' }))

    expect(await screen.findByText('First name is required.')).toBeInTheDocument()
    expect(await screen.findByText('Enter a valid email address.')).toBeInTheDocument()
  })

  it('asks before saving a likely duplicate, then saves once confirmed', async () => {
    const user = userEvent.setup()
    const onSaved = vi.fn()
    vi.mocked(createCustomer)
      .mockRejectedValueOnce(
        new ApiError(409, {
          status: 409,
          detail: 'Lina Mansour already uses this mobile number. Save anyway?',
        }),
      )
      .mockResolvedValueOnce(profile)
    render(<CustomerEditor customerId={null} onSaved={onSaved} onCancel={() => {}} />)

    await screen.findByRole('heading', { name: 'New customer' })
    await fillRequiredFields(user)
    await user.click(screen.getByRole('button', { name: 'Save customer' }))

    expect(
      await screen.findByText('Lina Mansour already uses this mobile number. Save anyway?'),
    ).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Save anyway' }))

    await waitFor(() => expect(onSaved).toHaveBeenCalledOnce())
    expect(createCustomer).toHaveBeenCalledTimes(2)
    expect(createCustomer).toHaveBeenLastCalledWith(
      expect.objectContaining({ allowDuplicatePhone: true }),
    )
  })

  it('drops the duplicate warning without a second save when cancelled', async () => {
    const user = userEvent.setup()
    vi.mocked(createCustomer).mockRejectedValue(
      new ApiError(409, {
        status: 409,
        detail: 'Lina Mansour already uses this mobile number. Save anyway?',
      }),
    )
    render(<CustomerEditor customerId={null} onSaved={() => {}} onCancel={() => {}} />)

    await screen.findByRole('heading', { name: 'New customer' })
    await fillRequiredFields(user)
    await user.click(screen.getByRole('button', { name: 'Save customer' }))

    await screen.findByRole('dialog')
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Cancel' }))

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(createCustomer).toHaveBeenCalledTimes(1)
  })

  it('shows a form-level banner when the failure carries no field errors', async () => {
    const user = userEvent.setup()
    vi.mocked(createCustomer).mockRejectedValue(new Error('boom'))
    render(<CustomerEditor customerId={null} onSaved={() => {}} onCancel={() => {}} />)

    await screen.findByRole('heading', { name: 'New customer' })
    await fillRequiredFields(user)
    await user.click(screen.getByRole('button', { name: 'Save customer' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('boom')
  })

  it('loads the profile for editing and submits the change', async () => {
    const user = userEvent.setup()
    const onSaved = vi.fn()
    render(<CustomerEditor customerId="c1" onSaved={onSaved} onCancel={() => {}} />)

    await screen.findByRole('heading', { name: 'Edit Lina Mansour' })
    const email = screen.getByLabelText('Email')
    await user.clear(email)
    await user.type(email, 'lina.mansour@debliss.example')
    await user.click(screen.getByRole('button', { name: 'Save customer' }))

    await waitFor(() => expect(onSaved).toHaveBeenCalledOnce())
    expect(updateCustomer).toHaveBeenCalledWith(
      'c1',
      expect.objectContaining({
        email: 'lina.mansour@debliss.example',
        dateOfBirth: '1990-04-12',
        allowDuplicatePhone: false,
      }),
    )
  })

  it('backs out through onCancel without saving', async () => {
    const user = userEvent.setup()
    const onCancel = vi.fn()
    render(<CustomerEditor customerId={null} onSaved={() => {}} onCancel={onCancel} />)

    await screen.findByRole('heading', { name: 'New customer' })
    await user.click(screen.getByRole('button', { name: 'Cancel' }))

    expect(onCancel).toHaveBeenCalledOnce()
    expect(createCustomer).not.toHaveBeenCalled()
  })
})
