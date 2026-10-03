import { useState } from 'react'
import { listCategories, searchServices, setServiceStatus } from '../api/catalogue.ts'
import { describeFailure } from '../api/client.ts'
import type { ServiceStatusFilter, ServiceSummary } from '../api/contracts.ts'
import { Pagination } from '../components/Pagination.tsx'
import { StatusPill } from '../components/StatusPill.tsx'
import { formatDuration, formatPrice } from '../format.ts'
import { useAsync } from '../useAsync.ts'
import { ServiceEditor } from './ServiceForm.tsx'

/**
 * Which service the editor is open on, or null while the list is showing. A
 * null `id` means "create", so one piece of state covers both directions.
 */
interface Editor {
  id: string | null
}

export function ServicesPage() {
  const [search, setSearch] = useState('')
  const [appliedSearch, setAppliedSearch] = useState('')
  const [categoryId, setCategoryId] = useState('')
  const [status, setStatus] = useState<ServiceStatusFilter>('All')
  const [bookableOnly, setBookableOnly] = useState(false)
  const [page, setPage] = useState(1)
  const [editor, setEditor] = useState<Editor | null>(null)
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
        <button type="button" className="button button-primary" onClick={() => setEditor({ id: null })}>
          New service
        </button>
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
        <p className="banner" role="alert">{services.failure}</p>
      )}
      {actionFailure !== undefined && (
        <p className="banner" role="alert">{actionFailure}</p>
      )}

      {services.data === undefined && services.failure === undefined && (
        <p className="empty">Loading services…</p>
      )}

      {services.data !== undefined && services.data.items.length === 0 && (
        <p className="empty">No services match these filters.</p>
      )}

      {services.data !== undefined && services.data.items.length > 0 && (
        <>
          <table className="grid">
            <thead>
              <tr>
                <th scope="col">Service</th>
                <th scope="col">Category</th>
                <th scope="col">Duration</th>
                <th scope="col" className="numeric">Price</th>
                <th scope="col" className="numeric">Staff</th>
                <th scope="col">Status</th>
                <th scope="col">Online</th>
                <th scope="col">Actions</th>
              </tr>
            </thead>
            <tbody>
              {services.data.items.map(service => {
                // An active service that cannot actually be booked is the case
                // worth flagging: its category went inactive, or everybody who
                // could perform it left. An inactive one is simply switched off.
                const stuck = service.isActive && !service.isBookable

                return (
                  <tr key={service.id} className={stuck ? 'row-warning' : undefined}>
                    <th scope="row">
                      {service.name}
                      {stuck && <span className="row-note">not bookable</span>}
                    </th>
                    <td>{service.categoryName ?? '—'}</td>
                    <td>{formatDuration(service.durationMinutes)}</td>
                    <td className="numeric">{formatPrice(service.price)}</td>
                    <td className="numeric">{service.eligibleStaffCount}</td>
                    <td><StatusPill isActive={service.isActive} /></td>
                    <td>{service.isOnlineBookable ? 'Yes' : 'No'}</td>
                    <td className="actions">
                      <button
                        type="button"
                        className="button"
                        onClick={() => setEditor({ id: service.id })}
                      >
                        Edit
                      </button>
                      <button
                        type="button"
                        className="button"
                        onClick={() => toggleStatus(service)}
                      >
                        {service.isActive ? 'Deactivate' : 'Activate'}
                      </button>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>

          <Pagination result={services.data} onChange={setPage} />
        </>
      )}
    </section>
  )
}
