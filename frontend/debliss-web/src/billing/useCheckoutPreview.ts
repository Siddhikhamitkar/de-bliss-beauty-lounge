import { useEffect, useState } from 'react'
import { getCheckoutPreview } from '../api/billing.ts'
import { describeFailure } from '../api/client.ts'
import type { CheckoutInvoiceRequest, CheckoutPreview, Invoice } from '../api/contracts.ts'

/** Keyed results are hidden in the same render as an edit, before effect cleanup.
 * Both requests are cancelled logically; an older response can never authorize payment.
 * Always obtain a zero-redemption quote first, including when loyalty is disabled.
 */
export function useCheckoutPreview(invoice: Invoice | undefined, request: CheckoutInvoiceRequest, revision: string, suspended: boolean, selection: string) {
  const [attempt, setAttempt] = useState(0)
  const [base, setBase] = useState<{ key: string; value: CheckoutPreview }>()
  const [result, setResult] = useState<{ key: string; value?: CheckoutPreview; failure?: string }>()
  const id = invoice?.customerId != null && invoice.status === 'Draft' ? invoice.id : undefined
  const body = JSON.stringify(request)
  const baseKey = JSON.stringify([invoice, { ...request, loyaltyPointsToRedeem: 0 }, revision, attempt, suspended])
  const key = JSON.stringify([baseKey, body, selection])
  const [previousKey, setPreviousKey] = useState(key)
  // Returning to an earlier input must still obtain a fresh quote; never revive
  // its old result simply because its serialized values happen to match again.
  if (previousKey !== key) {
    setPreviousKey(key)
    setResult(undefined)
  }

  useEffect(() => {
    if (id === undefined || suspended) return
    let ignore = false
    const payload = JSON.parse(body) as CheckoutInvoiceRequest
    async function load() {
      try {
        const initial = await getCheckoutPreview(id!, { ...payload, loyaltyPointsToRedeem: 0 })
        if (ignore) return
        setBase({ key: baseKey, value: initial })
        const selected = (payload.loyaltyPointsToRedeem ?? 0) === 0 ? initial : await getCheckoutPreview(id!, payload)
        if (!ignore) setResult({ key, value: selected })
      } catch (error: unknown) {
        if (!ignore) setResult({ key, failure: describeFailure(error) })
      }
    }
    void load()
    return () => { ignore = true }
  }, [id, body, key, baseKey, suspended])

  const current = !suspended && result?.key === key ? result : undefined
  return {
    data: current?.value,
    base: !suspended && base?.key === baseKey ? base.value : undefined,
    failure: current?.failure,
    pending: id !== undefined && current === undefined,
    reload: () => setAttempt(value => value + 1),
  }
}
