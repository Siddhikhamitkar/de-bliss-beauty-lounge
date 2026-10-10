import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { reverseStockMovement, searchStockMovements } from '../api/inventory.ts'
import type { PagedResult, StockMovement } from '../api/contracts.ts'
import { StockHistory } from './StockHistory.tsx'

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

function paged(items: StockMovement[]): PagedResult<StockMovement> {
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

function renderHistory(canManageInventory = true) {
  const props = { productId: 'p1', canManageInventory, onChanged: vi.fn() }
  render(<StockHistory {...props} />)
  return props
}

describe('StockHistory', () => {
  beforeEach(() => {
    vi.mocked(searchStockMovements).mockResolvedValue(paged([movement()]))
    vi.mocked(reverseStockMovement).mockResolvedValue(movement())
  })

  it('lists movements with their full audit context', async () => {
    renderHistory()

    const table = await screen.findByRole('table', { name: 'Stock movements' })
    const row = within(table).getAllByRole('row')[1]
    expect(within(row).getByText('Stock received')).toBeInTheDocument()
    expect(within(row).getByText('+10')).toBeInTheDocument()
    expect(within(row).getByText('2 → 12')).toBeInTheDocument()
    expect(within(row).getByText('DEL-100')).toBeInTheDocument()
    expect(within(row).getByText('Weekly delivery')).toBeInTheDocument()
    expect(within(row).getByText('Omar Owner')).toBeInTheDocument()

    expect(searchStockMovements).toHaveBeenCalledExactlyOnceWith('p1', {
      type: undefined,
      page: undefined,
    })
  })

  it('reverses a movement as its own auditable entry', async () => {
    const user = userEvent.setup()
    const { onChanged } = renderHistory()

    await user.click(await screen.findByRole('button', { name: 'Reverse' }))

    expect(
      screen.getByRole('heading', { name: 'Reverse Stock received of +10' }),
    ).toBeInTheDocument()

    await user.type(screen.getByLabelText('Reason'), 'Delivery was never ours')
    await user.click(screen.getByRole('button', { name: 'Record reversal' }))

    expect(reverseStockMovement).toHaveBeenCalledExactlyOnceWith('p1', 'm1', {
      notes: 'Delivery was never ours',
    })
    expect(await screen.findByText('Movement reversed.')).toBeInTheDocument()
    expect(onChanged).toHaveBeenCalled()
    expect(searchStockMovements).toHaveBeenCalledTimes(2)
  })

  it('offers no reversal on a reversal', async () => {
    vi.mocked(searchStockMovements).mockResolvedValue(
      paged([movement({ id: 'm2', movementType: 'Reversal', quantityChange: -10 })]),
    )
    renderHistory()

    await screen.findByRole('table', { name: 'Stock movements' })
    expect(screen.queryByRole('button', { name: 'Reverse' })).not.toBeInTheDocument()
  })

  it('keeps the reverse action from the receptionist', async () => {
    renderHistory(false)

    const table = await screen.findByRole('table', { name: 'Stock movements' })
    expect(within(table).queryByRole('columnheader', { name: 'Actions' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Reverse' })).not.toBeInTheDocument()
  })

  it('filters the trail by movement type', async () => {
    const user = userEvent.setup()
    renderHistory()

    await user.selectOptions(await screen.findByLabelText('Type'), 'RetailSale')

    expect(searchStockMovements).toHaveBeenNthCalledWith(2, 'p1', {
      type: 'RetailSale',
      page: undefined,
    })
  })

  it('states it plainly when the trail is empty', async () => {
    vi.mocked(searchStockMovements).mockResolvedValue(paged([]))
    renderHistory()

    expect(await screen.findByText('No stock movements recorded yet.')).toBeInTheDocument()
  })
})
