import { useState } from 'react'
import { listCategories, setCategoryStatus } from '../api/catalogue.ts'
import { describeFailure } from '../api/client.ts'
import type { ServiceCategory, ServiceStatusFilter } from '../api/contracts.ts'
import { StatusPill } from '../components/StatusPill.tsx'
import { useAsync } from '../useAsync.ts'
import { CategoryEditor } from './CategoryForm.tsx'

export function CategoriesPage() {
  const [status, setStatus] = useState<ServiceStatusFilter>('All')
  const [editor, setEditor] = useState<{ id: string | null } | null>(null)
  const [actionFailure, setActionFailure] = useState<string>()

  const categories = useAsync(
    () => listCategories(status === 'All' ? undefined : status),
    [status],
  )

  function toggleStatus(category: ServiceCategory) {
    setActionFailure(undefined)

    setCategoryStatus(category.id, !category.isActive)
      .then(() => categories.reload())
      .catch((error: unknown) => setActionFailure(describeFailure(error)))
  }

  if (editor !== null) {
    return (
      <CategoryEditor
        categoryId={editor.id}
        onSaved={() => {
          setEditor(null)
          categories.reload()
        }}
        onCancel={() => setEditor(null)}
      />
    )
  }

  return (
    <section className="panel">
      <div className="panel-header">
        <h2>Service categories</h2>
        <button type="button" className="button button-primary" onClick={() => setEditor({ id: null })}>
          New category
        </button>
      </div>

      <div className="filters">
        <label className="field">
          <span>Status</span>
          <select
            name="status"
            value={status}
            onChange={event => setStatus(event.target.value as ServiceStatusFilter)}
          >
            <option value="All">All</option>
            <option value="Active">Active</option>
            <option value="Inactive">Inactive</option>
          </select>
        </label>
      </div>

      {categories.failure !== undefined && (
        <p className="banner" role="alert">{categories.failure}</p>
      )}
      {actionFailure !== undefined && (
        <p className="banner" role="alert">{actionFailure}</p>
      )}

      {categories.data === undefined && categories.failure === undefined && (
        <p className="empty">Loading categories…</p>
      )}

      {categories.data !== undefined && categories.data.length === 0 && (
        <p className="empty">No categories in this view.</p>
      )}

      {categories.data !== undefined && categories.data.length > 0 && (
        <table className="grid">
          <thead>
            <tr>
              <th scope="col">Category</th>
              <th scope="col">Description</th>
              <th scope="col" className="numeric">Position</th>
              <th scope="col" className="numeric">Services</th>
              <th scope="col">Status</th>
              <th scope="col">Actions</th>
            </tr>
          </thead>
          <tbody>
            {categories.data.map(category => (
              <tr key={category.id}>
                <th scope="row">{category.name}</th>
                <td>{category.description ?? '—'}</td>
                <td className="numeric">{category.sortOrder}</td>
                <td className="numeric">{category.serviceCount}</td>
                <td><StatusPill isActive={category.isActive} /></td>
                <td className="actions">
                  <button
                    type="button"
                    className="button"
                    onClick={() => setEditor({ id: category.id })}
                  >
                    Edit
                  </button>
                  <button
                    type="button"
                    className="button"
                    onClick={() => toggleStatus(category)}
                  >
                    {category.isActive ? 'Deactivate' : 'Activate'}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  )
}
