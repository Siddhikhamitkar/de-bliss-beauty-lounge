import { useState } from 'react'
import { searchInvoices } from '../api/billing.ts'
import type {
  InvoicePaymentMethodFilter,
  InvoiceStatusFilter,
  InvoiceSummary,
} from '../api/contracts.ts'
import { useAsync } from '../useAsync.ts'
import { Pagination } from '../components/Pagination.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { EmptyState } from '../components/EmptyState.tsx'
import { ErrorState } from '../components/ErrorState.tsx'
import { BillingFilters } from './BillingFilters.tsx'
import { InvoiceList } from './InvoiceList.tsx'
import { InvoiceDrawer } from './InvoiceDrawer.tsx'

interface BillingPageProps {
  /** Bumped after a checkout elsewhere (the calendar) so the list reloads. */
  revision: number
  /** Draft invoices can reopen the checkout screen for their appointment. */
  onResumeCheckout: (appointmentId: string) => void
  /** Coupons are managed from here, like categories from Services. */
  onManageCoupons?: () => void
}

/** The invoice list: search by number or customer, filter by status and method. */
export function BillingPage({ revision, onResumeCheckout, onManageCoupons }: BillingPageProps) {
  const [search, setSearch] = useState('')
  const [appliedSearch, setAppliedSearch] = useState('')
  const [status, setStatus] = useState<InvoiceStatusFilter>('All')
  const [paymentMethod, setPaymentMethod] = useState<InvoicePaymentMethodFilter>('All')
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState<InvoiceSummary | null>(null)

  const invoices = useAsync(
    () =>
      searchInvoices({
        search: appliedSearch === '' ? undefined : appliedSearch,
        status: status === 'All' ? undefined : status,
        paymentMethod: paymentMethod === 'All' ? undefined : paymentMethod,
        page: page > 1 ? page : undefined,
      }),
    [appliedSearch, status, paymentMethod, page, revision],
  )

  return (
    <>
      <section className="panel" aria-label="Invoices">
        <div className="panel-header">
          <div className="panel-heading">
            <h2>Billing</h2>
            <p className="panel-lede">
              Invoices, payments and coupon usage across the salon.
            </p>
          </div>
          {onManageCoupons !== undefined && (
            <div className="panel-actions">
              <button type="button" className="button" onClick={onManageCoupons}>
                Coupons
              </button>
            </div>
          )}
        </div>

        <BillingFilters
          search={search}
          onSearchChange={setSearch}
          status={status}
          onStatusChange={value => {
            setStatus(value)
            setPage(1)
          }}
          paymentMethod={paymentMethod}
          onPaymentMethodChange={value => {
            setPaymentMethod(value)
            setPage(1)
          }}
          onApply={() => {
            setPage(1)
            setAppliedSearch(search.trim())
          }}
        />

        {invoices.failure !== undefined && (
          <ErrorState message={invoices.failure} onRetry={invoices.reload} />
        )}

        {invoices.data === undefined && invoices.failure === undefined && (
          <LoadingState label="Loading invoices…" />
        )}

        {invoices.data !== undefined && invoices.data.items.length === 0 && (
          <EmptyState>No invoices match these filters.</EmptyState>
        )}

        {invoices.data !== undefined && invoices.data.items.length > 0 && (
          <>
            <InvoiceList invoices={invoices.data.items} onOpen={setSelected} />
            <Pagination result={invoices.data} onChange={setPage} />
          </>
        )}
      </section>

      {selected !== null && (
        <InvoiceDrawer
          invoice={selected}
          onClose={() => setSelected(null)}
          onResumeCheckout={appointmentId => {
            setSelected(null)
            onResumeCheckout(appointmentId)
          }}
        />
      )}
    </>
  )
}
