import { useEffect, useState } from 'react'
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
import { CustomerDrawer } from './CustomerDrawer.tsx'

interface CustomersPageProps {
  /** Hands the customer to the calendar to start a booking for them. */
  onBookAppointment: (customer: CustomerSummary) => void
  /** A dashboard "Add customer" hand-off: open the create form on arrival. */
  pendingCreate?: boolean
  onPendingCreateHandled?: () => void
}

/**
 * Shows either the list or the editor. The list stays mounted under a drawer,
 * so opening a record keeps search, filters and page exactly as they were; a
 * form with a null `customerId` means "create", so one piece of state covers
 * every direction without a router.
 */
type CustomerView = { kind: 'list' } | { kind: 'form'; customerId: string | null }

export function CustomersPage({
  onBookAppointment,
  pendingCreate = false,
  onPendingCreateHandled,
}: CustomersPageProps) {
  const [search, setSearch] = useState('')
  const [appliedSearch, setAppliedSearch] = useState('')
  const [status, setStatus] = useState<CustomerStatusFilter>('All')
  const [page, setPage] = useState(1)
  // The dashboard's "Add customer" arrives with a fresh mount, so the hand-off
  // opens the create form straight from the initial state.
  const [view, setView] = useState<CustomerView>(() =>
    pendingCreate ? { kind: 'form', customerId: null } : { kind: 'list' },
  )
  const [selected, setSelected] = useState<CustomerSummary | null>(null)
  const [actionFailure, setActionFailure] = useState<string>()

  // Clear the hand-off once it has been picked up by the initializer above.
  useEffect(() => {
    if (pendingCreate) {
      onPendingCreateHandled?.()
    }
  }, [pendingCreate, onPendingCreateHandled])

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
        onCancel={() => setView({ kind: 'list' })}
      />
    )
  }

  return (
    <>
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
              onOpen={setSelected}
              onToggleStatus={toggleStatus}
            />
            <Pagination result={customers.data} onChange={setPage} />
          </>
        )}
      </section>

      {selected !== null && (
        <CustomerDrawer
          customer={selected}
          onClose={() => setSelected(null)}
          onEdit={customerId => {
            setSelected(null)
            setView({ kind: 'form', customerId })
          }}
          onBookAppointment={onBookAppointment}
          onChanged={customers.reload}
        />
      )}
    </>
  )
}
