import { useState } from 'react'
import { describeFailure } from '../api/client.ts'
import { searchCustomers, setCustomerStatus } from '../api/customers.ts'
import type { CustomerStatusFilter, CustomerSummary } from '../api/contracts.ts'
import { useAsync } from '../useAsync.ts'
import { Pagination } from '../components/Pagination.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { EmptyState } from '../components/EmptyState.tsx'
import { ErrorState } from '../components/ErrorState.tsx'
import { CustomerFilters } from './CustomerFilters.tsx'
import { CustomerList } from './CustomerList.tsx'
import { CustomerEditor } from './CustomerForm.tsx'
import { CustomerDetails } from './CustomerDetails.tsx'

/**
 * What the page is showing. A form with a null `customerId` means "create", so
 * one piece of state covers every direction without a router.
 */
type CustomerView =
  | { kind: 'list' }
  | { kind: 'profile'; customerId: string }
  | { kind: 'form'; customerId: string | null }

export function CustomersPage() {
  const [search, setSearch] = useState('')
  const [appliedSearch, setAppliedSearch] = useState('')
  const [status, setStatus] = useState<CustomerStatusFilter>('All')
  const [page, setPage] = useState(1)
  const [view, setView] = useState<CustomerView>({ kind: 'list' })
  const [actionFailure, setActionFailure] = useState<string>()

  const customers = useAsync(
    () =>
      searchCustomers({
        search: appliedSearch === '' ? undefined : appliedSearch,
        status: status === 'All' ? undefined : status,
        page: page > 1 ? page : undefined,
      }),
    [appliedSearch, status, page],
  )

  function toggleStatus(customer: CustomerSummary) {
    setActionFailure(undefined)

    setCustomerStatus(customer.id, !customer.isActive)
      .then(() => customers.reload())
      .catch((error: unknown) => setActionFailure(describeFailure(error)))
  }

  if (view.kind === 'form') {
    return (
      <CustomerEditor
        customerId={view.customerId}
        onSaved={() => {
          setView({ kind: 'list' })
          customers.reload()
        }}
        onCancel={() =>
          setView(
            view.customerId === null
              ? { kind: 'list' }
              : { kind: 'profile', customerId: view.customerId },
          )
        }
      />
    )
  }

  if (view.kind === 'profile') {
    return (
      <CustomerDetails
        customerId={view.customerId}
        onBack={() => setView({ kind: 'list' })}
        onEdit={customerId => setView({ kind: 'form', customerId })}
      />
    )
  }

  return (
    <section className="panel">
      <div className="panel-header">
        <h2>Customers</h2>
        <button
          type="button"
          className="button button-primary"
          onClick={() => setView({ kind: 'form', customerId: null })}
        >
          New customer
        </button>
      </div>

      <CustomerFilters
        search={search}
        onSearchChange={setSearch}
        status={status}
        onStatusChange={value => {
          setStatus(value)
          setPage(1)
        }}
        onApply={() => {
          setPage(1)
          setAppliedSearch(search.trim())
        }}
      />

      {customers.failure !== undefined && (
        <ErrorState message={customers.failure} onRetry={customers.reload} />
      )}
      {actionFailure !== undefined && (
        <p className="banner" role="alert">{actionFailure}</p>
      )}

      {customers.data === undefined && customers.failure === undefined && (
        <LoadingState label="Loading customers…" />
      )}

      {customers.data !== undefined && customers.data.items.length === 0 && (
        <EmptyState>No customers match these filters.</EmptyState>
      )}

      {customers.data !== undefined && customers.data.items.length > 0 && (
        <>
          <CustomerList
            customers={customers.data.items}
            onView={customer => setView({ kind: 'profile', customerId: customer.id })}
            onEdit={customer => setView({ kind: 'form', customerId: customer.id })}
            onToggleStatus={toggleStatus}
          />
          <Pagination result={customers.data} onChange={setPage} />
        </>
      )}
    </section>
  )
}
