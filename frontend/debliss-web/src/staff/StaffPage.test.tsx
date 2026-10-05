import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { searchStaff, getStaff, getStaffServices, getWeekSchedule, listTimeOff, setStaffStatus } from '../api/staff.ts'
import { searchIndividualServices } from '../api/catalogue.ts'
import type {
  PagedResult,
  ServiceSummary,
  StaffProfile,
  StaffSummary,
  WeekSchedule,
} from '../api/contracts.ts'
import { StaffPage } from './StaffPage.tsx'

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
  searchIndividualServices: vi.fn(),
}))

function paged(items: StaffSummary[]): PagedResult<StaffSummary> {
  return {
    items,
    totalCount: items.length,
    page: 1,
    pageSize: 50,
    totalPages: 1,
    hasPreviousPage: false,
    hasNextPage: false,
  }
}

function member(overrides: Partial<StaffSummary> = {}): StaffSummary {
  return {
    id: 's1',
    firstName: 'Aisha',
    lastName: 'Khan',
    jobTitle: 'Senior Stylist',
    calendarColor: '#E91E63',
    isActive: true,
    serviceCount: 2,
    sortOrder: 0,
    createdAt: '2026-10-03T00:00:00Z',
    updatedAt: null,
    ...overrides,
  }
}

function profile(overrides: Partial<StaffProfile> = {}): StaffProfile {
  return {
    id: 's1',
    firstName: 'Aisha',
    lastName: 'Khan',
    email: 'aisha@debliss.test',
    phoneNumber: null,
    jobTitle: 'Senior Stylist',
    calendarColor: '#E91E63',
    isActive: true,
    sortOrder: 0,
    services: [],
    createdAt: '2026-10-03T00:00:00Z',
    updatedAt: null,
    ...overrides,
  }
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

describe('StaffPage', () => {
  beforeEach(() => {
    vi.mocked(searchStaff).mockResolvedValue(paged([member()]))
    vi.mocked(setStaffStatus).mockResolvedValue(profile())
    vi.mocked(getStaff).mockResolvedValue(profile())
    vi.mocked(getStaffServices).mockResolvedValue([])
    vi.mocked(searchIndividualServices).mockResolvedValue(emptyPagedServices)
    vi.mocked(getWeekSchedule).mockResolvedValue(emptyWeek)
    vi.mocked(listTimeOff).mockResolvedValue([])
  })

  it('shows a loading state before the first answer', () => {
    vi.mocked(searchStaff).mockReturnValue(new Promise(() => {}))

    render(<StaffPage />)

    expect(screen.getByRole('status')).toHaveTextContent('Loading staff…')
  })

  it('lists the members the API returns', async () => {
    render(<StaffPage />)

    expect(await screen.findByText('Aisha Khan')).toBeInTheDocument()
    expect(screen.getByText('Senior Stylist')).toBeInTheDocument()
    expect(screen.getByText('Active', { selector: 'span.pill' })).toBeInTheDocument()
  })

  it('shows the empty state when nothing matches the filters', async () => {
    vi.mocked(searchStaff).mockResolvedValue(paged([]))

    render(<StaffPage />)

    expect(
      await screen.findByText('No staff members match these filters.'),
    ).toBeInTheDocument()
  })

  it('shows the load failure with a working retry', async () => {
    vi.mocked(searchStaff).mockRejectedValue(new Error('boom'))

    render(<StaffPage />)

    expect(await screen.findByRole('alert')).toHaveTextContent('boom')

    vi.mocked(searchStaff).mockResolvedValue(paged([member()]))
    await userEvent.setup().click(screen.getByRole('button', { name: 'Try again' }))

    expect(await screen.findByText('Aisha Khan')).toBeInTheDocument()
    expect(searchStaff).toHaveBeenCalledTimes(2)
  })

  it('applies the typed search, trimmed, as a fresh query', async () => {
    const user = userEvent.setup()
    render(<StaffPage />)

    await screen.findByText('Aisha Khan')
    await user.type(screen.getByLabelText('Search'), '  jasmin ')
    await user.click(screen.getByRole('button', { name: 'Apply' }))

    await waitFor(() =>
      expect(searchStaff).toHaveBeenLastCalledWith({
        search: 'jasmin',
        status: undefined,
        page: undefined,
      }),
    )
  })

  it('narrows the list when the status filter changes', async () => {
    const user = userEvent.setup()
    render(<StaffPage />)

    await screen.findByText('Aisha Khan')
    await user.selectOptions(screen.getByLabelText('Status'), 'Inactive')

    await waitFor(() =>
      expect(searchStaff).toHaveBeenLastCalledWith({
        search: undefined,
        status: 'Inactive',
        page: undefined,
      }),
    )
  })

  it('toggles status from the row action menu without opening the drawer', async () => {
    const user = userEvent.setup()
    render(<StaffPage />)

    await screen.findByText('Aisha Khan')
    await user.click(screen.getByRole('button', { name: 'Actions for Aisha Khan' }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()

    await user.click(screen.getByRole('menuitem', { name: 'Deactivate' }))

    await waitFor(() => expect(setStaffStatus).toHaveBeenCalledWith('s1', false))
    await waitFor(() => expect(searchStaff).toHaveBeenCalledTimes(2))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('shows a failed toggle as a banner instead of reloading', async () => {
    const user = userEvent.setup()
    vi.mocked(setStaffStatus).mockRejectedValue(new Error('boom'))
    render(<StaffPage />)

    await screen.findByText('Aisha Khan')
    await user.click(screen.getByRole('button', { name: 'Actions for Aisha Khan' }))
    await user.click(screen.getByRole('menuitem', { name: 'Deactivate' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('boom')
    expect(searchStaff).toHaveBeenCalledTimes(1)
  })

  it('opens the record drawer on a row click, leaving the list mounted', async () => {
    const user = userEvent.setup()
    render(<StaffPage />)

    await screen.findByText('Aisha Khan')
    await user.click(screen.getByText('Aisha Khan'))

    const drawer = await screen.findByRole('dialog', { name: 'Staff Aisha Khan' })
    expect(await within(drawer).findByText('aisha@debliss.test')).toBeInTheDocument()
    // The list and its controls are still there behind the drawer.
    expect(screen.getByRole('heading', { name: 'Staff', level: 2 })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'New staff member' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Apply' })).toBeInTheDocument()
  })

  it('opens the editor from the drawer', async () => {
    const user = userEvent.setup()
    render(<StaffPage />)

    await screen.findByText('Aisha Khan')
    await user.click(screen.getByText('Aisha Khan'))
    const drawer = await screen.findByRole('dialog', { name: 'Staff Aisha Khan' })
    await within(drawer).findByText('aisha@debliss.test')
    await user.click(within(drawer).getByRole('button', { name: 'Edit' }))

    expect(await screen.findByRole('heading', { name: 'Edit Aisha Khan' })).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('keeps search and filter state when the drawer closes', async () => {
    const user = userEvent.setup()
    render(<StaffPage />)

    await screen.findByText('Aisha Khan')
    await user.type(screen.getByLabelText('Search'), 'jasmin')
    await user.click(screen.getByRole('button', { name: 'Apply' }))
    await user.selectOptions(screen.getByLabelText('Status'), 'Inactive')
    await waitFor(() =>
      expect(searchStaff).toHaveBeenLastCalledWith({
        search: 'jasmin',
        status: 'Inactive',
        page: undefined,
      }),
    )

    await user.click(screen.getByText('Aisha Khan'))
    const drawer = await screen.findByRole('dialog', { name: 'Staff Aisha Khan' })
    await user.click(within(drawer).getByRole('button', { name: 'Close' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(screen.getByLabelText('Search')).toHaveValue('jasmin')
    expect(screen.getByLabelText('Status')).toHaveValue('Inactive')
    // Closing the drawer reloads nothing: the list keeps its three queries.
    expect(searchStaff).toHaveBeenCalledTimes(3)
  })
})
