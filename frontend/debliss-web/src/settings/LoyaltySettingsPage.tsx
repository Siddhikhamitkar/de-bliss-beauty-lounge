import { useState } from 'react'
import { ApiError, describeFailure } from '../api/client.ts'
import type { LoyaltySettings } from '../api/contracts.ts'
import { getLoyaltySettings, updateLoyaltySettings } from '../api/loyalty.ts'
import { ErrorState } from '../components/ErrorState.tsx'
import { FieldErrors } from '../components/FieldErrors.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { useAsync } from '../useAsync.ts'

type NumberField = 'earningPercentage' | 'pointValueAed' | 'minimumRedemptionPoints' | 'maximumRedemptionPercentage' | 'minimumInvoiceAmount'
const fields: { key: NumberField; label: string; max?: number }[] = [
  { key: 'earningPercentage', label: 'Earning percentage (%)', max: 100 },
  { key: 'pointValueAed', label: 'Value of one point (AED)' },
  { key: 'minimumRedemptionPoints', label: 'Minimum redemption points' },
  { key: 'maximumRedemptionPercentage', label: 'Maximum redemption (% of eligible invoice)', max: 100 },
  { key: 'minimumInvoiceAmount', label: 'Minimum invoice amount (AED, optional)' },
]

export function LoyaltySettingsPage() {
  const settings = useAsync(getLoyaltySettings, [])
  const [loadedFrom, setLoadedFrom] = useState<LoyaltySettings>()
  const [values, setValues] = useState<Record<NumberField, string>>({ earningPercentage: '', pointValueAed: '', minimumRedemptionPoints: '', maximumRedemptionPercentage: '', minimumInvoiceAmount: '' })
  const [enabled, setEnabled] = useState(false)
  const [partial, setPartial] = useState(true)
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const [failure, setFailure] = useState<string>()
  const [errors, setErrors] = useState<Record<string, string[]>>()

  if (settings.data !== undefined && settings.data !== loadedFrom) {
    setLoadedFrom(settings.data)
    setEnabled(settings.data.isEnabled)
    setPartial(settings.data.allowPartialRedemption)
    setValues(Object.fromEntries(fields.map(({ key }) => [key, settings.data![key] === null ? '' : String(settings.data![key])])) as Record<NumberField, string>)
  }

  async function save() {
    const invalid: Record<string, string[]> = {}
    for (const { key, label } of fields) {
      if (key !== 'minimumInvoiceAmount' && values[key].trim() === '') invalid[key] = [`${label} is required.`]
      if (values[key] !== '' && !Number.isFinite(Number(values[key]))) invalid[key] = ['Enter a valid number.']
    }
    setErrors(invalid)
    setFailure(undefined)
    setSaved(false)
    if (Object.keys(invalid).length > 0) return
    setSaving(true)
    try {
      await updateLoyaltySettings({
        isEnabled: enabled,
        allowPartialRedemption: partial,
        earningPercentage: Number(values.earningPercentage),
        pointValueAed: Number(values.pointValueAed),
        minimumRedemptionPoints: Number(values.minimumRedemptionPoints),
        maximumRedemptionPercentage: Number(values.maximumRedemptionPercentage),
        minimumInvoiceAmount: values.minimumInvoiceAmount.trim() === '' ? null : Number(values.minimumInvoiceAmount),
      })
      setSaved(true)
      settings.reload()
    } catch (error: unknown) {
      if (error instanceof ApiError && Object.keys(error.errors).length > 0) {
        setErrors(error.errors)
      } else {
        setFailure(describeFailure(error))
      }
    } finally {
      setSaving(false)
    }
  }

  return (
    <section className="panel" aria-label="Loyalty settings">
      <div className="panel-header"><div><h2>Loyalty settings</h2><p className="row-subtext">Owner-only rules for earning and redeeming customer points. Finalized invoices keep their original loyalty amounts.</p></div></div>
      {settings.failure !== undefined && <ErrorState message={settings.failure} onRetry={settings.reload} />}
      {settings.data === undefined && settings.failure === undefined && <LoadingState label="Loading loyalty settings…" />}
      {settings.data !== undefined && <div className="settings-form">
        <label className="payment-method"><input type="checkbox" checked={enabled} disabled={saving} onChange={event => { setEnabled(event.target.checked); setSaved(false) }} />Enable loyalty program</label>
        <FieldErrors messages={errors?.isEnabled} />
        {fields.map(({ key, label, max }) => <label className="field" key={key}>
          <span>{label}</span>
          <input type="number" name={key} min={key === 'pointValueAed' ? '0.01' : '0'} max={max} step="0.01" value={values[key]} disabled={saving} onChange={event => { setValues(current => ({ ...current, [key]: event.target.value })); setSaved(false) }} />
          <FieldErrors messages={errors?.[key]} />
        </label>)}
        <label className="payment-method"><input type="checkbox" checked={partial} disabled={saving} onChange={event => { setPartial(event.target.checked); setSaved(false) }} />Allow partial redemption</label>
        <FieldErrors messages={errors?.allowPartialRedemption} />
        <p className="row-subtext">Points are rounded to 2 decimal places. Earning is based on eligible spend excluding VAT, tips and discounts, including loyalty redeemed on this invoice.</p>
        <p className="row-subtext">When partial redemption is off, only the customer’s FULL available balance can be redeemed, and only if it fits the minimum points, invoice minimum and maximum percentage limits. Otherwise no redemption is allowed.</p>
        {failure !== undefined && <p className="banner" role="alert">{failure}</p>}
        <div className="settings-actions"><button type="button" className="button button-primary" disabled={saving} onClick={() => void save()}>Save changes</button>{saved && <p className="row-subtext" role="status">Saved — new checkouts use these loyalty rules.</p>}</div>
      </div>}
    </section>
  )
}
