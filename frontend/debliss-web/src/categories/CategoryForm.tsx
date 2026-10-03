import { useState } from 'react'
import type { FormEvent } from 'react'
import { ApiError, describeFailure } from '../api/client.ts'
import { createCategory, getServiceCategory, updateCategory } from '../api/catalogue.ts'
import type { SaveServiceCategoryRequest, ServiceCategory } from '../api/contracts.ts'
import { useAsync } from '../useAsync.ts'
import { FieldErrors } from '../components/FieldErrors.tsx'

interface CategoryEditorProps {
  categoryId: string | null
  onSaved: () => void
  onCancel: () => void
}

// Loads the category being edited (if any), then hands a complete starting point to
// the stateless CategoryForm. Remounting via key resets the draft after a save.
export function CategoryEditor({ categoryId, onSaved, onCancel }: CategoryEditorProps) {
  const existing = useAsync<ServiceCategory | null>(
    () => (categoryId === null ? Promise.resolve(null) : getServiceCategory(categoryId)),
    [categoryId],
  )

  if (existing.failure !== undefined) {
    return (
      <section className="panel" aria-label="Category form">
        <div className="panel-header">
          <h2>{categoryId === null ? 'New category' : 'Edit category'}</h2>
        </div>
        <p className="banner">{existing.failure}</p>
        <div className="form-actions">
          <button type="button" className="button" onClick={onCancel}>
            Back to categories
          </button>
        </div>
      </section>
    )
  }

  if (existing.data === undefined) {
    return (
      <section className="panel" aria-label="Category form">
        <div className="panel-header">
          <h2>{categoryId === null ? 'New category' : 'Edit category'}</h2>
        </div>
        <p>Loading…</p>
      </section>
    )
  }

  return (
    <CategoryForm
      key={categoryId ?? 'new'}
      initial={existing.data}
      onSaved={onSaved}
      onCancel={onCancel}
    />
  )
}

interface CategoryFormProps {
  initial: ServiceCategory | null
  onSaved: () => void
  onCancel: () => void
}

// Mirrors ServiceCatalogueLimits on the API; the server remains the authority.
const Limits = {
  name: 100,
  description: 500,
} as const

interface Draft {
  name: string
  description: string
  sortOrder: string
  isActive: boolean
}

function toDraft(category: ServiceCategory | null): Draft {
  if (category === null) {
    return { name: '', description: '', sortOrder: '0', isActive: true }
  }

  return {
    name: category.name,
    description: category.description ?? '',
    sortOrder: String(category.sortOrder),
    isActive: category.isActive,
  }
}

export function CategoryForm({ initial, onSaved, onCancel }: CategoryFormProps) {
  const [draft, setDraft] = useState<Draft>(() => toDraft(initial))
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  const [failure, setFailure] = useState<string>()
  const [isSaving, setIsSaving] = useState(false)

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setFailure(undefined)
    setFieldErrors({})

    // A blank position means "unspecified", which the API stores as 0 — unlike
    // duration/price, position is not ambiguous at 0, so no client-side error here.
    const sortOrder = draft.sortOrder.trim() === '' ? 0 : Number(draft.sortOrder)
    if (!Number.isFinite(sortOrder)) {
      setFieldErrors({ sortOrder: ['Enter the position as a whole number.'] })
      return
    }

    const request: SaveServiceCategoryRequest = {
      name: draft.name,
      description: draft.description,
      sortOrder,
      isActive: draft.isActive,
    }

    setIsSaving(true)
    try {
      if (initial === null) {
        await createCategory(request)
      } else {
        await updateCategory(initial.id, request)
      }
      onSaved()
    } catch (error) {
      if (error instanceof ApiError && Object.keys(error.errors).length > 0) {
        setFieldErrors(error.errors)
      } else {
        setFailure(describeFailure(error))
      }
    } finally {
      setIsSaving(false)
    }
  }

  return (
    <section className="panel" aria-label="Category form">
      <div className="panel-header">
        <h2>{initial === null ? 'New category' : `Edit ${initial.name}`}</h2>
      </div>

      {failure !== undefined && <p className="banner">{failure}</p>}

      <form className="form" onSubmit={submit} noValidate>
        <label className="field">
          Name
          <input
            value={draft.name}
            onChange={event => setDraft(current => ({ ...current, name: event.target.value }))}
            maxLength={Limits.name}
            required
          />
          <FieldErrors messages={fieldErrors.name} />
        </label>

        <label className="field">
          Position
          <input
            type="number"
            className="numeric"
            step={1}
            value={draft.sortOrder}
            onChange={event => setDraft(current => ({ ...current, sortOrder: event.target.value }))}
          />
          <FieldErrors messages={fieldErrors.sortOrder} />
        </label>

        <label className="field field-wide">
          Description
          <textarea
            rows={3}
            value={draft.description}
            onChange={event => setDraft(current => ({ ...current, description: event.target.value }))}
            maxLength={Limits.description}
          />
          <FieldErrors messages={fieldErrors.description} />
        </label>

        <label className="checkbox">
          <input
            type="checkbox"
            checked={draft.isActive}
            onChange={event => setDraft(current => ({ ...current, isActive: event.target.checked }))}
          />
          Active
        </label>

        <div className="form-actions field-wide">
          <button type="submit" className="button button-primary" disabled={isSaving}>
            {isSaving ? 'Saving…' : 'Save category'}
          </button>
          <button type="button" className="button" onClick={onCancel} disabled={isSaving}>
            Cancel
          </button>
        </div>
      </form>
    </section>
  )
}
