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
 * Every note a customer has collected, newest first, one card each. Notes are
 * corrected in place rather than deleted, so the history of what the front
 * desk knew — including when they learned it — stays readable.
 */
export function CustomerNotes({ customerId }: CustomerNotesProps) {
  const notes = useAsync(() => listCustomerNotes(customerId), [customerId])
  const [editingId, setEditingId] = useState<string | null>(null)
  const [isAdding, setIsAdding] = useState(false)

  return (
    <section className="tab-section" aria-label="Customer notes">
      <div className="tab-section-head">
        <h4>Notes</h4>
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

      {notes.data !== undefined && notes.data.length > 0 && (
        <ul className="note-cards">
          {notes.data.map(note =>
            editingId === note.id ? (
              <li className="note-card" key={note.id}>
                <CustomerNoteForm
                  customerId={customerId}
                  existing={note}
                  onSaved={() => {
                    setEditingId(null)
                    notes.reload()
                  }}
                  onCancel={() => setEditingId(null)}
                />
              </li>
            ) : (
              <li className="note-card" key={note.id}>
                <p className="note-card-text">{note.note}</p>
                <div className="note-card-meta">
                  <span>
                    {formatDateTime(note.createdAt)}
                    {note.updatedAt !== null && ' · edited'}
                  </span>
                  <button type="button" className="button" onClick={() => setEditingId(note.id)}>
                    Edit
                  </button>
                </div>
              </li>
            ),
          )}
        </ul>
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
