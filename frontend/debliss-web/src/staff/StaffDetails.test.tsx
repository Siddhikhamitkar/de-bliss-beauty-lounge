import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { getStaff, setStaffStatus } from '../api/staff.ts'
import { searchServices } from '../api/catalogue.ts'
import { getStaffServices, getWeekSchedule, listTimeOff } from '../api/staff.ts'
import type {
  PagedResult,
  ServiceSummary,
  StaffProfile,
  WeekSchedule,
} from '../api/contracts.ts'
import { StaffDetails } from './StaffDetails.tsx'

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

vi.mock('../api/catalogue.ts', () => ({
  searchServices: vi.fn(),
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

const emptyWeek: WeekSchedule = {
  days: [
    'Monday',
    'Tuesday',
    'Wednesday',
    'Thursday',
    'Friday',
    'Saturday',
    'Sunday',
  ].map(day => ({ dayOfWeek: day, isWorkingDay: false, intervals: [] })),
}

describe('StaffDetails', () => {
  beforeEach(() => {
    vi.mocked(getStaff).mockResolvedValue(profile)
    vi.mocked(setStaffStatus).mockResolvedValue(profile)
    vi.mocked(getStaffServices).mockResolvedValue([])
    vi.mocked(searchServices).mockResolvedValue({
      items: [],
      totalCount: 0,
      page: 1,
      pageSize: 200,
      totalPages: 1,
      hasPreviousPage: false,
      hasNextPage: false,
    } satisfies PagedResult<ServiceSummary>)
    vi.mocked(getWeekSchedule).mockResolvedValue(emptyWeek)
    vi.mocked(listTimeOff).mockResolvedValue([])
  })

  it('renders the contact details and the three sections', async () => {
    render(<StaffDetails staffId="s1" onBack={() => {}} onEdit={() => {}} />)

    expect(
      await screen.findByRole('heading', { name: 'Jasmin Rae' }),
    ).toBeInTheDocument()
    expect(screen.getByText('jasmin.rae@debliss.example')).toBeInTheDocument()
    expect(screen.getByText('+971 50 555 0100')).toBeInTheDocument()
    expect(screen.getByText('#E91E63')).toBeInTheDocument()
    expect(screen.getByText('Active')).toBeInTheDocument()

    expect(await screen.findByText('No services assigned yet.')).toBeInTheDocument()
    expect(screen.getByText('No time-off records yet.')).toBeInTheDocument()
  })

  it('shows the load failure and offers a way back', async () => {
    vi.mocked(getStaff).mockRejectedValue(new Error('boom'))
    const onBack = vi.fn()
    const user = userEvent.setup()
    render(<StaffDetails staffId="s1" onBack={onBack} onEdit={() => {}} />)

    expect(await screen.findByRole('alert')).toHaveTextContent('boom')

    await user.click(screen.getByRole('button', { name: 'Back to staff' }))
    expect(onBack).toHaveBeenCalledOnce()
  })

  it('deactivates the member through the API and reloads', async () => {
    const user = userEvent.setup()
    render(<StaffDetails staffId="s1" onBack={() => {}} onEdit={() => {}} />)

    await screen.findByRole('heading', { name: 'Jasmin Rae' })
    await user.click(screen.getByRole('button', { name: 'Deactivate' }))

    await waitFor(() => expect(setStaffStatus).toHaveBeenCalledWith('s1', false))
    await waitFor(() => expect(getStaff).toHaveBeenCalledTimes(2))
  })

  it('opens the editor for the member', async () => {
    const user = userEvent.setup()
    const onEdit = vi.fn()
    render(<StaffDetails staffId="s1" onBack={() => {}} onEdit={onEdit} />)

    await screen.findByRole('heading', { name: 'Jasmin Rae' })
    await user.click(screen.getByRole('button', { name: 'Edit' }))

    expect(onEdit).toHaveBeenCalledWith('s1')
  })
})
