import { PAYMENT_METHOD_LABELS } from '../api/contracts.ts'
import type { PaymentMethod, Receipt } from '../api/contracts.ts'
import { formatDateTime, formatMoney, formatPrice } from '../format.ts'

function methodLabel(method: string): string {
  return PAYMENT_METHOD_LABELS[method as PaymentMethod] ?? method
}

/** The printable sheet itself, shared by the checkout success step and reprints. */
export function ReceiptView({ receipt }: { receipt: Receipt }) {
  return (
    <article className="receipt-sheet" aria-label={`Receipt ${receipt.invoiceNumber}`}>
      <header className="receipt-head">
        <h3>{receipt.salonName}</h3>
        <p className="receipt-title">Receipt · {receipt.invoiceNumber}</p>
      </header>

      <dl className="receipt-meta">
        <div>
          <dt>Customer</dt>
          <dd>{receipt.customerName}</dd>
        </div>
        <div>
          <dt>Issued</dt>
          <dd>{formatDateTime(receipt.issuedAt)}</dd>
        </div>
      </dl>

      <table className="receipt-items">
        <thead>
          <tr>
            <th scope="col">Service</th>
            <th scope="col" className="numeric">Qty</th>
            <th scope="col" className="numeric">Price</th>
            <th scope="col" className="numeric">Amount</th>
          </tr>
        </thead>
        <tbody>
          {receipt.items.map((line, index) => (
            <tr key={index}>
              <td>{line.description}</td>
              <td className="numeric">{line.quantity}</td>
              <td className="numeric">{formatPrice(line.unitPrice)}</td>
              <td className="numeric">{formatPrice(line.lineTotal)}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <dl className="receipt-totals">
        <div>
          <dt>Subtotal</dt>
          <dd>{formatMoney(receipt.currency, receipt.subTotal)}</dd>
        </div>
        {receipt.couponCode !== null && (
          <div>
            <dt>Coupon ({receipt.couponCode})</dt>
            <dd>−{formatMoney(receipt.currency, receipt.couponDiscount)}</dd>
          </div>
        )}
        {receipt.manualDiscount > 0 && (
          <div>
            <dt>Manual discount</dt>
            <dd>−{formatMoney(receipt.currency, receipt.manualDiscount)}</dd>
          </div>
        )}
        <div className="receipt-total-row">
          <dt>Total</dt>
          <dd>{formatMoney(receipt.currency, receipt.totalAmount)}</dd>
        </div>
      </dl>

      <dl className="receipt-meta receipt-payment">
        <div>
          <dt>Payment method</dt>
          <dd>{methodLabel(receipt.paymentMethod)}</dd>
        </div>
        {receipt.paymentReference !== null && (
          <div>
            <dt>Reference</dt>
            <dd>{receipt.paymentReference}</dd>
          </div>
        )}
        <div>
          <dt>Paid</dt>
          <dd>{formatDateTime(receipt.paidAt)}</dd>
        </div>
      </dl>
    </article>
  )
}
