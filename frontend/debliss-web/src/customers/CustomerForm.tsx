import { useState } from 'react'
import type { FormEvent } from 'react'
import { ApiError, describeFailure } from '../api/client.ts'
import { createCustomer, createCustomerNote, getCustomer, updateCustomer } from '../api/customers.ts'
import type { CustomerProfile, SaveCustomerRequest } from '../api/contracts.ts'
import { useAsync } from '../useAsync.ts'
import { ConfirmationDialog } from '../components/ConfirmationDialog.tsx'
import { FieldErrors } from '../components/FieldErrors.tsx'

interface CustomerEditorProps {
  customerId: string | null
  onSaved: () => void
  onCancel: () => void
}

// Loads the profile being edited, then hands a complete starting point to the
// stateless CustomerForm. Remounting via key resets the draft when the customer
// changes or after a save.
export function CustomerEditor({ customerId, onSaved, onCancel }: CustomerEditorProps) {
  const existing = useAsync<CustomerProfile | null>(
    () => (customerId === null ? Promise.resolve(null) : getCustomer(customerId)),
    [customerId],
  )

  if (existing.failure !== undefined) {
    return (
      <section className="panel" aria-label="Customer form">
        <div className="panel-header">
          <h2>{customerId === null ? 'New customer' : 'Edit customer'}</h2>
        </div>
        <p className="banner" role="alert">{existing.failure}</p>
        <div className="form-actions">
          <button type="button" className="button" onClick={onCancel}>
            Back to customers
          </button>
        </div>
      </section>
    )
  }

  if (existing.data === undefined) {
    return (
      <section className="panel" aria-label="Customer form">
        <div className="panel-header">
          <h2>{customerId === null ? 'New customer' : 'Edit customer'}</h2>
        </div>
        <p>Loading…</p>
      </section>
    )
  }

  return (
    <CustomerForm
      key={customerId ?? 'new'}
      initial={existing.data}
      onSaved={onSaved}
      onCancel={onCancel}
    />
  )
}

interface CustomerFormProps {
  initial: CustomerProfile | null
  onSaved: () => void
  onCancel: () => void
}

// Mirrors CustomerLimits on the API. The server is the authority on these
// bounds; the client only carries them so the input boxes can express them.
const Limits = {
  name: 100,
  email: 256,
  phone: 30,
  gender: 20,
  address: 500,
  note: 2000,
} as const

interface Draft {
  firstName: string
  lastName: string
  phoneNumber: string
  email: string
  dateOfBirth: string
  gender: string
  address: string
  isActive: boolean
  note: string
}

function toDraft(customer: CustomerProfile | null): Draft {
  if (customer === null) {
    return {
      firstName: '',
      lastName: '',
      phoneNumber: '',
      email: '',
      dateOfBirth: '',
      gender: '',
      address: '',
      isActive: true,
      note: '',
    }
  }

  return {
    firstName: customer.firstName,
    lastName: customer.lastName,
    phoneNumber: customer.phoneNumber ?? '',
    email: customer.email ?? '',
    dateOfBirth: customer.dateOfBirth ?? '',
    gender: customer.gender ?? '',
    address: customer.address ?? '',
    isActive: customer.isActive,
    note: '',
  }
}

export function CustomerForm({ initial, onSaved, onCancel }: CustomerFormProps) {
  const [draft, setDraft] = useState<Draft>(() => toDraft(initial))
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  const [failure, setFailure] = useState<string>()
  const [isSaving, setIsSaving] = useState(false)
  const [duplicateWarning, setDuplicateWarning] = useState<string>()

  const update = (field: keyof Draft) => (event: { target: { value: string } }) =>
    setDraft(current => ({ ...current, [field]: event.target.value }))

  async function save(allowDuplicatePhone: boolean): Promise<void> {
    // Blank optional fields are sent as empty strings; the server trims and
    // stores NULL, so the client does not need a second normalization rule.
    // An empty date box likewise arrives as null.
    const request: SaveCustomerRequest = {
      firstName: draft.firstName,
      lastName: draft.lastName,
      phoneNumber: draft.phoneNumber,
      email: draft.email,
      dateOfBirth: draft.dateOfBirth === '' ? null : draft.dateOfBirth,
      gender: draft.gender,
      address: draft.address,
      isActive: draft.isActive,
      allowDuplicatePhone,
    }

    setIsSaving(true)
    try {
      if (initial === null) {
        const created = await createCustomer(request)
        const note = draft.note.trim()
        if (note !== '') {
          // The customer already exists at this point; a failed note must not
          // fail the whole save, or the retry would create a second customer.
          try {
            await createCustomerNote(created.id, { note })
          } catch {
            // The profile shows an empty notes list; the receptionist can retype
            // the note there rather than risk a duplicate customer.
          }
        }
      } else {
        await updateCustomer(initial.id, request)
      }
      onSaved()
    } catch (error) {
      // The API names the existing customer in the 409 detail; park that
      // message in a dialog and let the user decide whether to save anyway.
      if (error instanceof ApiError && error.status === 409) {
        setDuplicateWarning(error.message)
      } else if (error instanceof ApiError && Object.keys(error.errors).length > 0) {
        setFieldErrors(error.errors)
      } else {
        setFailure(describeFailure(error))
      }
    } finally {
      setIsSaving(false)
    }
  }

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setFailure(undefined)
    setFieldErrors({})
    setDuplicateWarning(undefined)
    void save(false)
  }

  return (
    <section className="panel" aria-label="Customer form">
      <div className="panel-header">
        <h2>{initial === null ? 'New customer' : `Edit ${initial.firstName} ${initial.lastName}`}</h2>
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
          Mobile
          <input
            type="tel"
            value={draft.phoneNumber}
            onChange={update('phoneNumber')}
            maxLength={Limits.phone}
            required
          />
          <FieldErrors messages={fieldErrors.phoneNumber} />
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
          Date of birth
          <input
            type="date"
            value={draft.dateOfBirth}
            onChange={update('dateOfBirth')}
          />
          <FieldErrors messages={fieldErrors.dateOfBirth} />
        </label>

        <label className="field">
          Gender
          <input
            value={draft.gender}
            onChange={update('gender')}
            maxLength={Limits.gender}
          />
          <FieldErrors messages={fieldErrors.gender} />
        </label>

        <label className="field">
          Address
          <input
            value={draft.address}
            onChange={update('address')}
            maxLength={Limits.address}
          />
          <FieldErrors messages={fieldErrors.address} />
        </label>

        {initial === null && (
          <label className="field">
            Notes
            <textarea
              value={draft.note}
              onChange={update('note')}
              maxLength={Limits.note}
            />
            <FieldErrors messages={fieldErrors.note} />
          </label>
        )}

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
            {isSaving ? 'Saving…' : 'Save customer'}
          </button>
          <button type="button" className="button" onClick={onCancel} disabled={isSaving}>
            Cancel
          </button>
        </div>
      </form>

      {duplicateWarning !== undefined && (
        <ConfirmationDialog
          title="Possible duplicate customer"
          confirmLabel="Save anyway"
          busy={isSaving}
          onConfirm={() => {
            setDuplicateWarning(undefined)
            void save(true)
          }}
          onCancel={() => setDuplicateWarning(undefined)}
        >
          <p>{duplicateWarning}</p>
        </ConfirmationDialog>
      )}
    </section>
  )
}
