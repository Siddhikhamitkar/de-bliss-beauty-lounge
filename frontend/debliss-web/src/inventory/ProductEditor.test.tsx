import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createProduct, getInventoryProduct, updateProduct } from '../api/inventory.ts'
import { ApiError } from '../api/client.ts'
import type { InventoryProduct } from '../api/contracts.ts'
import { ProductEditor } from './ProductEditor.tsx'

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

function renderEditor(productId: string | null) {
  const props = { productId, onSaved: vi.fn(), onCancel: vi.fn() }
  render(<ProductEditor {...props} />)
  return props
}

describe('ProductEditor', () => {
  beforeEach(() => {
    vi.mocked(createProduct).mockResolvedValue(product())
    vi.mocked(updateProduct).mockResolvedValue(product())
  })

  it('creates a retail product with the priced form', async () => {
    const user = userEvent.setup()
    const { onSaved } = renderEditor(null)

    await user.type(await screen.findByLabelText('Name'), 'Olaplex No. 3')
    await user.type(screen.getByLabelText('Brand'), 'Olaplex')
    await user.type(screen.getByLabelText('SKU'), 'OLP-003')
    await user.type(screen.getByLabelText('Category'), 'Hair Care')
    await user.clear(screen.getByLabelText('Unit of measure'))
    await user.type(screen.getByLabelText('Unit of measure'), 'bottle')
    await user.type(screen.getByLabelText('Cost price'), '55')
    await user.type(screen.getByLabelText('Selling price'), '95')
    await user.type(screen.getByLabelText('Minimum stock'), '4')
    await user.click(screen.getByRole('button', { name: 'Save product' }))

    expect(createProduct).toHaveBeenCalledExactlyOnceWith({
      name: 'Olaplex No. 3',
      brand: 'Olaplex',
      sku: 'OLP-003',
      category: 'Hair Care',
      inventoryType: 'Retail',
      unitOfMeasure: 'bottle',
      costPrice: 55,
      sellingPrice: 95,
      minimumStock: 4,
      isActive: true,
    })
    expect(onSaved).toHaveBeenCalled()
  })

  it('never prices a salon-use product', async () => {
    const user = userEvent.setup()
    renderEditor(null)

    await user.type(await screen.findByLabelText('Name'), 'Wella Developer')
    await user.type(screen.getByLabelText('SKU'), 'WEL-06')
    await user.selectOptions(screen.getByLabelText('Inventory type'), 'SalonUse')

    expect(screen.queryByLabelText('Cost price')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Selling price')).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Save product' }))

    expect(createProduct).toHaveBeenCalledExactlyOnceWith({
      name: 'Wella Developer',
      brand: null,
      sku: 'WEL-06',
      category: null,
      inventoryType: 'SalonUse',
      unitOfMeasure: 'pcs',
      costPrice: null,
      sellingPrice: null,
      minimumStock: 0,
      isActive: true,
    })
  })

  it('marks the fields the server rejects', async () => {
    const user = userEvent.setup()
    vi.mocked(createProduct).mockRejectedValue(
      new ApiError(400, { errors: { sku: ['That SKU is already in the catalogue.'] } }),
    )
    renderEditor(null)

    await user.type(await screen.findByLabelText('Name'), 'Olaplex No. 3')
    await user.type(screen.getByLabelText('SKU'), 'OLP-003')
    await user.type(screen.getByLabelText('Cost price'), '55')
    await user.type(screen.getByLabelText('Selling price'), '95')
    await user.click(screen.getByRole('button', { name: 'Save product' }))

    expect(await screen.findByText('That SKU is already in the catalogue.')).toBeInTheDocument()
  })

  it('shows a banner when the failure is not a field error', async () => {
    const user = userEvent.setup()
    vi.mocked(createProduct).mockRejectedValue(new Error('no draft today'))
    renderEditor(null)

    await user.type(await screen.findByLabelText('Name'), 'Olaplex No. 3')
    await user.type(screen.getByLabelText('SKU'), 'OLP-003')
    await user.type(screen.getByLabelText('Cost price'), '55')
    await user.type(screen.getByLabelText('Selling price'), '95')
    await user.click(screen.getByRole('button', { name: 'Save product' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('no draft today')
  })

  it('edits an existing product and keeps stock read-only', async () => {
    const user = userEvent.setup()
    vi.mocked(getInventoryProduct).mockResolvedValue(product())
    const { onSaved } = renderEditor('p1')

    expect(
      await screen.findByRole('heading', { name: 'Edit Olaplex No. 3' }),
    ).toBeInTheDocument()
    expect(getInventoryProduct).toHaveBeenCalledExactlyOnceWith('p1')
    expect(
      screen.getByText(/Current stock \(12 bottle\) only changes through stock movements/),
    ).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Save product' }))

    expect(updateProduct).toHaveBeenCalledExactlyOnceWith('p1', {
      name: 'Olaplex No. 3',
      brand: 'Olaplex',
      sku: 'OLP-003',
      category: 'Hair Care',
      inventoryType: 'Retail',
      unitOfMeasure: 'bottle',
      costPrice: 55,
      sellingPrice: 95,
      minimumStock: 4,
      isActive: true,
    })
    expect(onSaved).toHaveBeenCalled()
  })

  it('shows the load failure with a way back', async () => {
    vi.mocked(getInventoryProduct).mockRejectedValue(new Error('product is gone'))
    const { onCancel } = renderEditor('p1')

    expect(await screen.findByText('product is gone')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Back to inventory' }))
    expect(onCancel).toHaveBeenCalled()
  })
})
