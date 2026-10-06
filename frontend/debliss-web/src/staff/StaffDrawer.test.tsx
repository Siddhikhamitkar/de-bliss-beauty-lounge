import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ComponentProps } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { searchIndividualServices } from '../api/catalogue.ts'
import {
  getStaff,
  getStaffServices,
  getWeekSchedule,
  listStaffAttendance,
  listTimeOff,
  setStaffStatus,
} from '../api/staff.ts'
import type {
  PagedResult,
  ServiceSummary,
  StaffProfile,
  StaffSummary,
  WeekSchedule,
} from '../api/contracts.ts'
import { formatDate } from '../format.ts'
import { StaffDrawer } from './StaffDrawer.tsx'

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

vi.mock('../api/catalogue.ts', () => ({
  searchIndividualServices: vi.fn(),
}))

const summary: StaffSummary = {
  id: 's1',
  firstName: 'Jasmin',
  lastName: 'Rae',
  jobTitle: 'Stylist',
  calendarColor: '#E91E63',
  isActive: true,
  serviceCount: 1,
  sortOrder: 0,
  createdAt: '2026-06-01T00:00:00Z',
  updatedAt: null,
}

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
  createdAt: '2026-06-01T00:00:00Z',
  updatedAt: null,
}

const emptyWeek: WeekSchedule = {
  days: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'].map(
    day => ({ dayOfWeek: day, isWorkingDay: false, intervals: [] }),
  ),
}

const emptyPagedServices: PagedResult<ServiceSummary> = {
  items: [],
  totalCount: 0,
  page: 1,
  pageSize: 200,
  totalPages: 1,
  hasPreviousPage: false,
  hasNextPage: false,
}

function renderDrawer(overrides: Partial<ComponentProps<typeof StaffDrawer>> = {}) {
  const props = {
    member: summary,
    onClose: vi.fn(),
    onEdit: vi.fn(),
    onChanged: vi.fn(),
    canViewAttendance: true,
    ...overrides,
  }
  render(<StaffDrawer {...props} />)
  return props
}

describe('StaffDrawer', () => {
  beforeEach(() => {
    vi.mocked(getStaff).mockResolvedValue(profile)
    vi.mocked(setStaffStatus).mockResolvedValue(profile)
    vi.mocked(getStaffServices).mockResolvedValue([])
    vi.mocked(searchIndividualServices).mockResolvedValue(emptyPagedServices)
    vi.mocked(getWeekSchedule).mockResolvedValue(emptyWeek)
    vi.mocked(listTimeOff).mockResolvedValue([])
    vi.mocked(listStaffAttendance).mockResolvedValue([])
  })

  it('shows the row header immediately and the overview once it loads', async () => {
    renderDrawer()

    expect(screen.getByRole('dialog', { name: 'Staff Jasmin Rae' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Jasmin Rae' })).toBeInTheDocument()
    // The summary's role and status paint before the profile answers.
    expect(screen.getByText('Stylist')).toBeInTheDocument()
    expect(screen.getByText('Active', { selector: 'span.pill' })).toBeInTheDocument()

    // The overview fills in from the profile API without repeating the header.
    expect(await screen.findByText('jasmin.rae@debliss.example')).toBeInTheDocument()
    expect(screen.getByText('+971 50 555 0100')).toBeInTheDocument()
    expect(screen.getByText('#E91E63')).toBeInTheDocument()
    expect(screen.getByText(formatDate(profile.createdAt))).toBeInTheDocument()
    expect(screen.getAllByText('Stylist')).toHaveLength(1)
  })

  it('lists the services the member can perform on the services tab', async () => {
    const user = userEvent.setup()
    vi.mocked(getStaffServices).mockResolvedValue([
      { serviceId: 'sv1', name: 'Balayage Colour', categoryName: 'Colour', isActive: true },
    ])

    renderDrawer()
    const drawer = screen.getByRole('dialog', { name: 'Staff Jasmin Rae' })
    await user.click(within(drawer).getByRole('tab', { name: 'Services' }))

    expect(await within(drawer).findByText('Balayage Colour')).toBeInTheDocument()
    expect(within(drawer).getByText('Colour')).toBeInTheDocument()
  })

  it('fetches each roster panel only when its tab first opens', async () => {
    const user = userEvent.setup()
    renderDrawer()

    await screen.findByText('jasmin.rae@debliss.example')
    expect(getStaffServices).not.toHaveBeenCalled()
    expect(getWeekSchedule).not.toHaveBeenCalled()
    expect(listTimeOff).not.toHaveBeenCalled()
    expect(listStaffAttendance).not.toHaveBeenCalled()

    const drawer = screen.getByRole('dialog', { name: 'Staff Jasmin Rae' })

    await user.click(within(drawer).getByRole('tab', { name: 'Services' }))
    expect(await within(drawer).findByText('No services assigned yet.')).toBeInTheDocument()
    expect(getStaffServices).toHaveBeenCalledTimes(1)
    expect(getWeekSchedule).not.toHaveBeenCalled()

    await user.click(within(drawer).getByRole('tab', { name: 'Working hours' }))
    expect(await within(drawer).findByText('Monday')).toBeInTheDocument()
    expect(screen.getAllByText('Off')).toHaveLength(7)
    expect(getWeekSchedule).toHaveBeenCalledTimes(1)

    await user.click(within(drawer).getByRole('tab', { name: 'Time off' }))
    expect(await within(drawer).findByText('No time-off records yet.')).toBeInTheDocument()
    expect(listTimeOff).toHaveBeenCalledTimes(1)

    await user.click(within(drawer).getByRole('tab', { name: 'Attendance' }))
    expect(await within(drawer).findByText('No attendance records yet.')).toBeInTheDocument()
    expect(listStaffAttendance).toHaveBeenCalledTimes(1)
  })

  it('hides the Attendance tab unless the viewer may see attendance', async () => {
    renderDrawer({ canViewAttendance: false })

    await screen.findByText('jasmin.rae@debliss.example')

    const drawer = screen.getByRole('dialog', { name: 'Staff Jasmin Rae' })
    expect(within(drawer).queryByRole('tab', { name: 'Attendance' })).not.toBeInTheDocument()
    expect(within(drawer).getByRole('tab', { name: 'Staff details' })).toBeInTheDocument()
    expect(listStaffAttendance).not.toHaveBeenCalled()
  })

  it('keeps the header while the profile is still loading', () => {
    vi.mocked(getStaff).mockReturnValue(new Promise(() => {}))

    renderDrawer()

    expect(screen.getByRole('heading', { name: 'Jasmin Rae' })).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('Loading profile…')
  })

  it('shows the load failure with a working retry', async () => {
    vi.mocked(getStaff).mockRejectedValueOnce(new Error('boom'))

    renderDrawer()

    expect(await screen.findByRole('alert')).toHaveTextContent('boom')
    expect(screen.getByRole('heading', { name: 'Jasmin Rae' })).toBeInTheDocument()

    await userEvent.setup().click(screen.getByRole('button', { name: 'Try again' }))

    expect(await screen.findByText('jasmin.rae@debliss.example')).toBeInTheDocument()
    expect(getStaff).toHaveBeenCalledTimes(2)
  })

  it('deactivates through the action menu and reloads the record', async () => {
    const user = userEvent.setup()
    const props = renderDrawer()

    await screen.findByText('jasmin.rae@debliss.example')
    await user.click(screen.getByRole('button', { name: 'More staff actions' }))
    await user.click(screen.getByRole('menuitem', { name: 'Deactivate' }))

    await waitFor(() => expect(setStaffStatus).toHaveBeenCalledWith('s1', false))
    await waitFor(() => expect(getStaff).toHaveBeenCalledTimes(2))
    expect(props.onChanged).toHaveBeenCalledTimes(1)
  })

  it('shows a failed deactivation as a banner without reloading', async () => {
    const user = userEvent.setup()
    vi.mocked(setStaffStatus).mockRejectedValue(new Error('boom'))
    const props = renderDrawer()

    await screen.findByText('jasmin.rae@debliss.example')
    await user.click(screen.getByRole('button', { name: 'More staff actions' }))
    await user.click(screen.getByRole('menuitem', { name: 'Deactivate' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('boom')
    expect(getStaff).toHaveBeenCalledTimes(1)
    expect(props.onChanged).not.toHaveBeenCalled()
  })

  it('opens the editor from the header Edit action', async () => {
    const user = userEvent.setup()
    const props = renderDrawer()

    await screen.findByText('jasmin.rae@debliss.example')
    await user.click(screen.getByRole('button', { name: 'Edit' }))

    expect(props.onEdit).toHaveBeenCalledWith('s1')
  })

  it('exposes the management actions on their own tabs', async () => {
    const user = userEvent.setup()
    renderDrawer()

    const drawer = screen.getByRole('dialog', { name: 'Staff Jasmin Rae' })

    await user.click(within(drawer).getByRole('tab', { name: 'Services' }))
    expect(await within(drawer).findByRole('button', { name: 'Manage services' })).toBeInTheDocument()
    await user.click(within(drawer).getByRole('button', { name: 'Manage services' }))
    expect(
      await screen.findByText('No services found. Add services before assigning staff.'),
    ).toBeInTheDocument()

    await user.click(within(drawer).getByRole('tab', { name: 'Working hours' }))
    expect(
      await within(drawer).findByRole('button', { name: 'Manage working hours' }),
    ).toBeInTheDocument()

    await user.click(within(drawer).getByRole('tab', { name: 'Time off' }))
    expect(await within(drawer).findByRole('button', { name: 'Add time off' })).toBeInTheDocument()
  })

  it('closes from the close button and from Escape', async () => {
    const user = userEvent.setup()
    const props = renderDrawer()

    await user.click(screen.getByRole('button', { name: 'Close' }))
    expect(props.onClose).toHaveBeenCalledTimes(1)

    await user.keyboard('{Escape}')
    expect(props.onClose).toHaveBeenCalledTimes(2)
  })
})
