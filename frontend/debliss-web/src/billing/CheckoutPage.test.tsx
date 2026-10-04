import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { checkoutInvoice, getReceipt, startInvoice, validateCoupon } from '../api/billing.ts'
import { ApiError } from '../api/client.ts'
import type { Invoice, InvoiceItem, Receipt } from '../api/contracts.ts'
import { CheckoutPage } from './CheckoutPage.tsx'

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

function invoiceItem(overrides: Partial<InvoiceItem> = {}): InvoiceItem {
  return {
    id: 'i1',
    description: 'Balayage',
    quantity: 1,
    unitPrice: 150,
    lineTotal: 150,
    serviceId: 's1',
    appointmentServiceId: 'as1',
    sortOrder: 0,
    ...overrides,
  }
}

function invoice(overrides: Partial<Invoice> = {}): Invoice {
  return {
    id: 'inv1',
    invoiceNumber: 'INV-2026-0001',
    status: 'Draft',
    currency: 'AED',
    appointmentId: 'a1',
    customerId: 'c1',
    customerName: 'Lina Mansour',
    walkInCustomerName: null,
    couponId: null,
    couponCode: null,
    subTotal: 200,
    couponDiscount: 0,
    manualDiscount: 0,
    discountTotal: 0,
    totalAmount: 200,
    issuedAt: '2026-10-04T10:00:00Z',
    createdAt: '2026-10-04T10:00:00Z',
    updatedAt: null,
    items: [
      invoiceItem(),
      invoiceItem({ id: 'i2', description: 'Blow dry', unitPrice: 50, lineTotal: 50, sortOrder: 1 }),
    ],
    payments: [],
    ...overrides,
  }
}

function receipt(overrides: Partial<Receipt> = {}): Receipt {
  return {
    salonName: 'De Bliss Beauty Lounge',
    invoiceId: 'inv1',
    invoiceNumber: 'INV-2026-0001',
    customerName: 'Lina Mansour',
    items: [
      { description: 'Balayage', quantity: 1, unitPrice: 150, lineTotal: 150 },
      { description: 'Blow dry', quantity: 1, unitPrice: 50, lineTotal: 50 },
    ],
    subTotal: 200,
    couponCode: null,
    couponDiscount: 0,
    manualDiscount: 0,
    totalAmount: 200,
    currency: 'AED',
    paymentMethod: 'Cash',
    paymentReference: null,
    paidAt: '2026-10-04T10:30:00Z',
    issuedAt: '2026-10-04T10:00:00Z',
    ...overrides,
  }
}

function renderCheckout() {
  const onClose = vi.fn()
  const onPaid = vi.fn()
  render(<CheckoutPage appointmentId="a1" onClose={onClose} onPaid={onPaid} />)
  return { onClose, onPaid }
}

describe('CheckoutPage', () => {
  beforeEach(() => {
    vi.mocked(startInvoice).mockResolvedValue(invoice())
    vi.mocked(validateCoupon).mockResolvedValue({
      code: 'BLISS20',
      discountType: 'Percentage',
      discountValue: 20,
      discountAmount: 40,
    })
    vi.mocked(checkoutInvoice).mockResolvedValue(receipt())
    vi.mocked(getReceipt).mockResolvedValue(receipt())
  })

  it('loads the appointment services and totals without any re-entry', async () => {
    renderCheckout()

    expect(screen.getByRole('status')).toHaveTextContent('Loading checkout…')

    expect(await screen.findByText('Balayage')).toBeInTheDocument()
    expect(screen.getByText('Blow dry')).toBeInTheDocument()
    expect(screen.getByText('INV-2026-0001 · Lina Mansour')).toBeInTheDocument()
    // Subtotal and TOTAL both read AED 200.00 before any discount.
    expect(screen.getAllByText('AED 200.00')).toHaveLength(2)
    expect(startInvoice).toHaveBeenCalledExactlyOnceWith('a1')
    expect(screen.getByLabelText('Amount collected')).toHaveValue(200)
  })

  it('applies a coupon the server validates and repriced the total', async () => {
    const user = userEvent.setup()
    renderCheckout()
    await screen.findByText('Balayage')

    await user.type(screen.getByPlaceholderText('e.g. BLISS20'), 'bliss20')
    await user.click(screen.getByRole('button', { name: 'Apply' }))

    expect(await screen.findByText('BLISS20')).toBeInTheDocument()
    expect(validateCoupon).toHaveBeenCalledExactlyOnceWith({ code: 'bliss20', subtotal: 200 })
    expect(screen.getByText('−AED 40.00')).toBeInTheDocument()
    expect(screen.getByText('AED 160.00')).toBeInTheDocument()
    expect(screen.getByLabelText('Amount collected')).toHaveValue(160)
  })

  it('removes an applied coupon and restores the original total', async () => {
    const user = userEvent.setup()
    renderCheckout()
    await screen.findByText('Balayage')

    await user.type(screen.getByPlaceholderText('e.g. BLISS20'), 'BLISS20')
    await user.click(screen.getByRole('button', { name: 'Apply' }))
    await screen.findByText('BLISS20')

    await user.click(screen.getByRole('button', { name: 'Remove' }))

    expect(screen.getByPlaceholderText('e.g. BLISS20')).toBeInTheDocument()
    expect(screen.getAllByText('AED 200.00')).toHaveLength(2)
    expect(screen.getByLabelText('Amount collected')).toHaveValue(200)
  })

  it('shows the backend rejection for an invalid coupon without inventing detail', async () => {
    const user = userEvent.setup()
    vi.mocked(validateCoupon).mockRejectedValue(
      new ApiError(400, { errors: { code: ['Coupon code is invalid or expired.'] } }),
    )

    renderCheckout()
    await screen.findByText('Balayage')

    await user.type(screen.getByPlaceholderText('e.g. BLISS20'), 'NOPE')
    await user.click(screen.getByRole('button', { name: 'Apply' }))

    expect(
      await screen.findByText('Coupon code is invalid or expired.'),
    ).toBeInTheDocument()
    expect(screen.getByLabelText('Amount collected')).toHaveValue(200)
  })

  it('applies a fixed manual discount and reprises the total', async () => {
    const user = userEvent.setup()
    renderCheckout()
    await screen.findByText('Balayage')

    await user.type(screen.getByLabelText('Discount value'), '10')

    expect(screen.getByText('−AED 10.00')).toBeInTheDocument()
    expect(screen.getByText('AED 190.00')).toBeInTheDocument()
    expect(screen.getByLabelText('Amount collected')).toHaveValue(190)
  })

  it('applies a percentage manual discount against the subtotal', async () => {
    const user = userEvent.setup()
    renderCheckout()
    await screen.findByText('Balayage')

    await user.selectOptions(screen.getByLabelText('Discount type'), 'Percentage')
    await user.type(screen.getByLabelText('Discount value'), '10')

    expect(screen.getByText('−AED 20.00')).toBeInTheDocument()
    expect(screen.getByText('AED 180.00')).toBeInTheDocument()
    expect(screen.getByLabelText('Amount collected')).toHaveValue(180)
  })

  it('relabels the reference field to match the chosen payment method', async () => {
    const user = userEvent.setup()
    renderCheckout()
    await screen.findByText('Balayage')

    expect(screen.getByText('Reference (optional)')).toBeInTheDocument()

    await user.click(screen.getByRole('radio', { name: 'Bank Transfer' }))
    expect(screen.getByText('Transaction reference (optional)')).toBeInTheDocument()

    await user.click(screen.getByRole('radio', { name: 'Card' }))
    expect(screen.getByText('Receipt reference (optional)')).toBeInTheDocument()

    await user.click(screen.getByRole('radio', { name: 'Cash' }))
    expect(screen.getByText('Reference (optional)')).toBeInTheDocument()
  })

  it('completes a cash payment and shows the receipt', async () => {
    const user = userEvent.setup()
    const { onClose, onPaid } = renderCheckout()
    await screen.findByText('Balayage')

    await user.click(screen.getByRole('button', { name: 'Complete Payment' }))

    expect(checkoutInvoice).toHaveBeenCalledExactlyOnceWith('inv1', {
      couponCode: null,
      manualDiscountType: null,
      manualDiscountValue: null,
      paymentMethod: 'Cash',
      amount: 200,
      reference: null,
    })
    expect(onPaid).toHaveBeenCalled()

    const dialog = await screen.findByRole('dialog', { name: 'Receipt INV-2026-0001' })
    expect(dialog).toBeInTheDocument()
    expect(screen.getByText('De Bliss Beauty Lounge')).toBeInTheDocument()
    expect(screen.getByText('Receipt · INV-2026-0001')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Done' }))
    expect(onClose).toHaveBeenCalled()
  })

  it('sends the coupon, discount, bank transfer reference and collected amount together', async () => {
    const user = userEvent.setup()
    renderCheckout()
    await screen.findByText('Balayage')

    await user.type(screen.getByPlaceholderText('e.g. BLISS20'), 'BLISS20')
    await user.click(screen.getByRole('button', { name: 'Apply' }))
    await screen.findByText('BLISS20')

    await user.type(screen.getByLabelText('Discount value'), '10')
    await user.click(screen.getByRole('radio', { name: 'Bank Transfer' }))
    await user.type(screen.getByLabelText('Transaction reference (optional)'), 'TX-889')
    await user.click(screen.getByRole('button', { name: 'Complete Payment' }))

    expect(checkoutInvoice).toHaveBeenCalledExactlyOnceWith('inv1', {
      couponCode: 'BLISS20',
      manualDiscountType: 'Fixed',
      manualDiscountValue: 10,
      paymentMethod: 'BankTransfer',
      amount: 150,
      reference: 'TX-889',
    })
  })

  it('surfaces field errors the checkout API returns', async () => {
    const user = userEvent.setup()
    vi.mocked(checkoutInvoice).mockRejectedValue(
      new ApiError(400, { errors: { amount: ['The amount collected is required.'] } }),
    )

    renderCheckout()
    await screen.findByText('Balayage')

    await user.click(screen.getByRole('button', { name: 'Complete Payment' }))

    expect(await screen.findByText('The amount collected is required.')).toBeInTheDocument()
    expect(screen.queryByRole('dialog', { name: 'Receipt INV-2026-0001' })).not.toBeInTheDocument()
  })

  it('resuming a paid invoice shows its receipt instead of the checkout form', async () => {
    vi.mocked(startInvoice).mockResolvedValue(invoice({ status: 'Paid' }))

    renderCheckout()

    expect(
      await screen.findByRole('dialog', { name: 'Receipt INV-2026-0001' }),
    ).toBeInTheDocument()
    expect(getReceipt).toHaveBeenCalledExactlyOnceWith('inv1')
    expect(screen.queryByRole('button', { name: 'Complete Payment' })).not.toBeInTheDocument()
  })

  it('shows the load failure with a working retry', async () => {
    vi.mocked(startInvoice)
      .mockRejectedValueOnce(new Error('boom'))
      .mockResolvedValueOnce(invoice())

    renderCheckout()

    expect(await screen.findByText('boom')).toBeInTheDocument()

    await userEvent.setup().click(screen.getByRole('button', { name: 'Try again' }))

    expect(await screen.findByText('Balayage')).toBeInTheDocument()
  })
})
