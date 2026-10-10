import { useState } from 'react'
import type { FormEvent, KeyboardEvent } from 'react'
import {
  adjustStock,
  getInventoryProduct,
  receiveStock,
  recordUsage,
  setProductStatus,
} from '../api/inventory.ts'
import { ApiError, describeFailure } from '../api/client.ts'
import type { InventoryProduct } from '../api/contracts.ts'
import { DetailDrawer, DrawerHeader } from '../components/DetailDrawer.tsx'
import { FieldErrors } from '../components/FieldErrors.tsx'
import { RowActionMenu } from '../components/RowActionMenu.tsx'
import { formatDay, formatPrice } from '../format.ts'
import { useAsync } from '../useAsync.ts'
import { StockHistory } from './StockHistory.tsx'
import { StockStatusPill } from './StockStatusPill.tsx'

type InventoryTabId = 'overview' | 'history'

type StockAction = 'receive' | 'usage' | 'adjust' | null

const TABS: { id: InventoryTabId; label: string }[] = [
  { id: 'overview', label: 'Overview' },
  { id: 'history', label: 'Stock history' },
]

interface InventoryDrawerProps {
  /** The row that was opened; paints the header before the full record loads. */
  product: InventoryProduct
  /** Receiving stock, usage, adjustments and deactivation are owner-only. */
  canManageInventory: boolean
  onClose: () => void
  onEdit: (productId: string) => void
  /** A stock level or product flag changed; the list should reload. */
  onChanged: () => void
}

/**
 * One product as a tabbed workspace: the overview carries the stock figures and
 * the owner's stock actions, the history tab is the append-only audit trail.
 * Receptionists see everything but the write actions.
 */
export function InventoryDrawer({
  product,
  canManageInventory,
  onClose,
  onEdit,
  onChanged,
}: InventoryDrawerProps) {
  const [revision, setRevision] = useState(0)
  const details = useAsync(() => getInventoryProduct(product.id), [product.id, revision])
  const [activeTab, setActiveTab] = useState<InventoryTabId>('overview')
  const [stockAction, setStockAction] = useState<StockAction>(null)
  const [actionFailure, setActionFailure] = useState<string>()
  const [savedMessage, setSavedMessage] = useState<string>()

  const record = details.data
  const name = record?.name ?? product.name
  const isActive = record?.isActive ?? product.isActive

  function handleTabKeys(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') {
      return
    }

    event.preventDefault()
    const step = event.key === 'ArrowRight' ? 1 : -1
    const index = TABS.findIndex(tab => tab.id === activeTab)
    setActiveTab(TABS[(index + step + TABS.length) % TABS.length].id)
  }

  function toggleStatus() {
    setActionFailure(undefined)

    setProductStatus(product.id, !isActive)
      .then(() => {
        setRevision(current => current + 1)
        onChanged()
      })
      .catch((error: unknown) => setActionFailure(describeFailure(error)))
  }

  /** Shared tail for every stock action: refresh the drawer and the list. */
  function onStockSaved(message: string) {
    setStockAction(null)
    setSavedMessage(message)
    setRevision(current => current + 1)
    onChanged()
  }

  return (
    <DetailDrawer
      label={`Product ${name}`}
      onClose={onClose}
      panelClassName="drawer-workspace"
    >
      <DrawerHeader
        title={name}
        subtitle={
          <>
            <span>{record?.sku ?? product.sku}</span>
            <span>{record?.inventoryType === 'Retail' || product.inventoryType === 'Retail'
              ? 'Retail'
              : 'Salon-use'}</span>
            {record !== undefined && <StockStatusPill status={record.stockStatus} />}
          </>
        }
        actions={
          canManageInventory ? (
            <button type="button" className="button" onClick={() => onEdit(product.id)}>
              Edit
            </button>
          ) : undefined
        }
        menu={
          canManageInventory ? (
            <RowActionMenu
              label="More product actions"
              actions={[
                {
                  label: isActive ? 'Deactivate' : 'Activate',
                  onSelect: toggleStatus,
                  isDanger: isActive,
                },
              ]}
            />
          ) : undefined
        }
        onClose={onClose}
      />

      {actionFailure !== undefined && (
        <p className="banner" role="alert">{actionFailure}</p>
      )}
      {savedMessage !== undefined && (
        <p className="banner banner-success" role="status">{savedMessage}</p>
      )}

      <div
        className="drawer-tabs"
        role="tablist"
        aria-label="Product sections"
        onKeyDown={handleTabKeys}
      >
        {TABS.map(tab => (
          <button
            key={tab.id}
            type="button"
            role="tab"
            id={`inventory-tab-${tab.id}`}
            aria-selected={activeTab === tab.id}
            aria-controls="inventory-tabpanel"
            tabIndex={activeTab === tab.id ? 0 : -1}
            className="drawer-tab"
            onClick={() => setActiveTab(tab.id)}
          >
            {tab.label}
          </button>
        ))}
      </div>

      <div
        className="drawer-tab-content"
        role="tabpanel"
        id="inventory-tabpanel"
        aria-labelledby={`inventory-tab-${activeTab}`}
      >
        {details.failure !== undefined && (
          <p className="banner" role="alert">{details.failure}</p>
        )}

        {record === undefined && details.failure === undefined && (
          <p>Loading…</p>
        )}

        {record !== undefined && activeTab === 'overview' && (
          <>
            <section className="tab-section" aria-label="Product details">
              <div className="tab-section-head">
                <h4>Product</h4>
              </div>
              <dl className="kv-grid">
                <dt>Brand</dt>
                <dd>{record.brand ?? '—'}</dd>
                <dt>Category</dt>
                <dd>{record.category ?? '—'}</dd>
                <dt>Unit of measure</dt>
                <dd>{record.unitOfMeasure}</dd>
                {record.inventoryType === 'Retail' && (
                  <>
                    <dt>Cost price</dt>
                    <dd>{formatPrice(record.costPrice ?? 0)}</dd>
                    <dt>Selling price</dt>
                    <dd>{formatPrice(record.sellingPrice ?? 0)}</dd>
                  </>
                )}
                <dt>Active</dt>
                <dd>{record.isActive ? 'Yes' : 'No'}</dd>
                <dt>Added</dt>
                <dd>{formatDay(record.createdAt)}</dd>
              </dl>
            </section>

            <section className="tab-section" aria-label="Stock levels">
              <div className="tab-section-head">
                <h4>Stock</h4>
              </div>
              <dl className="kv-grid">
                <dt>Current stock</dt>
                <dd>
                  {record.currentStock} {record.unitOfMeasure}
                  {' '}<StockStatusPill status={record.stockStatus} />
                </dd>
                <dt>Minimum stock</dt>
                <dd>{record.minimumStock}</dd>
                <dt>Last stock added</dt>
                <dd>
                  {record.lastStockAddedAt === null
                    ? 'No stock received yet'
                    : `${formatDay(record.lastStockAddedAt)} (${record.lastAddedQuantity} ${record.unitOfMeasure})`}
                </dd>
              </dl>

              {canManageInventory && (
                <div className="drawer-actions">
                  <button
                    type="button"
                    className="button"
                    onClick={() => setStockAction(current => (current === 'receive' ? null : 'receive'))}
                  >
                    Receive stock
                  </button>
                  {record.inventoryType === 'SalonUse' && (
                    <button
                      type="button"
                      className="button"
                      onClick={() => setStockAction(current => (current === 'usage' ? null : 'usage'))}
                    >
                      Record usage
                    </button>
                  )}
                  <button
                    type="button"
                    className="button"
                    onClick={() => setStockAction(current => (current === 'adjust' ? null : 'adjust'))}
                  >
                    Adjust stock
                  </button>
                </div>
              )}

              {canManageInventory && stockAction === 'receive' && (
                <ReceiveStockForm
                  productId={product.id}
                  unitOfMeasure={record.unitOfMeasure}
                  onSaved={() => onStockSaved('Stock received.')}
                  onCancel={() => setStockAction(null)}
                />
              )}
              {canManageInventory && stockAction === 'usage' && (
                <UsageForm
                  productId={product.id}
                  unitOfMeasure={record.unitOfMeasure}
                  onSaved={() => onStockSaved('Usage recorded.')}
                  onCancel={() => setStockAction(null)}
                />
              )}
              {canManageInventory && stockAction === 'adjust' && (
                <AdjustStockForm
                  productId={product.id}
                  unitOfMeasure={record.unitOfMeasure}
                  onSaved={() => onStockSaved('Stock adjusted.')}
                  onCancel={() => setStockAction(null)}
                />
              )}
            </section>
          </>
        )}

        {activeTab === 'history' && (
          <StockHistory
            productId={product.id}
            canManageInventory={canManageInventory}
            onChanged={onChanged}
          />
        )}
      </div>
    </DetailDrawer>
  )
}

interface StockFormProps {
  productId: string
  unitOfMeasure: string
  onSaved: () => void
  onCancel: () => void
}

/** Errors from these forms key on the request's own fields; anything else is a banner. */
function useStockForm() {
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  const [failure, setFailure] = useState<string>()
  const [saving, setSaving] = useState(false)

  function fail(error: unknown) {
    if (error instanceof ApiError && Object.keys(error.errors).length > 0) {
      setFieldErrors(error.errors)
    } else {
      setFailure(describeFailure(error))
    }
  }

  return { fieldErrors, failure, saving, setFieldErrors, setFailure, setSaving, fail }
}

function parseQuantity(text: string): number | null {
  if (text.trim() === '') {
    return null
  }

  const value = Number(text)

  return Number.isNaN(value) ? null : value
}

function ReceiveStockForm({ productId, unitOfMeasure, onSaved, onCancel }: StockFormProps) {
  const form = useStockForm()
  const [quantity, setQuantity] = useState('')
  const [receivedAt, setReceivedAt] = useState('')
  const [reference, setReference] = useState('')
  const [notes, setNotes] = useState('')
  const [kind, setKind] = useState<'StockReceived' | 'OpeningStock'>('StockReceived')

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    form.setFieldErrors({})
    form.setFailure(undefined)

    form.setSaving(true)
    try {
      await receiveStock(productId, {
        quantity: parseQuantity(quantity),
        receivedAt: receivedAt === '' ? null : `${receivedAt}T00:00:00`,
        reference: reference.trim() === '' ? null : reference.trim(),
        notes: notes.trim() === '' ? null : notes.trim(),
        kind,
      })
      onSaved()
    } catch (error) {
      form.fail(error)
    } finally {
      form.setSaving(false)
    }
  }

  return (
    <form className="form" onSubmit={submit} noValidate>
      <div className="tab-section-head">
        <h4>Receive stock</h4>
      </div>

      {form.failure !== undefined && <p className="banner" role="alert">{form.failure}</p>}

      <label className="field">
        Quantity ({unitOfMeasure})
        <input
          type="number"
          className="numeric"
          min="0"
          step="0.001"
          value={quantity}
          onChange={event => setQuantity(event.target.value)}
          required
        />
        <FieldErrors messages={form.fieldErrors.quantity} />
      </label>

      <label className="field">
        Date received
        <input
          type="date"
          value={receivedAt}
          onChange={event => setReceivedAt(event.target.value)}
        />
        <FieldErrors messages={form.fieldErrors.receivedAt} />
      </label>

      <label className="field">
        Kind
        <select
          value={kind}
          onChange={event => setKind(event.target.value as 'StockReceived' | 'OpeningStock')}
        >
          <option value="StockReceived">Stock received</option>
          <option value="OpeningStock">Opening stock</option>
        </select>
        <FieldErrors messages={form.fieldErrors.kind} />
      </label>

      <label className="field">
        Reference (supplier or delivery note)
        <input
          type="text"
          value={reference}
          onChange={event => setReference(event.target.value)}
        />
        <FieldErrors messages={form.fieldErrors.reference} />
      </label>

      <label className="field">
        Notes
        <input type="text" value={notes} onChange={event => setNotes(event.target.value)} />
        <FieldErrors messages={form.fieldErrors.notes} />
      </label>

      <div className="form-actions field-wide">
        <button type="submit" className="button button-primary" disabled={form.saving}>
          {form.saving ? 'Saving…' : 'Record receipt'}
        </button>
        <button type="button" className="button" onClick={onCancel} disabled={form.saving}>
          Cancel
        </button>
      </div>
    </form>
  )
}

function UsageForm({ productId, unitOfMeasure, onSaved, onCancel }: StockFormProps) {
  const form = useStockForm()
  const [quantity, setQuantity] = useState('')
  const [usageType, setUsageType] = useState<'Consumption' | 'Wastage'>('Consumption')
  const [reference, setReference] = useState('')
  const [notes, setNotes] = useState('')

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    form.setFieldErrors({})
    form.setFailure(undefined)

    form.setSaving(true)
    try {
      await recordUsage(productId, {
        quantity: parseQuantity(quantity),
        usageType,
        reference: reference.trim() === '' ? null : reference.trim(),
        notes: notes.trim() === '' ? null : notes.trim(),
      })
      onSaved()
    } catch (error) {
      form.fail(error)
    } finally {
      form.setSaving(false)
    }
  }

  return (
    <form className="form" onSubmit={submit} noValidate>
      <div className="tab-section-head">
        <h4>Record salon-use</h4>
      </div>

      {form.failure !== undefined && <p className="banner" role="alert">{form.failure}</p>}

      <label className="field">
        Quantity ({unitOfMeasure})
        <input
          type="number"
          className="numeric"
          min="0"
          step="0.001"
          value={quantity}
          onChange={event => setQuantity(event.target.value)}
          required
        />
        <FieldErrors messages={form.fieldErrors.quantity} />
      </label>

      <label className="field">
        Usage type
        <select
          value={usageType}
          onChange={event => setUsageType(event.target.value as 'Consumption' | 'Wastage')}
        >
          <option value="Consumption">Consumption</option>
          <option value="Wastage">Wastage</option>
        </select>
        <FieldErrors messages={form.fieldErrors.usageType} />
      </label>

      <label className="field">
        Reference
        <input
          type="text"
          value={reference}
          onChange={event => setReference(event.target.value)}
        />
        <FieldErrors messages={form.fieldErrors.reference} />
      </label>

      <label className="field">
        Reason
        <input
          type="text"
          value={notes}
          onChange={event => setNotes(event.target.value)}
          placeholder="e.g. Balayage service"
          required
        />
        <FieldErrors messages={form.fieldErrors.notes} />
      </label>

      <div className="form-actions field-wide">
        <button type="submit" className="button button-primary" disabled={form.saving}>
          {form.saving ? 'Saving…' : 'Save usage'}
        </button>
        <button type="button" className="button" onClick={onCancel} disabled={form.saving}>
          Cancel
        </button>
      </div>
    </form>
  )
}

function AdjustStockForm({ productId, unitOfMeasure, onSaved, onCancel }: StockFormProps) {
  const form = useStockForm()
  const [quantityChange, setQuantityChange] = useState('')
  const [reference, setReference] = useState('')
  const [notes, setNotes] = useState('')

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    form.setFieldErrors({})
    form.setFailure(undefined)

    form.setSaving(true)
    try {
      await adjustStock(productId, {
        quantityChange: parseQuantity(quantityChange),
        reference: reference.trim() === '' ? null : reference.trim(),
        notes: notes.trim() === '' ? null : notes.trim(),
      })
      onSaved()
    } catch (error) {
      form.fail(error)
    } finally {
      form.setSaving(false)
    }
  }

  return (
    <form className="form" onSubmit={submit} noValidate>
      <div className="tab-section-head">
        <h4>Adjust stock</h4>
      </div>

      {form.failure !== undefined && <p className="banner" role="alert">{form.failure}</p>}

      <label className="field">
        Change ({unitOfMeasure}, negative to remove)
        <input
          type="number"
          className="numeric"
          step="0.001"
          value={quantityChange}
          onChange={event => setQuantityChange(event.target.value)}
          required
        />
        <FieldErrors messages={form.fieldErrors.quantityChange} />
      </label>

      <label className="field">
        Reference
        <input
          type="text"
          value={reference}
          onChange={event => setReference(event.target.value)}
        />
        <FieldErrors messages={form.fieldErrors.reference} />
      </label>

      <label className="field">
        Reason
        <input
          type="text"
          value={notes}
          onChange={event => setNotes(event.target.value)}
          placeholder="e.g. Physical count"
        />
        <FieldErrors messages={form.fieldErrors.notes} />
      </label>

      <div className="form-actions field-wide">
        <button type="submit" className="button button-primary" disabled={form.saving}>
          {form.saving ? 'Saving…' : 'Apply adjustment'}
        </button>
        <button type="button" className="button" onClick={onCancel} disabled={form.saving}>
          Cancel
        </button>
      </div>
    </form>
  )
}
