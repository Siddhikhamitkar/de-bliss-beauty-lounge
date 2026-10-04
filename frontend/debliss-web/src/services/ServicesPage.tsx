import { useState } from 'react'
import { listCategories, searchServices, setServiceStatus } from '../api/catalogue.ts'
import { describeFailure } from '../api/client.ts'
import type { ServiceStatusFilter, ServiceSummary } from '../api/contracts.ts'
import { EmptyState } from '../components/EmptyState.tsx'
import { ErrorState } from '../components/ErrorState.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { Pagination } from '../components/Pagination.tsx'
import { useAsync } from '../useAsync.ts'
import { ServiceDrawer } from './ServiceDrawer.tsx'
import { ServiceEditor } from './ServiceForm.tsx'
import { ServiceList } from './ServiceList.tsx'

/**
 * Which service the editor is open on, or null while the list is showing. A
 * null `id` means "create", so one piece of state covers both directions.
 */
interface Editor {
  id: string | null
}

export function ServicesPage({ onManageCategories }: { onManageCategories?: () => void }) {
  const [search, setSearch] = useState('')
  const [appliedSearch, setAppliedSearch] = useState('')
  const [categoryId, setCategoryId] = useState('')
  const [status, setStatus] = useState<ServiceStatusFilter>('All')
  const [bookableOnly, setBookableOnly] = useState(false)
  const [page, setPage] = useState(1)
  const [editor, setEditor] = useState<Editor | null>(null)
  const [selected, setSelected] = useState<ServiceSummary | null>(null)
  const [actionFailure, setActionFailure] = useState<string>()

  const categories = useAsync(() => listCategories(), [])

  const services = useAsync(
    () =>
      searchServices({
        search: appliedSearch === '' ? undefined : appliedSearch,
        categoryId: categoryId === '' ? undefined : categoryId,
        status: status === 'All' ? undefined : status,
        bookableOnly: bookableOnly ? true : undefined,
        page: page > 1 ? page : undefined,
      }),
    [appliedSearch, categoryId, status, bookableOnly, page],
  )

  function toggleStatus(service: ServiceSummary) {
    setActionFailure(undefined)

    setServiceStatus(service.id, !service.isActive)
      .then(() => services.reload())
      .catch((error: unknown) => setActionFailure(describeFailure(error)))
  }

  if (editor !== null) {
    return (
      <ServiceEditor
        serviceId={editor.id}
        onSaved={() => {
          setEditor(null)
          services.reload()
        }}
        onCancel={() => setEditor(null)}
      />
    )
  }

  return (
    <section className="panel">
      <div className="panel-header">
        <h2>Services</h2>
        <div className="panel-actions">
          {onManageCategories !== undefined && (
            <button type="button" className="button" onClick={onManageCategories}>
              Manage categories
            </button>
          )}
          <button type="button" className="button button-primary" onClick={() => setEditor({ id: null })}>
            New service
          </button>
        </div>
      </div>

      <form
        className="filters"
        onSubmit={event => {
          event.preventDefault()
          setPage(1)
          setAppliedSearch(search.trim())
        }}
      >
        <label className="field">
          <span>Search</span>
          <input
            type="search"
            name="search"
            placeholder="Name or description"
            value={search}
            onChange={event => setSearch(event.target.value)}
          />
        </label>

        <label className="field">
          <span>Category</span>
          <select
            name="categoryFilter"
            value={categoryId}
            onChange={event => {
              setCategoryId(event.target.value)
              setPage(1)
            }}
          >
            <option value="">All categories</option>
            {(categories.data ?? []).map(category => (
              <option key={category.id} value={category.id}>
                {category.isActive ? category.name : `${category.name} (inactive)`}
              </option>
            ))}
          </select>
        </label>

        <label className="field">
          <span>Status</span>
          <select
            name="status"
            value={status}
            onChange={event => {
              setStatus(event.target.value as ServiceStatusFilter)
              setPage(1)
            }}
          >
            <option value="All">All</option>
            <option value="Active">Active</option>
            <option value="Inactive">Inactive</option>
          </select>
        </label>

        <label className="checkbox">
          <input
            type="checkbox"
            name="bookableOnly"
            checked={bookableOnly}
            onChange={event => {
              setBookableOnly(event.target.checked)
              setPage(1)
            }}
          />
          <span>Bookable only</span>
        </label>

        <button type="submit" className="button">Apply</button>
      </form>

      {services.failure !== undefined && (
        <ErrorState message={services.failure} onRetry={services.reload} />
      )}
      {actionFailure !== undefined && (
        <p className="banner" role="alert">{actionFailure}</p>
      )}

      {services.data === undefined && services.failure === undefined && (
        <LoadingState label="Loading services…" />
      )}

      {services.data !== undefined && services.data.items.length === 0 && (
        <EmptyState>No services match these filters.</EmptyState>
      )}

      {services.data !== undefined && services.data.items.length > 0 && (
        <>
          <ServiceList
            services={services.data.items}
            onOpen={setSelected}
            onToggleStatus={toggleStatus}
          />

          <Pagination result={services.data} onChange={setPage} />
        </>
      )}

      {selected !== null && (
        <ServiceDrawer
          service={selected}
          onClose={() => setSelected(null)}
          onEdit={serviceId => {
            setSelected(null)
            setEditor({ id: serviceId })
          }}
          onChanged={services.reload}
        />
      )}
    </section>
  )
}
