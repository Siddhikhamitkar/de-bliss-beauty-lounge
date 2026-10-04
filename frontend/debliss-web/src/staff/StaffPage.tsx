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
import { StaffDrawer } from './StaffDrawer.tsx'

/**
 * Which member the editor is open on, or null while the list is showing. A
 * null `staffId` means "create", so one piece of state covers both directions.
 */
interface Editor {
  staffId: string | null
}

export function StaffPage() {
  const [search, setSearch] = useState('')
  const [appliedSearch, setAppliedSearch] = useState('')
  const [status, setStatus] = useState<StaffStatusFilter>('All')
  const [page, setPage] = useState(1)
  const [editor, setEditor] = useState<Editor | null>(null)
  const [selected, setSelected] = useState<StaffSummary | null>(null)
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

  if (editor !== null) {
    return (
      <StaffEditor
        staffId={editor.staffId}
        onSaved={() => {
          setEditor(null)
          staff.reload()
        }}
        onCancel={() => setEditor(null)}
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
          onClick={() => setEditor({ staffId: null })}
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
            onOpen={setSelected}
            onToggleStatus={toggleStatus}
          />
          <Pagination result={staff.data} onChange={setPage} />
        </>
      )}

      {selected !== null && (
        <StaffDrawer
          member={selected}
          onClose={() => setSelected(null)}
          onEdit={staffId => {
            setSelected(null)
            setEditor({ staffId })
          }}
          onChanged={staff.reload}
        />
      )}
    </section>
  )
}
