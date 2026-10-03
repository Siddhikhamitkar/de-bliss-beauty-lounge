import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/client.ts'
import { createStaff, getStaff, updateStaff } from '../api/staff.ts'
import type { StaffProfile } from '../api/contracts.ts'
import { StaffEditor } from './StaffForm.tsx'

vi.mock('../api/staff.ts', () => ({
  searchStaff: vi.fn(),
  getStaff: vi.fn(),
  createStaff: vi.fn(),
  updateStaff: vi.fn(),
  setStaffStatus: vi.fn(),
  getStaffServices: vi.fn(),
  setStaffServices: vi.fn(),
  getWeekSchedule: vi.fn(),
  saveWeekSchedule: vi.fn(),
  listTimeOff: vi.fn(),
  createTimeOff: vi.fn(),
  updateTimeOff: vi.fn(),
  deleteTimeOff: vi.fn(),
  checkStaffAvailability: vi.fn(),
}))

const profile: StaffProfile = {
  id: 's1',
  firstName: 'Jasmin',
  lastName: 'Rae',
  email: 'jasmin.rae@debliss.example',
  phoneNumber: '+971 50 555 0100',
  jobTitle: 'Stylist',
  calendarColor: '#E91E63',
  isActive: true,
  sortOrder: 0,
  services: [],
  createdAt: '2026-10-03T00:00:00Z',
  updatedAt: null,
}

async function fillRequiredFields(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText('First name'), 'Jasmin')
  await user.type(screen.getByLabelText('Last name'), 'Rae')
}

describe('StaffEditor', () => {
  beforeEach(() => {
    vi.mocked(getStaff).mockResolvedValue(profile)
    vi.mocked(createStaff).mockResolvedValue(profile)
    vi.mocked(updateStaff).mockResolvedValue(profile)
  })

  it('creates a member from the form fields', async () => {
    const user = userEvent.setup()
    const onSaved = vi.fn()
    render(<StaffEditor staffId={null} onSaved={onSaved} onCancel={() => {}} />)

    await screen.findByRole('heading', { name: 'New staff member' })
    await fillRequiredFields(user)
    await user.type(screen.getByLabelText('Role'), 'Stylist')
    await user.type(screen.getByLabelText('Email'), 'jasmin.rae@debliss.example')
    await user.type(screen.getByLabelText('Phone'), '+971 50 555 0100')
    await user.type(screen.getByLabelText('Calendar colour'), '#E91E63')
    await user.click(screen.getByRole('button', { name: 'Save staff member' }))

    await waitFor(() => expect(onSaved).toHaveBeenCalledOnce())
    expect(createStaff).toHaveBeenCalledWith({
      firstName: 'Jasmin',
      lastName: 'Rae',
      email: 'jasmin.rae@debliss.example',
      phoneNumber: '+971 50 555 0100',
      jobTitle: 'Stylist',
      calendarColor: '#E91E63',
      isActive: true,
      sortOrder: 0,
    })
    expect(getStaff).not.toHaveBeenCalled()
  })

  it('shows each server field error beside its own input', async () => {
    const user = userEvent.setup()
    vi.mocked(createStaff).mockRejectedValue(
      new ApiError(400, {
        status: 400,
        errors: {
          firstName: ['First name is required.'],
          calendarColor: ['Use a colour in the form #RGB or #RRGGBB, for example #E91E63.'],
        },
      }),
    )
    render(<StaffEditor staffId={null} onSaved={() => {}} onCancel={() => {}} />)

    await screen.findByRole('heading', { name: 'New staff member' })
    await user.click(screen.getByRole('button', { name: 'Save staff member' }))

    expect(await screen.findByText('First name is required.')).toBeInTheDocument()
    expect(
      await screen.findByText('Use a colour in the form #RGB or #RRGGBB, for example #E91E63.'),
    ).toBeInTheDocument()
  })

  it('shows a form-level banner when the failure carries no field errors', async () => {
    const user = userEvent.setup()
    vi.mocked(createStaff).mockRejectedValue(
      new ApiError(409, {
        status: 409,
        detail: 'A staff member with this email already exists.',
      }),
    )
    render(<StaffEditor staffId={null} onSaved={() => {}} onCancel={() => {}} />)

    await screen.findByRole('heading', { name: 'New staff member' })
    await fillRequiredFields(user)
    await user.click(screen.getByRole('button', { name: 'Save staff member' }))

    expect(
      await screen.findByText('A staff member with this email already exists.'),
    ).toBeInTheDocument()
  })

  it('loads the profile for editing and submits the change', async () => {
    const user = userEvent.setup()
    const onSaved = vi.fn()
    render(<StaffEditor staffId="s1" onSaved={onSaved} onCancel={() => {}} />)

    await screen.findByRole('heading', { name: 'Edit Jasmin Rae' })
    const role = screen.getByLabelText('Role')
    await user.clear(role)
    await user.type(role, 'Senior Stylist')
    await user.click(screen.getByRole('button', { name: 'Save staff member' }))

    await waitFor(() => expect(onSaved).toHaveBeenCalledOnce())
    expect(updateStaff).toHaveBeenCalledWith(
      's1',
      expect.objectContaining({ jobTitle: 'Senior Stylist' }),
    )
  })

  it('backs out through onCancel without saving', async () => {
    const user = userEvent.setup()
    const onCancel = vi.fn()
    render(<StaffEditor staffId={null} onSaved={() => {}} onCancel={onCancel} />)

    await screen.findByRole('heading', { name: 'New staff member' })
    await user.click(screen.getByRole('button', { name: 'Cancel' }))

    expect(onCancel).toHaveBeenCalledOnce()
    expect(createStaff).not.toHaveBeenCalled()
  })
})
