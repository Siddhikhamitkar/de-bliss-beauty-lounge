import { Fragment, useState } from 'react'
import { getInvoice, getReceipt } from '../api/billing.ts'
import { describeFailure } from '../api/client.ts'
import { PAYMENT_METHOD_LABELS } from '../api/contracts.ts'
import type { InvoiceSummary, PaymentMethod, Receipt } from '../api/contracts.ts'
import { DetailDrawer, DrawerHeader } from '../components/DetailDrawer.tsx'
import { ErrorState } from '../components/ErrorState.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { formatDateTime, formatMoney, formatPrice } from '../format.ts'
import { useAsync } from '../useAsync.ts'
import { InvoiceStatusPill } from './InvoiceStatusPill.tsx'
import { ReceiptOverlay } from './ReceiptOverlay.tsx'
import { discountedTotal } from './totals.ts'

interface InvoiceDrawerProps {
  /** The row that was opened; paints the header before the full record loads. */
  invoice: InvoiceSummary
  onClose: () => void
  /** Draft invoices only: reopens the checkout screen for the invoice. */
  onResumeCheckout: (invoiceId: string) => void
}

function methodLabel(method: string): string {
  return PAYMENT_METHOD_LABELS[method as PaymentMethod] ?? method
}

/** One invoice as a read-only record, with resume-checkout or receipt reprint. */
export function InvoiceDrawer({ invoice, onClose, onResumeCheckout }: InvoiceDrawerProps) {
  const details = useAsync(() => getInvoice(invoice.id), [invoice.id])
  const record = details.data
  const [receipt, setReceipt] = useState<Receipt>()
  const [receiptFailure, setReceiptFailure] = useState<string>()

  const status = record?.status ?? invoice.status
  const bundleName = record?.bundleName ?? null

  function printReceipt() {
    setReceiptFailure(undefined)

    getReceipt(invoice.id)
      .then(setReceipt)
      .catch((error: unknown) => setReceiptFailure(describeFailure(error)))
  }

  return (
    <DetailDrawer
      label={`Invoice ${invoice.invoiceNumber}`}
      onClose={onClose}
      panelClassName="drawer-workspace"
    >
      <DrawerHeader
        title={invoice.invoiceNumber}
        subtitle={
          <>
            <span>{invoice.customerName}</span>
            <InvoiceStatusPill status={status} />
          </>
        }
        actions={
          record !== undefined && status === 'Draft' ? (
            <button
              type="button"
              className="button button-primary"
              onClick={() => onResumeCheckout(record.id)}
            >
              Resume checkout
            </button>
          ) : status === 'Paid' ? (
            <button type="button" className="button" onClick={printReceipt}>
              Print receipt
            </button>
          ) : undefined
        }
        onClose={onClose}
      />

      {receiptFailure !== undefined && (
        <p className="banner" role="alert">{receiptFailure}</p>
      )}

      {details.failure !== undefined && (
        <ErrorState message={details.failure} onRetry={details.reload} />
      )}

      {details.data === undefined && details.failure === undefined && (
        <LoadingState label="Loading invoice…" />
      )}

      {details.data !== undefined && (
        <>
          <section className="tab-section" aria-label="Invoice summary">
            <div className="tab-section-head">
              <h4>Summary</h4>
            </div>
            <dl className="kv-grid">
              <dt>Customer</dt>
              <dd>
                {details.data.customerName}
                {details.data.walkInCustomerName !== null &&
                  ` (${details.data.walkInCustomerName})`}
              </dd>
              <dt>Issued</dt>
              <dd>{details.data.issuedAt === null ? '—' : formatDateTime(details.data.issuedAt)}</dd>
              <dt>Status</dt>
              <dd><InvoiceStatusPill status={details.data.status} /></dd>
              {bundleName !== null && (
                <>
                  <dt>Bundle</dt>
                  <dd>{bundleName}</dd>
                </>
              )}
              <dt>Coupon</dt>
              <dd>{details.data.couponCode ?? '—'}</dd>
              <dt>Loyalty points redeemed</dt>
              <dd>{details.data.loyaltyPointsRedeemed.toFixed(2)}</dd>
              <dt>Loyalty points earned</dt>
              <dd>{details.data.loyaltyPointsEarned.toFixed(2)}</dd>
            </dl>
          </section>

          <section className="tab-section" aria-label="Invoice services">
            <div className="tab-section-head">
              <h4>Services</h4>
            </div>
            <table className="grid">
              <thead>
                <tr>
                  <th scope="col">Service</th>
                  <th scope="col" className="numeric">Qty</th>
                  <th scope="col" className="numeric">Price</th>
                  <th scope="col" className="numeric">Amount</th>
                </tr>
              </thead>
              <tbody>
                {details.data.items.map(item => (
                  <tr key={item.id}>
                    <td>{item.description}</td>
                    <td className="numeric">{item.quantity}</td>
                    <td className="numeric">{formatPrice(item.unitPrice)}</td>
                    <td className="numeric">{formatPrice(item.lineTotal)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </section>

          <section className="tab-section" aria-label="Invoice totals">
            <div className="tab-section-head">
              <h4>Totals</h4>
            </div>
            <dl className="receipt-totals invoice-totals">
              <div>
                <dt>Subtotal</dt>
                <dd>{formatMoney(details.data.currency, details.data.subTotal)}</dd>
              </div>
              {details.data.bundleDiscount > 0 && (
                <div>
                  <dt>Bundle saving</dt>
                  <dd>−{formatMoney(details.data.currency, details.data.bundleDiscount)}</dd>
                </div>
              )}
              {details.data.couponDiscount > 0 && (
                <div>
                  <dt>Coupon discount</dt>
                  <dd>−{formatMoney(details.data.currency, details.data.couponDiscount)}</dd>
                </div>
              )}
              {details.data.manualDiscount > 0 && (
                <div>
                  <dt>Manual discount</dt>
                  <dd>−{formatMoney(details.data.currency, details.data.manualDiscount)}</dd>
                </div>
              )}
              {details.data.loyaltyDiscount > 0 && (
                <div><dt>Loyalty discount</dt><dd>−{formatMoney(details.data.currency, details.data.loyaltyDiscount)}</dd></div>
              )}
              {(details.data.taxRate ?? 0) > 0 && (
                <>
                  <div>
                    <dt>
                      {details.data.pricesIncludeTax ? 'Total (VAT included)' : 'Total before VAT'}
                    </dt>
                    <dd>
                      {formatMoney(
                        details.data.currency,
                        discountedTotal(details.data),
                      )}
                    </dd>
                  </div>
                  <div>
                    <dt>
                      {details.data.pricesIncludeTax
                        ? `Includes VAT (${details.data.taxRate}%)`
                        : `VAT (${details.data.taxRate}%)`}
                    </dt>
                    <dd>
                      {details.data.pricesIncludeTax ? '' : '+'}
                      {formatMoney(details.data.currency, details.data.taxTotal ?? 0)}
                    </dd>
                  </div>
                </>
              )}
              {(details.data.tipAmount ?? 0) > 0 && (
                <div>
                  <dt>
                    {details.data.tipPercentage != null
                      ? `Tip (${details.data.tipPercentage}%)`
                      : 'Tip'}
                  </dt>
                  <dd>+{formatMoney(details.data.currency, details.data.tipAmount ?? 0)}</dd>
                </div>
              )}
              <div className="receipt-total-row">
                <dt>Total</dt>
                <dd>{formatMoney(details.data.currency, details.data.totalAmount)}</dd>
              </div>
            </dl>
          </section>

          <section className="tab-section" aria-label="Invoice payments">
            <div className="tab-section-head">
              <h4>Payments</h4>
            </div>
            {details.data.payments.length === 0 ? (
              <p className="empty">No payment recorded yet.</p>
            ) : (
              <dl className="kv-grid">
                {details.data.payments.map(payment => (
                  <Fragment key={payment.id}>
                    <dt>Method</dt>
                    <dd>{methodLabel(payment.method)}</dd>
                    <dt>Amount</dt>
                    <dd>{formatMoney(payment.currency, payment.amount)}</dd>
                    {payment.reference !== null && (
                      <>
                        <dt>Reference</dt>
                        <dd>{payment.reference}</dd>
                      </>
                    )}
                    <dt>Paid</dt>
                    <dd>{formatDateTime(payment.paidAt)}</dd>
                  </Fragment>
                ))}
              </dl>
            )}
          </section>
        </>
      )}

      {receipt !== undefined && (
        <ReceiptOverlay receipt={receipt} onDone={() => setReceipt(undefined)} />
      )}
    </DetailDrawer>
  )
}
