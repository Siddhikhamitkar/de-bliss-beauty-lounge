import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/client.ts'
import { searchIndividualServices } from '../api/catalogue.ts'
import { getStaffServices, setStaffServices } from '../api/staff.ts'
import type { PagedResult, ServiceSummary, StaffServiceItem } from '../api/contracts.ts'
import { ServiceAssignment } from './ServiceAssignment.tsx'

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

function paged(items: ServiceSummary[]): PagedResult<ServiceSummary> {
  return {
    items,
    totalCount: items.length,
    page: 1,
    pageSize: 200,
    totalPages: 1,
    hasPreviousPage: false,
    hasNextPage: false,
  }
}

function service(overrides: Partial<ServiceSummary> = {}): ServiceSummary {
  return {
    id: 'v1',
    name: 'Gel Manicure',
    categoryId: 'c1',
    categoryName: 'Nails',
    durationMinutes: 90,
    price: 28,
    isActive: true,
    isOnlineBookable: true,
    isBookable: true,
    eligibleStaffCount: 1,
    sortOrder: 0,
    createdAt: '2026-10-03T00:00:00Z',
    updatedAt: null,
    itemKind: 'Service',
    serviceCount: null,
    standardTotalPrice: null,
    ...overrides,
  }
}

function assignment(overrides: Partial<StaffServiceItem> = {}): StaffServiceItem {
  return {
    serviceId: 'v1',
    name: 'Gel Manicure',
    categoryName: 'Nails',
    isActive: true,
    ...overrides,
  }
}

describe('ServiceAssignment', () => {
  beforeEach(() => {
    vi.mocked(getStaffServices).mockResolvedValue([assignment()])
    vi.mocked(searchIndividualServices).mockResolvedValue(paged([service()]))
    vi.mocked(setStaffServices).mockResolvedValue([])
  })

  it('lists the services the member can perform', async () => {
    render(<ServiceAssignment staffId="s1" />)

    expect(await screen.findByText('Gel Manicure')).toBeInTheDocument()
    expect(screen.getByText('Nails')).toBeInTheDocument()
  })

  it('shows the empty state when nothing is assigned yet', async () => {
    vi.mocked(getStaffServices).mockResolvedValue([])

    render(<ServiceAssignment staffId="s1" />)

    expect(await screen.findByText('No services assigned yet.')).toBeInTheDocument()
  })

  it('shows the load failure with a retry', async () => {
    vi.mocked(getStaffServices).mockRejectedValue(new Error('boom'))

    render(<ServiceAssignment staffId="s1" />)

    expect(await screen.findByRole('alert')).toHaveTextContent('boom')
  })

  it('replaces the assignment set with the checked services', async () => {
    const user = userEvent.setup()
    vi.mocked(searchIndividualServices).mockResolvedValue(
      paged([
        service(),
        service({ id: 'v2', name: 'Cut & Blow Dry', categoryName: 'Hair' }),
      ]),
    )
    render(<ServiceAssignment staffId="s1" />)

    await screen.findByText('Gel Manicure')
    await user.click(screen.getByRole('button', { name: 'Manage services' }))

    await user.click(screen.getByRole('checkbox', { name: 'Gel Manicure · Nails' }))
    await user.click(screen.getByRole('checkbox', { name: 'Cut & Blow Dry · Hair' }))
    await user.click(screen.getByRole('button', { name: 'Save services' }))

    await waitFor(() => expect(setStaffServices).toHaveBeenCalledWith('s1', ['v2']))
  })

  it('marks an inactive service in the picker so it stays visible', async () => {
    const user = userEvent.setup()
    vi.mocked(getStaffServices).mockResolvedValue([])
    vi.mocked(searchIndividualServices).mockResolvedValue(
      paged([service({ id: 'v3', name: 'Old Perm Treatment', isActive: false })]),
    )
    render(<ServiceAssignment staffId="s1" />)

    await screen.findByText('No services assigned yet.')
    await user.click(screen.getByRole('button', { name: 'Manage services' }))

    expect(
      screen.getByRole('checkbox', { name: 'Old Perm Treatment · Nails (inactive)' }),
    ).toBeInTheDocument()
  })

  it('shows the server field error beside the picker', async () => {
    const user = userEvent.setup()
    vi.mocked(setStaffServices).mockRejectedValue(
      new ApiError(400, {
        status: 400,
        errors: { serviceIds: ['Staff cannot be assigned duplicate services.'] },
      }),
    )
    render(<ServiceAssignment staffId="s1" />)

    await screen.findByText('Gel Manicure')
    await user.click(screen.getByRole('button', { name: 'Manage services' }))
    await user.click(screen.getByRole('button', { name: 'Save services' }))

    expect(
      await screen.findByText('Staff cannot be assigned duplicate services.'),
    ).toBeInTheDocument()
  })
})
