import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ComponentProps } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  adjustStock,
  getInventoryProduct,
  receiveStock,
  recordUsage,
  searchStockMovements,
  setProductStatus,
} from '../api/inventory.ts'
import { ApiError } from '../api/client.ts'
import type { InventoryProduct, StockMovement } from '../api/contracts.ts'
import { InventoryDrawer } from './InventoryDrawer.tsx'

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

function movement(overrides: Partial<StockMovement> = {}): StockMovement {
  return {
    id: 'm1',
    productId: 'p1',
    movementType: 'StockReceived',
    quantityChange: 10,
    stockBefore: 2,
    stockAfter: 12,
    occurredAt: '2026-10-09T12:00:00Z',
    reference: 'DEL-100',
    notes: 'Weekly delivery',
    invoiceId: null,
    invoiceNumber: null,
    performedByUserId: 'u1',
    performedByName: 'Omar Owner',
    reversalOfMovementId: null,
    createdAt: '2026-10-09T12:00:00Z',
    ...overrides,
  }
}

function renderDrawer(overrides: Partial<ComponentProps<typeof InventoryDrawer>> = {}) {
  const props = {
    product: product(),
    canManageInventory: true,
    onClose: vi.fn(),
    onEdit: vi.fn(),
    onChanged: vi.fn(),
    ...overrides,
  }
  render(<InventoryDrawer {...props} />)
  return props
}

describe('InventoryDrawer', () => {
  beforeEach(() => {
    vi.mocked(getInventoryProduct).mockResolvedValue(product())
    vi.mocked(setProductStatus).mockResolvedValue(product())
    vi.mocked(receiveStock).mockResolvedValue(movement())
    vi.mocked(recordUsage).mockResolvedValue(movement())
    vi.mocked(adjustStock).mockResolvedValue(movement())
    vi.mocked(searchStockMovements).mockResolvedValue({
      items: [movement()],
      totalCount: 1,
      page: 1,
      pageSize: 50,
      totalPages: 1,
      hasPreviousPage: false,
      hasNextPage: false,
    })
  })

  it('shows the header and the full overview once the record loads', async () => {
    renderDrawer()

    expect(screen.getByRole('dialog', { name: 'Product Olaplex No. 3' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Olaplex No. 3' })).toBeInTheDocument()
    // The summary paints before the record answers.
    expect(screen.getByText('OLP-003')).toBeInTheDocument()

    expect(await screen.findByText('Hair Care')).toBeInTheDocument()
    expect(screen.getByText('bottle')).toBeInTheDocument()
    expect(screen.getByText('55.00')).toBeInTheDocument()
    expect(screen.getByText('95.00')).toBeInTheDocument()
    expect(screen.getByText('Yes')).toBeInTheDocument()
    // Stock figures with the latest delivery, not the latest event.
    expect(screen.getByText('12 bottle')).toBeInTheDocument()
    expect(screen.getByText('09/10/2026 (10 bottle)')).toBeInTheDocument()
    expect(screen.getAllByText('In stock')).toHaveLength(2)
  })

  it('keeps every write action away from the receptionist', async () => {
    renderDrawer({ canManageInventory: false })
    await screen.findByText('Hair Care')

    expect(screen.queryByRole('button', { name: 'Edit' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Receive stock' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Adjust stock' })).not.toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'More product actions' }),
    ).not.toBeInTheDocument()
  })

  it('offers usage recording only on salon-use products', async () => {
    renderDrawer()
    await screen.findByText('Hair Care')
    expect(screen.queryByRole('button', { name: 'Record usage' })).not.toBeInTheDocument()
  })

  it('records salon-use usage on a back-bar product', async () => {
    const user = userEvent.setup()
    const backBar = product({ inventoryType: 'SalonUse', unitOfMeasure: 'ml' })
    vi.mocked(getInventoryProduct).mockResolvedValue(backBar)
    const { onChanged } = renderDrawer({ product: backBar })

    await user.click(await screen.findByRole('button', { name: 'Record usage' }))
    await user.type(screen.getByLabelText('Quantity (ml)'), '25')
    await user.type(screen.getByLabelText('Reason'), 'Balayage service')
    await user.click(screen.getByRole('button', { name: 'Save usage' }))

    expect(recordUsage).toHaveBeenCalledExactlyOnceWith('p1', {
      quantity: 25,
      usageType: 'Consumption',
      reference: null,
      notes: 'Balayage service',
    })
    expect(await screen.findByText('Usage recorded.')).toBeInTheDocument()
    expect(onChanged).toHaveBeenCalled()
    expect(getInventoryProduct).toHaveBeenCalledTimes(2)
  })

  it('receives stock through the form and refreshes the figures', async () => {
    const user = userEvent.setup()
    const { onChanged } = renderDrawer()
    await screen.findByText('Hair Care')

    await user.click(screen.getByRole('button', { name: 'Receive stock' }))
    await user.type(screen.getByLabelText('Quantity (bottle)'), '6')
    await user.type(screen.getByLabelText('Date received'), '2026-10-09')
    await user.selectOptions(screen.getByLabelText('Kind'), 'OpeningStock')
    await user.type(screen.getByLabelText('Reference (supplier or delivery note)'), 'DEL-2')
    await user.click(screen.getByRole('button', { name: 'Record receipt' }))

    expect(receiveStock).toHaveBeenCalledExactlyOnceWith('p1', {
      quantity: 6,
      receivedAt: '2026-10-09T00:00:00',
      reference: 'DEL-2',
      notes: null,
      kind: 'OpeningStock',
    })
    expect(await screen.findByText('Stock received.')).toBeInTheDocument()
    expect(onChanged).toHaveBeenCalled()
    expect(getInventoryProduct).toHaveBeenCalledTimes(2)
  })

  it('shows the field errors the server returns for a receipt', async () => {
    const user = userEvent.setup()
    vi.mocked(receiveStock).mockRejectedValue(
      new ApiError(400, { errors: { quantity: ['Quantity must be greater than zero.'] } }),
    )
    renderDrawer()
    await screen.findByText('Hair Care')

    await user.click(screen.getByRole('button', { name: 'Receive stock' }))
    await user.type(screen.getByLabelText('Quantity (bottle)'), '0')
    await user.click(screen.getByRole('button', { name: 'Record receipt' }))

    expect(await screen.findByText('Quantity must be greater than zero.')).toBeInTheDocument()
  })

  it('adjusts stock with a signed correction', async () => {
    const user = userEvent.setup()
    const { onChanged } = renderDrawer()
    await screen.findByText('Hair Care')

    await user.click(screen.getByRole('button', { name: 'Adjust stock' }))
    await user.type(screen.getByLabelText('Change (bottle, negative to remove)'), '-3')
    await user.type(screen.getByLabelText('Reason'), 'Physical count')
    await user.click(screen.getByRole('button', { name: 'Apply adjustment' }))

    expect(adjustStock).toHaveBeenCalledExactlyOnceWith('p1', {
      quantityChange: -3,
      reference: null,
      notes: 'Physical count',
    })
    expect(await screen.findByText('Stock adjusted.')).toBeInTheDocument()
    expect(onChanged).toHaveBeenCalled()
  })

  it('deactivates the product from the actions menu', async () => {
    const user = userEvent.setup()
    const { onChanged } = renderDrawer()
    await screen.findByText('Hair Care')

    await user.click(screen.getByRole('button', { name: 'More product actions' }))
    await user.click(screen.getByRole('menuitem', { name: 'Deactivate' }))

    expect(setProductStatus).toHaveBeenCalledExactlyOnceWith('p1', false)
    expect(onChanged).toHaveBeenCalled()
  })

  it('switches to the movement history tab', async () => {
    const user = userEvent.setup()
    renderDrawer()
    await screen.findByText('Hair Care')

    await user.click(screen.getByRole('tab', { name: 'Stock history' }))

    const history = await screen.findByRole('table', { name: 'Stock movements' })
    expect(within(history).getByText('Stock received')).toBeInTheDocument()
    expect(within(history).getByText('+10')).toBeInTheDocument()
    expect(within(history).getByText('2 → 12')).toBeInTheDocument()
  })

  it('hands the edit button to the owner only', async () => {
    const user = userEvent.setup()
    const { onEdit } = renderDrawer()
    await screen.findByText('Hair Care')

    await user.click(screen.getByRole('button', { name: 'Edit' }))

    expect(onEdit).toHaveBeenCalledExactlyOnceWith('p1')
  })
})
