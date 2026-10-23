import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { getInventoryProduct, searchInventoryProducts } from '../api/inventory.ts'
import type { InventoryProduct, PagedResult } from '../api/contracts.ts'
import { InventoryPage } from './InventoryPage.tsx'

vi.mock('../api/inventory.ts', () => ({
  searchInventoryProducts: vi.fn(),
  getInventoryProduct: vi.fn(),
  getSellableProducts: vi.fn(),
  createProduct: vi.fn(),
  updateProduct: vi.fn(),
  setProductStatus: vi.fn(),
  receiveStock: vi.fn(),
  recordUsage: vi.fn(),
  adjustStock: vi.fn(),
  reverseStockMovement: vi.fn(),
  searchStockMovements: vi.fn(),
}))

vi.mock('../api/imports.ts', async importOriginal => ({
  ...await importOriginal<typeof import('../api/imports.ts')>(),
  downloadImportErrorReport: vi.fn(),
  downloadImportTemplate: vi.fn(),
  executeImport: vi.fn(),
  importTemplateName: vi.fn((kind: string) => `${kind}-import-template.xlsx`),
  previewImport: vi.fn(),
}))

function product(overrides: Partial<InventoryProduct> = {}): InventoryProduct {
  return {
    id: 'p1',
    name: 'Olaplex No. 3',
    brand: 'Olaplex',
    sku: 'OLP-003',
    category: 'Hair Care',
    inventoryType: 'Retail',
    unitOfMeasure: 'bottle',
    costPrice: 55,
    sellingPrice: 95,
    currentStock: 12,
    minimumStock: 4,
    stockStatus: 'InStock',
    isActive: true,
    lastStockAddedAt: '2026-10-09T12:00:00Z',
    lastAddedQuantity: 10,
    createdAt: '2026-09-01T12:00:00Z',
    updatedAt: null,
    ...overrides,
  }
}

function paged(
  items: InventoryProduct[],
  overrides: Partial<PagedResult<InventoryProduct>> = {},
): PagedResult<InventoryProduct> {
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

describe('InventoryPage', () => {
  beforeEach(() => {
    vi.mocked(searchInventoryProducts).mockResolvedValue(
      paged([
        product(),
        product({
          id: 'p2',
          name: 'Wella Developer',
          brand: null,
          sku: 'WEL-060',
          category: null,
          inventoryType: 'SalonUse',
          unitOfMeasure: 'ml',
          costPrice: null,
          sellingPrice: null,
          currentStock: 2,
          minimumStock: 5,
          stockStatus: 'LowStock',
          lastStockAddedAt: null,
          lastAddedQuantity: null,
        }),
      ]),
    )
    vi.mocked(getInventoryProduct).mockResolvedValue(product())
  })

  it('lists retail and salon-use products with their stock figures', async () => {
    render(<InventoryPage canManageInventory />)

    expect(await screen.findByRole('button', { name: 'Olaplex No. 3' })).toBeInTheDocument()
    expect(screen.getByText('Olaplex · OLP-003')).toBeInTheDocument()

    const table = screen.getByRole('table', { name: 'Products' })
    // The type filter's options carry the same words, so scope to the rows.
    expect(within(table).getByText('Retail')).toBeInTheDocument()
    expect(within(table).getByText('Salon-use')).toBeInTheDocument()
    // The retail row: 12 on the shelf, latest delivery 10 on 09/10/2026.
    expect(within(table).getByText('12')).toBeInTheDocument()
    expect(within(table).getByText('09/10/2026')).toBeInTheDocument()
    expect(within(table).getByText('10')).toBeInTheDocument()
    expect(within(table).getByText('In stock')).toBeInTheDocument()
    // The salon-use row never received anything and sits below its minimum.
    expect(screen.getByText('No stock received yet')).toBeInTheDocument()
    expect(within(table).getByText('Low stock')).toBeInTheDocument()
    expect(screen.getByText('—')).toBeInTheDocument()
  })

  it('shows the add-product action only to the owner', () => {
    const { rerender } = render(<InventoryPage canManageInventory />)

    expect(screen.getByRole('button', { name: 'Add product' })).toBeInTheDocument()

    rerender(<InventoryPage canManageInventory={false} />)
    expect(screen.queryByRole('button', { name: 'Add product' })).not.toBeInTheDocument()
  })

  it('opens the product import with the retail and salon-use flavours, owner only', async () => {
    const user = userEvent.setup()
    const { rerender } = render(<InventoryPage canManageInventory />)
    await screen.findByText('Olaplex No. 3')

    await user.click(screen.getByRole('button', { name: 'Import' }))

    const dialog = await screen.findByRole('dialog', { name: 'Import products' })

    expect(within(dialog).getByRole('radio', { name: /Retail products/ })).toBeChecked()
    expect(within(dialog).getByRole('radio', { name: /Salon-use products/ })).toBeInTheDocument()

    rerender(<InventoryPage canManageInventory={false} />)

    expect(screen.queryByRole('button', { name: 'Import' })).not.toBeInTheDocument()
  })

  it('sends the filters to the API as they are applied', async () => {
    const user = userEvent.setup()
    render(<InventoryPage canManageInventory />)
    await screen.findByText('Olaplex No. 3')

    expect(searchInventoryProducts).toHaveBeenNthCalledWith(1, {
      search: undefined,
      category: undefined,
      type: undefined,
      stockStatus: undefined,
      page: undefined,
    })

    await user.type(screen.getByLabelText('Search'), 'olaplex')
    await user.type(screen.getByLabelText('Category'), 'Hair Care')
    await user.click(screen.getByRole('button', { name: 'Apply' }))

    expect(searchInventoryProducts).toHaveBeenNthCalledWith(2, {
      search: 'olaplex',
      category: 'Hair Care',
      type: undefined,
      stockStatus: undefined,
      page: undefined,
    })

    await user.selectOptions(screen.getByLabelText('Type'), 'SalonUse')
    expect(searchInventoryProducts).toHaveBeenNthCalledWith(3, {
      search: 'olaplex',
      category: 'Hair Care',
      type: 'SalonUse',
      stockStatus: undefined,
      page: undefined,
    })

    await user.selectOptions(screen.getByLabelText('Stock status'), 'LowStock')
    expect(searchInventoryProducts).toHaveBeenNthCalledWith(4, {
      search: 'olaplex',
      category: 'Hair Care',
      type: 'SalonUse',
      stockStatus: 'LowStock',
      page: undefined,
    })
  })

  it('opens the product drawer from a row', async () => {
    const user = userEvent.setup()
    render(<InventoryPage canManageInventory />)
    await screen.findByText('Olaplex No. 3')

    await user.click(screen.getByRole('button', { name: 'Olaplex No. 3' }))

    expect(await screen.findByRole('dialog', { name: 'Product Olaplex No. 3' })).toBeInTheDocument()
    expect(getInventoryProduct).toHaveBeenCalledExactlyOnceWith('p1')
  })

  it('shows the empty state when nothing matches the filters', async () => {
    vi.mocked(searchInventoryProducts).mockResolvedValue(paged([]))

    render(<InventoryPage canManageInventory />)

    expect(await screen.findByText('No products match these filters.')).toBeInTheDocument()
  })

  it('shows the load failure with a working retry', async () => {
    vi.mocked(searchInventoryProducts)
      .mockRejectedValueOnce(new Error('boom'))
      .mockResolvedValueOnce(paged([product()]))

    render(<InventoryPage canManageInventory />)

    expect(await screen.findByText('boom')).toBeInTheDocument()

    await userEvent.setup().click(screen.getByRole('button', { name: 'Try again' }))

    expect(await screen.findByRole('button', { name: 'Olaplex No. 3' })).toBeInTheDocument()
  })
})
