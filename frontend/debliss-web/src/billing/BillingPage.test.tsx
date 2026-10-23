import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createRetailInvoice, getInvoice, getReceipt, searchInvoices } from '../api/billing.ts'
import type {
  Invoice,
  InvoiceItem,
  InvoiceSummary,
  PagedResult,
  Receipt,
} from '../api/contracts.ts'
import { BillingPage } from './BillingPage.tsx'
import { formatDateTime } from '../format.ts'

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

function paged(
  items: InvoiceSummary[],
  overrides: Partial<PagedResult<InvoiceSummary>> = {},
): PagedResult<InvoiceSummary> {
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

function summary(overrides: Partial<InvoiceSummary> = {}): InvoiceSummary {
  return {
    id: 'inv1',
    invoiceNumber: 'INV-2026-0001',
    customerId: 'c1',
    customerName: 'Lina Mansour',
    appointmentId: 'a1',
    status: 'Draft',
    currency: 'AED',
    subTotal: 200,
    discountTotal: 20,
    totalAmount: 180,
    paymentMethod: null,
    // A draft is not issued yet; the API sends null until checkout completes.
    issuedAt: null,
    createdAt: '2026-10-04T10:00:00Z',
    ...overrides,
  }
}

function item(overrides: Partial<InvoiceItem> = {}): InvoiceItem {
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

function detail(overrides: Partial<Invoice> = {}): Invoice {
  return {
    id: 'inv1',
    invoiceNumber: 'INV-2026-0001',
    status: 'Draft',
    currency: 'AED',
    appointmentId: 'a1',
    customerId: 'c1',
    customerName: 'Lina Mansour',
    walkInCustomerName: null,
    couponId: 'cp1',
    couponCode: 'BLISS20',
    subTotal: 200,
    bundleDiscount: 0,
    couponDiscount: 20,
    manualDiscount: 0,
    loyaltyDiscount: 0,
    loyaltyPointsRedeemed: 0,
    loyaltyPointsEarned: 0,
    discountTotal: 20,
    totalAmount: 180,
    issuedAt: '2026-10-04T10:00:00Z',
    createdAt: '2026-10-04T10:00:00Z',
    updatedAt: null,
    items: [
      item(),
      item({ id: 'i2', description: 'Blow dry', unitPrice: 50, lineTotal: 50, sortOrder: 1 }),
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
    couponCode: 'BLISS20',
    couponDiscount: 20,
    manualDiscount: 0,
    loyaltyDiscount: 0,
    loyaltyPointsRedeemed: 0,
    loyaltyPointsEarned: 0,
    totalAmount: 180,
    currency: 'AED',
    paymentMethod: 'Cash',
    paymentReference: null,
    paidAt: '2026-10-04T10:30:00Z',
    issuedAt: '2026-10-04T10:00:00Z',
    ...overrides,
  }
}

function renderPage(props: Partial<Parameters<typeof BillingPage>[0]> = {}) {
  const onResumeCheckout = vi.fn()
  const onStartRetailSale = vi.fn()
  const onManageCoupons = vi.fn()
  render(
    <BillingPage
      revision={0}
      onResumeCheckout={onResumeCheckout}
      onStartRetailSale={onStartRetailSale}
      onManageCoupons={onManageCoupons}
      {...props}
    />,
  )
  return { onResumeCheckout, onStartRetailSale, onManageCoupons }
}

describe('BillingPage', () => {
  beforeEach(() => {
    vi.mocked(searchInvoices).mockResolvedValue(paged([summary()]))
    vi.mocked(getInvoice).mockResolvedValue(detail())
    vi.mocked(getReceipt).mockResolvedValue(receipt())
    vi.mocked(createRetailInvoice).mockResolvedValue(
      detail({ appointmentId: null, customerName: 'Maya Walk-in' }),
    )
  })

  it('shows a loading state before the first answer', () => {
    vi.mocked(searchInvoices).mockReturnValue(new Promise(() => {}))

    renderPage()

    expect(screen.getByRole('status')).toHaveTextContent('Loading invoices…')
  })

  it('lists the invoices the API returns with subtotal, discount and total', async () => {
    renderPage()

    expect(await screen.findByText('Lina Mansour')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'INV-2026-0001' })).toBeInTheDocument()
    expect(screen.getByText('Draft', { selector: 'span.pill' })).toBeInTheDocument()
    expect(screen.getByText('AED 200.00')).toBeInTheDocument()
    expect(screen.getByText('−AED 20.00')).toBeInTheDocument()
    expect(screen.getByText('AED 180.00')).toBeInTheDocument()
    // Draft: neither payment method nor issue date exists yet — both show a dash.
    expect(screen.getAllByText('—')).toHaveLength(2)
  })

  it('shows the issue date once an invoice is issued', async () => {
    vi.mocked(searchInvoices).mockResolvedValue(
      paged([summary({ status: 'Paid', paymentMethod: 'Cash', issuedAt: '2026-10-04T10:00:00Z' })]),
    )

    renderPage()

    expect(await screen.findByText('Lina Mansour')).toBeInTheDocument()
    expect(screen.getByText(formatDateTime('2026-10-04T10:00:00Z'))).toBeInTheDocument()
  })

  it('shows the empty state when nothing matches the filters', async () => {
    vi.mocked(searchInvoices).mockResolvedValue(paged([]))

    renderPage()

    expect(
      await screen.findByText('No invoices match these filters.'),
    ).toBeInTheDocument()
  })

  it('shows the load failure with a working retry', async () => {
    vi.mocked(searchInvoices)
      .mockRejectedValueOnce(new Error('boom'))
      .mockResolvedValueOnce(paged([summary()]))

    renderPage()

    expect(await screen.findByText('boom')).toBeInTheDocument()

    await userEvent.setup().click(screen.getByRole('button', { name: 'Try again' }))

    expect(await screen.findByRole('button', { name: 'INV-2026-0001' })).toBeInTheDocument()
  })

  it('sends the search, status and payment method filters to the API', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('Lina Mansour')

    expect(searchInvoices).toHaveBeenNthCalledWith(1, {
      search: undefined,
      status: undefined,
      paymentMethod: undefined,
      page: undefined,
    })

    await user.type(screen.getByLabelText('Search'), 'INV-2026')
    await user.click(screen.getByRole('button', { name: 'Apply' }))

    expect(searchInvoices).toHaveBeenNthCalledWith(2, {
      search: 'INV-2026',
      status: undefined,
      paymentMethod: undefined,
      page: undefined,
    })

    await user.selectOptions(screen.getByLabelText('Status'), 'Draft')
    expect(searchInvoices).toHaveBeenNthCalledWith(3, {
      search: 'INV-2026',
      status: 'Draft',
      paymentMethod: undefined,
      page: undefined,
    })

    await user.selectOptions(screen.getByLabelText('Payment method'), 'BankTransfer')
    expect(searchInvoices).toHaveBeenNthCalledWith(4, {
      search: 'INV-2026',
      status: 'Draft',
      paymentMethod: 'BankTransfer',
      page: undefined,
    })
  })

  it('opens the invoice drawer from a row with its services and totals', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('Lina Mansour')

    await user.click(screen.getByRole('button', { name: 'INV-2026-0001' }))

    const drawer = await screen.findByRole('dialog', { name: 'Invoice INV-2026-0001' })
    expect(getInvoice).toHaveBeenCalledExactlyOnceWith('inv1')
    expect(await within(drawer).findByText('Balayage')).toBeInTheDocument()
    expect(within(drawer).getByText('Blow dry')).toBeInTheDocument()
    expect(within(drawer).getByText('BLISS20')).toBeInTheDocument()
    expect(within(drawer).getByText('−AED 20.00')).toBeInTheDocument()
    expect(within(drawer).getByText('AED 180.00')).toBeInTheDocument()
    expect(within(drawer).getByText('No payment recorded yet.')).toBeInTheDocument()
  })

  it('shows the bundle name and saving as their own rows in the drawer', async () => {
    const user = userEvent.setup()
    // No coupon here: on a bundled visit the 20.00 off is the bundle's saving.
    vi.mocked(getInvoice).mockResolvedValue(
      detail({
        couponId: null,
        couponCode: null,
        couponDiscount: 0,
        bundleName: 'Makeover',
        bundleDiscount: 20,
        discountTotal: 20,
        totalAmount: 180,
      }),
    )

    renderPage()
    await screen.findByText('Lina Mansour')

    await user.click(screen.getByRole('button', { name: 'INV-2026-0001' }))

    const drawer = await screen.findByRole('dialog', { name: 'Invoice INV-2026-0001' })
    expect(await within(drawer).findByText('Bundle', { selector: 'dt' })).toBeInTheDocument()
    expect(within(drawer).getByText('Makeover')).toBeInTheDocument()
    expect(within(drawer).getByText('Bundle saving', { selector: 'dt' })).toBeInTheDocument()
    expect(within(drawer).getByText('−AED 20.00')).toBeInTheDocument()
    expect(within(drawer).getByText('AED 180.00')).toBeInTheDocument()
    // The coupon row stays, showing that no code was applied.
    expect(within(drawer).getByText('—')).toBeInTheDocument()
  })

  it('shows loyalty breakdown without subtracting it twice from the stored invoice total', async () => {
    vi.mocked(getInvoice).mockResolvedValue(detail({ status: 'Paid', bundleDiscount: 10, couponDiscount: 20, manualDiscount: 5, loyaltyDiscount: 15, loyaltyPointsRedeemed: 15, loyaltyPointsEarned: 7.5, discountTotal: 50, taxRate: 5, pricesIncludeTax: false, taxTotal: 7.5, tipAmount: 10, totalAmount: 167.5 }))
    renderPage()
    await userEvent.setup().click(await screen.findByRole('button', { name: 'INV-2026-0001' }))
    const drawer = await screen.findByRole('dialog', { name: 'Invoice INV-2026-0001' })
    expect((await within(drawer).findByText('Loyalty discount')).nextElementSibling).toHaveTextContent('−AED 15.00')
    expect(within(drawer).getByText('Total before VAT').nextElementSibling).toHaveTextContent('AED 150.00')
    expect(within(drawer).getByText('Total', { selector: 'dt' }).nextElementSibling).toHaveTextContent('AED 167.50')
    expect(within(drawer).getByText('Loyalty points earned').nextElementSibling).toHaveTextContent('7.50')
  })

  it('resumes checkout for a draft invoice from its drawer', async () => {
    const user = userEvent.setup()
    const { onResumeCheckout } = renderPage()
    await screen.findByText('Lina Mansour')

    await user.click(screen.getByRole('button', { name: 'INV-2026-0001' }))
    await user.click(await screen.findByRole('button', { name: 'Resume checkout' }))

    expect(onResumeCheckout).toHaveBeenCalledExactlyOnceWith('inv1')
    expect(screen.queryByRole('dialog', { name: 'Invoice INV-2026-0001' })).not.toBeInTheDocument()
  })

  it('reprints the receipt for a paid invoice from its drawer', async () => {
    const user = userEvent.setup()
    vi.mocked(searchInvoices).mockResolvedValue(
      paged([summary({ status: 'Paid', paymentMethod: 'Cash' })]),
    )
    vi.mocked(getInvoice).mockResolvedValue(
      detail({
        status: 'Paid',
        payments: [
          {
            id: 'p1',
            amount: 180,
            currency: 'AED',
            method: 'Cash',
            status: 'Paid',
            reference: null,
            paidAt: '2026-10-04T10:30:00Z',
          },
        ],
      }),
    )

    renderPage()
    await screen.findByText('Lina Mansour')

    await user.click(screen.getByRole('button', { name: 'INV-2026-0001' }))
    await screen.findByText('Summary')

    expect(screen.queryByRole('button', { name: 'Resume checkout' })).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Print receipt' }))

    expect(
      await screen.findByRole('dialog', { name: 'Receipt INV-2026-0001' }),
    ).toBeInTheDocument()
    expect(getReceipt).toHaveBeenCalledExactlyOnceWith('inv1')
  })

  it('reprints a receipt that keeps the bundle saving as its own line', async () => {
    const user = userEvent.setup()
    vi.mocked(searchInvoices).mockResolvedValue(
      paged([summary({ status: 'Paid', paymentMethod: 'Cash' })]),
    )
    vi.mocked(getInvoice).mockResolvedValue(
      detail({
        status: 'Paid',
        couponId: null,
        couponCode: null,
        couponDiscount: 0,
        bundleName: 'Makeover',
        bundleDiscount: 20,
        totalAmount: 180,
        payments: [
          {
            id: 'p1',
            amount: 180,
            currency: 'AED',
            method: 'Cash',
            status: 'Paid',
            reference: null,
            paidAt: '2026-10-04T10:30:00Z',
          },
        ],
      }),
    )
    vi.mocked(getReceipt).mockResolvedValue(
      receipt({
        bundleName: 'Makeover',
        bundleDiscount: 20,
        couponCode: null,
        couponDiscount: 0,
        totalAmount: 180,
      }),
    )

    renderPage()
    await screen.findByText('Lina Mansour')

    await user.click(screen.getByRole('button', { name: 'INV-2026-0001' }))
    await screen.findByText('Summary')

    await user.click(screen.getByRole('button', { name: 'Print receipt' }))

    const receiptDialog = await screen.findByRole('dialog', { name: 'Receipt INV-2026-0001' })
    expect(within(receiptDialog).getByText('Bundle saving (Makeover)')).toBeInTheDocument()
    expect(within(receiptDialog).getByText('−AED 20.00')).toBeInTheDocument()
    expect(within(receiptDialog).getByText('AED 180.00')).toBeInTheDocument()
  })

  it('opens the coupons screen from the header', async () => {
    const user = userEvent.setup()
    const { onManageCoupons } = renderPage()
    await screen.findByText('Lina Mansour')

    await user.click(screen.getByRole('button', { name: 'Coupons' }))

    expect(onManageCoupons).toHaveBeenCalled()
  })

  it('starts a walk-in retail sale and opens its draft in checkout', async () => {
    const user = userEvent.setup()
    const { onStartRetailSale } = renderPage()
    await screen.findByText('Lina Mansour')

    await user.click(screen.getByRole('button', { name: 'New retail sale' }))
    await user.type(screen.getByLabelText('Walk-in customer name'), 'Maya')
    await user.click(screen.getByRole('button', { name: 'Start sale' }))

    expect(createRetailInvoice).toHaveBeenCalledExactlyOnceWith('Maya')
    expect(onStartRetailSale).toHaveBeenCalledExactlyOnceWith('inv1')
  })

  it('starts an unnamed retail sale — the walk-in name stays optional', async () => {
    const user = userEvent.setup()
    const { onStartRetailSale } = renderPage()
    await screen.findByText('Lina Mansour')

    await user.click(screen.getByRole('button', { name: 'New retail sale' }))
    await user.click(screen.getByRole('button', { name: 'Start sale' }))

    expect(createRetailInvoice).toHaveBeenCalledExactlyOnceWith('')
    expect(onStartRetailSale).toHaveBeenCalledExactlyOnceWith('inv1')
  })

  it('shows the failure when the retail draft cannot be created', async () => {
    const user = userEvent.setup()
    vi.mocked(createRetailInvoice).mockRejectedValue(new Error('no draft today'))
    const { onStartRetailSale } = renderPage()
    await screen.findByText('Lina Mansour')

    await user.click(screen.getByRole('button', { name: 'New retail sale' }))
    await user.click(screen.getByRole('button', { name: 'Start sale' }))

    expect(await screen.findByText('no draft today')).toBeInTheDocument()
    expect(onStartRetailSale).not.toHaveBeenCalled()
  })
})
