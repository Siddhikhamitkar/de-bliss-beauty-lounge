import { useState } from 'react'
import { getTaxSettings, updateTaxSettings } from '../api/settings.ts'
import { ApiError, describeFailure } from '../api/client.ts'
import type { TaxSettings } from '../api/contracts.ts'
import { ErrorState } from '../components/ErrorState.tsx'
import { FieldErrors } from '../components/FieldErrors.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { useAsync } from '../useAsync.ts'

function parseNumber(text: string): number | null {
  if (text.trim() === '') {
    return null
  }

  const value = Number(text)

  return Number.isNaN(value) ? null : value
}

/**
 * The salon's tax configuration, owner-only: the VAT rate every checkout,
 * invoice and receipt is charged at, and whether menu prices already contain
 * it. Both rules live on the server — this screen edits the single source of
 * truth, and finalized invoices keep the snapshot they were billed with, so a
 * change here never rewrites history.
 */
export function SettingsPage() {
  const settings = useAsync(() => getTaxSettings(), [])
  const [rateInput, setRateInput] = useState('')
  const [pricesIncludeTax, setPricesIncludeTax] = useState(false)
  const [saving, setSaving] = useState(false)
  const [failure, setFailure] = useState<string>()
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>()
  const [savedNote, setSavedNote] = useState(false)
  // The form mirrors the last settings the server handed back; typing in it
  // never re-triggers the sync. Adjusted during render — the same pattern
  // useAsync itself uses — so a reload lands the saved values in one paint.
  const [loadedFrom, setLoadedFrom] = useState<TaxSettings>()

  if (settings.data !== undefined && settings.data !== loadedFrom) {
    setLoadedFrom(settings.data)
    setRateInput(String(settings.data.taxRate))
    setPricesIncludeTax(settings.data.pricesIncludeTax)
  }

  function save() {
    setSaving(true)
    setFailure(undefined)
    setFieldErrors(undefined)
    setSavedNote(false)

    updateTaxSettings({ taxRate: parseNumber(rateInput), pricesIncludeTax })
      .then(() => {
        setSavedNote(true)
        settings.reload()
      })
      .catch((error: unknown) => {
        if (error instanceof ApiError && Object.keys(error.errors).length > 0) {
          setFieldErrors(error.errors)
        } else {
          setFailure(describeFailure(error))
        }
      })
      .finally(() => setSaving(false))
  }

  return (
    <section className="panel" aria-label="Tax settings">
      <div className="panel-header">
        <div>
          <h2>Tax settings</h2>
          <p className="row-subtext">
            The VAT rate and pricing convention applied to every new checkout. Invoices already
            finalized keep the rate they were billed with.
          </p>
        </div>
      </div>

      {settings.failure !== undefined && (
        <ErrorState message={settings.failure} onRetry={settings.reload} />
      )}

      {settings.data === undefined && settings.failure === undefined && (
        <LoadingState label="Loading tax settings…" />
      )}

      {settings.data !== undefined && (
        <div className="settings-form">
          <label className="field">
            <span>VAT rate (%)</span>
            <input
              type="number"
              name="taxRate"
              min="0"
              max="100"
              step="0.01"
              value={rateInput}
              onChange={event => {
                setRateInput(event.target.value)
                setSavedNote(false)
              }}
            />
            <FieldErrors messages={fieldErrors?.['taxRate']} />
          </label>

          <div className="field">
            <span>Pricing convention</span>
            <div className="payment-methods" role="radiogroup" aria-label="Pricing convention">
              <label className="payment-method">
                <input
                  type="radio"
                  name="pricesIncludeTax"
                  value="exclusive"
                  checked={!pricesIncludeTax}
                  onChange={() => {
                    setPricesIncludeTax(false)
                    setSavedNote(false)
                  }}
                />
                Added at checkout
              </label>
              <label className="payment-method">
                <input
                  type="radio"
                  name="pricesIncludeTax"
                  value="inclusive"
                  checked={pricesIncludeTax}
                  onChange={() => {
                    setPricesIncludeTax(true)
                    setSavedNote(false)
                  }}
                />
                Included in prices
              </label>
            </div>
            <p className="row-subtext">
              {pricesIncludeTax
                ? 'Menu prices already contain VAT; receipts show the tax inside the total.'
                : 'Menu prices exclude VAT; the rate is added to the discounted amount at checkout.'}
            </p>
            <FieldErrors messages={fieldErrors?.['pricesIncludeTax']} />
          </div>

          {failure !== undefined && <p className="banner" role="alert">{failure}</p>}

          <div className="settings-actions">
            <button
              type="button"
              className="button button-primary"
              disabled={saving}
              onClick={save}
            >
              Save changes
            </button>
            {savedNote && (
              <p className="row-subtext" role="status">
                Saved — new checkouts use this configuration immediately.
              </p>
            )}
          </div>
        </div>
      )}
    </section>
  )
}
