import { useState } from 'react'
import type { FormEvent } from 'react'
import { reverseStockMovement, searchStockMovements } from '../api/inventory.ts'
import { ApiError, describeFailure } from '../api/client.ts'
import type { StockMovement, StockMovementType, StockMovementTypeFilter } from '../api/contracts.ts'
import { EmptyState } from '../components/EmptyState.tsx'
import { ErrorState } from '../components/ErrorState.tsx'
import { FieldErrors } from '../components/FieldErrors.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { Pagination } from '../components/Pagination.tsx'
import { formatDateTime } from '../format.ts'
import { useAsync } from '../useAsync.ts'

const MOVEMENT_TYPE_LABELS: Record<StockMovementType, string> = {
  OpeningStock: 'Opening stock',
  StockReceived: 'Stock received',
  RetailSale: 'Retail sale',
  Consumption: 'Consumption',
  Wastage: 'Wastage',
  Adjustment: 'Adjustment',
  Reversal: 'Reversal',
}

interface StockHistoryProps {
  productId: string
  /** Reversing a movement is an owner action; the history itself everyone reads. */
  canManageInventory: boolean
  onChanged: () => void
}

/**
 * A product's append-only movement history, newest first: what changed, the
 * stock on both sides, when, why, by whom and — for sales — which invoice the
 * stock left through. The owner can reverse a mistaken movement, which appends
 * its own row rather than rewriting the past.
 */
export function StockHistory({ productId, canManageInventory, onChanged }: StockHistoryProps) {
  const [type, setType] = useState<StockMovementTypeFilter>('All')
  const [page, setPage] = useState(1)
  const [reversing, setReversing] = useState<StockMovement | null>(null)
  const [savedMessage, setSavedMessage] = useState<string>()
  const [revision, setRevision] = useState(0)

  const movements = useAsync(
    () =>
      searchStockMovements(productId, {
        type: type === 'All' ? undefined : type,
        page: page > 1 ? page : undefined,
      }),
    [productId, type, page, revision],
  )

  return (
    <section className="tab-section" aria-label="Stock movement history">
      <div className="tab-section-head">
        <h4>Movement history</h4>
      </div>

      {savedMessage !== undefined && (
        <p className="banner banner-success" role="status">{savedMessage}</p>
      )}

      <div className="filters">
        <label className="field">
          <span>Type</span>
          <select
            name="movementType"
            value={type}
            onChange={event => {
              setType(event.target.value as StockMovementTypeFilter)
              setPage(1)
            }}
          >
            <option value="All">All</option>
            {Object.entries(MOVEMENT_TYPE_LABELS).map(([value, label]) => (
              <option key={value} value={value}>{label}</option>
            ))}
          </select>
        </label>
      </div>

      {movements.failure !== undefined && (
        <ErrorState message={movements.failure} onRetry={movements.reload} />
      )}

      {movements.data === undefined && movements.failure === undefined && (
        <LoadingState label="Loading history…" />
      )}

      {movements.data !== undefined && movements.data.items.length === 0 && (
        <EmptyState>No stock movements recorded yet.</EmptyState>
      )}

      {movements.data !== undefined && movements.data.items.length > 0 && (
        <>
          <table className="grid" aria-label="Stock movements">
            <thead>
              <tr>
                <th scope="col">When</th>
                <th scope="col">Type</th>
                <th scope="col" className="numeric">Change</th>
                <th scope="col" className="numeric">Before → after</th>
                <th scope="col">Reference / notes</th>
                <th scope="col">Invoice</th>
                <th scope="col">By</th>
                {canManageInventory && <th scope="col">Actions</th>}
              </tr>
            </thead>
            <tbody>
              {movements.data.items.map(movement => (
                <tr key={movement.id}>
                  <td>{formatDateTime(movement.occurredAt)}</td>
                  <td>{MOVEMENT_TYPE_LABELS[movement.movementType] ?? movement.movementType}</td>
                  <td className="numeric">
                    {movement.quantityChange > 0 ? `+${movement.quantityChange}` : movement.quantityChange}
                  </td>
                  <td className="numeric">{movement.stockBefore} → {movement.stockAfter}</td>
                  <td>
                    {movement.reference ?? '—'}
                    {movement.notes !== null && (
                      <span className="row-subtext">{movement.notes}</span>
                    )}
                  </td>
                  <td>{movement.invoiceNumber ?? '—'}</td>
                  <td>{movement.performedByName ?? '—'}</td>
                  {canManageInventory && (
                    <td>
                      {movement.movementType !== 'Reversal' && (
                        <button
                          type="button"
                          className="button"
                          onClick={() => {
                            setReversing(movement)
                            setSavedMessage(undefined)
                          }}
                        >
                          Reverse
                        </button>
                      )}
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
          <Pagination result={movements.data} onChange={setPage} />
        </>
      )}

      {reversing !== null && (
        <ReversalForm
          productId={productId}
          movement={reversing}
          onSaved={() => {
            setReversing(null)
            setSavedMessage('Movement reversed.')
            setRevision(current => current + 1)
            onChanged()
          }}
          onCancel={() => setReversing(null)}
        />
      )}
    </section>
  )
}

interface ReversalFormProps {
  productId: string
  movement: StockMovement
  onSaved: () => void
  onCancel: () => void
}

function ReversalForm({ productId, movement, onSaved, onCancel }: ReversalFormProps) {
  const [notes, setNotes] = useState('')
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  const [failure, setFailure] = useState<string>()
  const [saving, setSaving] = useState(false)

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setFieldErrors({})
    setFailure(undefined)

    setSaving(true)
    try {
      await reverseStockMovement(productId, movement.id, {
        notes: notes.trim() === '' ? null : notes.trim(),
      })
      onSaved()
    } catch (error) {
      if (error instanceof ApiError && Object.keys(error.errors).length > 0) {
        setFieldErrors(error.errors)
      } else {
        setFailure(describeFailure(error))
      }
    } finally {
      setSaving(false)
    }
  }

  return (
    <form className="form" onSubmit={submit} noValidate aria-label="Reverse movement">
      <div className="tab-section-head">
        <h4>
          Reverse {MOVEMENT_TYPE_LABELS[movement.movementType] ?? movement.movementType} of{' '}
          {movement.quantityChange > 0 ? `+${movement.quantityChange}` : movement.quantityChange}
        </h4>
      </div>

      <p className="panel-lede">
        The original movement stays on record; the reversal is its own entry.
      </p>

      {failure !== undefined && <p className="banner" role="alert">{failure}</p>}

      <label className="field">
        Reason
        <input
          type="text"
          value={notes}
          onChange={event => setNotes(event.target.value)}
          placeholder="e.g. Delivery was never ours"
          required
        />
        <FieldErrors messages={fieldErrors.notes} />
      </label>

      <div className="form-actions field-wide">
        <button type="submit" className="button button-primary" disabled={saving}>
          {saving ? 'Saving…' : 'Record reversal'}
        </button>
        <button type="button" className="button" onClick={onCancel} disabled={saving}>
          Cancel
        </button>
      </div>
    </form>
  )
}
