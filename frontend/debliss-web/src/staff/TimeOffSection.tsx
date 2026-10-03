import { useState } from 'react'
import type { FormEvent } from 'react'
import { ApiError, describeFailure } from '../api/client.ts'
import { createTimeOff, deleteTimeOff, listTimeOff, updateTimeOff } from '../api/staff.ts'
import type { SaveStaffTimeOffRequest, StaffTimeOff } from '../api/contracts.ts'
import { formatDateTime } from '../format.ts'
import { useAsync } from '../useAsync.ts'
import { FieldErrors } from '../components/FieldErrors.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { EmptyState } from '../components/EmptyState.tsx'
import { ErrorState } from '../components/ErrorState.tsx'
import { ConfirmationDialog } from '../components/ConfirmationDialog.tsx'

/** Enum names the API accepts, in the order the picker shows them. */
const TimeOffTypes = ['Leave', 'Sick', 'Holiday', 'Training', 'Personal', 'Other'] as const

// Friendly labels over the wire names; an unknown future name still renders.
const TimeOffTypeLabels: Record<string, string> = {
  Leave: 'Annual leave',
  Sick: 'Sick leave',
  Holiday: 'Holiday',
  Training: 'Training',
  Personal: 'Personal leave',
  Other: 'Other',
}

function timeOffTypeLabel(name: string): string {
  return TimeOffTypeLabels[name] ?? name
}

/** An ISO timestamp as a datetime-local box value, and back. */
function toLocalInputValue(iso: string): string {
  const date = new Date(iso)
  const pad = (value: number) => String(value).padStart(2, '0')

  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`
}

function fromLocalInputValue(local: string): string {
  return new Date(local).toISOString()
}

type TimeOffFormState = { kind: 'create' } | { kind: 'edit'; entry: StaffTimeOff } | null

/**
 * Dated leave, sick, and training records. Entries are never edited away:
 * updates change a record in place, and removal is confirmed first and
 * refused by the API once an entry has started.
 */
export function TimeOffSection({ staffId }: { staffId: string }) {
  const timeOff = useAsync(() => listTimeOff(staffId), [staffId])
  const [form, setForm] = useState<TimeOffFormState>(null)
  const [pendingDelete, setPendingDelete] = useState<StaffTimeOff | null>(null)
  const [isDeleting, setIsDeleting] = useState(false)
  const [deleteFailure, setDeleteFailure] = useState<string>()

  function closeForm() {
    setForm(null)
  }

  return (
    <section className="panel" aria-label="Time off">
      <div className="panel-header">
        <h2>Time off</h2>
        {form === null && (
          <button
            type="button"
            className="button button-primary"
            onClick={() => setForm({ kind: 'create' })}
          >
            Add time off
          </button>
        )}
      </div>

      {deleteFailure !== undefined && (
        <p className="banner" role="alert">{deleteFailure}</p>
      )}

      {timeOff.failure !== undefined && (
        <ErrorState message={timeOff.failure} onRetry={timeOff.reload} />
      )}

      {timeOff.failure === undefined && timeOff.data === undefined && (
        <LoadingState label="Loading time off…" />
      )}

      {timeOff.failure === undefined && timeOff.data !== undefined && form === null && (
        timeOff.data.length === 0 ? (
          <EmptyState>No time-off records yet.</EmptyState>
        ) : (
          <TimeOffList
            entries={timeOff.data}
            onEdit={entry => setForm({ kind: 'edit', entry })}
            onDelete={entry => setPendingDelete(entry)}
          />
        )
      )}

      {form !== null && (
        <TimeOffForm
          key={form.kind === 'edit' ? form.entry.id : 'create'}
          initial={form.kind === 'edit' ? form.entry : null}
          onSubmit={async body => {
            if (form.kind === 'edit') {
              await updateTimeOff(staffId, form.entry.id, body)
            } else {
              await createTimeOff(staffId, body)
            }
          }}
          onSaved={() => {
            closeForm()
            timeOff.reload()
          }}
          onCancel={closeForm}
        />
      )}

      {pendingDelete !== null && (
        <ConfirmationDialog
          title="Remove this time-off entry?"
          confirmLabel="Remove"
          busy={isDeleting}
          onCancel={() => setPendingDelete(null)}
          onConfirm={() => {
            setDeleteFailure(undefined)
            setIsDeleting(true)
            deleteTimeOff(staffId, pendingDelete.id)
              .then(() => {
                setPendingDelete(null)
                timeOff.reload()
              })
              .catch((error: unknown) => {
                // A started entry comes back as 409 and stays on the record.
                // Close the dialog so the section banner can say so.
                setPendingDelete(null)
                setDeleteFailure(describeFailure(error))
              })
              .finally(() => setIsDeleting(false))
          }}
        >
          <p>
            {timeOffTypeLabel(pendingDelete.type)},{' '}
            {formatDateTime(pendingDelete.startsAt)} – {formatDateTime(pendingDelete.endsAt)}, will
            be removed. Entries that have already started cannot be removed.
          </p>
        </ConfirmationDialog>
      )}
    </section>
  )
}

interface TimeOffListProps {
  entries: StaffTimeOff[]
  onEdit: (entry: StaffTimeOff) => void
  onDelete: (entry: StaffTimeOff) => void
}

export function TimeOffList({ entries, onEdit, onDelete }: TimeOffListProps) {
  return (
    <table className="grid">
      <thead>
        <tr>
          <th scope="col">Type</th>
          <th scope="col">From</th>
          <th scope="col">To</th>
          <th scope="col">Reason</th>
          <th scope="col">Actions</th>
        </tr>
      </thead>
      <tbody>
        {entries.map(entry => (
          <tr key={entry.id}>
            <th scope="row">{timeOffTypeLabel(entry.type)}</th>
            <td>{formatDateTime(entry.startsAt)}</td>
            <td>{formatDateTime(entry.endsAt)}</td>
            <td>{entry.reason ?? '—'}</td>
            <td className="actions">
              <button type="button" className="button" onClick={() => onEdit(entry)}>
                Edit
              </button>
              <button type="button" className="button" onClick={() => onDelete(entry)}>
                Delete
              </button>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}

interface TimeOffFormProps {
  initial: StaffTimeOff | null
  onSubmit: (body: SaveStaffTimeOffRequest) => Promise<void>
  onSaved: () => void
  onCancel: () => void
}

export function TimeOffForm({ initial, onSubmit, onSaved, onCancel }: TimeOffFormProps) {
  const [type, setType] = useState(initial?.type ?? '')
  const [startsAt, setStartsAt] = useState(() =>
    initial === null ? '' : toLocalInputValue(initial.startsAt),
  )
  const [endsAt, setEndsAt] = useState(() =>
    initial === null ? '' : toLocalInputValue(initial.endsAt),
  )
  const [reason, setReason] = useState(initial?.reason ?? '')
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  const [failure, setFailure] = useState<string>()
  const [isSaving, setIsSaving] = useState(false)

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setFailure(undefined)
    setFieldErrors({})

    // A blank datetime-local box has no usable value; the server expects a
    // real timestamp, so this is the one check the client owns.
    const failures: Record<string, string[]> = {}
    if (startsAt === '') {
      failures.startsAt = ['Choose a start date and time.']
    }
    if (endsAt === '') {
      failures.endsAt = ['Choose an end date and time.']
    }

    if (Object.keys(failures).length > 0) {
      setFieldErrors(failures)
      return
    }

    setIsSaving(true)
    try {
      await onSubmit({
        type: type === '' ? null : type,
        startsAt: fromLocalInputValue(startsAt),
        endsAt: fromLocalInputValue(endsAt),
        reason: reason,
      })
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
    <form className="form" onSubmit={submit} noValidate>
      {failure !== undefined && <p className="banner" role="alert">{failure}</p>}

      <label className="field">
        Type
        <select value={type} onChange={event => setType(event.target.value)}>
          <option value="">Choose a type</option>
          {TimeOffTypes.map(name => (
            <option key={name} value={name}>{timeOffTypeLabel(name)}</option>
          ))}
        </select>
        <FieldErrors messages={fieldErrors.type} />
      </label>

      <label className="field">
        Starts
        <input
          type="datetime-local"
          value={startsAt}
          onChange={event => setStartsAt(event.target.value)}
        />
        <FieldErrors messages={fieldErrors.startsAt} />
      </label>

      <label className="field">
        Ends
        <input
          type="datetime-local"
          value={endsAt}
          onChange={event => setEndsAt(event.target.value)}
        />
        <FieldErrors messages={fieldErrors.endsAt} />
      </label>

      <label className="field field-wide">
        Reason
        <textarea
          rows={2}
          value={reason}
          onChange={event => setReason(event.target.value)}
          maxLength={500}
        />
        <FieldErrors messages={fieldErrors.reason} />
      </label>

      <div className="form-actions">
        <button type="submit" className="button button-primary" disabled={isSaving}>
          {isSaving ? 'Saving…' : initial === null ? 'Add time off' : 'Save time off'}
        </button>
        <button type="button" className="button" onClick={onCancel} disabled={isSaving}>
          Cancel
        </button>
      </div>
    </form>
  )
}
