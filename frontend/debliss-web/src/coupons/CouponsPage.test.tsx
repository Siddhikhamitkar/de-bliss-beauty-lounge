import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  createCoupon,
  getCoupon,
  searchCoupons,
  setCouponStatus,
  updateCoupon,
} from '../api/billing.ts'
import { ApiError } from '../api/client.ts'
import type { Coupon, PagedResult } from '../api/contracts.ts'
import { formatDate } from '../format.ts'
import { CouponsPage } from './CouponsPage.tsx'

vi.mock('../api/billing.ts', () => ({
  searchInvoices: vi.fn(),
  getInvoice: vi.fn(),
  startInvoice: vi.fn(),
  checkoutInvoice: vi.fn(),
  getReceipt: vi.fn(),
  validateCoupon: vi.fn(),
  searchCoupons: vi.fn(),
  getCoupon: vi.fn(),
  createCoupon: vi.fn(),
  updateCoupon: vi.fn(),
  setCouponStatus: vi.fn(),
}))

function paged(items: Coupon[], overrides: Partial<PagedResult<Coupon>> = {}): PagedResult<Coupon> {
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

function coupon(overrides: Partial<Coupon> = {}): Coupon {
  return {
    id: 'cp1',
    code: 'BLISS20',
    discountType: 'Percentage',
    discountValue: 20,
    isActive: true,
    validFrom: '2026-09-01',
    validUntil: '2026-12-31',
    createdAt: '2026-09-01T08:00:00Z',
    updatedAt: null,
    ...overrides,
  }
}

describe('CouponsPage', () => {
  beforeEach(() => {
    vi.mocked(searchCoupons).mockResolvedValue(paged([coupon()]))
    vi.mocked(getCoupon).mockResolvedValue(coupon())
    vi.mocked(createCoupon).mockResolvedValue(coupon({ id: 'cp2', code: 'WELCOME10', discountType: 'Fixed', discountValue: 10 }))
    vi.mocked(updateCoupon).mockResolvedValue(coupon())
    vi.mocked(setCouponStatus).mockResolvedValue(coupon({ isActive: false }))
  })

  it('shows a loading state before the first answer', () => {
    vi.mocked(searchCoupons).mockReturnValue(new Promise(() => {}))

    render(<CouponsPage />)

    expect(screen.getByRole('status')).toHaveTextContent('Loading coupons…')
  })

  it('lists the coupons the API returns', async () => {
    render(<CouponsPage />)

    expect(await screen.findByText('BLISS20')).toBeInTheDocument()
    expect(screen.getByText('20.00%')).toBeInTheDocument()
    expect(screen.getByText('Percentage')).toBeInTheDocument()
    expect(screen.getByText(formatDate('2026-09-01'))).toBeInTheDocument()
    expect(screen.getByText(formatDate('2026-12-31'))).toBeInTheDocument()
    expect(screen.getByText('Active', { selector: 'span.pill' })).toBeInTheDocument()
  })

  it('shows the empty state when the filter matches nothing', async () => {
    vi.mocked(searchCoupons).mockResolvedValue(paged([]))

    render(<CouponsPage />)

    expect(await screen.findByText('No coupons in this view.')).toBeInTheDocument()
  })

  it('filters by status', async () => {
    const user = userEvent.setup()
    render(<CouponsPage />)
    await screen.findByText('BLISS20')

    await user.selectOptions(screen.getByLabelText('Status'), 'Active')

    expect(searchCoupons).toHaveBeenNthCalledWith(1, { status: undefined, page: undefined })
    expect(searchCoupons).toHaveBeenNthCalledWith(2, { status: 'Active', page: undefined })
  })

  it('deactivates and reactivates a coupon without deleting it', async () => {
    const user = userEvent.setup()
    render(<CouponsPage />)
    await screen.findByText('BLISS20')

    vi.mocked(searchCoupons).mockResolvedValue(paged([coupon({ isActive: false })]))
    await user.click(screen.getByRole('button', { name: 'Deactivate' }))

    expect(setCouponStatus).toHaveBeenCalledExactlyOnceWith('cp1', false)

    expect(await screen.findByRole('button', { name: 'Activate' })).toBeInTheDocument()
    expect(screen.getByText('Inactive', { selector: 'span.pill' })).toBeInTheDocument()
  })

  it('surfaces a failed status toggle without losing the list', async () => {
    const user = userEvent.setup()
    vi.mocked(setCouponStatus).mockRejectedValue(new Error('nope'))

    render(<CouponsPage />)
    await screen.findByText('BLISS20')

    await user.click(screen.getByRole('button', { name: 'Deactivate' }))

    expect(await screen.findByText('nope')).toBeInTheDocument()
    expect(screen.getByText('BLISS20')).toBeInTheDocument()
  })

  it('creates a coupon from the form and returns to the list', async () => {
    const user = userEvent.setup()
    render(<CouponsPage />)
    await screen.findByText('BLISS20')

    await user.click(screen.getByRole('button', { name: 'New coupon' }))
    await user.type(screen.getByLabelText('Code'), 'welcome10')
    await user.type(screen.getByLabelText('Discount value'), '10')
    await user.click(screen.getByRole('button', { name: 'Save coupon' }))

    expect(createCoupon).toHaveBeenCalledExactlyOnceWith({
      code: 'welcome10',
      discountType: 'Fixed',
      discountValue: 10,
      validFrom: null,
      validUntil: null,
      isActive: true,
    })
    // Back on the list, reloaded.
    expect(await screen.findByRole('button', { name: 'New coupon' })).toBeInTheDocument()
    expect(searchCoupons).toHaveBeenCalledTimes(2)
  })

  it('refuses to save a coupon without a discount value', async () => {
    const user = userEvent.setup()
    render(<CouponsPage />)
    await screen.findByText('BLISS20')

    await user.click(screen.getByRole('button', { name: 'New coupon' }))
    await user.type(screen.getByLabelText('Code'), 'EMPTY')
    await user.click(screen.getByRole('button', { name: 'Save coupon' }))

    expect(await screen.findByText('Enter a discount value.')).toBeInTheDocument()
    expect(createCoupon).not.toHaveBeenCalled()
  })

  it('shows backend field errors when a save is rejected', async () => {
    const user = userEvent.setup()
    vi.mocked(createCoupon).mockRejectedValue(
      new ApiError(400, { errors: { code: ['Coupon code is required.'] } }),
    )

    render(<CouponsPage />)
    await screen.findByText('BLISS20')

    await user.click(screen.getByRole('button', { name: 'New coupon' }))
    await user.type(screen.getByLabelText('Discount value'), '10')
    await user.click(screen.getByRole('button', { name: 'Save coupon' }))

    expect(await screen.findByText('Coupon code is required.')).toBeInTheDocument()
  })

  it('edits an existing coupon from the form', async () => {
    const user = userEvent.setup()
    render(<CouponsPage />)
    await screen.findByText('BLISS20')

    await user.click(screen.getByRole('button', { name: 'Edit' }))

    expect(await screen.findByRole('heading', { name: 'Edit BLISS20' })).toBeInTheDocument()
    expect(getCoupon).toHaveBeenCalledExactlyOnceWith('cp1')

    const value = screen.getByLabelText('Discount value')
    await user.clear(value)
    await user.type(value, '15')
    await user.click(screen.getByRole('button', { name: 'Save coupon' }))

    expect(updateCoupon).toHaveBeenCalledExactlyOnceWith('cp1', {
      code: 'BLISS20',
      discountType: 'Percentage',
      discountValue: 15,
      validFrom: '2026-09-01',
      validUntil: '2026-12-31',
      isActive: true,
    })
  })
})
