import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { CustomerLoyaltySummary, LoyaltyTransaction, PagedResult } from '../api/contracts.ts'
import { getCustomerLoyalty, getLoyaltyTransactions } from '../api/loyalty.ts'
import { CustomerLoyalty } from './CustomerLoyalty.tsx'

vi.mock('../api/loyalty.ts', () => ({ getCustomerLoyalty: vi.fn(), getLoyaltyTransactions: vi.fn() }))
const summary: CustomerLoyaltySummary = { customerId: 'c1', availablePoints: 0, lifetimePointsEarned: 0, pointsRedeemed: 0, pointsDebt: 0, recentTransactions: [] }
const earn: LoyaltyTransaction = { id: 't1', customerId: 'c1', invoiceId: 'inv1', type: 'Earn', points: 5.25, occurredAt: '2026-10-01T12:00:00Z', description: 'Earned on checkout', eligibleSpend: 105, earningPercentage: 5, pointValueAed: 1, createdByUserId: 'u1', reversesTransactionId: null }
function paged(items: LoyaltyTransaction[], page = 1): PagedResult<LoyaltyTransaction> {
  return { items, page, pageSize: 50, totalCount: 51, totalPages: 2, hasPreviousPage: page > 1, hasNextPage: page < 2 }
}
beforeEach(() => { vi.mocked(getCustomerLoyalty).mockResolvedValue(summary) })

describe('customer loyalty', () => {
  it('shows an empty read-only summary without fetching owner settings or history', async () => {
    render(<CustomerLoyalty customerId="c1" />)
    expect(await screen.findByText('No loyalty transactions yet.')).toBeInTheDocument()
    for (const label of ['Available points', 'Lifetime points earned', 'Points redeemed', 'Points debt']) expect(screen.getByText(label).nextElementSibling).toHaveTextContent('0.00')
    expect(getCustomerLoyalty).toHaveBeenCalledExactlyOnceWith('c1')
    expect(getLoyaltyTransactions).not.toHaveBeenCalled()
    expect(screen.queryByRole('button', { name: /adjust/i })).not.toBeInTheDocument()
  })
  it('shows recent entries and paginates full history without requiring invoiceNumber', async () => {
    const user = userEvent.setup()
    vi.mocked(getCustomerLoyalty).mockResolvedValue({ ...summary, availablePoints: 3.25, lifetimePointsEarned: 5.25, pointsRedeemed: 2, pointsDebt: 1, recentTransactions: [earn] })
    vi.mocked(getLoyaltyTransactions).mockImplementation(async (_id, page) => page === 2 ? paged([{ ...earn, id: 't2', type: 'Reversal', points: -2, description: 'Reversed earning', reversesTransactionId: 't1' }], 2) : paged([earn]))
    render(<CustomerLoyalty customerId="c1" />)
    expect(await screen.findByText('Earned on checkout')).toBeInTheDocument()
    expect(screen.getByText('Invoice: inv1')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'View full history' }))
    expect(await screen.findByText('Page 1 of 2 · 51 transactions')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Next' }))
    expect(await screen.findByText('Reversed earning')).toBeInTheDocument()
    expect(within(screen.getByRole('table')).getByText('-2.00')).toBeInTheDocument()
    expect(getLoyaltyTransactions).toHaveBeenLastCalledWith('c1', 2, 50)
    expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled()
    await user.click(screen.getByRole('button', { name: 'Previous' }))
    expect(await screen.findByText('Earned on checkout')).toBeInTheDocument()
  })
  it('retries summary and history errors', async () => {
    const user = userEvent.setup()
    vi.mocked(getCustomerLoyalty).mockRejectedValueOnce(new Error('Summary unavailable'))
    vi.mocked(getLoyaltyTransactions).mockRejectedValueOnce(new Error('History unavailable')).mockResolvedValue(paged([]))
    render(<CustomerLoyalty customerId="c1" />)
    expect(await screen.findByText('Summary unavailable')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Try again' }))
    await user.click(await screen.findByRole('button', { name: 'View full history' }))
    expect(await screen.findByText('History unavailable')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Try again' }))
    expect(await screen.findByText('No loyalty transactions yet.')).toBeInTheDocument()
  })
})
