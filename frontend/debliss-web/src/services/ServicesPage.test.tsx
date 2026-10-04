import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { getService, listCategories, listStaffOptions, searchServices, setServiceStatus } from '../api/catalogue.ts'
import type { PagedResult, Service, ServiceSummary } from '../api/contracts.ts'
import { formatDate, formatPrice } from '../format.ts'
import { ServicesPage } from './ServicesPage.tsx'

vi.mock('../api/catalogue.ts', () => ({
  searchServices: vi.fn(),
  getService: vi.fn(),
  createService: vi.fn(),
  updateService: vi.fn(),
  setServiceStatus: vi.fn(),
  listCategories: vi.fn(),
  getServiceCategory: vi.fn(),
  createCategory: vi.fn(),
  updateCategory: vi.fn(),
  setCategoryStatus: vi.fn(),
  listStaffOptions: vi.fn(),
}))

function paged(items: ServiceSummary[], overrides: Partial<PagedResult<ServiceSummary>> = {}): PagedResult<ServiceSummary> {
  return {
    items,
    totalCount: items.length,
    page: 1,
    pageSize: 50,
    totalPages: 1,
    hasPreviousPage: false,
    hasNextPage: false,
    ...overrides,
  }
}

function service(overrides: Partial<ServiceSummary> = {}): ServiceSummary {
  return {
    id: 's1',
    name: 'Gel Manicure',
    categoryId: 'cat1',
    categoryName: 'Nails',
    durationMinutes: 45,
    price: 120,
    isActive: true,
    isOnlineBookable: true,
    isBookable: true,
    eligibleStaffCount: 2,
    sortOrder: 0,
    createdAt: '2026-09-01T00:00:00Z',
    updatedAt: null,
    ...overrides,
  }
}

const details: Service = {
  id: 's1',
  name: 'Gel Manicure',
  description: 'A long-lasting gel finish.',
  categoryId: 'cat1',
  categoryName: 'Nails',
  isCategoryActive: true,
  durationMinutes: 45,
  price: 120,
  isActive: true,
  isOnlineBookable: true,
  isBookable: true,
  sortOrder: 0,
  eligibleStaff: [
    {
      staffId: 'st1',
      firstName: 'Ana',
      lastName: 'Bianchi',
      jobTitle: 'Nail Artist',
      isActive: true,
    },
    {
      staffId: 'st2',
      firstName: 'Mona',
      lastName: 'Farouk',
      jobTitle: null,
      isActive: false,
    },
  ],
  createdAt: '2026-09-01T00:00:00Z',
  updatedAt: '2026-09-15T00:00:00Z',
}

describe('ServicesPage', () => {
  beforeEach(() => {
    vi.mocked(searchServices).mockResolvedValue(paged([service()]))
    vi.mocked(getService).mockResolvedValue(details)
    vi.mocked(setServiceStatus).mockResolvedValue(details)
    vi.mocked(listCategories).mockResolvedValue([])
    vi.mocked(listStaffOptions).mockResolvedValue([])
  })

  it('shows a loading state before the first answer', () => {
    vi.mocked(searchServices).mockReturnValue(new Promise(() => {}))

    render(<ServicesPage />)

    expect(screen.getByRole('status')).toHaveTextContent('Loading services…')
  })

  it('lists the services the API returns', async () => {
    render(<ServicesPage />)

    expect(await screen.findByText('Gel Manicure')).toBeInTheDocument()
    expect(screen.getByRole('cell', { name: 'Nails' })).toBeInTheDocument()
    expect(screen.getByRole('cell', { name: '45 min' })).toBeInTheDocument()
    expect(screen.getByRole('cell', { name: formatPrice(120) })).toBeInTheDocument()
    expect(screen.getByText('Active', { selector: 'span.pill' })).toBeInTheDocument()
  })

  it('flags an active service that cannot be booked', async () => {
    vi.mocked(searchServices).mockResolvedValue(
      paged([service({ isBookable: false })]),
    )

    render(<ServicesPage />)

    expect(await screen.findByText('not bookable')).toBeInTheDocument()
  })

  it('shows the empty state when nothing matches the filters', async () => {
    vi.mocked(searchServices).mockResolvedValue(paged([]))

    render(<ServicesPage />)

    expect(
      await screen.findByText('No services match these filters.'),
    ).toBeInTheDocument()
  })

  it('shows the load failure with a working retry', async () => {
    vi.mocked(searchServices).mockRejectedValue(new Error('boom'))

    render(<ServicesPage />)

    expect(await screen.findByRole('alert')).toHaveTextContent('boom')

    vi.mocked(searchServices).mockResolvedValue(paged([service()]))
    await userEvent.setup().click(screen.getByRole('button', { name: 'Try again' }))

    expect(await screen.findByText('Gel Manicure')).toBeInTheDocument()
    expect(searchServices).toHaveBeenCalledTimes(2)
  })

  it('opens the record drawer on a row click, leaving the list mounted', async () => {
    const user = userEvent.setup()
    render(<ServicesPage />)

    await screen.findByText('Gel Manicure')
    await user.click(screen.getByText('Gel Manicure'))

    expect(
      await screen.findByRole('dialog', { name: 'Service Gel Manicure' }),
    ).toBeInTheDocument()
    // The list and its controls are still there behind the drawer.
    expect(screen.getByRole('heading', { name: 'Services' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'New service' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Apply' })).toBeInTheDocument()
  })

  it('shows the summary tiles and description on the overview tab', async () => {
    const user = userEvent.setup()
    render(<ServicesPage />)

    await screen.findByText('Gel Manicure')
    await user.click(screen.getByText('Gel Manicure'))
    const drawer = await screen.findByRole('dialog', { name: 'Service Gel Manicure' })

    // The header carries the category; the summary tiles carry the rest.
    expect(within(drawer).getAllByText('Nails')).toHaveLength(1)
    expect(within(drawer).getByText('45 min')).toBeInTheDocument()
    expect(within(drawer).getByText(formatPrice(120))).toBeInTheDocument()
    expect(within(drawer).getByText('2')).toBeInTheDocument()
    expect(within(drawer).getByText('Yes')).toBeInTheDocument()
    expect(within(drawer).getByText('A long-lasting gel finish.')).toBeInTheDocument()
  })

  it('explains why an active service is not bookable', async () => {
    const user = userEvent.setup()
    vi.mocked(getService).mockResolvedValue({ ...details, isBookable: false })
    render(<ServicesPage />)

    await screen.findByText('Gel Manicure')
    await user.click(screen.getByText('Gel Manicure'))
    const drawer = await screen.findByRole('dialog', { name: 'Service Gel Manicure' })

    expect(
      await within(drawer).findByText(
        'Not bookable: the category is inactive or no eligible staff member is active.',
      ),
    ).toBeInTheDocument()
  })

  it('lists the assigned staff on the roster tab', async () => {
    const user = userEvent.setup()
    render(<ServicesPage />)

    await screen.findByText('Gel Manicure')
    await user.click(screen.getByText('Gel Manicure'))
    const drawer = await screen.findByRole('dialog', { name: 'Service Gel Manicure' })

    expect(within(drawer).queryByText('Ana Bianchi')).not.toBeInTheDocument()
    await user.click(within(drawer).getByRole('tab', { name: 'Assigned staff' }))

    expect(within(drawer).getByText('Ana Bianchi')).toBeInTheDocument()
    expect(within(drawer).getByText('Nail Artist')).toBeInTheDocument()
    expect(within(drawer).getByText('Mona Farouk')).toBeInTheDocument()
    expect(within(drawer).getByText('inactive')).toBeInTheDocument()
  })

  it('shows the full record on the details tab', async () => {
    const user = userEvent.setup()
    render(<ServicesPage />)

    await screen.findByText('Gel Manicure')
    await user.click(screen.getByText('Gel Manicure'))
    const drawer = await screen.findByRole('dialog', { name: 'Service Gel Manicure' })
    await user.click(within(drawer).getByRole('tab', { name: 'Service details' }))

    // The name and category repeat from the header in the record itself.
    expect(within(drawer).getAllByText('Gel Manicure')).toHaveLength(2)
    expect(within(drawer).getAllByText('Nails')).toHaveLength(2)
    expect(within(drawer).getAllByText('Yes')).toHaveLength(2)
    expect(within(drawer).getByText(formatDate(details.createdAt))).toBeInTheDocument()
    expect(within(drawer).getByText(formatDate(details.updatedAt as string))).toBeInTheDocument()
  })

  it('renders the record as labelled tabs over one panel', async () => {
    const user = userEvent.setup()
    render(<ServicesPage />)

    await screen.findByText('Gel Manicure')
    await user.click(screen.getByText('Gel Manicure'))
    const drawer = await screen.findByRole('dialog', { name: 'Service Gel Manicure' })

    const tablist = within(drawer).getByRole('tablist', { name: 'Service sections' })
    expect(within(tablist).getAllByRole('tab')).toHaveLength(3)
    expect(within(tablist).getByRole('tab', { name: 'Overview' })).toHaveAttribute(
      'aria-selected',
      'true',
    )
    expect(within(drawer).getByRole('tabpanel')).toHaveAttribute(
      'aria-labelledby',
      'service-tab-overview',
    )
  })

  it('moves between tabs with arrow keys', async () => {
    const user = userEvent.setup()
    render(<ServicesPage />)

    await screen.findByText('Gel Manicure')
    await user.click(screen.getByText('Gel Manicure'))
    const drawer = await screen.findByRole('dialog', { name: 'Service Gel Manicure' })

    await user.click(within(drawer).getByRole('tab', { name: 'Overview' }))
    await user.keyboard('{ArrowRight}')

    const staffTab = within(drawer).getByRole('tab', { name: 'Assigned staff' })
    expect(staffTab).toHaveAttribute('aria-selected', 'true')
    expect(staffTab).toHaveFocus()

    await user.keyboard('{ArrowLeft}')
    expect(within(drawer).getByRole('tab', { name: 'Overview' })).toHaveAttribute(
      'aria-selected',
      'true',
    )
  })

  it('keeps the header up while the record loads', async () => {
    const user = userEvent.setup()
    vi.mocked(getService).mockReturnValue(new Promise(() => {}))
    render(<ServicesPage />)

    await screen.findByText('Gel Manicure')
    await user.click(screen.getByText('Gel Manicure'))

    const drawer = await screen.findByRole('dialog', { name: 'Service Gel Manicure' })
    expect(within(drawer).getByRole('heading', { name: 'Gel Manicure' })).toBeInTheDocument()
    expect(within(drawer).getByRole('status')).toHaveTextContent('Loading service…')
  })

  it('shows the drawer load failure with a working retry', async () => {
    const user = userEvent.setup()
    vi.mocked(getService).mockRejectedValueOnce(new Error('boom'))
    render(<ServicesPage />)

    await screen.findByText('Gel Manicure')
    await user.click(screen.getByText('Gel Manicure'))

    const drawer = await screen.findByRole('dialog', { name: 'Service Gel Manicure' })
    expect(await within(drawer).findByRole('alert')).toHaveTextContent('boom')

    await user.click(within(drawer).getByRole('button', { name: 'Try again' }))

    expect(await within(drawer).findByText('A long-lasting gel finish.')).toBeInTheDocument()
    expect(getService).toHaveBeenCalledTimes(2)
  })

  it('opens the editor from the drawer', async () => {
    const user = userEvent.setup()
    render(<ServicesPage />)

    await screen.findByText('Gel Manicure')
    await user.click(screen.getByText('Gel Manicure'))
    const drawer = await screen.findByRole('dialog', { name: 'Service Gel Manicure' })
    await user.click(within(drawer).getByRole('button', { name: 'Edit' }))

    expect(await screen.findByRole('heading', { name: 'Edit Gel Manicure' })).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('toggles status from the row action menu without opening the drawer', async () => {
    const user = userEvent.setup()
    render(<ServicesPage />)

    await screen.findByText('Gel Manicure')
    await user.click(screen.getByRole('button', { name: 'Actions for Gel Manicure' }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()

    await user.click(screen.getByRole('menuitem', { name: 'Deactivate' }))

    await waitFor(() => expect(setServiceStatus).toHaveBeenCalledWith('s1', false))
    await waitFor(() => expect(searchServices).toHaveBeenCalledTimes(2))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('deactivates from the drawer menu and refreshes the record', async () => {
    const user = userEvent.setup()
    render(<ServicesPage />)

    await screen.findByText('Gel Manicure')
    await user.click(screen.getByText('Gel Manicure'))
    const drawer = await screen.findByRole('dialog', { name: 'Service Gel Manicure' })
    await user.click(within(drawer).getByRole('button', { name: 'More service actions' }))
    await user.click(within(drawer).getByRole('menuitem', { name: 'Deactivate' }))

    await waitFor(() => expect(setServiceStatus).toHaveBeenCalledWith('s1', false))
    await waitFor(() => expect(getService).toHaveBeenCalledTimes(2))
    await waitFor(() => expect(searchServices).toHaveBeenCalledTimes(2))
  })

  it('keeps search and filter state when the drawer closes', async () => {
    const user = userEvent.setup()
    render(<ServicesPage />)

    await screen.findByText('Gel Manicure')
    await user.type(screen.getByLabelText('Search'), 'gel')
    await user.click(screen.getByRole('button', { name: 'Apply' }))
    await user.selectOptions(screen.getByLabelText('Status'), 'Inactive')
    await waitFor(() =>
      expect(searchServices).toHaveBeenLastCalledWith({
        search: 'gel',
        categoryId: undefined,
        status: 'Inactive',
        bookableOnly: undefined,
        page: undefined,
      }),
    )

    await user.click(screen.getByText('Gel Manicure'))
    const drawer = await screen.findByRole('dialog', { name: 'Service Gel Manicure' })
    await user.click(within(drawer).getByRole('button', { name: 'Close' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(screen.getByLabelText('Search')).toHaveValue('gel')
    expect(screen.getByLabelText('Status')).toHaveValue('Inactive')
    // Closing the drawer reloads nothing: the list keeps its three queries.
    expect(searchServices).toHaveBeenCalledTimes(3)
  })
})
