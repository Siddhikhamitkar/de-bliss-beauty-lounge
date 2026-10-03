import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { searchStaff, setStaffStatus } from '../api/staff.ts'
import type { PagedResult, StaffProfile, StaffSummary } from '../api/contracts.ts'
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

describe('StaffPage', () => {
  beforeEach(() => {
    vi.mocked(searchStaff).mockResolvedValue(paged([member()]))
    vi.mocked(setStaffStatus).mockResolvedValue(profile())
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

  it('deactivates a member through the API and reloads', async () => {
    const user = userEvent.setup()
    render(<StaffPage />)

    await screen.findByText('Aisha Khan')
    await user.click(screen.getByRole('button', { name: 'Deactivate' }))

    await waitFor(() => expect(setStaffStatus).toHaveBeenCalledWith('s1', false))
    await waitFor(() => expect(searchStaff).toHaveBeenCalledTimes(2))
  })

  it('shows a failed toggle as a banner instead of reloading', async () => {
    const user = userEvent.setup()
    vi.mocked(setStaffStatus).mockRejectedValue(new Error('boom'))
    render(<StaffPage />)

    await screen.findByText('Aisha Khan')
    await user.click(screen.getByRole('button', { name: 'Deactivate' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('boom')
    expect(searchStaff).toHaveBeenCalledTimes(1)
  })
})
