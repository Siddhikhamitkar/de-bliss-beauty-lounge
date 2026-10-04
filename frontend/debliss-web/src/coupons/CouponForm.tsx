import { useState } from 'react'
import type { FormEvent } from 'react'
import { createCoupon, getCoupon, updateCoupon } from '../api/billing.ts'
import { ApiError, describeFailure } from '../api/client.ts'
import { DISCOUNT_TYPE_LABELS, DISCOUNT_TYPES } from '../api/contracts.ts'
import type { Coupon, DiscountType, SaveCouponRequest } from '../api/contracts.ts'
import { FieldErrors } from '../components/FieldErrors.tsx'
import { useAsync } from '../useAsync.ts'

interface CouponEditorProps {
  couponId: string | null
  onSaved: () => void
  onCancel: () => void
}

// Loads the coupon being edited (if any), then hands a complete starting point to
// the stateless CouponForm. Remounting via key resets the draft after a save.
export function CouponEditor({ couponId, onSaved, onCancel }: CouponEditorProps) {
  const existing = useAsync<Coupon | null>(
    () => (couponId === null ? Promise.resolve(null) : getCoupon(couponId)),
    [couponId],
  )

  if (existing.failure !== undefined) {
    return (
      <section className="panel" aria-label="Coupon form">
        <div className="panel-header">
          <h2>{couponId === null ? 'New coupon' : 'Edit coupon'}</h2>
        </div>
        <p className="banner">{existing.failure}</p>
        <div className="form-actions">
          <button type="button" className="button" onClick={onCancel}>
            Back to coupons
          </button>
        </div>
      </section>
    )
  }

  if (existing.data === undefined) {
    return (
      <section className="panel" aria-label="Coupon form">
        <div className="panel-header">
          <h2>{couponId === null ? 'New coupon' : 'Edit coupon'}</h2>
        </div>
        <p>Loading…</p>
      </section>
    )
  }

  return (
    <CouponForm
      key={couponId ?? 'new'}
      initial={existing.data}
      onSaved={onSaved}
      onCancel={onCancel}
    />
  )
}

interface CouponFormProps {
  initial: Coupon | null
  onSaved: () => void
  onCancel: () => void
}

// Mirrors the API's coupon limits; the server remains the authority.
const CODE_MAX_LENGTH = 50

interface Draft {
  code: string
  discountType: DiscountType
  discountValue: string
  validFrom: string
  validUntil: string
  isActive: boolean
}

function toDraft(coupon: Coupon | null): Draft {
  if (coupon === null) {
    return {
      code: '',
      discountType: 'Fixed',
      discountValue: '',
      validFrom: '',
      validUntil: '',
      isActive: true,
    }
  }

  return {
    code: coupon.code,
    discountType: coupon.discountType,
    discountValue: String(coupon.discountValue),
    validFrom: coupon.validFrom ?? '',
    validUntil: coupon.validUntil ?? '',
    isActive: coupon.isActive,
  }
}

export function CouponForm({ initial, onSaved, onCancel }: CouponFormProps) {
  const [draft, setDraft] = useState<Draft>(() => toDraft(initial))
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  const [failure, setFailure] = useState<string>()
  const [isSaving, setIsSaving] = useState(false)

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setFailure(undefined)
    setFieldErrors({})

    // Blank dates mean "no bound"; the API stores that as null.
    const value = draft.discountValue.trim() === '' ? null : Number(draft.discountValue)
    if (value === null || !Number.isFinite(value)) {
      setFieldErrors({ discountValue: ['Enter a discount value.'] })
      return
    }

    const request: SaveCouponRequest = {
      code: draft.code.trim(),
      discountType: draft.discountType,
      discountValue: value,
      validFrom: draft.validFrom === '' ? null : draft.validFrom,
      validUntil: draft.validUntil === '' ? null : draft.validUntil,
      isActive: draft.isActive,
    }

    setIsSaving(true)
    try {
      if (initial === null) {
        await createCoupon(request)
      } else {
        await updateCoupon(initial.id, request)
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
    <section className="panel" aria-label="Coupon form">
      <div className="panel-header">
        <h2>{initial === null ? 'New coupon' : `Edit ${initial.code}`}</h2>
      </div>

      {failure !== undefined && <p className="banner" role="alert">{failure}</p>}

      <form className="form" onSubmit={submit} noValidate>
        <label className="field">
          Code
          <input
            value={draft.code}
            onChange={event => setDraft(current => ({ ...current, code: event.target.value }))}
            maxLength={CODE_MAX_LENGTH}
            placeholder="e.g. BLISS20"
            required
          />
          <FieldErrors messages={fieldErrors.code} />
        </label>

        <label className="field">
          Discount type
          <select
            value={draft.discountType}
            onChange={event =>
              setDraft(current => ({ ...current, discountType: event.target.value as DiscountType }))
            }
          >
            {DISCOUNT_TYPES.map(type => (
              <option key={type} value={type}>
                {DISCOUNT_TYPE_LABELS[type]}
              </option>
            ))}
          </select>
          <FieldErrors messages={fieldErrors.discountType} />
        </label>

        <label className="field">
          Discount value
          <input
            type="number"
            className="numeric"
            min="0"
            step="0.01"
            value={draft.discountValue}
            onChange={event =>
              setDraft(current => ({ ...current, discountValue: event.target.value }))
            }
            required
          />
          <FieldErrors messages={fieldErrors.discountValue} />
        </label>

        <label className="field">
          Valid from
          <input
            type="date"
            value={draft.validFrom}
            onChange={event => setDraft(current => ({ ...current, validFrom: event.target.value }))}
          />
          <FieldErrors messages={fieldErrors.validFrom} />
        </label>

        <label className="field">
          Valid until
          <input
            type="date"
            value={draft.validUntil}
            onChange={event => setDraft(current => ({ ...current, validUntil: event.target.value }))}
          />
          <FieldErrors messages={fieldErrors.validUntil} />
        </label>

        <label className="checkbox">
          <input
            type="checkbox"
            checked={draft.isActive}
            onChange={event => setDraft(current => ({ ...current, isActive: event.target.checked }))}
          />
          Active
        </label>

        <div className="form-actions field-wide">
          <button type="submit" className="button button-primary" disabled={isSaving}>
            {isSaving ? 'Saving…' : 'Save coupon'}
          </button>
          <button type="button" className="button" onClick={onCancel} disabled={isSaving}>
            Cancel
          </button>
        </div>
      </form>
    </section>
  )
}
