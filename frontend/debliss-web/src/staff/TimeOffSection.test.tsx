import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/client.ts'
import { createTimeOff, deleteTimeOff, listTimeOff, updateTimeOff } from '../api/staff.ts'
import type { StaffTimeOff } from '../api/contracts.ts'
import { TimeOffSection } from './TimeOffSection.tsx'

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

const entry: StaffTimeOff = {
  id: 't1',
  staffId: 's1',
  type: 'Leave',
  startsAt: '2026-10-01T09:00:00+04:00',
  endsAt: '2026-10-02T18:00:00+04:00',
  reason: 'Visiting family',
  createdAt: '2026-09-20T00:00:00Z',
  updatedAt: null,
}

describe('TimeOffSection', () => {
  beforeEach(() => {
    vi.mocked(listTimeOff).mockResolvedValue([entry])
    vi.mocked(createTimeOff).mockResolvedValue(entry)
    vi.mocked(updateTimeOff).mockResolvedValue(entry)
    vi.mocked(deleteTimeOff).mockResolvedValue(undefined)
  })

  it('shows the empty state when there are no records', async () => {
    vi.mocked(listTimeOff).mockResolvedValue([])

    render(<TimeOffSection staffId="s1" />)

    expect(await screen.findByText('No time-off records yet.')).toBeInTheDocument()
  })

  it('lists entries with the friendly type label and reason', async () => {
    render(<TimeOffSection staffId="s1" />)

    expect(await screen.findByText('Annual leave')).toBeInTheDocument()
    expect(screen.getByText('Visiting family')).toBeInTheDocument()
  })

  it('blocks a blank submit and asks for both dates', async () => {
    const user = userEvent.setup()
    render(<TimeOffSection staffId="s1" />)

    await screen.findByText('Annual leave')
    await user.click(screen.getByRole('button', { name: 'Add time off' }))
    await user.click(screen.getByRole('button', { name: 'Add time off' }))

    expect(await screen.findByText('Choose a start date and time.')).toBeInTheDocument()
    expect(screen.getByText('Choose an end date and time.')).toBeInTheDocument()
    expect(createTimeOff).not.toHaveBeenCalled()
  })

  it('creates a record and closes the form', async () => {
    const user = userEvent.setup()
    render(<TimeOffSection staffId="s1" />)

    await screen.findByText('Annual leave')
    await user.click(screen.getByRole('button', { name: 'Add time off' }))

    await user.selectOptions(screen.getByLabelText('Type'), 'Sick')
    fireEvent.change(screen.getByLabelText('Starts'), {
      target: { value: '2026-10-10T09:00' },
    })
    fireEvent.change(screen.getByLabelText('Ends'), {
      target: { value: '2026-10-10T12:00' },
    })
    await user.type(screen.getByLabelText('Reason'), 'Flu')
    await user.click(screen.getByRole('button', { name: 'Add time off' }))

    await waitFor(() =>
      expect(createTimeOff).toHaveBeenCalledWith('s1', {
        type: 'Sick',
        startsAt: new Date('2026-10-10T09:00').toISOString(),
        endsAt: new Date('2026-10-10T12:00').toISOString(),
        reason: 'Flu',
      }),
    )
    await waitFor(() => expect(screen.queryByLabelText('Starts')).not.toBeInTheDocument())
  })

  it('loads an entry into the form and updates it in place', async () => {
    const user = userEvent.setup()
    render(<TimeOffSection staffId="s1" />)

    await screen.findByText('Annual leave')
    await user.click(screen.getByRole('button', { name: 'Edit' }))

    expect(screen.getByLabelText('Type')).toHaveValue('Leave')

    await user.click(screen.getByRole('button', { name: 'Save time off' }))

    await waitFor(() =>
      expect(updateTimeOff).toHaveBeenCalledWith(
        's1',
        't1',
        expect.objectContaining({ type: 'Leave', reason: 'Visiting family' }),
      ),
    )
  })

  it('confirms before deleting a future entry', async () => {
    const user = userEvent.setup()
    render(<TimeOffSection staffId="s1" />)

    await screen.findByText('Annual leave')
    await user.click(screen.getByRole('button', { name: 'Delete' }))

    expect(
      await screen.findByRole('dialog', { name: 'Remove this time-off entry?' }),
    ).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Remove' }))

    await waitFor(() => expect(deleteTimeOff).toHaveBeenCalledWith('s1', 't1'))
    await waitFor(() =>
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument(),
    )
  })

  it('keeps a started entry and reports the 409 as a banner', async () => {
    const user = userEvent.setup()
    vi.mocked(deleteTimeOff).mockRejectedValue(
      new ApiError(409, {
        status: 409,
        detail:
          'Time off that has already started is part of the historical record and cannot be deleted.',
      }),
    )
    render(<TimeOffSection staffId="s1" />)

    await screen.findByText('Annual leave')
    await user.click(screen.getByRole('button', { name: 'Delete' }))
    await user.click(await screen.findByRole('button', { name: 'Remove' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Time off that has already started is part of the historical record and cannot be deleted.',
    )
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(screen.getByText('Annual leave')).toBeInTheDocument()
  })
})
