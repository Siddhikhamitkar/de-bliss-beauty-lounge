import { useEffect, useState } from 'react'
import { checkoutInvoice, getReceipt, startInvoice, validateCoupon } from '../api/billing.ts'
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
  PaymentMethod,
  Receipt,
} from '../api/contracts.ts'
import { ErrorState } from '../components/ErrorState.tsx'
import { FieldErrors } from '../components/FieldErrors.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { formatMoney, formatPrice } from '../format.ts'
import { useAsync } from '../useAsync.ts'
import { ReceiptOverlay } from './ReceiptOverlay.tsx'

interface CheckoutPageProps {
  /** The completed appointment being checked out; the invoice loads on mount. */
  appointmentId: string
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

/** The reference field's real-world label depends on how the money arrived. */
const REFERENCE_LABELS: Record<PaymentMethod, string> = {
  Cash: 'Reference (optional)',
  Card: 'Receipt reference (optional)',
  BankTransfer: 'Transaction reference (optional)',
}

/**
 * The receptionist's checkout: the appointment's services load automatically,
 * one coupon and one manual discount can come off, and one of three payment
 * methods closes the visit. The server revalidates every number; this screen
 * only previews it.
 */
export function CheckoutPage({ appointmentId, onClose, onPaid }: CheckoutPageProps) {
  const invoice = useAsync(() => startInvoice(appointmentId), [appointmentId])
  const record = invoice.data

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
  const couponDiscount = coupon?.discountAmount ?? 0
  const discountNumber = parseNumber(discountValue)
  const manualDiscount =
    discountNumber !== null && discountNumber > 0
      ? discountType === 'Percentage'
        ? round2((discountNumber / 100) * subtotal)
        : discountNumber
      : 0
  const total = round2(Math.max(0, subtotal - couponDiscount - manualDiscount))

  // The amount tracks the live total until the receptionist overtypes it.
  const amount = amountOverride ?? total.toFixed(2)

  function applyCoupon() {
    const code = couponInput.trim()

    if (code === '') {
      return
    }

    setCouponBusy(true)
    setCouponFailure(undefined)

    validateCoupon({ code, subtotal })
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

  return (
    <div className="checkout-overlay" role="dialog" aria-modal="true" aria-label="Checkout">
      <div className="checkout-panel">
        <header className="checkout-head">
          <div>
            <h2>Checkout</h2>
            {record !== undefined && (
              <p className="checkout-customer">
                {record.invoiceNumber} · {record.customerName}
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
            <table className="grid checkout-items">
              <thead>
                <tr>
                  <th scope="col">Service</th>
                  <th scope="col" className="numeric">Qty</th>
                  <th scope="col" className="numeric">Price</th>
                  <th scope="col" className="numeric">Amount</th>
                </tr>
              </thead>
              <tbody>
                {record.items.map(item => (
                  <tr key={item.id}>
                    <td>{item.description}</td>
                    <td className="numeric">{item.quantity}</td>
                    <td className="numeric">{formatPrice(item.unitPrice)}</td>
                    <td className="numeric">{formatPrice(item.lineTotal)}</td>
                  </tr>
                ))}
              </tbody>
            </table>

            <div className="checkout-totals">
              <div className="receipt-totals">
                <div>
                  <dt>Subtotal</dt>
                  <dd>{formatMoney(currency, subtotal)}</dd>
                </div>
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
