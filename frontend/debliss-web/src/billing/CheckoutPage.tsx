import { useEffect, useState } from 'react'
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
import { ApiError, describeFailure } from '../api/client.ts'
import {
  DISCOUNT_TYPE_LABELS,
  DISCOUNT_TYPES,
  PAYMENT_METHODS,
  PAYMENT_METHOD_LABELS,
} from '../api/contracts.ts'
import type {
  CheckoutInvoiceRequest,
  CouponValidation,
  DiscountType,
  InventoryProductOption,
  InvoiceItem,
  PaymentMethod,
  Receipt,
} from '../api/contracts.ts'
import { EmptyState } from '../components/EmptyState.tsx'
import { ErrorState } from '../components/ErrorState.tsx'
import { FieldErrors } from '../components/FieldErrors.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { formatMoney, formatPrice } from '../format.ts'
import { useAsync } from '../useAsync.ts'
import { ReceiptOverlay } from './ReceiptOverlay.tsx'

interface CheckoutPageProps {
  /** The completed appointment being checked out; the invoice loads on mount. */
  appointmentId?: string
  /** An existing draft invoice opened directly, e.g. a walk-in retail sale. */
  invoiceId?: string
  onClose: () => void
  /** A payment landed; the billing list should reload when it is next shown. */
  onPaid: () => void
}

function round2(value: number): number {
  return Math.round(value * 100) / 100
}

function parseNumber(text: string): number | null {
  if (text.trim() === '') {
    return null
  }

  const value = Number(text)

  return Number.isNaN(value) ? null : value
}

/** The first field-level message a line mutation failed with, if any. */
function lineErrorText(error: unknown): string {
  if (error instanceof ApiError) {
    const first = Object.values(error.errors).flat()[0]

    if (first !== undefined) {
      return first
    }
  }

  return describeFailure(error)
}

function productOptionLabel(product: InventoryProductOption): string {
  const identity = product.brand === null ? product.name : `${product.name} · ${product.brand}`

  return `${identity} — ${formatPrice(product.sellingPrice)}`
}

/** Product lines are hand-added and editable; service lines come from the booking. */
function isProductLine(item: InvoiceItem): boolean {
  return item.productId != null
}

/** The reference field's real-world label depends on how the money arrived. */
const REFERENCE_LABELS: Record<PaymentMethod, string> = {
  Cash: 'Reference (optional)',
  Card: 'Receipt reference (optional)',
  BankTransfer: 'Transaction reference (optional)',
}

/**
 * The receptionist's checkout: the appointment's services load automatically
 * (or, for a walk-in retail sale, the bill starts empty and fills with
 * products from the picker), one coupon and one manual discount can come off,
 * and one of three payment methods closes the visit. The server revalidates
 * every number — including the stock behind each product line; this screen
 * only previews it.
 */
export function CheckoutPage({ appointmentId, invoiceId, onClose, onPaid }: CheckoutPageProps) {
  const invoice = useAsync(
    () =>
      appointmentId !== undefined
        ? startInvoice(appointmentId)
        : getInvoice(invoiceId ?? ''),
    [appointmentId, invoiceId],
  )
  const record = invoice.data
  // The active retail catalogue behind the product picker; viewing it is part
  // of both roles, so it loads for every checkout.
  const products = useAsync(() => getSellableProducts(), [])

  const [couponInput, setCouponInput] = useState('')
  const [coupon, setCoupon] = useState<CouponValidation>()
  const [couponBusy, setCouponBusy] = useState(false)
  const [couponFailure, setCouponFailure] = useState<string>()
  const [discountType, setDiscountType] = useState<DiscountType>('Fixed')
  const [discountValue, setDiscountValue] = useState('')
  const [method, setMethod] = useState<PaymentMethod>('Cash')
  const [amountOverride, setAmountOverride] = useState<string>()
  const [reference, setReference] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [submitFailure, setSubmitFailure] = useState<string>()
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>()
  const [receipt, setReceipt] = useState<Receipt>()
  const [receiptFailure, setReceiptFailure] = useState<string>()
  const [selectedProductId, setSelectedProductId] = useState('')
  const [productQty, setProductQty] = useState('1')
  // Product-line quantities are typed here until blur/Enter commits them.
  const [qtyEdits, setQtyEdits] = useState<Record<string, string>>({})
  const [lineBusy, setLineBusy] = useState(false)
  const [lineFailure, setLineFailure] = useState<string>()

  // Resuming a checkout whose invoice was already paid shows its receipt.
  const paidInvoiceId = record !== undefined && record.status === 'Paid' ? record.id : undefined

  useEffect(() => {
    if (paidInvoiceId === undefined || receipt !== undefined || receiptFailure !== undefined) {
      return
    }

    let cancelled = false

    getReceipt(paidInvoiceId)
      .then(value => {
        if (!cancelled) {
          setReceipt(value)
        }
      })
      .catch((error: unknown) => {
        if (!cancelled) {
          setReceiptFailure(describeFailure(error))
        }
      })

    return () => {
      cancelled = true
    }
  }, [paidInvoiceId, receipt, receiptFailure])

  const subtotal = record?.subTotal ?? 0
  const currency = record?.currency ?? ''
  const bundleName = record?.bundleName ?? null
  const bundleDiscount = record?.bundleDiscount ?? 0

  // The bundle's saving comes off first, so a percentage coupon or manual
  // discount is computed against what is still owed — the same base the server
  // validates against. It is zero for a visit that was not booked as a bundle.
  const discountable = round2(subtotal - bundleDiscount)
  const couponDiscount = coupon?.discountAmount ?? 0
  const discountNumber = parseNumber(discountValue)
  const manualDiscount =
    discountNumber !== null && discountNumber > 0
      ? discountType === 'Percentage'
        ? round2((discountNumber / 100) * discountable)
        : discountNumber
      : 0
  const total = round2(Math.max(0, discountable - couponDiscount - manualDiscount))

  // The amount tracks the live total until the receptionist overtypes it.
  const amount = amountOverride ?? total.toFixed(2)

  function applyCoupon() {
    const code = couponInput.trim()

    if (code === '') {
      return
    }

    setCouponBusy(true)
    setCouponFailure(undefined)

    validateCoupon({ code, subtotal: discountable })
      .then(setCoupon)
      .catch((error: unknown) => {
        if (error instanceof ApiError && error.errors['code']?.[0] !== undefined) {
          setCouponFailure(error.errors['code'][0])
        } else {
          setCouponFailure(describeFailure(error))
        }
      })
      .finally(() => setCouponBusy(false))
  }

  function completePayment() {
    if (record === undefined) {
      return
    }

    const parsedAmount = parseNumber(amount)
    const request: CheckoutInvoiceRequest = {
      couponCode: coupon?.code ?? null,
      manualDiscountType: manualDiscount > 0 ? discountType : null,
      manualDiscountValue: manualDiscount > 0 ? discountNumber : null,
      paymentMethod: method,
      amount: parsedAmount,
      reference: reference.trim() === '' ? null : reference.trim(),
    }

    setSubmitting(true)
    setSubmitFailure(undefined)
    setFieldErrors(undefined)

    checkoutInvoice(record.id, request)
      .then(value => {
        setReceipt(value)
        onPaid()
      })
      .catch((error: unknown) => {
        if (error instanceof ApiError && Object.keys(error.errors).length > 0) {
          setFieldErrors(error.errors)
        } else {
          setSubmitFailure(describeFailure(error))
        }
      })
      .finally(() => setSubmitting(false))
  }

  function addProduct() {
    if (record === undefined || selectedProductId === '') {
      return
    }

    const quantity = parseNumber(productQty)

    if (quantity === null) {
      return
    }

    setLineBusy(true)
    setLineFailure(undefined)

    addInvoiceLine(record.id, { productId: selectedProductId, quantity })
      .then(() => {
        setSelectedProductId('')
        setProductQty('1')
        invoice.reload()
      })
      .catch((error: unknown) => setLineFailure(lineErrorText(error)))
      .finally(() => setLineBusy(false))
  }

  function commitQuantity(item: InvoiceItem) {
    const text = qtyEdits[item.id]

    if (record === undefined || text === undefined) {
      return
    }

    const quantity = parseNumber(text)

    // An untouched or unparseable edit falls back to the server's number.
    if (quantity === null || quantity === item.quantity) {
      setQtyEdits(current => {
        const next = { ...current }
        delete next[item.id]
        return next
      })
      return
    }

    setLineBusy(true)
    setLineFailure(undefined)

    updateInvoiceLine(record.id, item.id, { quantity })
      .then(() => invoice.reload())
      .catch((error: unknown) => setLineFailure(lineErrorText(error)))
      .finally(() => setLineBusy(false))
  }

  function removeLine(item: InvoiceItem) {
    if (record === undefined) {
      return
    }

    setLineBusy(true)
    setLineFailure(undefined)

    removeInvoiceLine(record.id, item.id)
      .then(() => invoice.reload())
      .catch((error: unknown) => setLineFailure(lineErrorText(error)))
      .finally(() => setLineBusy(false))
  }

  const hasProductLines = record?.items.some(isProductLine) ?? false

  return (
    <div className="checkout-overlay" role="dialog" aria-modal="true" aria-label="Checkout">
      <div className="checkout-panel">
        <header className="checkout-head">
          <div>
            <h2>Checkout</h2>
            {record !== undefined && (
              <p className="checkout-customer">
                {record.invoiceNumber} · {record.customerName ?? 'Walk-in customer'}
              </p>
            )}
          </div>
          <button type="button" className="button" onClick={onClose}>
            Cancel
          </button>
        </header>

        {invoice.failure !== undefined && (
          <ErrorState message={invoice.failure} onRetry={invoice.reload} />
        )}

        {record === undefined && invoice.failure === undefined && (
          <LoadingState label="Loading checkout…" />
        )}

        {receiptFailure !== undefined && (
          <p className="banner" role="alert">{receiptFailure}</p>
        )}

        {record !== undefined && record.status === 'Draft' && (
          <section className="panel" aria-label="Checkout details">
            {record.items.length === 0 ? (
              <EmptyState>No items yet — add a retail product below.</EmptyState>
            ) : (
              <table className="grid checkout-items">
                <thead>
                  <tr>
                    <th scope="col">Item</th>
                    <th scope="col" className="numeric">Qty</th>
                    <th scope="col" className="numeric">Price</th>
                    <th scope="col" className="numeric">Amount</th>
                    {hasProductLines && <th scope="col">Actions</th>}
                  </tr>
                </thead>
                <tbody>
                  {record.items.map(item => (
                    <tr key={item.id}>
                      <td>{item.description}</td>
                      {isProductLine(item) ? (
                        <td className="numeric">
                          <input
                            type="number"
                            className="checkout-qty-input"
                            aria-label={`Quantity of ${item.description}`}
                            min="0.01"
                            step="0.01"
                            value={qtyEdits[item.id] ?? String(item.quantity)}
                            disabled={lineBusy}
                            onChange={event =>
                              setQtyEdits(current => ({
                                ...current,
                                [item.id]: event.target.value,
                              }))
                            }
                            onBlur={() => commitQuantity(item)}
                            onKeyDown={event => {
                              if (event.key === 'Enter') {
                                commitQuantity(item)
                              }
                            }}
                          />
                        </td>
                      ) : (
                        <td className="numeric">{item.quantity}</td>
                      )}
                      <td className="numeric">{formatPrice(item.unitPrice)}</td>
                      <td className="numeric">{formatPrice(item.lineTotal)}</td>
                      {hasProductLines && (
                        <td>
                          {isProductLine(item) && (
                            <button
                              type="button"
                              className="button"
                              disabled={lineBusy}
                              onClick={() => removeLine(item)}
                            >
                              Remove
                            </button>
                          )}
                        </td>
                      )}
                    </tr>
                  ))}
                </tbody>
              </table>
            )}

            <div className="field">
              <span>Add retail product</span>
              <div className="checkout-discount-row">
                <select
                  name="product"
                  aria-label="Retail product"
                  value={selectedProductId}
                  disabled={lineBusy}
                  onChange={event => setSelectedProductId(event.target.value)}
                >
                  <option value="">Select a product…</option>
                  {(products.data ?? []).map(product => (
                    <option
                      key={product.id}
                      value={product.id}
                      disabled={product.currentStock <= 0}
                    >
                      {productOptionLabel(product)}
                      {product.currentStock <= 0 ? ' — out of stock' : ''}
                    </option>
                  ))}
                </select>
                <input
                  type="number"
                  name="productQuantity"
                  aria-label="Product quantity"
                  min="0.01"
                  step="0.01"
                  value={productQty}
                  disabled={lineBusy}
                  onChange={event => setProductQty(event.target.value)}
                />
                <button
                  type="button"
                  className="button"
                  disabled={
                    lineBusy || selectedProductId === '' || parseNumber(productQty) === null
                  }
                  onClick={addProduct}
                >
                  Add
                </button>
              </div>
              {products.data !== undefined && products.data.length === 0 && (
                <p className="row-subtext">No active retail products in the catalogue.</p>
              )}
              {products.failure !== undefined && (
                <p className="banner" role="alert">{products.failure}</p>
              )}
              {lineFailure !== undefined && (
                <p className="banner" role="alert">{lineFailure}</p>
              )}
            </div>

            <div className="checkout-totals">
              <div className="receipt-totals">
                <div>
                  <dt>Subtotal</dt>
                  <dd>{formatMoney(currency, subtotal)}</dd>
                </div>
                {bundleDiscount > 0 && (
                  <div>
                    <dt>{bundleName === null ? 'Bundle saving' : `Bundle saving (${bundleName})`}</dt>
                    <dd>−{formatMoney(currency, bundleDiscount)}</dd>
                  </div>
                )}
              </div>

              <div className="field">
                <span>Coupon code</span>
                {coupon === undefined ? (
                  <>
                    <div className="checkout-coupon-row">
                      <input
                        type="text"
                        name="couponCode"
                        placeholder="e.g. BLISS20"
                        value={couponInput}
                        onChange={event => setCouponInput(event.target.value)}
                      />
                      <button
                        type="button"
                        className="button"
                        disabled={couponBusy}
                        onClick={applyCoupon}
                      >
                        Apply
                      </button>
                    </div>
                    {couponFailure !== undefined && (
                      <p className="banner" role="alert">{couponFailure}</p>
                    )}
                  </>
                ) : (
                  <div className="checkout-applied-coupon">
                    <strong>{coupon.code}</strong>
                    <span>−{formatMoney(currency, couponDiscount)}</span>
                    <button
                      type="button"
                      className="button"
                      onClick={() => {
                        setCoupon(undefined)
                        setCouponInput('')
                        setCouponFailure(undefined)
                      }}
                    >
                      Remove
                    </button>
                  </div>
                )}
                <FieldErrors messages={fieldErrors?.['couponCode']} />
              </div>

              <div className="field">
                <span>Manual discount</span>
                <div className="checkout-discount-row">
                  <div className="checkout-discount-parts">
                    <select
                      name="manualDiscountType"
                      aria-label="Discount type"
                      value={discountType}
                      onChange={event => setDiscountType(event.target.value as DiscountType)}
                    >
                      {DISCOUNT_TYPES.map(type => (
                        <option key={type} value={type}>
                          {DISCOUNT_TYPE_LABELS[type]}
                        </option>
                      ))}
                    </select>
                    <input
                      type="number"
                      name="manualDiscountValue"
                      aria-label="Discount value"
                      min="0"
                      step="0.01"
                      placeholder={discountType === 'Percentage' ? '%' : formatPrice(0)}
                      value={discountValue}
                      onChange={event => setDiscountValue(event.target.value)}
                    />
                  </div>
                  {manualDiscount > 0 && (
                    <span className="checkout-discount-preview">
                      −{formatMoney(currency, manualDiscount)}
                    </span>
                  )}
                </div>
                <FieldErrors
                  messages={[
                    ...(fieldErrors?.['manualDiscountType'] ?? []),
                    ...(fieldErrors?.['manualDiscountValue'] ?? []),
                  ]}
                />
              </div>

              <div className="checkout-total-row">
                <span>TOTAL</span>
                <span className="checkout-total-value">{formatMoney(currency, total)}</span>
              </div>
            </div>

            <div className="field">
              <span>Payment method</span>
              <div className="payment-methods" role="radiogroup" aria-label="Payment method">
                {PAYMENT_METHODS.map(option => (
                  <label key={option} className="payment-method">
                    <input
                      type="radio"
                      name="paymentMethod"
                      value={option}
                      checked={method === option}
                      onChange={() => setMethod(option)}
                    />
                    {PAYMENT_METHOD_LABELS[option]}
                  </label>
                ))}
              </div>
              <FieldErrors messages={fieldErrors?.['paymentMethod']} />
            </div>

            <div className="checkout-pay-grid">
              <label className="field">
                <span>Amount collected</span>
                <input
                  type="number"
                  name="amount"
                  min="0"
                  step="0.01"
                  value={amount}
                  onChange={event => setAmountOverride(event.target.value)}
                />
                <FieldErrors messages={fieldErrors?.['amount']} />
              </label>

              <label className="field">
                <span>{REFERENCE_LABELS[method]}</span>
                <input
                  type="text"
                  name="reference"
                  value={reference}
                  onChange={event => setReference(event.target.value)}
                />
                <FieldErrors messages={fieldErrors?.['reference']} />
              </label>
            </div>

            {submitFailure !== undefined && (
              <p className="banner" role="alert">{submitFailure}</p>
            )}

            <div className="checkout-actions">
              <button
                type="button"
                className="button button-primary"
                disabled={submitting || couponBusy}
                onClick={completePayment}
              >
                Complete Payment
              </button>
            </div>
          </section>
        )}

        {record !== undefined && record.status !== 'Draft' && receipt === undefined && (
          <LoadingState label="Loading receipt…" />
        )}

        {receipt !== undefined && (
          <ReceiptOverlay
            receipt={receipt}
            onDone={() => {
              setReceipt(undefined)
              onClose()
            }}
          />
        )}
      </div>
    </div>
  )
}
