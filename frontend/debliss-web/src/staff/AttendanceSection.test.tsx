import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { listStaffAttendance } from '../api/staff.ts'
import type { StaffAttendance } from '../api/contracts.ts'
import { formatDate, formatTime } from '../format.ts'
import { AttendanceSection } from './AttendanceSection.tsx'

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
  listStaffAttendance: vi.fn(),
}))

const records: StaffAttendance[] = [
  {
    id: 'a1',
    staffId: 's1',
    workDate: '2026-10-04',
    checkInAt: '2026-10-04T09:05:00+04:00',
    checkOutAt: null,
    status: 'CheckedIn',
    isScheduledWorkingDay: true,
    scheduledIntervals: [{ start: '09:00', end: '17:00' }],
    createdAt: '2026-10-04T09:05:00+04:00',
    updatedAt: null,
  },
  {
    id: 'a2',
    staffId: 's1',
    workDate: '2026-10-02',
    checkInAt: '2026-10-02T09:58:00+04:00',
    checkOutAt: '2026-10-02T18:32:00+04:00',
    status: 'CheckedOut',
    isScheduledWorkingDay: true,
    scheduledIntervals: [
      { start: '10:00', end: '13:00' },
      { start: '14:00', end: '18:00' },
    ],
    createdAt: '2026-10-02T09:58:00+04:00',
    updatedAt: '2026-10-02T18:32:00+04:00',
  },
  {
    id: 'a3',
    staffId: 's1',
    workDate: '2026-10-01',
    checkInAt: '2026-10-01T11:00:00+04:00',
    checkOutAt: '2026-10-01T15:00:00+04:00',
    status: 'CheckedOut',
    isScheduledWorkingDay: false,
    scheduledIntervals: [],
    createdAt: '2026-10-01T11:00:00+04:00',
    updatedAt: '2026-10-01T15:00:00+04:00',
  },
]

describe('AttendanceSection', () => {
  beforeEach(() => {
    vi.mocked(listStaffAttendance).mockResolvedValue(records)
  })

  it('shows the empty state when there are no records', async () => {
    vi.mocked(listStaffAttendance).mockResolvedValue([])

    render(<AttendanceSection staffId="s1" />)

    expect(await screen.findByText('No attendance records yet.')).toBeInTheDocument()
  })

  it('says the range is empty rather than the whole history once a filter is set', async () => {
    vi.mocked(listStaffAttendance).mockResolvedValue([])

    render(<AttendanceSection staffId="s1" />)

    await screen.findByText('No attendance records yet.')
    fireEvent.change(screen.getByLabelText('From'), { target: { value: '2026-09-01' } })

    expect(await screen.findByText('No attendance records in this range.')).toBeInTheDocument()
  })

  it('lists each day with its planned shift, both clock times and status', async () => {
    render(<AttendanceSection staffId="s1" />)

    // Open day: one interval, check-out still pending.
    expect(await screen.findByText(formatDate('2026-10-04'))).toBeInTheDocument()
    expect(screen.getByText('09:00–17:00')).toBeInTheDocument()
    expect(screen.getByText(formatTime('2026-10-04T09:05:00+04:00'))).toBeInTheDocument()
    expect(screen.getByText('—')).toBeInTheDocument()
    expect(screen.getByText('Checked in')).toBeInTheDocument()

    // Closed day: the break in the planned shift reads as two intervals.
    expect(screen.getByText(formatDate('2026-10-02'))).toBeInTheDocument()
    expect(screen.getByText('10:00–13:00, 14:00–18:00')).toBeInTheDocument()
    expect(screen.getByText(formatTime('2026-10-02T09:58:00+04:00'))).toBeInTheDocument()
    expect(screen.getByText(formatTime('2026-10-02T18:32:00+04:00'))).toBeInTheDocument()

    // A weekday the pattern leaves free.
    expect(screen.getByText(formatDate('2026-10-01'))).toBeInTheDocument()
    expect(screen.getByText('Not scheduled')).toBeInTheDocument()
    expect(screen.getAllByText('Checked out')).toHaveLength(2)
  })

  it('asks for the default window first and the chosen range as the filters change', async () => {
    render(<AttendanceSection staffId="s1" />)

    await screen.findByText(formatDate('2026-10-04'))
    expect(listStaffAttendance).toHaveBeenCalledWith('s1', undefined, undefined)

    fireEvent.change(screen.getByLabelText('From'), { target: { value: '2026-09-01' } })
    await waitFor(() =>
      expect(listStaffAttendance).toHaveBeenCalledWith('s1', '2026-09-01', undefined),
    )

    fireEvent.change(screen.getByLabelText('To'), { target: { value: '2026-09-30' } })
    await waitFor(() =>
      expect(listStaffAttendance).toHaveBeenCalledWith('s1', '2026-09-01', '2026-09-30'),
    )
  })

  it('shows the load failure with a working retry', async () => {
    vi.mocked(listStaffAttendance).mockRejectedValueOnce(new Error('boom'))

    render(<AttendanceSection staffId="s1" />)

    expect(await screen.findByRole('alert')).toHaveTextContent('boom')

    await userEvent.setup().click(screen.getByRole('button', { name: 'Try again' }))

    expect(await screen.findByText('Checked in')).toBeInTheDocument()
    expect(listStaffAttendance).toHaveBeenCalledTimes(2)
  })
})
