import { searchInvoices } from '../api/billing.ts'
import { InvoiceStatusPill } from '../billing/InvoiceStatusPill.tsx'
import { ErrorState } from '../components/ErrorState.tsx'
import { EmptyState } from '../components/EmptyState.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { formatDate, formatMoney } from '../format.ts'
import { useAsync } from '../useAsync.ts'

interface CustomerSalesProps {
  customerId: string
}

/**
 * The customer's invoices, read through the billing API — the same rows the
 * Billing screen lists, narrowed to this customer. Read-only: payments are
 * taken at checkout, never from the customer record.
 */
export function CustomerSales({ customerId }: CustomerSalesProps) {
  const { data, failure, reload } = useAsync(
    () => searchInvoices({ customerId, pageSize: 50 }),
    [customerId],
  )

  if (failure !== undefined) {
    return (
      <section className="tab-section" aria-label="Sales">
        <div className="tab-section-head">
          <h4>Sales</h4>
        </div>
        <ErrorState message={failure} onRetry={reload} />
      </section>
    )
  }

  if (data === undefined) {
    return (
      <section className="tab-section" aria-label="Sales">
        <div className="tab-section-head">
          <h4>Sales</h4>
        </div>
        <LoadingState label="Loading sales…" />
      </section>
    )
  }

  if (data.items.length === 0) {
    return (
      <section className="tab-section" aria-label="Sales">
        <div className="tab-section-head">
          <h4>Sales</h4>
        </div>
        <EmptyState>No sales yet.</EmptyState>
      </section>
    )
  }

  return (
    <section className="tab-section" aria-label="Sales">
      <div className="tab-section-head">
        <h4>Sales</h4>
      </div>
      <table className="grid">
        <thead>
          <tr>
            <th scope="col">Invoice</th>
            <th scope="col">Status</th>
            <th scope="col" className="numeric">Total</th>
            <th scope="col">Issued</th>
          </tr>
        </thead>
        <tbody>
          {data.items.map(invoice => (
            <tr key={invoice.id}>
              <th scope="row">{invoice.invoiceNumber}</th>
              <td><InvoiceStatusPill status={invoice.status} /></td>
              <td className="numeric">{formatMoney(invoice.currency, invoice.totalAmount)}</td>
              <td>{invoice.issuedAt === null ? '—' : formatDate(invoice.issuedAt)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  )
}
