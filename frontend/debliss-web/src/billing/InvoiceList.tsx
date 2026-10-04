import { PAYMENT_METHOD_LABELS } from '../api/contracts.ts'
import type { InvoiceSummary, PaymentMethod } from '../api/contracts.ts'
import { formatDateTime, formatMoney } from '../format.ts'
import { InvoiceStatusPill } from './InvoiceStatusPill.tsx'

interface InvoiceListProps {
  invoices: InvoiceSummary[]
  onOpen: (invoice: InvoiceSummary) => void
}

function methodLabel(method: string | null): string {
  if (method === null) {
    return '—'
  }

  return PAYMENT_METHOD_LABELS[method as PaymentMethod] ?? method
}

export function InvoiceList({ invoices, onOpen }: InvoiceListProps) {
  return (
    <table className="grid">
      <thead>
        <tr>
          <th scope="col">Invoice</th>
          <th scope="col">Customer</th>
          <th scope="col">Status</th>
          <th scope="col" className="numeric">Subtotal</th>
          <th scope="col" className="numeric">Discount</th>
          <th scope="col" className="numeric">Total</th>
          <th scope="col">Method</th>
          <th scope="col">Issued</th>
        </tr>
      </thead>
      <tbody>
        {invoices.map(invoice => (
          <tr
            key={invoice.id}
            className="row-clickable"
            onClick={() => onOpen(invoice)}
          >
            <th scope="row">
              <button
                type="button"
                className="row-link"
                onClick={event => {
                  event.stopPropagation()
                  onOpen(invoice)
                }}
              >
                {invoice.invoiceNumber}
              </button>
            </th>
            <td>{invoice.customerName}</td>
            <td><InvoiceStatusPill status={invoice.status} /></td>
            <td className="numeric">{formatMoney(invoice.currency, invoice.subTotal)}</td>
            <td className="numeric">
              {invoice.discountTotal > 0
                ? `−${formatMoney(invoice.currency, invoice.discountTotal)}`
                : '—'}
            </td>
            <td className="numeric">{formatMoney(invoice.currency, invoice.totalAmount)}</td>
            <td>{methodLabel(invoice.paymentMethod)}</td>
            <td>{invoice.issuedAt === null ? '—' : formatDateTime(invoice.issuedAt)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
