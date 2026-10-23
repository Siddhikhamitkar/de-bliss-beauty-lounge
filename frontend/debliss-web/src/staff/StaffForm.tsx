import { useState } from 'react'
import type { FormEvent } from 'react'
import { ApiError, describeFailure } from '../api/client.ts'
import { createStaff, getStaff, updateStaff } from '../api/staff.ts'
import type { SaveStaffRequest, StaffProfile, StaffSalaryDetails } from '../api/contracts.ts'
import { getStaffSalary } from '../api/salary.ts'
import { MONEY_ERROR, parseCents } from '../salary/money.ts'
import '../salary/salary.css'
import { useAsync } from '../useAsync.ts'
import { FieldErrors } from '../components/FieldErrors.tsx'

interface StaffEditorProps {
  staffId: string | null
  canManageSalary?: boolean
  onSaved: () => void
  onCancel: () => void
}

// Loads the profile being edited, then hands a complete starting point to the
// stateless StaffForm. Remounting via key resets the draft when the member
// changes or after a save.
export function StaffEditor(props: StaffEditorProps) {
  // A new identity/permission must not briefly display a previous financial draft.
  return <LoadedStaffEditor key={`${props.staffId ?? 'new'}-${!!props.canManageSalary}`} {...props} />
}

function LoadedStaffEditor({ staffId, canManageSalary = false, onSaved, onCancel }: StaffEditorProps) {
  const existing = useAsync(async () => {
    if (staffId === null) return { profile: null, salary: null }
    const [profile, salary] = await Promise.all([
      getStaff(staffId),
      canManageSalary ? getStaffSalary(staffId) : Promise.resolve(null),
    ])
    return { profile, salary }
  }, [staffId, canManageSalary])

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
      initial={existing.data.profile}
      initialSalary={existing.data.salary}
      canManageSalary={canManageSalary}
      onSaved={onSaved}
      onCancel={onCancel}
    />
  )
}

interface StaffFormProps {
  initial: StaffProfile | null
  initialSalary?: StaffSalaryDetails | null
  canManageSalary?: boolean
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

const salaryMoneyFields = [
  ['totalSalary', 'Agreed total salary (AED)'],
  ['basicSalary', 'Basic salary (AED)'],
  ['otherAllowances', 'Other allowances (AED)'],
] as const
const salaryBankFields = [
  ['ibanNumber', 'IBAN', 50],
  ['bankName', 'Bank name', 200],
  ['bankAccountNumber', 'Bank account number', 100],
] as const

type SalaryDraft = Record<keyof StaffSalaryDetails, string>

export function StaffForm({ initial, initialSalary, canManageSalary = false, onSaved, onCancel }: StaffFormProps) {
  const [draft, setDraft] = useState<Draft>(() => toDraft(initial))
  const [salary, setSalary] = useState<SalaryDraft>(() => ({
    totalSalary: initialSalary?.totalSalary?.toString() ?? '',
    basicSalary: initialSalary?.basicSalary?.toString() ?? '',
    otherAllowances: initialSalary?.otherAllowances?.toString() ?? '',
    ibanNumber: initialSalary?.ibanNumber ?? '',
    bankName: initialSalary?.bankName ?? '',
    bankAccountNumber: initialSalary?.bankAccountNumber ?? '',
  }))
  const total = parseCents(salary.totalSalary)
  const basic = salary.basicSalary.trim() === '' ? 0 : parseCents(salary.basicSalary)
  const other = salary.otherAllowances.trim() === '' ? 0 : parseCents(salary.otherAllowances)
  const salaryMismatch = total !== undefined && basic !== undefined && other !== undefined && total !== basic + other
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

    let salaryDetails: StaffSalaryDetails | undefined
    if (canManageSalary) {
      salaryDetails = {
        totalSalary: null, basicSalary: null, otherAllowances: null,
        ibanNumber: null, bankName: null, bankAccountNumber: null,
      }
      for (const [field] of salaryMoneyFields) {
        if (salary[field].trim() === '') continue
        const cents = parseCents(salary[field])
        if (cents === undefined) failures[`salary.${field}`] = [MONEY_ERROR]
        else salaryDetails[field] = cents / 100
      }
      for (const [field, label, max] of salaryBankFields) {
        if (salary[field].length > max) failures[`salary.${field}`] = [`${label} must be ${max} characters or fewer.`]
        // Keep leading zeros and internal formatting; these are identifiers, not numbers.
        salaryDetails[field] = salary[field].trim() === '' ? null : salary[field]
      }
    }

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
      ...(canManageSalary ? { salary: salaryDetails } : {}),
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

        {canManageSalary && (
          <fieldset className="salary-section field-wide" disabled={isSaving}>
            <legend>Salary & bank details · Owner only</legend>
            <p className="panel-lede">Optional amounts in AED. Leave blank for not set; zero is a recorded amount.</p>
            <div className="salary-grid">
              {salaryMoneyFields.map(([field, label]) => (
                <label className="field" key={field}>
                  {label}
                  <input inputMode="decimal" value={salary[field]} aria-invalid={!!fieldErrors[`salary.${field}`]}
                    onChange={event => setSalary(current => ({ ...current, [field]: event.target.value }))} />
                  <FieldErrors messages={fieldErrors[`salary.${field}`]} />
                </label>
              ))}
              {salaryBankFields.map(([field, label, max]) => (
                <label className="field" key={field}>
                  {label}
                  <input type="text" autoComplete="off" value={salary[field]} maxLength={max}
                    aria-invalid={!!fieldErrors[`salary.${field}`]}
                    onChange={event => setSalary(current => ({ ...current, [field]: event.target.value }))} />
                  <FieldErrors messages={fieldErrors[`salary.${field}`]} />
                </label>
              ))}
            </div>
            {salaryMismatch && <p className="salary-warning" role="status">Basic salary and other allowances do not add up to the agreed total. The agreed total will not be changed.</p>}
          </fieldset>
        )}

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
