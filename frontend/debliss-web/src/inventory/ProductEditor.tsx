import { useState } from 'react'
import type { FormEvent } from 'react'
import { createProduct, getInventoryProduct, updateProduct } from '../api/inventory.ts'
import { ApiError, describeFailure } from '../api/client.ts'
import type { InventoryProduct, InventoryType, SaveProductRequest } from '../api/contracts.ts'
import { FieldErrors } from '../components/FieldErrors.tsx'
import { useAsync } from '../useAsync.ts'

interface ProductEditorProps {
  productId: string | null
  onSaved: () => void
  onCancel: () => void
}

// Loads the product being edited (if any), then hands a complete starting point
// to the stateless ProductForm. Remounting via key resets the draft after a save.
export function ProductEditor({ productId, onSaved, onCancel }: ProductEditorProps) {
  const existing = useAsync<InventoryProduct | null>(
    () => (productId === null ? Promise.resolve(null) : getInventoryProduct(productId)),
    [productId],
  )

  if (existing.failure !== undefined) {
    return (
      <section className="panel" aria-label="Product form">
        <div className="panel-header">
          <h2>{productId === null ? 'New product' : 'Edit product'}</h2>
        </div>
        <p className="banner">{existing.failure}</p>
        <div className="form-actions">
          <button type="button" className="button" onClick={onCancel}>
            Back to inventory
          </button>
        </div>
      </section>
    )
  }

  if (existing.data === undefined) {
    return (
      <section className="panel" aria-label="Product form">
        <div className="panel-header">
          <h2>{productId === null ? 'New product' : 'Edit product'}</h2>
        </div>
        <p>Loading…</p>
      </section>
    )
  }

  return (
    <ProductForm
      key={productId ?? 'new'}
      initial={existing.data}
      onSaved={onSaved}
      onCancel={onCancel}
    />
  )
}

interface ProductFormProps {
  initial: InventoryProduct | null
  onSaved: () => void
  onCancel: () => void
}

interface Draft {
  name: string
  brand: string
  sku: string
  category: string
  inventoryType: InventoryType
  unitOfMeasure: string
  costPrice: string
  sellingPrice: string
  minimumStock: string
  isActive: boolean
}

function toDraft(product: InventoryProduct | null): Draft {
  if (product === null) {
    return {
      name: '',
      brand: '',
      sku: '',
      category: '',
      inventoryType: 'Retail',
      unitOfMeasure: 'pcs',
      costPrice: '',
      sellingPrice: '',
      minimumStock: '',
      isActive: true,
    }
  }

  return {
    name: product.name,
    brand: product.brand ?? '',
    sku: product.sku,
    category: product.category ?? '',
    inventoryType: product.inventoryType,
    unitOfMeasure: product.unitOfMeasure,
    costPrice: product.costPrice === null ? '' : String(product.costPrice),
    sellingPrice: product.sellingPrice === null ? '' : String(product.sellingPrice),
    minimumStock: String(product.minimumStock),
    isActive: product.isActive,
  }
}

function parseNumber(text: string): number | null {
  if (text.trim() === '') {
    return null
  }

  const value = Number(text)

  return Number.isNaN(value) ? null : value
}

/**
 * The product create/edit form. Retail products carry cost and selling prices
 * because they are sold through billing; salon-use products never carry prices
 * because they are never sold. Stock is not editable here: only movements
 * change it, so the audit trail stays whole.
 */
export function ProductForm({ initial, onSaved, onCancel }: ProductFormProps) {
  const [draft, setDraft] = useState<Draft>(() => toDraft(initial))
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  const [failure, setFailure] = useState<string>()
  const [isSaving, setIsSaving] = useState(false)

  const isRetail = draft.inventoryType === 'Retail'

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setFailure(undefined)
    setFieldErrors({})

    const request: SaveProductRequest = {
      name: draft.name.trim(),
      brand: draft.brand.trim() === '' ? null : draft.brand.trim(),
      sku: draft.sku.trim(),
      category: draft.category.trim() === '' ? null : draft.category.trim(),
      inventoryType: draft.inventoryType,
      unitOfMeasure: draft.unitOfMeasure.trim() === '' ? null : draft.unitOfMeasure.trim(),
      // Salon-use products are never priced; the prices only travel for retail.
      costPrice: isRetail ? parseNumber(draft.costPrice) : null,
      sellingPrice: isRetail ? parseNumber(draft.sellingPrice) : null,
      minimumStock: parseNumber(draft.minimumStock) ?? 0,
      isActive: draft.isActive,
    }

    setIsSaving(true)
    try {
      if (initial === null) {
        await createProduct(request)
      } else {
        await updateProduct(initial.id, request)
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
    <section className="panel" aria-label="Product form">
      <div className="panel-header">
        <h2>{initial === null ? 'New product' : `Edit ${initial.name}`}</h2>
      </div>

      {failure !== undefined && <p className="banner" role="alert">{failure}</p>}

      <form className="form" onSubmit={submit} noValidate>
        <label className="field">
          Name
          <input
            value={draft.name}
            onChange={event => setDraft(current => ({ ...current, name: event.target.value }))}
            maxLength={200}
            placeholder="e.g. L'Oréal Colour 1.0"
            required
          />
          <FieldErrors messages={fieldErrors.name} />
        </label>

        <label className="field">
          Brand
          <input
            value={draft.brand}
            onChange={event => setDraft(current => ({ ...current, brand: event.target.value }))}
            maxLength={100}
          />
          <FieldErrors messages={fieldErrors.brand} />
        </label>

        <label className="field">
          SKU
          <input
            value={draft.sku}
            onChange={event => setDraft(current => ({ ...current, sku: event.target.value }))}
            maxLength={50}
            placeholder="e.g. LC-100"
            required
          />
          <FieldErrors messages={fieldErrors.sku} />
        </label>

        <label className="field">
          Category
          <input
            value={draft.category}
            onChange={event => setDraft(current => ({ ...current, category: event.target.value }))}
            maxLength={100}
            placeholder="e.g. Hair Colour"
          />
          <FieldErrors messages={fieldErrors.category} />
        </label>

        <label className="field">
          Inventory type
          <select
            value={draft.inventoryType}
            onChange={event =>
              setDraft(current => ({
                ...current,
                inventoryType: event.target.value as InventoryType,
              }))
            }
          >
            <option value="Retail">Retail (sold through billing)</option>
            <option value="SalonUse">Salon-use (back-bar stock)</option>
          </select>
          <FieldErrors messages={fieldErrors.inventoryType} />
        </label>

        <label className="field">
          Unit of measure
          <input
            value={draft.unitOfMeasure}
            onChange={event =>
              setDraft(current => ({ ...current, unitOfMeasure: event.target.value }))
            }
            maxLength={20}
            placeholder="pcs, ml, g"
          />
          <FieldErrors messages={fieldErrors.unitOfMeasure} />
        </label>

        {isRetail && (
          <>
            <label className="field">
              Cost price
              <input
                type="number"
                className="numeric"
                min="0"
                step="0.01"
                value={draft.costPrice}
                onChange={event =>
                  setDraft(current => ({ ...current, costPrice: event.target.value }))
                }
                required
              />
              <FieldErrors messages={fieldErrors.costPrice} />
            </label>

            <label className="field">
              Selling price
              <input
                type="number"
                className="numeric"
                min="0"
                step="0.01"
                value={draft.sellingPrice}
                onChange={event =>
                  setDraft(current => ({ ...current, sellingPrice: event.target.value }))
                }
                required
              />
              <FieldErrors messages={fieldErrors.sellingPrice} />
            </label>
          </>
        )}

        <label className="field">
          Minimum stock
          <input
            type="number"
            className="numeric"
            min="0"
            step="0.001"
            value={draft.minimumStock}
            onChange={event =>
              setDraft(current => ({ ...current, minimumStock: event.target.value }))
            }
          />
          <FieldErrors messages={fieldErrors.minimumStock} />
        </label>

        <label className="checkbox">
          <input
            type="checkbox"
            checked={draft.isActive}
            onChange={event =>
              setDraft(current => ({ ...current, isActive: event.target.checked }))
            }
          />
          Active
        </label>

        {initial !== null && (
          <p className="panel-lede">
            Current stock ({initial.currentStock} {initial.unitOfMeasure}) only changes through
            stock movements, never by editing the product.
          </p>
        )}

        <div className="form-actions field-wide">
          <button type="submit" className="button button-primary" disabled={isSaving}>
            {isSaving ? 'Saving…' : 'Save product'}
          </button>
          <button type="button" className="button" onClick={onCancel} disabled={isSaving}>
            Cancel
          </button>
        </div>
      </form>
    </section>
  )
}
