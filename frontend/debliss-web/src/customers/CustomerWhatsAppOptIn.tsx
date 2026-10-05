import { useState } from 'react'
import { describeFailure } from '../api/client.ts'
import { setCustomerWhatsAppOptIn } from '../api/customers.ts'
import type { CustomerProfile, WhatsAppOptInStatus } from '../api/contracts.ts'
import { ConfirmationDialog } from '../components/ConfirmationDialog.tsx'
import { formatDateTime } from '../format.ts'

interface CustomerWhatsAppOptInProps {
  profile: CustomerProfile
  onChanged: () => void
}

/** The copy each consent action must show before it can be recorded. */
const CONFIRMATIONS: Record<
  WhatsAppOptInStatus,
  { title: string; confirmLabel: string; body: (name: string) => string }
> = {
  OptedIn: {
    title: 'Record WhatsApp consent',
    confirmLabel: 'Record opt-in',
    body: name =>
      `Record ${name}'s consent to receive appointment messages on WhatsApp? Confirmation, ` +
      `reminder, cancellation and reschedule messages may then be sent to their number.`,
  },
  OptedOut: {
    title: 'Record a WhatsApp opt-out',
    confirmLabel: 'Record opt-out',
    body: name =>
      `${name} will not receive any appointment message on WhatsApp until consent is recorded again.`,
  },
  Unknown: {
    title: 'Clear the consent record',
    confirmLabel: 'Clear record',
    body: name =>
      `Remove the consent record entirely? With no record, ${name} counts as not consented and ` +
      `no appointment messages are sent.`,
  },
}

/**
 * The consent gate for appointment messages, recorded by the front desk in
 * person. Nothing is ever assumed: the reminder engine sends only while this
 * record says OptedIn, so recording and withdrawing here is the switch that
 * decides whether a customer hears from the salon at all.
 */
export function CustomerWhatsAppOptIn({ profile, onChanged }: CustomerWhatsAppOptInProps) {
  const [confirming, setConfirming] = useState<WhatsAppOptInStatus>()
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<string>()

  function record(status: WhatsAppOptInStatus) {
    setConfirming(undefined)
    setBusy(true)
    setFailure(undefined)

    setCustomerWhatsAppOptIn(profile.id, status)
      .then(() => onChanged())
      .catch((error: unknown) => setFailure(describeFailure(error)))
      .finally(() => setBusy(false))
  }

  function consentLine(): string {
    switch (profile.whatsAppOptInStatus) {
      case 'OptedIn':
        return (
          `Opted in${profile.whatsAppOptInDate === null ? '' : ` on ${formatDateTime(profile.whatsAppOptInDate)}`}` +
          (profile.whatsAppOptInSource === null ? '' : ` · recorded at ${profile.whatsAppOptInSource}`)
        )
      case 'OptedOut':
        return `Opted out${
          profile.whatsAppOptOutDate === null ? '' : ` on ${formatDateTime(profile.whatsAppOptOutDate)}`
        }`
      case 'Unknown':
        return 'Not recorded'
    }
  }

  return (
    <section className="tab-section" aria-label="WhatsApp consent">
      <div className="tab-section-head">
        <h4>WhatsApp messages</h4>
      </div>
      <p className="reminder-note">
        Appointment messages are sent on WhatsApp only while the customer's consent is recorded
        here. Capture it in person — nothing is ever assumed.
      </p>

      <dl className="kv-grid">
        <dt>Consent</dt>
        <dd>{consentLine()}</dd>
      </dl>

      <div className="whatsapp-optin-actions">
        {profile.whatsAppOptInStatus !== 'OptedIn' && (
          <button
            type="button"
            className="button"
            disabled={busy}
            onClick={() => setConfirming('OptedIn')}
          >
            Record opt-in
          </button>
        )}
        {profile.whatsAppOptInStatus !== 'OptedOut' && (
          <button
            type="button"
            className="button"
            disabled={busy}
            onClick={() => setConfirming('OptedOut')}
          >
            Record opt-out
          </button>
        )}
        {profile.whatsAppOptInStatus !== 'Unknown' && (
          <button
            type="button"
            className="button"
            disabled={busy}
            onClick={() => setConfirming('Unknown')}
          >
            Clear record
          </button>
        )}
      </div>

      {failure !== undefined && (
        <p className="banner" role="alert">
          {failure}
        </p>
      )}

      {confirming !== undefined && (
        <ConfirmationDialog
          title={CONFIRMATIONS[confirming].title}
          confirmLabel={CONFIRMATIONS[confirming].confirmLabel}
          busy={busy}
          onCancel={() => setConfirming(undefined)}
          onConfirm={() => record(confirming)}
        >
          <p>{CONFIRMATIONS[confirming].body(profile.firstName)}</p>
        </ConfirmationDialog>
      )}
    </section>
  )
}
