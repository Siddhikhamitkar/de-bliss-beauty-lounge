import { useState } from 'react'
import type { FormEvent } from 'react'
import { ApiError, describeFailure } from '../api/client.ts'
import { createStaff, getStaff, updateStaff } from '../api/staff.ts'
import type { SaveStaffRequest, StaffProfile } from '../api/contracts.ts'
import { useAsync } from '../useAsync.ts'
import { FieldErrors } from '../components/FieldErrors.tsx'

interface StaffEditorProps {
  staffId: string | null
  onSaved: () => void
  onCancel: () => void
}

// Loads the profile being edited, then hands a complete starting point to the
// stateless StaffForm. Remounting via key resets the draft when the member
// changes or after a save.
export function StaffEditor({ staffId, onSaved, onCancel }: StaffEditorProps) {
  const existing = useAsync<StaffProfile | null>(
    () => (staffId === null ? Promise.resolve(null) : getStaff(staffId)),
    [staffId],
  )

  if (existing.failure !== undefined) {
    return (
      <section className="panel" aria-label="Staff form">
        <div className="panel-header">
          <h2>{staffId === null ? 'New staff member' : 'Edit staff member'}</h2>
        </div>
        <p className="banner" role="alert">{existing.failure}</p>
        <div className="form-actions">
          <button type="button" className="button" onClick={onCancel}>
            Back to staff
          </button>
        </div>
      </section>
    )
  }

  if (existing.data === undefined) {
    return (
      <section className="panel" aria-label="Staff form">
        <div className="panel-header">
          <h2>{staffId === null ? 'New staff member' : 'Edit staff member'}</h2>
        </div>
        <p>Loading…</p>
      </section>
    )
  }

  return (
    <StaffForm
      key={staffId ?? 'new'}
      initial={existing.data}
      onSaved={onSaved}
      onCancel={onCancel}
    />
  )
}

interface StaffFormProps {
  initial: StaffProfile | null
  onSaved: () => void
  onCancel: () => void
}

// Mirrors StaffingLimits on the API. The server is the authority on these
// bounds; the client only carries them so the input boxes can express them.
const Limits = {
  name: 100,
  email: 256,
  phone: 30,
  jobTitle: 100,
  calendarColor: 9,
} as const

// Same pattern the server checks, used only to preview the colour swatch live;
// the API remains the authority and reports malformed values as field errors.
const ColorPattern = /^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$/

interface Draft {
  firstName: string
  lastName: string
  jobTitle: string
  email: string
  phoneNumber: string
  calendarColor: string
  isActive: boolean
  sortOrder: string
}

function toDraft(staff: StaffProfile | null): Draft {
  if (staff === null) {
    return {
      firstName: '',
      lastName: '',
      jobTitle: '',
      email: '',
      phoneNumber: '',
      calendarColor: '',
      isActive: true,
      sortOrder: '0',
    }
  }

  return {
    firstName: staff.firstName,
    lastName: staff.lastName,
    jobTitle: staff.jobTitle ?? '',
    email: staff.email ?? '',
    phoneNumber: staff.phoneNumber ?? '',
    calendarColor: staff.calendarColor ?? '',
    isActive: staff.isActive,
    sortOrder: String(staff.sortOrder),
  }
}

export function StaffForm({ initial, onSaved, onCancel }: StaffFormProps) {
  const [draft, setDraft] = useState<Draft>(() => toDraft(initial))
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  const [failure, setFailure] = useState<string>()
  const [isSaving, setIsSaving] = useState(false)

  const swatch = ColorPattern.test(draft.calendarColor) ? draft.calendarColor : undefined

  const update = (field: keyof Draft) => (event: { target: { value: string } }) =>
    setDraft(current => ({ ...current, [field]: event.target.value }))

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setFailure(undefined)
    setFieldErrors({})

    // Position 0 is meaningful ("no specific position"), so a blank box means 0
    // rather than an error.
    const failures: Record<string, string[]> = {}
    const sortOrder =
      draft.sortOrder.trim() === '' ? 0 : readNumber(draft.sortOrder, 'sortOrder', 'position', failures)

    if (Object.keys(failures).length > 0) {
      setFieldErrors(failures)
      return
    }

    // Blank optional fields are sent as empty strings; the server trims and
    // stores NULL, so the client does not need a second normalization rule.
    const request: SaveStaffRequest = {
      firstName: draft.firstName,
      lastName: draft.lastName,
      email: draft.email,
      phoneNumber: draft.phoneNumber,
      jobTitle: draft.jobTitle,
      calendarColor: draft.calendarColor,
      isActive: draft.isActive,
      sortOrder,
    }

    setIsSaving(true)
    try {
      if (initial === null) {
        await createStaff(request)
      } else {
        await updateStaff(initial.id, request)
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
    <section className="panel" aria-label="Staff form">
      <div className="panel-header">
        <h2>{initial === null ? 'New staff member' : `Edit ${initial.firstName} ${initial.lastName}`}</h2>
      </div>

      {failure !== undefined && <p className="banner" role="alert">{failure}</p>}

      <form className="form" onSubmit={submit} noValidate>
        <label className="field">
          First name
          <input
            value={draft.firstName}
            onChange={update('firstName')}
            maxLength={Limits.name}
            required
          />
          <FieldErrors messages={fieldErrors.firstName} />
        </label>

        <label className="field">
          Last name
          <input
            value={draft.lastName}
            onChange={update('lastName')}
            maxLength={Limits.name}
            required
          />
          <FieldErrors messages={fieldErrors.lastName} />
        </label>

        <label className="field">
          Role
          <input
            value={draft.jobTitle}
            onChange={update('jobTitle')}
            maxLength={Limits.jobTitle}
            placeholder="Stylist"
          />
          <FieldErrors messages={fieldErrors.jobTitle} />
        </label>

        <label className="field">
          Email
          <input
            type="email"
            value={draft.email}
            onChange={update('email')}
            maxLength={Limits.email}
          />
          <FieldErrors messages={fieldErrors.email} />
        </label>

        <label className="field">
          Phone
          <input
            type="tel"
            value={draft.phoneNumber}
            onChange={update('phoneNumber')}
            maxLength={Limits.phone}
          />
          <FieldErrors messages={fieldErrors.phoneNumber} />
        </label>

        <label className="field">
          Calendar colour
          <span className="color-field">
            <span
              className="color-swatch"
              style={swatch === undefined ? undefined : { backgroundColor: swatch }}
              aria-hidden="true"
            />
            <input
              value={draft.calendarColor}
              onChange={update('calendarColor')}
              maxLength={Limits.calendarColor}
              placeholder="#E91E63"
            />
          </span>
          <FieldErrors messages={fieldErrors.calendarColor} />
        </label>

        <label className="field">
          Position
          <input
            type="number"
            className="numeric"
            min={0}
            step={1}
            value={draft.sortOrder}
            onChange={update('sortOrder')}
          />
          <FieldErrors messages={fieldErrors.sortOrder} />
        </label>

        <label className="checkbox">
          <input
            type="checkbox"
            checked={draft.isActive}
            onChange={event => setDraft(current => ({ ...current, isActive: event.target.checked }))}
          />
          Active
        </label>

        <div className="form-actions">
          <button type="submit" className="button button-primary" disabled={isSaving}>
            {isSaving ? 'Saving…' : 'Save staff member'}
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
