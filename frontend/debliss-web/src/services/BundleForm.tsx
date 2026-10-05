import { useState } from 'react'
import type { FormEvent, ReactNode } from 'react'
import { ApiError, describeFailure } from '../api/client.ts'
import {
  createBundle,
  getBundle,
  listCategories,
  searchIndividualServices,
  updateBundle,
} from '../api/catalogue.ts'
import type {
  Bundle,
  SaveBundleRequest,
  ServiceCategory,
  ServiceSummary,
} from '../api/contracts.ts'
import { useAsync } from '../useAsync.ts'
import { FieldErrors } from '../components/FieldErrors.tsx'
import { formatDuration, formatPrice } from '../format.ts'

interface BundleEditorProps {
  bundleId: string | null
  onSaved: () => void
  onCancel: () => void
}

/** A service the component picker can offer, reduced to what the form needs. */
export interface ComponentOption {
  id: string
  name: string
  categoryName: string | null
  durationMinutes: number
  price: number
  isActive: boolean
}

/**
 * Loads everything the bundle form needs, then hands a complete starting point
 * to the stateless BundleForm. Remounting via key resets the draft when the
 * underlying bundle changes.
 */
export function BundleEditor({ bundleId, onSaved, onCancel }: BundleEditorProps) {
  const existing = useAsync<Bundle | null>(
    () => (bundleId === null ? Promise.resolve(null) : getBundle(bundleId)),
    [bundleId],
  )
  const categories = useAsync(() => listCategories(), [])
  // Only active services can be added to a bundle, so that is all the picker
  // offers. Components that have since gone inactive are merged back in from
  // the loaded bundle below — hiding them would let a save silently drop them.
  // Bundles are excluded: a bundle is made of services, never of other bundles.
  const services = useAsync(
    () => searchIndividualServices({ status: 'Active', pageSize: 200 }),
    [],
  )

  const failure = existing.failure ?? categories.failure ?? services.failure
  if (failure !== undefined) {
    return (
      <BundleFormShell title={bundleId === null ? 'New bundle' : 'Edit bundle'}>
        <p className="banner">{failure}</p>
        <div className="form-actions">
          <button type="button" className="button" onClick={onCancel}>
            Back to services
          </button>
        </div>
      </BundleFormShell>
    )
  }

  if (
    existing.data === undefined ||
    categories.data === undefined ||
    services.data === undefined
  ) {
    return (
      <BundleFormShell title={bundleId === null ? 'New bundle' : 'Edit bundle'}>
        <p>Loading…</p>
      </BundleFormShell>
    )
  }

  return (
    <BundleForm
      key={bundleId ?? 'new'}
      initial={existing.data}
      categories={categories.data}
      options={componentOptions(services.data.items, existing.data)}
      onSaved={onSaved}
      onCancel={onCancel}
    />
  )
}

function BundleFormShell({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="panel" aria-label="Bundle form">
      <div className="panel-header">
        <h2>{title}</h2>
      </div>
      {children}
    </section>
  )
}

/** The picker's offer list plus any already-chosen component it no longer shows. */
function componentOptions(services: ServiceSummary[], bundle: Bundle | null): ComponentOption[] {
  const options: ComponentOption[] = services.map(service => ({
    id: service.id,
    name: service.name,
    categoryName: service.categoryName,
    durationMinutes: service.durationMinutes,
    price: service.price,
    isActive: service.isActive,
  }))

  if (bundle === null) {
    return options
  }

  const offered = new Set(options.map(option => option.id))

  return [
    ...options,
    ...bundle.items
      .filter(item => !offered.has(item.serviceId))
      .map(item => ({
        id: item.serviceId,
        name: item.name,
        categoryName: null,
        durationMinutes: item.durationMinutes,
        price: item.price,
        isActive: item.isActive,
      })),
  ]
}

interface BundleFormProps {
  initial: Bundle | null
  categories: ServiceCategory[]
  options: ComponentOption[]
  onSaved: () => void
  onCancel: () => void
}

// Mirrors ServiceCatalogueLimits on the API. The server is the authority on
// these bounds; the client only carries them so the inputs can express them.
const Limits = {
  name: 150,
  description: 1000,
  price: 100000,
} as const

/** A bundle is not a bundle below this; the server rejects anything smaller. */
const MinimumComponents = 2

interface Draft {
  name: string
  categoryId: string
  description: string
  price: string
  isActive: boolean
  sortOrder: string
  componentIds: string[]
}

function toDraft(bundle: Bundle | null): Draft {
  if (bundle === null) {
    return {
      name: '',
      categoryId: '',
      description: '',
      price: '',
      isActive: true,
      sortOrder: '0',
      componentIds: [],
    }
  }

  return {
    name: bundle.name,
    categoryId: bundle.categoryId ?? '',
    description: bundle.description ?? '',
    price: String(bundle.price),
    isActive: bundle.isActive,
    sortOrder: String(bundle.sortOrder),
    componentIds: bundle.items.map(item => item.serviceId),
  }
}

export function BundleForm({
  initial,
  categories,
  options,
  onSaved,
  onCancel,
}: BundleFormProps) {
  const [draft, setDraft] = useState<Draft>(() => toDraft(initial))
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  const [failure, setFailure] = useState<string>()
  const [isSaving, setIsSaving] = useState(false)

  const byId = new Map(options.map(option => [option.id, option]))
  const selected = draft.componentIds
    .map(id => byId.get(id))
    .filter((option): option is ComponentOption => option !== undefined)
  const available = options.filter(option => !draft.componentIds.includes(option.id))

  const durationMinutes = selected.reduce((total, option) => total + option.durationMinutes, 0)
  const standardTotal = selected.reduce((total, option) => total + option.price, 0)
  const priceValue = Number(draft.price)
  const hasPrice = draft.price.trim() !== '' && Number.isFinite(priceValue)
  const savings = hasPrice ? standardTotal - priceValue : 0
  const exceedsStandard = hasPrice && priceValue > standardTotal && selected.length > 0

  function toggleComponent(serviceId: string) {
    setDraft(current => ({
      ...current,
      componentIds: current.componentIds.includes(serviceId)
        ? current.componentIds.filter(id => id !== serviceId)
        : [...current.componentIds, serviceId],
    }))
  }

  function moveComponent(serviceId: string, step: number) {
    setDraft(current => {
      const index = current.componentIds.indexOf(serviceId)
      const target = index + step
      if (index < 0 || target < 0 || target >= current.componentIds.length) {
        return current
      }

      const componentIds = [...current.componentIds]
      ;[componentIds[index], componentIds[target]] = [componentIds[target], componentIds[index]]

      return { ...current, componentIds }
    })
  }

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setFailure(undefined)
    setFieldErrors({})

    // JSON has no NaN, so an unparsable box would otherwise become null on the
    // wire and the server's "required" error would point at the wrong thing.
    const failures: Record<string, string[]> = {}
    const price = readNumber(draft.price, 'price', 'price', failures)
    // Position 0 is meaningful ("no specific position"), so a blank box means 0.
    const sortOrder = draft.sortOrder.trim() === ''
      ? 0
      : readNumber(draft.sortOrder, 'sortOrder', 'position', failures)

    if (Object.keys(failures).length > 0) {
      setFieldErrors(failures)
      return
    }

    const request: SaveBundleRequest = {
      name: draft.name,
      // Unlike a service, a bundle must sit in a category; the server says so
      // and the select is required, so an empty value never gets that far.
      categoryId: draft.categoryId === '' ? null : draft.categoryId,
      description: draft.description,
      price,
      isActive: draft.isActive,
      sortOrder,
      items: draft.componentIds,
    }

    setIsSaving(true)
    try {
      if (initial === null) {
        await createBundle(request)
      } else {
        await updateBundle(initial.id, request)
      }
      onSaved()
    } catch (error) {
      // A validation problem carries per-field messages to show beside each
      // input; anything else is a single message for the form-level banner.
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
    <section className="panel" aria-label="Bundle form">
      <div className="panel-header">
        <h2>{initial === null ? 'New bundle' : `Edit ${initial.name}`}</h2>
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
            required
          >
            <option value="">Choose a category…</option>
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

        <fieldset className="bundle-picker field-wide">
          <legend>Services in this bundle</legend>
          <p className="row-note">
            Shown in delivery order — the sequence the appointment is performed in.
          </p>

          {selected.length === 0 ? (
            <p className="bundle-empty">
              Pick at least {MinimumComponents} services below to start a bundle.
            </p>
          ) : (
            <ol className="bundle-components">
              {selected.map((option, index) => (
                <li key={option.id} className="bundle-component">
                  <span className="bundle-component-name">
                    {option.name}
                    {!option.isActive && <span className="roster-note">inactive</span>}
                  </span>
                  <span className="bundle-component-meta">
                    {formatDuration(option.durationMinutes)} · {formatPrice(option.price)}
                  </span>
                  <span className="bundle-component-actions">
                    <button
                      type="button"
                      className="button button-compact"
                      disabled={index === 0}
                      onClick={() => moveComponent(option.id, -1)}
                      aria-label={`Move ${option.name} earlier`}
                    >
                      ↑
                    </button>
                    <button
                      type="button"
                      className="button button-compact"
                      disabled={index === selected.length - 1}
                      onClick={() => moveComponent(option.id, 1)}
                      aria-label={`Move ${option.name} later`}
                    >
                      ↓
                    </button>
                    <button
                      type="button"
                      className="button button-compact"
                      onClick={() => toggleComponent(option.id)}
                      aria-label={`Remove ${option.name} from the bundle`}
                    >
                      Remove
                    </button>
                  </span>
                </li>
              ))}
            </ol>
          )}

          <div className="bundle-available">
            {available.length === 0 ? (
              <p className="bundle-empty">
                No further services to add. Every active service is already in this bundle.
              </p>
            ) : (
              available.map(option => (
                <label key={option.id} className="checkbox">
                  <input
                    type="checkbox"
                    checked={false}
                    onChange={() => toggleComponent(option.id)}
                  />
                  {option.name}
                  {option.categoryName === null ? '' : ` · ${option.categoryName}`}
                  <span className="bundle-component-meta">{formatPrice(option.price)}</span>
                </label>
              ))
            )}
          </div>

          <FieldErrors messages={fieldErrors.items} />
        </fieldset>

        <label className="field">
          Bundle price
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

        <div className="field bundle-summary">
          <span>What the customer gets</span>
          <dl>
            <dt>Services</dt>
            <dd>{selected.length}</dd>
            <dt>Duration</dt>
            <dd>{formatDuration(durationMinutes)}</dd>
            <dt>Bought separately</dt>
            <dd>{formatPrice(standardTotal)}</dd>
            <dt>Saving</dt>
            <dd>{hasPrice ? formatPrice(savings) : '—'}</dd>
          </dl>
          {exceedsStandard && (
            <p className="field-errors" role="alert">
              A bundle cannot cost more than its services bought separately.
            </p>
          )}
        </div>

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
            {isSaving ? 'Saving…' : 'Save bundle'}
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
