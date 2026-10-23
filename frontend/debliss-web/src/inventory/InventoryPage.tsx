import { useState } from 'react'
import type { FormEvent } from 'react'
import { searchInventoryProducts } from '../api/inventory.ts'
import type {
  InventoryProduct,
  InventoryStockStatusFilter,
  InventoryTypeFilter,
} from '../api/contracts.ts'
import { EmptyState } from '../components/EmptyState.tsx'
import { ErrorState } from '../components/ErrorState.tsx'
import { ImportDialog } from '../components/ImportDialog.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { Pagination } from '../components/Pagination.tsx'
import { formatDay } from '../format.ts'
import { useAsync } from '../useAsync.ts'
import { InventoryDrawer } from './InventoryDrawer.tsx'
import { ProductEditor } from './ProductEditor.tsx'
import { StockStatusPill } from './StockStatusPill.tsx'

interface InventoryPageProps {
  /** Creating products and moving stock is owner-only; receptionists read and sell. */
  canManageInventory: boolean
}

/**
 * The salon's stock list: retail and salon-use products side by side, each with
 * the level on the shelf, the latest delivery and the minimum it should not
 * fall below. Rows open the product drawer with its movement history.
 */
export function InventoryPage({ canManageInventory }: InventoryPageProps) {
  const [search, setSearch] = useState('')
  const [appliedSearch, setAppliedSearch] = useState('')
  const [category, setCategory] = useState('')
  const [appliedCategory, setAppliedCategory] = useState('')
  const [type, setType] = useState<InventoryTypeFilter>('All')
  const [stockStatus, setStockStatus] = useState<InventoryStockStatusFilter>('All')
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState<InventoryProduct | null>(null)
  const [editor, setEditor] = useState<{ productId: string | null } | null>(null)
  const [importing, setImporting] = useState(false)
  // Bumped whenever the drawer or editor writes, so the list re-reads stock.
  const [revision, setRevision] = useState(0)

  const products = useAsync(
    () =>
      searchInventoryProducts({
        search: appliedSearch === '' ? undefined : appliedSearch,
        category: appliedCategory === '' ? undefined : appliedCategory,
        type: type === 'All' ? undefined : type,
        stockStatus: stockStatus === 'All' ? undefined : stockStatus,
        page: page > 1 ? page : undefined,
      }),
    [appliedSearch, appliedCategory, type, stockStatus, page, revision],
  )

  const applyFilters = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setPage(1)
    setAppliedSearch(search.trim())
    setAppliedCategory(category.trim())
  }

  if (editor !== null) {
    return (
      <ProductEditor
        productId={editor.productId}
        onSaved={() => {
          setEditor(null)
          setRevision(current => current + 1)
        }}
        onCancel={() => setEditor(null)}
      />
    )
  }

  return (
    <>
      <section className="panel" aria-label="Inventory">
        <div className="panel-header">
          <div className="panel-heading">
            <h2>Inventory</h2>
            <p className="panel-lede">
              Retail products for sale and salon-use stock, with every movement on record.
            </p>
          </div>
          {canManageInventory && (
            <div className="panel-actions">
              <button type="button" className="button" onClick={() => setImporting(true)}>
                Import
              </button>
              <button
                type="button"
                className="button button-primary"
                onClick={() => setEditor({ productId: null })}
              >
                Add product
              </button>
            </div>
          )}
        </div>

        <form className="filters" onSubmit={applyFilters}>
          <label className="field">
            <span>Search</span>
            <input
              type="search"
              name="search"
              placeholder="Name, brand or SKU"
              value={search}
              onChange={event => setSearch(event.target.value)}
            />
          </label>

          <label className="field">
            <span>Category</span>
            <input
              type="text"
              name="category"
              placeholder="e.g. Hair Colour"
              value={category}
              onChange={event => setCategory(event.target.value)}
            />
          </label>

          <label className="field">
            <span>Type</span>
            <select
              name="type"
              value={type}
              onChange={event => {
                setType(event.target.value as InventoryTypeFilter)
                setPage(1)
              }}
            >
              <option value="All">All</option>
              <option value="Retail">Retail</option>
              <option value="SalonUse">Salon-use</option>
            </select>
          </label>

          <label className="field">
            <span>Stock status</span>
            <select
              name="stockStatus"
              value={stockStatus}
              onChange={event => {
                setStockStatus(event.target.value as InventoryStockStatusFilter)
                setPage(1)
              }}
            >
              <option value="All">All</option>
              <option value="InStock">In stock</option>
              <option value="LowStock">Low stock</option>
              <option value="OutOfStock">Out of stock</option>
            </select>
          </label>

          <button type="submit" className="button">Apply</button>
        </form>

        {products.failure !== undefined && (
          <ErrorState message={products.failure} onRetry={products.reload} />
        )}

        {products.data === undefined && products.failure === undefined && (
          <LoadingState label="Loading inventory…" />
        )}

        {products.data !== undefined && products.data.items.length === 0 && (
          <EmptyState>No products match these filters.</EmptyState>
        )}

        {products.data !== undefined && products.data.items.length > 0 && (
          <>
            <table className="grid" aria-label="Products">
              <thead>
                <tr>
                  <th scope="col">Product</th>
                  <th scope="col">Type</th>
                  <th scope="col" className="numeric">Current stock</th>
                  <th scope="col">Last stock added</th>
                  <th scope="col" className="numeric">Latest added</th>
                  <th scope="col" className="numeric">Minimum</th>
                  <th scope="col">Stock status</th>
                </tr>
              </thead>
              <tbody>
                {products.data.items.map(item => (
                  <tr
                    key={item.id}
                    className="row-clickable"
                    onClick={() => setSelected(item)}
                  >
                    <th scope="row">
                      <button
                        type="button"
                        className="row-link"
                        onClick={event => {
                          event.stopPropagation()
                          setSelected(item)
                        }}
                      >
                        {item.name}
                      </button>
                      <span className="row-subtext">
                        {[item.brand, item.sku].filter(part => part !== null).join(' · ') || '—'}
                      </span>
                    </th>
                    <td>{item.inventoryType === 'Retail' ? 'Retail' : 'Salon-use'}</td>
                    <td className="numeric">{item.currentStock}</td>
                    <td>
                      {item.lastStockAddedAt === null ? (
                        'No stock received yet'
                      ) : (
                        formatDay(item.lastStockAddedAt)
                      )}
                    </td>
                    <td className="numeric">{item.lastAddedQuantity ?? '—'}</td>
                    <td className="numeric">{item.minimumStock}</td>
                    <td><StockStatusPill status={item.stockStatus} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
            <Pagination result={products.data} onChange={setPage} />
          </>
        )}
      </section>

      {importing && (
        <ImportDialog
          flavours={[
            {
              kind: 'retail',
              label: 'Retail products',
              description: 'Carry cost and selling prices; sellable at the counter.',
            },
            {
              kind: 'salon-use',
              label: 'Salon-use products',
              description: 'Counted in units like ml or pcs; never priced for sale.',
            },
          ]}
          title="Import products"
          onClose={() => setImporting(false)}
          onImported={() => setRevision(current => current + 1)}
        />
      )}

      {selected !== null && (
        <InventoryDrawer
          product={selected}
          canManageInventory={canManageInventory}
          onClose={() => setSelected(null)}
          onEdit={productId => {
            setSelected(null)
            setEditor({ productId })
          }}
          onChanged={() => setRevision(current => current + 1)}
        />
      )}
    </>
  )
}
