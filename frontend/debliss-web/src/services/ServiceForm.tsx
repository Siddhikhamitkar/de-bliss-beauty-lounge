import { useState } from 'react'
import type { FormEvent } from 'react'
import { ApiError, describeFailure } from '../api/client.ts'
import {
  createService,
  getService,
  listCategories,
  listStaffOptions,
  updateService,
} from '../api/catalogue.ts'
import type {
  SaveServiceRequest,
  Service,
  ServiceCategory,
  StaffOption,
} from '../api/contracts.ts'
import { useAsync } from '../useAsync.ts'
import { FieldErrors } from '../components/FieldErrors.tsx'

interface ServiceEditorProps {
  serviceId: string | null
  onSaved: () => void
  onCancel: () => void
}

// Loads everything the form needs, then hands a complete starting point to the
// stateless ServiceForm. Remounting ServiceForm via key resets its draft when the
// underlying service changes or after a save-and-continue.
export function ServiceEditor({ serviceId, onSaved, onCancel }: ServiceEditorProps) {
  const existing = useAsync<Service | null>(
    () => (serviceId === null ? Promise.resolve(null) : getService(serviceId)),
    [serviceId],
  )
  const categories = useAsync(() => listCategories(), [])
  const staff = useAsync(() => listStaffOptions(), [])

  const failure = existing.failure ?? categories.failure ?? staff.failure
  if (failure !== undefined) {
    return (
      <section className="panel" aria-label="Service form">
        <div className="panel-header">
          <h2>{serviceId === null ? 'New service' : 'Edit service'}</h2>
        </div>
        <p className="banner">{failure}</p>
        <div className="form-actions">
          <button type="button" className="button" onClick={onCancel}>
            Back to services
          </button>
        </div>
      </section>
    )
  }

  if (existing.data === undefined || categories.data === undefined || staff.data === undefined) {
    return (
      <section className="panel" aria-label="Service form">
        <div className="panel-header">
          <h2>{serviceId === null ? 'New service' : 'Edit service'}</h2>
        </div>
        <p>Loading…</p>
      </section>
    )
  }

  return (
    <ServiceForm
      key={serviceId ?? 'new'}
      initial={existing.data}
      categories={categories.data}
      staffOptions={staff.data}
      onSaved={onSaved}
      onCancel={onCancel}
    />
  )
}

interface ServiceFormProps {
  initial: Service | null
  categories: ServiceCategory[]
  staffOptions: StaffOption[]
  onSaved: () => void
  onCancel: () => void
}

// Mirrors ServiceCatalogueLimits on the API. The server is the authority on these
// bounds (it validates and reports violations); the client only carries them so the
// input boxes can express them.
const Limits = {
  name: 150,
  description: 1000,
  durationMinutes: 1440,
  price: 100000,
} as const

interface Draft {
  name: string
  categoryId: string
  description: string
  durationMinutes: string
  price: string
  isActive: boolean
  isOnlineBookable: boolean
  sortOrder: string
  eligibleStaffIds: string[]
}

function toDraft(service: Service | null): Draft {
  if (service === null) {
    return {
      name: '',
      categoryId: '',
      description: '',
      durationMinutes: '60',
      price: '',
      isActive: true,
      isOnlineBookable: true,
      sortOrder: '0',
      eligibleStaffIds: [],
    }
  }

  return {
    name: service.name,
    categoryId: service.categoryId ?? '',
    description: service.description ?? '',
    durationMinutes: String(service.durationMinutes),
    price: String(service.price),
    isActive: service.isActive,
    isOnlineBookable: service.isOnlineBookable,
    sortOrder: String(service.sortOrder),
    eligibleStaffIds: service.eligibleStaff.map(member => member.staffId),
  }
}

export function ServiceForm({ initial, categories, staffOptions, onSaved, onCancel }: ServiceFormProps) {
  const [draft, setDraft] = useState<Draft>(() => toDraft(initial))
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  const [failure, setFailure] = useState<string>()
  const [isSaving, setIsSaving] = useState(false)

  const toggleStaff = (staffId: string) => {
    setDraft(current => ({
      ...current,
      eligibleStaffIds: current.eligibleStaffIds.includes(staffId)
        ? current.eligibleStaffIds.filter(id => id !== staffId)
        : [...current.eligibleStaffIds, staffId],
    }))
  }

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setFailure(undefined)
    setFieldErrors({})

    // JSON has no NaN, so an unparsable box would otherwise become null on the wire
    // and the server's "required" error would point at the wrong thing. The client
    // therefore owns this one check; every business rule stays on the server.
    const failures: Record<string, string[]> = {}
    const durationMinutes = readNumber(draft.durationMinutes, 'durationMinutes', 'duration', failures)
    const price = readNumber(draft.price, 'price', 'price', failures)
    // Position 0 is meaningful ("no specific position"), so a blank box means 0
    // rather than an error.
    const sortOrder = draft.sortOrder.trim() === ''
      ? 0
      : readNumber(draft.sortOrder, 'sortOrder', 'position', failures)

    if (Object.keys(failures).length > 0) {
      setFieldErrors(failures)
      return
    }

    const request: SaveServiceRequest = {
      name: draft.name,
      // The API treats an empty categoryId as "leave uncategorised"; a blank box is
      // that, not a validation error.
      categoryId: draft.categoryId === '' ? null : draft.categoryId,
      description: draft.description,
      durationMinutes,
      price,
      isActive: draft.isActive,
      isOnlineBookable: draft.isOnlineBookable,
      sortOrder,
      eligibleStaffIds: draft.eligibleStaffIds,
    }

    setIsSaving(true)
    try {
      if (initial === null) {
        await createService(request)
      } else {
        await updateService(initial.id, request)
      }
      onSaved()
    } catch (error) {
      // A validation problem carries per-field messages to show beside each input;
      // anything else is a single message for the form-level banner.
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
    <section className="panel" aria-label="Service form">
      <div className="panel-header">
        <h2>{initial === null ? 'New service' : `Edit ${initial.name}`}</h2>
      </div>

      {failure !== undefined && <p className="banner">{failure}</p>}

      <form className="form" onSubmit={submit} noValidate>
        <label className="field field-wide">
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
          Category
          <select
            value={draft.categoryId}
            onChange={event => setDraft(current => ({ ...current, categoryId: event.target.value }))}
          >
            <option value="">Uncategorised</option>
            {categories.map(category => (
              <option key={category.id} value={category.id}>
                {category.name}
                {category.isActive ? '' : ' (inactive)'}
              </option>
            ))}
          </select>
          <FieldErrors messages={fieldErrors.categoryId} />
        </label>

        <label className="field">
          Duration (minutes)
          <input
            type="number"
            className="numeric"
            min={1}
            max={Limits.durationMinutes}
            step={1}
            value={draft.durationMinutes}
            onChange={event => setDraft(current => ({ ...current, durationMinutes: event.target.value }))}
            required
          />
          <FieldErrors messages={fieldErrors.durationMinutes} />
        </label>

        <label className="field">
          Price
          <input
            type="number"
            className="numeric"
            min={0}
            max={Limits.price}
            step="0.01"
            value={draft.price}
            onChange={event => setDraft(current => ({ ...current, price: event.target.value }))}
            required
          />
          <FieldErrors messages={fieldErrors.price} />
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

        <label className="checkbox">
          <input
            type="checkbox"
            checked={draft.isOnlineBookable}
            onChange={event => setDraft(current => ({ ...current, isOnlineBookable: event.target.checked }))}
          />
          Bookable online
        </label>

        <fieldset className="staff-picker field-wide">
          <legend>Eligible staff</legend>
          {staffOptions.length === 0 ? (
            <p className="row-note">No staff found. Add staff members before assigning services.</p>
          ) : (
            staffOptions.map(option => (
              <label key={option.id} className="checkbox">
                <input
                  type="checkbox"
                  checked={draft.eligibleStaffIds.includes(option.id)}
                  onChange={() => toggleStaff(option.id)}
                />
                {option.displayName}
                {option.jobTitle === null ? '' : ` · ${option.jobTitle}`}
                {option.isActive ? '' : ' (inactive)'}
              </label>
            ))
          )}
          <FieldErrors messages={fieldErrors.eligibleStaffIds} />
        </fieldset>

        <div className="form-actions field-wide">
          <button type="submit" className="button button-primary" disabled={isSaving}>
            {isSaving ? 'Saving…' : 'Save service'}
          </button>
          <button type="button" className="button" onClick={onCancel} disabled={isSaving}>
            Cancel
          </button>
        </div>
      </form>
    </section>
  )
}

function readNumber(
  raw: string,
  field: string,
  label: string,
  failures: Record<string, string[]>,
): number {
  if (raw.trim() === '') {
    failures[field] = [`Enter the ${label}.`]
    return 0
  }
  const value = Number(raw)
  if (!Number.isFinite(value)) {
    failures[field] = [`Enter the ${label} as a number.`]
    return 0
  }
  return value
}
