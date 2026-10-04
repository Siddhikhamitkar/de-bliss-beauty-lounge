import { useState } from 'react'
import { searchCoupons, setCouponStatus } from '../api/billing.ts'
import { describeFailure } from '../api/client.ts'
import { DISCOUNT_TYPE_LABELS } from '../api/contracts.ts'
import type { Coupon, CouponStatusFilter } from '../api/contracts.ts'
import { EmptyState } from '../components/EmptyState.tsx'
import { ErrorState } from '../components/ErrorState.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { Pagination } from '../components/Pagination.tsx'
import { StatusPill } from '../components/StatusPill.tsx'
import { formatDate, formatPrice } from '../format.ts'
import { useAsync } from '../useAsync.ts'
import { CouponEditor } from './CouponForm.tsx'

function describeDiscount(coupon: Coupon): string {
  return coupon.discountType === 'Percentage'
    ? `${formatPrice(coupon.discountValue)}%`
    : formatPrice(coupon.discountValue)
}

/**
 * Coupon management: the codes the checkout can apply. Coupons are retired by
 * deactivating them, never deleted, so a historical invoice can always explain
 * where its discount came from.
 */
export function CouponsPage() {
  const [status, setStatus] = useState<CouponStatusFilter>('All')
  const [page, setPage] = useState(1)
  const [editor, setEditor] = useState<{ id: string | null } | null>(null)
  const [actionFailure, setActionFailure] = useState<string>()

  const coupons = useAsync(
    () =>
      searchCoupons({
        status: status === 'All' ? undefined : status,
        page: page > 1 ? page : undefined,
      }),
    [status, page],
  )

  function toggleStatus(coupon: Coupon) {
    setActionFailure(undefined)

    setCouponStatus(coupon.id, !coupon.isActive)
      .then(() => coupons.reload())
      .catch((error: unknown) => setActionFailure(describeFailure(error)))
  }

  if (editor !== null) {
    return (
      <CouponEditor
        couponId={editor.id}
        onSaved={() => {
          setEditor(null)
          coupons.reload()
        }}
        onCancel={() => setEditor(null)}
      />
    )
  }

  return (
    <section className="panel" aria-label="Coupons">
      <div className="panel-header">
        <h2>Coupons</h2>
        <button type="button" className="button button-primary" onClick={() => setEditor({ id: null })}>
          New coupon
        </button>
      </div>

      <div className="filters">
        <label className="field">
          <span>Status</span>
          <select
            name="status"
            value={status}
            onChange={event => {
              setStatus(event.target.value as CouponStatusFilter)
              setPage(1)
            }}
          >
            <option value="All">All</option>
            <option value="Active">Active</option>
            <option value="Inactive">Inactive</option>
          </select>
        </label>
      </div>

      {coupons.failure !== undefined && (
        <ErrorState message={coupons.failure} onRetry={coupons.reload} />
      )}
      {actionFailure !== undefined && (
        <p className="banner" role="alert">{actionFailure}</p>
      )}

      {coupons.data === undefined && coupons.failure === undefined && (
        <LoadingState label="Loading coupons…" />
      )}

      {coupons.data !== undefined && coupons.data.items.length === 0 && (
        <EmptyState>No coupons in this view.</EmptyState>
      )}

      {coupons.data !== undefined && coupons.data.items.length > 0 && (
        <>
          <table className="grid">
            <thead>
              <tr>
                <th scope="col">Code</th>
                <th scope="col">Discount</th>
                <th scope="col">Type</th>
                <th scope="col">Valid from</th>
                <th scope="col">Valid until</th>
                <th scope="col">Status</th>
                <th scope="col">Actions</th>
              </tr>
            </thead>
            <tbody>
              {coupons.data.items.map(coupon => (
                <tr key={coupon.id}>
                  <th scope="row">{coupon.code}</th>
                  <td className="numeric">{describeDiscount(coupon)}</td>
                  <td>{DISCOUNT_TYPE_LABELS[coupon.discountType]}</td>
                  <td>{coupon.validFrom === null ? '—' : formatDate(coupon.validFrom)}</td>
                  <td>{coupon.validUntil === null ? '—' : formatDate(coupon.validUntil)}</td>
                  <td><StatusPill isActive={coupon.isActive} /></td>
                  <td className="actions">
                    <button
                      type="button"
                      className="button"
                      onClick={() => setEditor({ id: coupon.id })}
                    >
                      Edit
                    </button>
                    <button
                      type="button"
                      className="button"
                      onClick={() => toggleStatus(coupon)}
                    >
                      {coupon.isActive ? 'Deactivate' : 'Activate'}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <Pagination result={coupons.data} onChange={setPage} />
        </>
      )}
    </section>
  )
}
