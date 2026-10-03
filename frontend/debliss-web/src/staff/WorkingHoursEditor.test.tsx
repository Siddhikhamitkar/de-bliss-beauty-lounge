import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/client.ts'
import { getWeekSchedule, saveWeekSchedule } from '../api/staff.ts'
import type { DaySchedule, WeekSchedule } from '../api/contracts.ts'
import { WorkingHoursEditor } from './WorkingHoursEditor.tsx'

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

const Weekdays = [
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
  'Saturday',
  'Sunday',
] as const

function week(patch: Partial<Record<(typeof Weekdays)[number], DaySchedule>>): WeekSchedule {
  return {
    days: Weekdays.map(
      day =>
        patch[day] ?? { dayOfWeek: day, isWorkingDay: false, intervals: [] },
    ),
  }
}

describe('WorkingHoursEditor', () => {
  beforeEach(() => {
    vi.mocked(getWeekSchedule).mockResolvedValue(week({}))
    vi.mocked(saveWeekSchedule).mockResolvedValue(week({}))
  })

  it('reads the saved pattern as a week rota', async () => {
    vi.mocked(getWeekSchedule).mockResolvedValue(
      week({
        Monday: {
          dayOfWeek: 'Monday',
          isWorkingDay: true,
          intervals: [
            { start: '09:00', end: '13:00' },
            { start: '14:00', end: '18:00' },
          ],
        },
      }),
    )

    render(<WorkingHoursEditor staffId="s1" />)

    expect(
      await screen.findByText('09:00 – 13:00, 14:00 – 18:00'),
    ).toBeInTheDocument()
    expect(screen.getAllByText('Off')).toHaveLength(6)
  })

  it('seeds a checked day with a full-time block and saves the week', async () => {
    const user = userEvent.setup()
    render(<WorkingHoursEditor staffId="s1" />)

    await screen.findAllByText('Off')
    await user.click(screen.getByRole('button', { name: 'Manage working hours' }))
    await user.click(screen.getByRole('checkbox', { name: 'Monday' }))

    expect(screen.getByLabelText('Monday interval 1 start')).toHaveValue('09:00')
    expect(screen.getByLabelText('Monday interval 1 end')).toHaveValue('17:00')

    await user.click(screen.getByRole('button', { name: 'Save working hours' }))

    await waitFor(() =>
      expect(saveWeekSchedule).toHaveBeenCalledWith(
        's1',
        expect.arrayContaining([
          {
            dayOfWeek: 'Monday',
            isWorkingDay: true,
            intervals: [{ start: '09:00', end: '17:00' }],
          },
        ]),
      ),
    )
  })

  it('shows the server’s per-day message under the editor', async () => {
    const user = userEvent.setup()
    vi.mocked(saveWeekSchedule).mockRejectedValue(
      new ApiError(400, {
        status: 400,
        errors: { days: ['Monday: end time must be after the start time.'] },
      }),
    )
    render(<WorkingHoursEditor staffId="s1" />)

    await screen.findAllByText('Off')
    await user.click(screen.getByRole('button', { name: 'Manage working hours' }))
    await user.click(screen.getByRole('button', { name: 'Save working hours' }))

    expect(
      await screen.findByText('Monday: end time must be after the start time.'),
    ).toBeInTheDocument()
  })

  it('shows the load failure with a retry', async () => {
    vi.mocked(getWeekSchedule).mockRejectedValue(new Error('boom'))

    render(<WorkingHoursEditor staffId="s1" />)

    expect(await screen.findByRole('alert')).toHaveTextContent('boom')
  })
})
