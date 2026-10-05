import { useState } from 'react'
import { ApiError, describeFailure } from '../api/client.ts'
import { searchIndividualServices } from '../api/catalogue.ts'
import { getStaffServices, setStaffServices } from '../api/staff.ts'
import { useAsync } from '../useAsync.ts'
import { FieldErrors } from '../components/FieldErrors.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { EmptyState } from '../components/EmptyState.tsx'
import { ErrorState } from '../components/ErrorState.tsx'

/**
 * The services a member can perform. View mode lists the assignment; manage
 * mode offers the whole catalogue as checkboxes and replaces the set on save,
 * mirroring the staff picker on the service form.
 */
export function ServiceAssignment({ staffId }: { staffId: string }) {
  const [isManaging, setIsManaging] = useState(false)
  const [selectedIds, setSelectedIds] = useState<string[]>([])
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  const [failure, setFailure] = useState<string>()
  const [isSaving, setIsSaving] = useState(false)

  const assigned = useAsync(() => getStaffServices(staffId), [staffId])
  // The picker shows the whole catalogue so an inactive service stays visible
  // on the member it is assigned to; the salon runs fewer services than one
  // page holds, so a single max-size page is enough for now. Bundles are left
  // out because eligibility is a property of a service, not of a bundle.
  const catalogue = useAsync(() => searchIndividualServices({ pageSize: 200 }), [])

  function startManaging() {
    setSelectedIds((assigned.data ?? []).map(service => service.serviceId))
    setFieldErrors({})
    setFailure(undefined)
    setIsManaging(true)
  }

  function toggle(serviceId: string) {
    setSelectedIds(current =>
      current.includes(serviceId)
        ? current.filter(id => id !== serviceId)
        : [...current, serviceId],
    )
  }

  async function save() {
    setFailure(undefined)
    setFieldErrors({})
    setIsSaving(true)
    try {
      await setStaffServices(staffId, selectedIds)
      setIsManaging(false)
      assigned.reload()
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

  const failureMessage = assigned.failure ?? catalogue.failure
  const reload = assigned.failure !== undefined ? assigned.reload : catalogue.reload

  return (
    <section className="tab-section" aria-label="Services">
      <div className="tab-section-head">
        <h4>Services</h4>
        {!isManaging && (
          <button type="button" className="button" onClick={startManaging}>
            Manage services
          </button>
        )}
      </div>

      {failure !== undefined && <p className="banner" role="alert">{failure}</p>}

      {failureMessage !== undefined && <ErrorState message={failureMessage} onRetry={reload} />}

      {failureMessage === undefined && (assigned.data === undefined || catalogue.data === undefined) && (
        <LoadingState label="Loading services…" />
      )}

      {failureMessage === undefined && assigned.data !== undefined && catalogue.data !== undefined && (
        isManaging ? (
          <fieldset className="staff-picker">
            <legend>Services this member can perform</legend>
            {catalogue.data.items.length === 0 ? (
              <p className="row-note">No services found. Add services before assigning staff.</p>
            ) : (
              catalogue.data.items.map(service => (
                <label key={service.id} className="checkbox">
                  <input
                    type="checkbox"
                    checked={selectedIds.includes(service.id)}
                    onChange={() => toggle(service.id)}
                  />
                  {service.name}
                  {service.categoryName === null ? '' : ` · ${service.categoryName}`}
                  {service.isActive ? '' : ' (inactive)'}
                </label>
              ))
            )}
            <FieldErrors messages={fieldErrors.serviceIds} />
            <div className="form-actions">
              <button
                type="button"
                className="button button-primary"
                onClick={save}
                disabled={isSaving}
              >
                {isSaving ? 'Saving…' : 'Save services'}
              </button>
              <button
                type="button"
                className="button"
                onClick={() => setIsManaging(false)}
                disabled={isSaving}
              >
                Cancel
              </button>
            </div>
          </fieldset>
        ) : assigned.data.length === 0 ? (
          <EmptyState>No services assigned yet.</EmptyState>
        ) : (
          <table className="grid">
            <thead>
              <tr>
                <th scope="col">Service</th>
                <th scope="col">Category</th>
                <th scope="col">Status</th>
              </tr>
            </thead>
            <tbody>
              {assigned.data.map(service => (
                <tr key={service.serviceId} className={service.isActive ? undefined : 'row-warning'}>
                  <th scope="row">
                    {service.name}
                    {!service.isActive && <span className="row-note">inactive service</span>}
                  </th>
                  <td>{service.categoryName ?? '—'}</td>
                  <td>{service.isActive ? 'Active' : 'Inactive'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )
      )}
    </section>
  )
}
