import { useState } from 'react'
import { describeFailure } from '../api/client.ts'
import { searchStaff, setStaffStatus } from '../api/staff.ts'
import type { StaffStatusFilter, StaffSummary } from '../api/contracts.ts'
import { useAsync } from '../useAsync.ts'
import { Pagination } from '../components/Pagination.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { EmptyState } from '../components/EmptyState.tsx'
import { ErrorState } from '../components/ErrorState.tsx'
import { StaffFilters } from './StaffFilters.tsx'
import { StaffList } from './StaffList.tsx'
import { StaffEditor } from './StaffForm.tsx'
import { StaffDetails } from './StaffDetails.tsx'

/**
 * What the page is showing. A form with a null `staffId` means "create", so one
 * piece of state covers every direction without a router.
 */
type StaffView =
  | { kind: 'list' }
  | { kind: 'profile'; staffId: string }
  | { kind: 'form'; staffId: string | null }

export function StaffPage() {
  const [search, setSearch] = useState('')
  const [appliedSearch, setAppliedSearch] = useState('')
  const [status, setStatus] = useState<StaffStatusFilter>('All')
  const [page, setPage] = useState(1)
  const [view, setView] = useState<StaffView>({ kind: 'list' })
  const [actionFailure, setActionFailure] = useState<string>()

  const staff = useAsync(
    () =>
      searchStaff({
        search: appliedSearch === '' ? undefined : appliedSearch,
        status: status === 'All' ? undefined : status,
        page: page > 1 ? page : undefined,
      }),
    [appliedSearch, status, page],
  )

  function toggleStatus(member: StaffSummary) {
    setActionFailure(undefined)

    setStaffStatus(member.id, !member.isActive)
      .then(() => staff.reload())
      .catch((error: unknown) => setActionFailure(describeFailure(error)))
  }

  if (view.kind === 'form') {
    return (
      <StaffEditor
        staffId={view.staffId}
        onSaved={() => {
          setView({ kind: 'list' })
          staff.reload()
        }}
        onCancel={() =>
          setView(view.staffId === null ? { kind: 'list' } : { kind: 'profile', staffId: view.staffId })
        }
      />
    )
  }

  if (view.kind === 'profile') {
    return (
      <StaffDetails
        staffId={view.staffId}
        onBack={() => setView({ kind: 'list' })}
        onEdit={staffId => setView({ kind: 'form', staffId })}
      />
    )
  }

  return (
    <section className="panel">
      <div className="panel-header">
        <h2>Staff</h2>
        <button
          type="button"
          className="button button-primary"
          onClick={() => setView({ kind: 'form', staffId: null })}
        >
          New staff member
        </button>
      </div>

      <StaffFilters
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

      {staff.failure !== undefined && (
        <ErrorState message={staff.failure} onRetry={staff.reload} />
      )}
      {actionFailure !== undefined && (
        <p className="banner" role="alert">{actionFailure}</p>
      )}

      {staff.data === undefined && staff.failure === undefined && (
        <LoadingState label="Loading staff…" />
      )}

      {staff.data !== undefined && staff.data.items.length === 0 && (
        <EmptyState>No staff members match these filters.</EmptyState>
      )}

      {staff.data !== undefined && staff.data.items.length > 0 && (
        <>
          <StaffList
            members={staff.data.items}
            onView={member => setView({ kind: 'profile', staffId: member.id })}
            onEdit={member => setView({ kind: 'form', staffId: member.id })}
            onToggleStatus={toggleStatus}
          />
          <Pagination result={staff.data} onChange={setPage} />
        </>
      )}
    </section>
  )
}
