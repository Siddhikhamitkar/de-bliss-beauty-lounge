import { useState } from 'react'
import type { FormEvent } from 'react'
import { ApiError, describeFailure } from '../api/client.ts'
import { createCustomerNote, updateCustomerNote } from '../api/customers.ts'
import type { CustomerNote } from '../api/contracts.ts'
import { FieldErrors } from '../components/FieldErrors.tsx'

// Mirrors CustomerLimits.NoteMaxLength on the API; the server stays the
// authority and reports an over-long note as a field error.
const NoteMaxLength = 2000

interface CustomerNoteFormProps {
  customerId: string
  /** The note being edited, or null when the form is adding a new one. */
  existing: CustomerNote | null
  onSaved: () => void
  onCancel: () => void
}

export function CustomerNoteForm({ customerId, existing, onSaved, onCancel }: CustomerNoteFormProps) {
  const [note, setNote] = useState(existing?.note ?? '')
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  const [failure, setFailure] = useState<string>()
  const [isSaving, setIsSaving] = useState(false)

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setFailure(undefined)
    setFieldErrors({})

    setIsSaving(true)
    try {
      if (existing === null) {
        await createCustomerNote(customerId, { note })
      } else {
        await updateCustomerNote(customerId, existing.id, { note })
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
    <form className="form" onSubmit={submit} noValidate>
      {failure !== undefined && <p className="banner" role="alert">{failure}</p>}

      <label className="field">
        <span>{existing === null ? 'Add a note' : 'Edit note'}</span>
        <textarea
          value={note}
          onChange={event => setNote(event.target.value)}
          maxLength={NoteMaxLength}
          rows={3}
        />
        <FieldErrors messages={fieldErrors.note} />
      </label>

      <div className="form-actions">
        <button type="submit" className="button button-primary" disabled={isSaving}>
          {isSaving ? 'Saving…' : existing === null ? 'Add note' : 'Save note'}
        </button>
        <button type="button" className="button" onClick={onCancel} disabled={isSaving}>
          Cancel
        </button>
      </div>
    </form>
  )
}
