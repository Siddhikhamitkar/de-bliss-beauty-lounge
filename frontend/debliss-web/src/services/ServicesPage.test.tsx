import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  getBundle,
  getService,
  listCategories,
  listStaffOptions,
  searchIndividualServices,
  searchServices,
  setBundleStatus,
  setServiceStatus,
} from '../api/catalogue.ts'
import type { Bundle, PagedResult, Service, ServiceSummary } from '../api/contracts.ts'
import { downloadImportTemplate, executeImport, previewImport } from '../api/imports.ts'
import { formatDuration, formatDate, formatPrice } from '../format.ts'
import { ServicesPage } from './ServicesPage.tsx'

vi.mock('../api/imports.ts', async importOriginal => ({
  ...await importOriginal<typeof import('../api/imports.ts')>(),
  downloadImportTemplate: vi.fn(),
  downloadImportErrorReport: vi.fn(),
  previewImport: vi.fn(),
  executeImport: vi.fn(),
}))

vi.mock('../api/catalogue.ts', () => ({
  searchServices: vi.fn(),
  searchIndividualServices: vi.fn(),
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
  listBookableBundles: vi.fn(),
  getBundle: vi.fn(),
  createBundle: vi.fn(),
  updateBundle: vi.fn(),
  setBundleStatus: vi.fn(),
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
    itemKind: 'Service',
    serviceCount: null,
    standardTotalPrice: null,
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

/** The two components the bundle below is made of, in delivery order. */
const bundleItems: Bundle['items'] = [
  { serviceId: 's2', name: 'Colour', durationMinutes: 60, price: 80, displayOrder: 0, isActive: true },
  { serviceId: 's1', name: 'Cut', durationMinutes: 45, price: 40, displayOrder: 1, isActive: true },
]

const bundleRecord: Bundle = {
  id: 'b1',
  name: 'Makeover',
  description: 'Colour, then the cut.',
  categoryId: 'cat1',
  categoryName: 'Hair',
  isCategoryActive: true,
  durationMinutes: 105,
  price: 100,
  standardTotalPrice: 120,
  savingsAmount: 20,
  isActive: true,
  isBookable: true,
  sortOrder: 3,
  items: bundleItems,
  createdAt: '2026-09-02T00:00:00Z',
  updatedAt: null,
}

function bundleRow(overrides: Partial<ServiceSummary> = {}): ServiceSummary {
  return service({
    id: 'b1',
    name: 'Makeover',
    categoryId: 'cat1',
    categoryName: 'Hair',
    durationMinutes: 105,
    price: 100,
    isOnlineBookable: false,
    eligibleStaffCount: 0,
    itemKind: 'Bundle',
    serviceCount: 2,
    standardTotalPrice: 120,
    ...overrides,
  })
}

describe('ServicesPage', () => {
  beforeEach(() => {
    vi.mocked(searchServices).mockResolvedValue(paged([service()]))
    vi.mocked(getService).mockResolvedValue(details)
    vi.mocked(setServiceStatus).mockResolvedValue(details)
    vi.mocked(listCategories).mockResolvedValue([])
    vi.mocked(listStaffOptions).mockResolvedValue([])
    vi.mocked(getBundle).mockResolvedValue(bundleRecord)
    vi.mocked(setBundleStatus).mockResolvedValue(bundleRecord)
    vi.mocked(searchIndividualServices).mockResolvedValue(paged([service()]))
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
    expect(
      screen.getByRole('button', { name: 'Add to the service menu' }),
    ).toBeInTheDocument()
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

  it('renders a bundle row as its own kind of offering', async () => {
    vi.mocked(searchServices).mockResolvedValue(paged([service(), bundleRow()]))

    render(<ServicesPage />)

    const row = await screen.findByRole('row', { name: /Makeover/ })
    expect(within(row).getByText('Bundle · 2 services')).toBeInTheDocument()
    expect(within(row).getByRole('cell', { name: formatDuration(105) })).toBeInTheDocument()
    // The bundle price leads; what the components cost separately is struck through.
    expect(within(row).getByText(formatPrice(100))).toBeInTheDocument()
    expect(
      within(row).getByText(formatPrice(120), { selector: '.was-price' }),
    ).toBeInTheDocument()
    // A bundle has no staff of its own, and it is never offered online.
    expect(within(row).getAllByRole('cell', { name: '—' })).toHaveLength(2)
  })

  it('flags an active bundle that cannot be booked', async () => {
    vi.mocked(searchServices).mockResolvedValue(paged([bundleRow({ isBookable: false })]))

    render(<ServicesPage />)

    expect(await screen.findByText('not bookable')).toBeInTheDocument()
    expect(screen.getByRole('row', { name: /Makeover/ })).toHaveClass('row-warning')
  })

  it('opens the bundle drawer with the components in delivery order', async () => {
    const user = userEvent.setup()
    vi.mocked(searchServices).mockResolvedValue(paged([bundleRow()]))
    render(<ServicesPage />)

    await screen.findByText('Makeover')
    await user.click(screen.getByText('Makeover'))

    const drawer = await screen.findByRole('dialog', { name: 'Bundle Makeover' })
    expect(within(drawer).getByRole('tablist', { name: 'Bundle sections' })).toBeInTheDocument()
    expect(within(drawer).getByText('Colour, then the cut.')).toBeInTheDocument()
    // The saving is the reason a bundle exists, so the overview carries it.
    expect(within(drawer).getByText(formatPrice(20))).toBeInTheDocument()

    await user.click(within(drawer).getByRole('tab', { name: 'Services' }))

    // The colour goes on first even though the cut sits first on the menu.
    const [first, second] = within(drawer).getAllByRole('listitem')
    expect(within(first).getByText('Colour')).toBeInTheDocument()
    expect(within(second).getByText('Cut')).toBeInTheDocument()
    expect(within(drawer).getByText('Bought separately')).toBeInTheDocument()
  })

  it('explains why an active bundle is not bookable', async () => {
    const user = userEvent.setup()
    vi.mocked(searchServices).mockResolvedValue(paged([bundleRow({ isBookable: false })]))
    vi.mocked(getBundle).mockResolvedValue({ ...bundleRecord, isBookable: false })
    render(<ServicesPage />)

    await screen.findByText('Makeover')
    await user.click(screen.getByText('Makeover'))
    const drawer = await screen.findByRole('dialog', { name: 'Bundle Makeover' })

    expect(
      await within(drawer).findByText(
        'Not bookable: the category is inactive, or one of the services is no longer active and bookable.',
      ),
    ).toBeInTheDocument()
  })

  it('deactivates a bundle from the row menu through the bundle endpoint', async () => {
    const user = userEvent.setup()
    vi.mocked(searchServices).mockResolvedValue(paged([bundleRow()]))
    render(<ServicesPage />)

    await screen.findByText('Makeover')
    await user.click(screen.getByRole('button', { name: 'Actions for Makeover' }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    await user.click(screen.getByRole('menuitem', { name: 'Deactivate' }))

    await waitFor(() => expect(setBundleStatus).toHaveBeenCalledWith('b1', false))
    // A bundle shares the list with the services but not their status endpoint.
    expect(setServiceStatus).not.toHaveBeenCalled()
    await waitFor(() => expect(searchServices).toHaveBeenCalledTimes(2))
  })

  it('deactivates from the bundle drawer menu and refreshes the record', async () => {
    const user = userEvent.setup()
    vi.mocked(searchServices).mockResolvedValue(paged([bundleRow()]))
    render(<ServicesPage />)

    await screen.findByText('Makeover')
    await user.click(screen.getByText('Makeover'))
    const drawer = await screen.findByRole('dialog', { name: 'Bundle Makeover' })
    await user.click(within(drawer).getByRole('button', { name: 'More bundle actions' }))
    await user.click(within(drawer).getByRole('menuitem', { name: 'Deactivate' }))

    await waitFor(() => expect(setBundleStatus).toHaveBeenCalledWith('b1', false))
    await waitFor(() => expect(getBundle).toHaveBeenCalledTimes(2))
    await waitFor(() => expect(searchServices).toHaveBeenCalledTimes(2))
    expect(setServiceStatus).not.toHaveBeenCalled()
  })

  it('opens the bundle editor from the bundle drawer', async () => {
    const user = userEvent.setup()
    vi.mocked(searchServices).mockResolvedValue(paged([bundleRow()]))
    render(<ServicesPage />)

    await screen.findByText('Makeover')
    await user.click(screen.getByText('Makeover'))
    const drawer = await screen.findByRole('dialog', { name: 'Bundle Makeover' })
    await user.click(within(drawer).getByRole('button', { name: 'Edit' }))

    expect(await screen.findByRole('heading', { name: 'Edit Makeover' })).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(getBundle).toHaveBeenCalledWith('b1')
  })

  it('starts a new bundle from the add menu, offering services rather than bundles', async () => {
    const user = userEvent.setup()
    render(<ServicesPage />)

    await screen.findByText('Gel Manicure')
    await user.click(screen.getByRole('button', { name: 'Add to the service menu' }))
    await user.click(screen.getByRole('menuitem', { name: 'New bundle' }))

    expect(await screen.findByRole('heading', { name: 'New bundle' })).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    // A bundle is made of services, never of other bundles, so the picker reads
    // the individual-service list — and only active ones can be added to it.
    await waitFor(() =>
      expect(searchIndividualServices).toHaveBeenCalledWith({ status: 'Active', pageSize: 200 }),
    )
    expect(getBundle).not.toHaveBeenCalled()
  })

  describe('Excel import actions', () => {
    const realCreateObjectURL = URL.createObjectURL
    const realRevokeObjectURL = URL.revokeObjectURL

    beforeEach(() => {
      URL.createObjectURL = vi.fn(() => 'blob:services-template')
      URL.revokeObjectURL = vi.fn()
    })

    afterEach(() => {
      URL.createObjectURL = realCreateObjectURL
      URL.revokeObjectURL = realRevokeObjectURL
    })

    it('downloads the service template directly without opening the import dialog', async () => {
      const user = userEvent.setup()
      const blob = new Blob(['template'])
      let finish!: (blob: Blob) => void
      vi.mocked(downloadImportTemplate).mockReturnValue(new Promise(resolve => { finish = resolve }))
      const download = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
      render(<ServicesPage />)
      await screen.findByText('Gel Manicure')
      await user.click(screen.getByRole('button', { name: 'Download Excel Template' }))

      expect(downloadImportTemplate).toHaveBeenCalledExactlyOnceWith('services')
      expect(screen.getByRole('button', { name: 'Downloading…' })).toBeDisabled()
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
      await act(async () => finish(blob))
      expect(URL.createObjectURL).toHaveBeenCalledWith(blob)
      expect(download.mock.instances[0]).toHaveAttribute('download', 'services-import-template.xlsx')
      expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:services-template')
      expect(screen.getByRole('button', { name: 'Download Excel Template' })).toBeEnabled()
      expect(previewImport).not.toHaveBeenCalled()
      expect(executeImport).not.toHaveBeenCalled()
      download.mockRestore()
    })

    it('reports template download failure and allows retry', async () => {
      const user = userEvent.setup()
      vi.mocked(downloadImportTemplate).mockRejectedValue(new Error('Template unavailable.'))
      render(<ServicesPage />)
      await user.click(screen.getByRole('button', { name: 'Download Excel Template' }))
      expect(await screen.findByRole('alert')).toHaveTextContent('Template unavailable.')
      expect(screen.getByRole('button', { name: 'Download Excel Template' })).toBeEnabled()
    })

    it('opens the shared service dialog directly and reloads the list after a confirmed creation', async () => {
      const user = userEvent.setup()
      const file = new File(['workbook'], 'services.xlsx')
      vi.mocked(previewImport).mockResolvedValue({
        type: 'Services', fileName: file.name, totalRows: 1, validRows: 1, invalidRows: 0,
        duplicateRows: 0, existingRows: 0, openingStockRows: 0,
        rows: [{ rowNumber: 2, label: 'New treatment', status: 'Valid', fields: ['New treatment', 'Nails', '75', '30', '', 'No', ''] }],
        errors: [],
      })
      vi.mocked(executeImport).mockResolvedValue({
        type: 'Services', fileName: file.name, succeeded: true, created: 1, skippedInvalid: 0,
        skippedExisting: 0, failed: 0, openingStockMovements: 0, errors: [], message: null,
      })
      render(<ServicesPage />)
      await screen.findByText('Gel Manicure')
      await user.click(screen.getByRole('button', { name: 'Import Services' }))
      const dialog = screen.getByRole('dialog', { name: 'Import Services' })
      await user.upload(within(dialog).getByLabelText('Workbook (.xlsx)'), file)
      await user.click(within(dialog).getByRole('button', { name: 'Preview import' }))
      expect(previewImport).toHaveBeenCalledExactlyOnceWith('services', file)
      expect(searchServices).toHaveBeenCalledTimes(1)
      expect(executeImport).not.toHaveBeenCalled()

      vi.mocked(searchServices).mockResolvedValue(paged([service(), service({ id: 's2', name: 'New treatment' })]))
      await user.click(within(dialog).getByRole('button', { name: 'Import 1 service' }))
      expect(await within(dialog).findByText('Created 1 service.')).toBeInTheDocument()
      expect(executeImport).toHaveBeenCalledExactlyOnceWith('services', file, false)
      await waitFor(() => expect(searchServices).toHaveBeenCalledTimes(2))
      await user.click(within(dialog).getByRole('button', { name: 'Done' }))
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
      expect(screen.getByRole('row', { name: /New treatment/ })).toBeInTheDocument()
    })

    it('cancels without executing or reloading and opens a fresh dialog next time', async () => {
      const user = userEvent.setup()
      render(<ServicesPage />)
      await screen.findByText('Gel Manicure')
      await user.click(screen.getByRole('button', { name: 'Import Services' }))
      await user.upload(screen.getByLabelText('Workbook (.xlsx)'), new File(['workbook'], 'services.xlsx'))
      await user.click(screen.getByRole('button', { name: 'Cancel' }))
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
      expect(executeImport).not.toHaveBeenCalled()
      expect(searchServices).toHaveBeenCalledTimes(1)
      await user.click(screen.getByRole('button', { name: 'Import Services' }))
      expect(screen.getByRole('button', { name: 'Preview import' })).toBeDisabled()
      expect(screen.getByText('No workbook selected.')).toBeInTheDocument()
    })
  })
})
