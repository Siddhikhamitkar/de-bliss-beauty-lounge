import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  addInvoiceLine,
  checkoutInvoice,
  getCheckoutPreview,
  getInvoice,
  getReceipt,
  removeInvoiceLine,
  setInvoiceTip,
  startInvoice,
  updateInvoiceLine,
  validateCoupon,
} from '../api/billing.ts'
import { getTaxSettings } from '../api/settings.ts'
import { getSellableProducts } from '../api/inventory.ts'
import { ApiError } from '../api/client.ts'
import type {
  CheckoutPreview,
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
  setInvoiceTip: vi.fn(),
  checkoutInvoice: vi.fn(),
  getCheckoutPreview: vi.fn(),
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

vi.mock('../api/settings.ts', () => ({
  getTaxSettings: vi.fn(),
  updateTaxSettings: vi.fn(),
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
    // Existing arithmetic tests exercise unlinked (no-loyalty) checkout.
    // Linked-customer quote tests below opt in explicitly.
    customerId: null,
    customerName: 'Lina Mansour',
    walkInCustomerName: null,
    couponId: null,
    couponCode: null,
    subTotal: 200,
    bundleDiscount: 0,
    couponDiscount: 0,
    manualDiscount: 0,
    loyaltyDiscount: 0,
    loyaltyPointsRedeemed: 0,
    loyaltyPointsEarned: 0,
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
    loyaltyDiscount: 0,
    loyaltyPointsRedeemed: 0,
    loyaltyPointsEarned: 0,
    totalAmount: 200,
    currency: 'AED',
    paymentMethod: 'Cash',
    paymentReference: null,
    paidAt: '2026-10-04T10:30:00Z',
    issuedAt: '2026-10-04T10:00:00Z',
    ...overrides,
  }
}

function preview(points = 0, overrides: Partial<CheckoutPreview> = {}): CheckoutPreview {
  return {
    subTotal: 200, bundleDiscount: 0, couponDiscount: 0, manualDiscount: 0,
    taxRate: 5, pricesIncludeTax: false, taxTotal: (200 - points) * 0.05,
    tipAmount: 0, totalAmount: (200 - points) * 1.05,
    loyalty: {
      isEnabled: true, availablePoints: 80, availableValue: 80, pointValueAed: 1,
      maximumRedeemablePoints: 80, maximumDiscount: 80, minimumRedemptionPoints: 10,
      maximumRedemptionPercentage: 50, allowPartialRedemption: true, minimumInvoiceAmount: null,
      unavailableReason: null, pointsToRedeem: points, discount: points, eligibleSpend: 200 - points,
      pointsEarned: (200 - points) * 0.05,
    },
    ...overrides,
  }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}

function renderCheckout() {
  const onClose = vi.fn()
  const onPaid = vi.fn()
  render(<CheckoutPage appointmentId="a1" onClose={onClose} onPaid={onPaid} />)
  return { onClose, onPaid }
}

describe('CheckoutPage', () => {
  beforeEach(() => {
    // Zero VAT keeps the legacy assertions — pre-tax amounts — reading exactly
    // what they are about; the VAT-specific tests resolve their own rate.
    vi.mocked(getTaxSettings).mockResolvedValue({ taxRate: 0, pricesIncludeTax: false })
    vi.mocked(startInvoice).mockResolvedValue(invoice())
    vi.mocked(getInvoice).mockResolvedValue(invoice())
    vi.mocked(addInvoiceLine).mockResolvedValue(invoice())
    vi.mocked(updateInvoiceLine).mockResolvedValue(invoice())
    vi.mocked(removeInvoiceLine).mockResolvedValue(invoice())
    vi.mocked(setInvoiceTip).mockResolvedValue(invoice())
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

  it('shows the VAT breakdown and adds a percentage tip on the post-VAT total', async () => {
    const user = userEvent.setup()
    vi.mocked(getTaxSettings).mockResolvedValue({ taxRate: 5, pricesIncludeTax: false })

    renderCheckout()
    await screen.findByText('Balayage')

    expect(screen.getByText('Total before VAT')).toBeInTheDocument()
    expect(screen.getByText('VAT (5%)')).toBeInTheDocument()
    expect(screen.getByText('+AED 10.00')).toBeInTheDocument()
    expect(screen.getByText('AED 210.00')).toBeInTheDocument()
    expect(screen.getByLabelText('Amount collected')).toHaveValue(210)

    await user.click(screen.getByRole('radio', { name: '10%' }))

    // The percentage rides on the post-VAT amount — 10% of 210.00, never of 200.00.
    expect(screen.getByText('Tip (10%)')).toBeInTheDocument()
    expect(screen.getByText('+AED 21.00')).toBeInTheDocument()
    expect(screen.getByText('10% of the pre-tip total (AED 210.00)')).toBeInTheDocument()
    expect(screen.getByText('AED 231.00')).toBeInTheDocument()
    expect(screen.getByLabelText('Amount collected')).toHaveValue(231)
    expect(setInvoiceTip).toHaveBeenCalledExactlyOnceWith('inv1', {
      tipPercentage: 10,
      tipAmount: null,
    })

    await user.click(screen.getByRole('button', { name: 'Complete Payment' }))

    expect(checkoutInvoice).toHaveBeenCalledExactlyOnceWith('inv1', {
      couponCode: null,
      manualDiscountType: null,
      manualDiscountValue: null,
      paymentMethod: 'Cash',
      amount: 231,
      reference: null,
      tipPercentage: 10,
      tipAmount: null,
    })
  })

  it('extracts VAT from tax-inclusive prices instead of adding it twice', async () => {
    vi.mocked(getTaxSettings).mockResolvedValue({ taxRate: 5, pricesIncludeTax: true })

    renderCheckout()
    await screen.findByText('Balayage')

    expect(screen.getByText('Total (VAT included)')).toBeInTheDocument()
    expect(screen.getByText('Includes VAT (5%)')).toBeInTheDocument()
    expect(screen.getByText('AED 9.52')).toBeInTheDocument()
    // The payable stays the quoted figure: VAT is inside it, never added again.
    expect(screen.getByLabelText('Amount collected')).toHaveValue(200)
  })

  it('keys a custom tip amount, commits it on blur and sends it at checkout', async () => {
    const user = userEvent.setup()
    renderCheckout()
    await screen.findByText('Balayage')

    await user.click(screen.getByRole('radio', { name: 'Custom' }))
    await user.type(screen.getByLabelText('Custom tip amount'), '15')
    await user.tab()

    expect(screen.getByText('+AED 15.00')).toBeInTheDocument()
    expect(screen.getByText('AED 215.00')).toBeInTheDocument()
    expect(screen.getByLabelText('Amount collected')).toHaveValue(215)
    expect(setInvoiceTip).toHaveBeenLastCalledWith('inv1', {
      tipPercentage: null,
      tipAmount: 15,
    })

    await user.click(screen.getByRole('button', { name: 'Complete Payment' }))

    expect(checkoutInvoice).toHaveBeenCalledExactlyOnceWith('inv1', {
      couponCode: null,
      manualDiscountType: null,
      manualDiscountValue: null,
      paymentMethod: 'Cash',
      amount: 215,
      reference: null,
      tipPercentage: null,
      tipAmount: 15,
    })
  })

  it('restores a tip saved on the draft when the checkout resumes', async () => {
    vi.mocked(startInvoice).mockResolvedValue(invoice({ tipPercentage: 10, tipAmount: 0 }))

    renderCheckout()

    expect(await screen.findByRole('radio', { name: '10%' })).toBeChecked()
    expect(screen.getByText('Tip (10%)')).toBeInTheDocument()
    expect(screen.getByText('+AED 20.00')).toBeInTheDocument()
    expect(screen.getByText('AED 220.00')).toBeInTheDocument()
    expect(screen.getByLabelText('Amount collected')).toHaveValue(220)
    // The saved choice is a fact to restore, not a change to re-save.
    expect(setInvoiceTip).not.toHaveBeenCalled()
  })

  it('clears the tip and restores the untipped payable', async () => {
    const user = userEvent.setup()
    renderCheckout()
    await screen.findByText('Balayage')

    await user.click(screen.getByRole('radio', { name: '10%' }))
    expect(screen.getByText('AED 220.00')).toBeInTheDocument()

    await user.click(screen.getByRole('radio', { name: 'No tip' }))

    expect(screen.queryByText('Tip (10%)')).not.toBeInTheDocument()
    expect(screen.getAllByText('AED 200.00')).toHaveLength(2)
    expect(screen.getByLabelText('Amount collected')).toHaveValue(200)
    expect(setInvoiceTip).toHaveBeenLastCalledWith('inv1', {
      tipPercentage: null,
      tipAmount: null,
    })
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
      tipPercentage: null,
      tipAmount: null,
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
      tipPercentage: null,
      tipAmount: null,
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
      invoice({
        appointmentId: null,
        customerId: null,
        customerName: null,
        walkInCustomerName: 'Maya',
        items: [],
      }),
    )

    render(<CheckoutPage invoiceId="inv1" onClose={vi.fn()} onPaid={vi.fn()} />)

    expect(
      await screen.findByText('No items yet — add a retail product below.'),
    ).toBeInTheDocument()
    expect(getInvoice).toHaveBeenCalledExactlyOnceWith('inv1')
    expect(startInvoice).not.toHaveBeenCalled()
    expect(screen.getByText('INV-2026-0001 · Maya')).toBeInTheDocument()
  })

  it('falls back to the generic label for an unnamed walk-in draft', async () => {
    vi.mocked(getInvoice).mockResolvedValue(
      invoice({ appointmentId: null, customerId: null, customerName: null, items: [] }),
    )

    render(<CheckoutPage invoiceId="inv1" onClose={vi.fn()} onPaid={vi.fn()} />)

    expect(await screen.findByText('INV-2026-0001 · Walk-in customer')).toBeInTheDocument()
    expect(screen.getByText(/Link a registered customer/)).toBeInTheDocument()
    expect(getCheckoutPreview).not.toHaveBeenCalled()
  })

  describe('linked-customer loyalty quotes', () => {
    beforeEach(() => {
      vi.mocked(startInvoice).mockResolvedValue(invoice({ customerId: 'c1' }))
      vi.mocked(getCheckoutPreview).mockImplementation(async (_id, request) => preview(request.loyaltyPointsToRedeem ?? 0))
    })

    it('loads a zero-point quote even when disabled and uses server VAT and payable', async () => {
      vi.mocked(getCheckoutPreview).mockResolvedValue(preview(0, { loyalty: { ...preview().loyalty, isEnabled: false, unavailableReason: 'Program disabled.', pointsEarned: 0 } }))
      renderCheckout()
      expect(await screen.findByText('Loyalty program is disabled.')).toBeInTheDocument()
      expect(screen.getByLabelText('Amount collected')).toHaveValue(210)
      expect(screen.getByText('+AED 10.00')).toBeInTheDocument()
      expect(getCheckoutPreview).toHaveBeenCalledWith('inv1', expect.objectContaining({ loyaltyPointsToRedeem: 0 }))
      expect(screen.getByRole('button', { name: 'Complete Payment' })).toBeEnabled()
    })

    it('quotes selected points, tracks amount, sends redemption and prints points on receipt', async () => {
      const user = userEvent.setup()
      vi.mocked(checkoutInvoice).mockResolvedValue(receipt({ totalAmount: 189, loyaltyDiscount: 20, loyaltyPointsRedeemed: 20, loyaltyPointsEarned: 9, taxRate: 5, taxTotal: 9 }))
      renderCheckout()
      const input = await screen.findByLabelText('Loyalty points to redeem')
      fireEvent.change(input, { target: { value: '20' } })
      await waitFor(() => expect(screen.getByLabelText('Amount collected')).toHaveValue(189))
      expect(getCheckoutPreview).toHaveBeenLastCalledWith('inv1', expect.objectContaining({ loyaltyPointsToRedeem: 20 }))
      expect(within(screen.getByRole('region', { name: 'Checkout loyalty' })).getByText('−AED 20.00')).toBeInTheDocument()
      await user.click(screen.getByRole('button', { name: 'Complete Payment' }))
      expect(checkoutInvoice).toHaveBeenCalledWith('inv1', expect.objectContaining({ loyaltyPointsToRedeem: 20, amount: 189 }))
      const printed = await screen.findByRole('article', { name: 'Receipt INV-2026-0001' })
      expect(within(printed).getByText('Loyalty points earned').nextElementSibling).toHaveTextContent('9.00')
      expect(within(printed).getByText('Loyalty points redeemed').nextElementSibling).toHaveTextContent('20.00')
    })

    it('keeps a manually overridden amount when the quote changes', async () => {
      renderCheckout()
      await screen.findByLabelText('Loyalty points to redeem')
      fireEvent.change(screen.getByLabelText('Amount collected'), { target: { value: '199' } })
      fireEvent.change(screen.getByLabelText('Loyalty points to redeem'), { target: { value: '20' } })
      await screen.findByText('AED 189.00')
      expect(screen.getByLabelText('Amount collected')).toHaveValue(199)
    })

    it('never applies an older requested quote after manual discount changes', async () => {
      const stale = deferred<CheckoutPreview>()
      const latest = deferred<CheckoutPreview>()
      vi.mocked(getCheckoutPreview).mockImplementation(async (_id, request) => {
        if (request.manualDiscountValue === 10) return latest.promise
        return request.loyaltyPointsToRedeem === 20 ? stale.promise : preview()
      })
      renderCheckout()
      fireEvent.change(await screen.findByLabelText('Loyalty points to redeem'), { target: { value: '20' } })
      await waitFor(() => expect(getCheckoutPreview).toHaveBeenLastCalledWith('inv1', expect.objectContaining({ loyaltyPointsToRedeem: 20 })))
      expect(screen.getByRole('button', { name: 'Complete Payment' })).toBeDisabled()
      fireEvent.change(screen.getByLabelText('Discount value'), { target: { value: '10' } })
      await act(async () => stale.resolve(preview(20)))
      expect(screen.getByLabelText('Amount collected')).toHaveValue(null)
      expect(screen.queryByText('AED 189.00')).not.toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Complete Payment' })).toBeDisabled()
      await act(async () => latest.resolve(preview(20, { manualDiscount: 10, taxTotal: 8.5, totalAmount: 178.5 })))
      await waitFor(() => expect(screen.getByLabelText('Amount collected')).toHaveValue(178.5))
    })

    it('ignores a stale selection response after points change', async () => {
      const stale = deferred<CheckoutPreview>()
      vi.mocked(getCheckoutPreview).mockImplementation(async (_id, request) => request.loyaltyPointsToRedeem === 20 ? stale.promise : preview(request.loyaltyPointsToRedeem ?? 0))
      renderCheckout()
      fireEvent.change(await screen.findByLabelText('Loyalty points to redeem'), { target: { value: '20' } })
      await waitFor(() => expect(getCheckoutPreview).toHaveBeenLastCalledWith('inv1', expect.objectContaining({ loyaltyPointsToRedeem: 20 })))
      fireEvent.change(screen.getByLabelText('Loyalty points to redeem'), { target: { value: '30' } })
      await waitFor(() => expect(screen.getByLabelText('Amount collected')).toHaveValue(178.5))
      await act(async () => stale.resolve(preview(20)))
      expect(screen.getByLabelText('Amount collected')).toHaveValue(178.5)
    })

    it('does not resurrect an old quote when the selection returns to its initial value', async () => {
      const pending = deferred<CheckoutPreview>()
      renderCheckout()
      await screen.findByLabelText('Loyalty points to redeem')
      vi.mocked(getCheckoutPreview).mockReturnValue(pending.promise)
      fireEvent.change(screen.getByLabelText('Loyalty points to redeem'), { target: { value: '20' } })
      fireEvent.change(screen.getByLabelText('Loyalty points to redeem'), { target: { value: '0' } })
      expect(screen.getByRole('button', { name: 'Complete Payment' })).toBeDisabled()
      expect(screen.getByLabelText('Amount collected')).toHaveValue(null)
      await act(async () => pending.resolve(preview()))
      await waitFor(() => expect(screen.getByRole('button', { name: 'Complete Payment' })).toBeEnabled())
    })

    it.each(['tip', 'coupon'])('ignores stale redemption after a %s edit and requotes the new request', async kind => {
      const stale = deferred<CheckoutPreview>()
      vi.mocked(getCheckoutPreview).mockImplementation(async (_id, request) => {
        if (request.tipPercentage === 10 || request.couponCode === 'BLISS20') return preview(request.loyaltyPointsToRedeem ?? 0, { totalAmount: 175, tipAmount: kind === 'tip' ? 17 : 0, couponDiscount: kind === 'coupon' ? 40 : 0 })
        return request.loyaltyPointsToRedeem === 20 ? stale.promise : preview()
      })
      renderCheckout()
      fireEvent.change(await screen.findByLabelText('Loyalty points to redeem'), { target: { value: '20' } })
      await waitFor(() => expect(getCheckoutPreview).toHaveBeenLastCalledWith('inv1', expect.objectContaining({ loyaltyPointsToRedeem: 20 })))
      const user = userEvent.setup()
      if (kind === 'tip') await user.click(screen.getByRole('radio', { name: '10%' }))
      else {
        fireEvent.change(screen.getByPlaceholderText('e.g. BLISS20'), { target: { value: 'BLISS20' } })
        await user.click(screen.getByRole('button', { name: 'Apply' }))
      }
      await waitFor(() => expect(screen.getByLabelText('Amount collected')).toHaveValue(175))
      await act(async () => stale.resolve(preview(20)))
      expect(screen.getByLabelText('Amount collected')).toHaveValue(175)
      expect(getCheckoutPreview).toHaveBeenLastCalledWith('inv1', expect.objectContaining(kind === 'tip' ? { tipPercentage: 10 } : { couponCode: 'BLISS20' }))
    })

    it('blocks quotes throughout a line edit and invoice reload, then uses the new snapshot', async () => {
      const refreshed = deferred<Invoice>()
      const first = invoice({ customerId: 'c1', items: [productLine()] })
      vi.mocked(startInvoice).mockResolvedValueOnce(first).mockReturnValueOnce(refreshed.promise)
      renderCheckout()
      await screen.findByLabelText('Loyalty points to redeem')
      const quantity = screen.getByLabelText('Quantity of Olaplex No. 3')
      fireEvent.change(quantity, { target: { value: '2' } })
      expect(screen.getByRole('button', { name: 'Complete Payment' })).toBeDisabled()
      fireEvent.blur(quantity)
      await waitFor(() => expect(startInvoice).toHaveBeenCalledTimes(2))
      expect(screen.getByRole('button', { name: 'Complete Payment' })).toBeDisabled()
      expect(screen.getByLabelText('Amount collected')).toHaveValue(null)
      vi.mocked(getCheckoutPreview).mockResolvedValue(preview(0, { subTotal: 190, totalAmount: 199.5 }))
      await act(async () => refreshed.resolve({ ...first, subTotal: 190, items: [productLine({ quantity: 2, lineTotal: 190 })] }))
      await waitFor(() => expect(screen.getByLabelText('Amount collected')).toHaveValue(199.5))
      expect(screen.getByRole('button', { name: 'Complete Payment' })).toBeEnabled()
    })

    it('blocks failed base quotes until an explicit retry succeeds', async () => {
      vi.mocked(getCheckoutPreview).mockRejectedValueOnce(new Error('Quote unavailable'))
      renderCheckout()
      expect(await screen.findByText(/Checkout quote failed: Quote unavailable/)).toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Complete Payment' })).toBeDisabled()
      await userEvent.setup().click(screen.getByRole('button', { name: 'Try again' }))
      await waitFor(() => expect(screen.getByLabelText('Amount collected')).toHaveValue(210))
      expect(screen.getByRole('button', { name: 'Complete Payment' })).toBeEnabled()
    })

    it('requires invalid redemption to be removed rather than silently collecting a stale total', async () => {
      const user = userEvent.setup()
      vi.mocked(getCheckoutPreview).mockImplementation(async (_id, request) => {
        if ((request.loyaltyPointsToRedeem ?? 0) > 0) throw new ApiError(400, { errors: { loyaltyPointsToRedeem: ['Balance changed: not enough points.'] } })
        return preview()
      })
      renderCheckout()
      fireEvent.change(await screen.findByLabelText('Loyalty points to redeem'), { target: { value: '20' } })
      expect(await screen.findByText(/Checkout quote failed: Balance changed/)).toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Complete Payment' })).toBeDisabled()
      expect(screen.getByLabelText('Amount collected')).toHaveValue(null)
      await user.click(screen.getByRole('button', { name: 'Remove loyalty redemption' }))
      await waitFor(() => expect(screen.getByLabelText('Amount collected')).toHaveValue(210))
      expect(screen.getByRole('button', { name: 'Complete Payment' })).toBeEnabled()
    })

    it('offers a full balance button, never partial input, when partial redemption is off', async () => {
      vi.mocked(getCheckoutPreview).mockImplementation(async (_id, request) => {
        const result = preview(request.loyaltyPointsToRedeem ?? 0)
        return { ...result, loyalty: { ...result.loyalty, allowPartialRedemption: false } }
      })
      renderCheckout()
      await userEvent.setup().click(await screen.findByRole('button', { name: 'Redeem full balance' }))
      expect(screen.queryByLabelText('Loyalty points to redeem')).not.toBeInTheDocument()
      await waitFor(() => expect(screen.getByLabelText('Amount collected')).toHaveValue(126))
      expect(getCheckoutPreview).toHaveBeenLastCalledWith('inv1', expect.objectContaining({ loyaltyPointsToRedeem: 80 }))
    })

    it('refreshes the quote and clears redemption after a checkout conflict', async () => {
      const user = userEvent.setup()
      vi.mocked(checkoutInvoice).mockRejectedValue(new ApiError(409, { detail: 'The customer balance changed.' }))
      renderCheckout()
      fireEvent.change(await screen.findByLabelText('Loyalty points to redeem'), { target: { value: '20' } })
      await waitFor(() => expect(screen.getByLabelText('Amount collected')).toHaveValue(189))
      await user.click(screen.getByRole('button', { name: 'Complete Payment' }))
      expect(await screen.findByText(/Review the refreshed total and select loyalty points again/)).toBeInTheDocument()
      await waitFor(() => expect(screen.getByLabelText('Amount collected')).toHaveValue(210))
      expect(screen.getByLabelText('Loyalty points to redeem')).toHaveValue(0)
      expect(getCheckoutPreview).toHaveBeenLastCalledWith('inv1', expect.objectContaining({ loyaltyPointsToRedeem: 0 }))
    })
  })
})
