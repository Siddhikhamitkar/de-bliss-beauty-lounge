import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  addInvoiceLine,
  checkoutInvoice,
  getInvoice,
  getReceipt,
  removeInvoiceLine,
  startInvoice,
  updateInvoiceLine,
  validateCoupon,
} from '../api/billing.ts'
import { getSellableProducts } from '../api/inventory.ts'
import { ApiError } from '../api/client.ts'
import type {
  Invoice,
  InvoiceItem,
  InventoryProductOption,
  Receipt,
} from '../api/contracts.ts'
import { CheckoutPage } from './CheckoutPage.tsx'

vi.mock('../api/billing.ts', () => ({
  searchInvoices: vi.fn(),
  getInvoice: vi.fn(),
  startInvoice: vi.fn(),
  createRetailInvoice: vi.fn(),
  addInvoiceLine: vi.fn(),
  updateInvoiceLine: vi.fn(),
  removeInvoiceLine: vi.fn(),
  checkoutInvoice: vi.fn(),
  getReceipt: vi.fn(),
  validateCoupon: vi.fn(),
  searchCoupons: vi.fn(),
  getCoupon: vi.fn(),
  createCoupon: vi.fn(),
  updateCoupon: vi.fn(),
  setCouponStatus: vi.fn(),
}))

vi.mock('../api/inventory.ts', () => ({
  getSellableProducts: vi.fn(),
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
    productId: null,
    sortOrder: 0,
    ...overrides,
  }
}

/** A hand-added retail product line: no service behind it, a product instead. */
function productLine(overrides: Partial<InvoiceItem> = {}): InvoiceItem {
  return invoiceItem({
    id: 'l1',
    description: 'Olaplex No. 3',
    unitPrice: 95,
    lineTotal: 95,
    serviceId: null,
    appointmentServiceId: null,
    productId: 'p1',
    sortOrder: 2,
    ...overrides,
  })
}

function productOption(
  overrides: Partial<InventoryProductOption> = {},
): InventoryProductOption {
  return {
    id: 'p1',
    name: 'Olaplex No. 3',
    brand: 'Olaplex',
    sku: 'OLP-003',
    sellingPrice: 95,
    currentStock: 12,
    unitOfMeasure: 'bottle',
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
    bundleDiscount: 0,
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
    bundleDiscount: 0,
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
    vi.mocked(getInvoice).mockResolvedValue(invoice())
    vi.mocked(addInvoiceLine).mockResolvedValue(invoice())
    vi.mocked(updateInvoiceLine).mockResolvedValue(invoice())
    vi.mocked(removeInvoiceLine).mockResolvedValue(invoice())
    vi.mocked(getSellableProducts).mockResolvedValue([
      productOption(),
      productOption({
        id: 'p2',
        name: 'Hair clip',
        brand: null,
        sku: 'CLP-001',
        sellingPrice: 15,
        currentStock: 0,
      }),
    ])
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

  it('shows the bundle saving and prices a coupon against what is still owed', async () => {
    const user = userEvent.setup()
    // The two lines still add up to 200.00; the bundle saving is the 20.00 off it.
    vi.mocked(startInvoice).mockResolvedValue(
      invoice({ bundleName: 'Makeover', bundleDiscount: 20, discountTotal: 20, totalAmount: 180 }),
    )
    vi.mocked(validateCoupon).mockResolvedValue({
      code: 'BLISS20',
      discountType: 'Percentage',
      discountValue: 20,
      discountAmount: 36,
    })

    renderCheckout()
    await screen.findByText('Balayage')

    expect(screen.getByText('Bundle saving (Makeover)')).toBeInTheDocument()
    expect(screen.getByText('−AED 20.00')).toBeInTheDocument()

    await user.type(screen.getByPlaceholderText('e.g. BLISS20'), 'BLISS20')
    await user.click(screen.getByRole('button', { name: 'Apply' }))

    // 20% of the bundle-adjusted 180.00, not of the 200.00 subtotal.
    expect(validateCoupon).toHaveBeenCalledExactlyOnceWith({ code: 'BLISS20', subtotal: 180 })
    expect(await screen.findByText('BLISS20')).toBeInTheDocument()
    expect(screen.getByText('−AED 36.00')).toBeInTheDocument()
    expect(screen.getByText('AED 144.00')).toBeInTheDocument()
    expect(screen.getByLabelText('Amount collected')).toHaveValue(144)
  })

  it('prices a manual percentage discount on the bundle-adjusted amount', async () => {
    const user = userEvent.setup()
    vi.mocked(startInvoice).mockResolvedValue(
      invoice({ bundleName: 'Makeover', bundleDiscount: 20, totalAmount: 180 }),
    )

    renderCheckout()
    await screen.findByText('Balayage')

    await user.selectOptions(screen.getByLabelText('Discount type'), 'Percentage')
    await user.type(screen.getByLabelText('Discount value'), '10')

    expect(screen.getByText('−AED 18.00')).toBeInTheDocument()
    expect(screen.getByText('AED 162.00')).toBeInTheDocument()
    expect(screen.getByLabelText('Amount collected')).toHaveValue(162)
  })

  it('adds a retail product from the picker and shows the priced line', async () => {
    const user = userEvent.setup()
    // The reload after adding the line brings back the bill with it priced.
    vi.mocked(startInvoice)
      .mockResolvedValueOnce(invoice())
      .mockResolvedValueOnce(
        invoice({
          subTotal: 390,
          totalAmount: 390,
          items: [
            invoiceItem(),
            invoiceItem({ id: 'i2', description: 'Blow dry', unitPrice: 50, lineTotal: 50, sortOrder: 1 }),
            productLine({ quantity: 2, lineTotal: 190 }),
          ],
        }),
      )

    renderCheckout()
    await screen.findByText('Balayage')

    await user.selectOptions(screen.getByLabelText('Retail product'), 'p1')
    await user.clear(screen.getByLabelText('Product quantity'))
    await user.type(screen.getByLabelText('Product quantity'), '2')
    await user.click(screen.getByRole('button', { name: 'Add' }))

    expect(addInvoiceLine).toHaveBeenCalledExactlyOnceWith('inv1', { productId: 'p1', quantity: 2 })
    expect(await screen.findByText('Olaplex No. 3')).toBeInTheDocument()
    expect(screen.getByLabelText('Quantity of Olaplex No. 3')).toHaveValue(2)
    expect(screen.getAllByText('AED 390.00')).toHaveLength(2)
    expect(screen.getByLabelText('Amount collected')).toHaveValue(390)
  })

  it('marks out-of-stock products unselectable in the picker', async () => {
    renderCheckout()
    await screen.findByText('Balayage')

    expect(screen.getByRole('option', { name: /Hair clip/ })).toBeDisabled()
    expect(screen.getByRole('option', { name: /Olaplex No\. 3/ })).toBeEnabled()
  })

  it('commits an edited product quantity on blur', async () => {
    const user = userEvent.setup()
    vi.mocked(startInvoice).mockResolvedValue(
      invoice({ subTotal: 95, totalAmount: 95, items: [productLine()] }),
    )

    renderCheckout()
    const quantity = await screen.findByLabelText('Quantity of Olaplex No. 3')
    expect(quantity).toHaveValue(1)

    await user.clear(quantity)
    await user.type(quantity, '3')
    await user.tab()

    expect(updateInvoiceLine).toHaveBeenCalledExactlyOnceWith('inv1', 'l1', { quantity: 3 })
  })

  it('reverts a quantity edit that parses to the number the server already has', async () => {
    const user = userEvent.setup()
    vi.mocked(startInvoice).mockResolvedValue(
      invoice({ subTotal: 95, totalAmount: 95, items: [productLine()] }),
    )

    renderCheckout()
    const quantity = await screen.findByLabelText('Quantity of Olaplex No. 3')

    await user.clear(quantity)
    await user.type(quantity, '1')
    await user.tab()

    expect(updateInvoiceLine).not.toHaveBeenCalled()
    expect(quantity).toHaveValue(1)
  })

  it('removes a product line while the booking services stay', async () => {
    const user = userEvent.setup()
    vi.mocked(startInvoice).mockResolvedValue(
      invoice({ subTotal: 245, totalAmount: 245, items: [invoiceItem(), productLine()] }),
    )

    renderCheckout()
    await screen.findByText('Balayage')

    await user.click(screen.getByRole('button', { name: 'Remove' }))

    expect(removeInvoiceLine).toHaveBeenCalledExactlyOnceWith('inv1', 'l1')
  })

  it('shows the first validation message when adding a product fails', async () => {
    const user = userEvent.setup()
    vi.mocked(addInvoiceLine).mockRejectedValue(
      new ApiError(400, { errors: { productId: ['Not enough stock on the shelf.'] } }),
    )

    renderCheckout()
    await screen.findByText('Balayage')

    await user.selectOptions(screen.getByLabelText('Retail product'), 'p1')
    await user.click(screen.getByRole('button', { name: 'Add' }))

    expect(await screen.findByText('Not enough stock on the shelf.')).toBeInTheDocument()
  })

  it('opens an existing walk-in draft by invoice id instead of an appointment', async () => {
    vi.mocked(getInvoice).mockResolvedValue(
      invoice({ appointmentId: null, customerId: null, customerName: null, items: [] }),
    )

    render(<CheckoutPage invoiceId="inv1" onClose={vi.fn()} onPaid={vi.fn()} />)

    expect(
      await screen.findByText('No items yet — add a retail product below.'),
    ).toBeInTheDocument()
    expect(getInvoice).toHaveBeenCalledExactlyOnceWith('inv1')
    expect(startInvoice).not.toHaveBeenCalled()
    expect(screen.getByText('INV-2026-0001 · Walk-in customer')).toBeInTheDocument()
  })
})
