import { useState } from 'react'
import { listCustomerNotes } from '../api/customers.ts'
import { useAsync } from '../useAsync.ts'
import { formatDateTime } from '../format.ts'
import { LoadingState } from '../components/LoadingState.tsx'
import { ErrorState } from '../components/ErrorState.tsx'
import { EmptyState } from '../components/EmptyState.tsx'
import { CustomerNoteForm } from './CustomerNoteForm.tsx'

interface CustomerNotesProps {
  customerId: string
}

/**
 * Every note a customer has collected, newest first. Notes are corrected in
 * place rather than deleted, so the history of what the front desk knew —
 * including when they learned it — stays readable.
 */
export function CustomerNotes({ customerId }: CustomerNotesProps) {
  const notes = useAsync(() => listCustomerNotes(customerId), [customerId])
  const [editingId, setEditingId] = useState<string | null>(null)
  const [isAdding, setIsAdding] = useState(false)

  return (
    <section className="panel" aria-label="Customer notes">
      <div className="panel-header">
        <h2>Notes</h2>
        {!isAdding && editingId === null && (
          <button type="button" className="button" onClick={() => setIsAdding(true)}>
            Add note
          </button>
        )}
      </div>

      {notes.failure !== undefined && <ErrorState message={notes.failure} onRetry={notes.reload} />}
      {notes.data === undefined && notes.failure === undefined && (
        <LoadingState label="Loading notes…" />
      )}

      {notes.data !== undefined && notes.data.length === 0 && !isAdding && (
        <EmptyState>No notes yet.</EmptyState>
      )}

      {notes.data !== undefined &&
        notes.data.map(note =>
          editingId === note.id ? (
            <CustomerNoteForm
              key={note.id}
              customerId={customerId}
              existing={note}
              onSaved={() => {
                setEditingId(null)
                notes.reload()
              }}
              onCancel={() => setEditingId(null)}
            />
          ) : (
            <div className="note-entry" key={note.id}>
              <p>{note.note}</p>
              <p className="note-meta">
                {formatDateTime(note.createdAt)}
                {note.updatedAt !== null && ' · edited'}
                {' · '}
                <button type="button" className="button" onClick={() => setEditingId(note.id)}>
                  Edit
                </button>
              </p>
            </div>
          ),
        )}

      {isAdding && (
        <CustomerNoteForm
          customerId={customerId}
          existing={null}
          onSaved={() => {
            setIsAdding(false)
            notes.reload()
          }}
          onCancel={() => setIsAdding(false)}
        />
      )}
    </section>
  )
}
