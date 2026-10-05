import { useState } from 'react'
import { ApiError, describeFailure } from '../api/client.ts'
import {
  getReminderProvider,
  getReminderRules,
  sendTestMessage,
  updateReminderRule,
} from '../api/reminders.ts'
import type { ReminderRule, SendTestMessageResult } from '../api/reminders.ts'
import { ConfirmationDialog } from '../components/ConfirmationDialog.tsx'
import { ErrorState } from '../components/ErrorState.tsx'
import { FieldErrors } from '../components/FieldErrors.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { StatusPill } from '../components/StatusPill.tsx'
import { formatDuration } from '../format.ts'
import { useAsync } from '../useAsync.ts'

const MIN_OFFSET_MINUTES = 1
const MAX_OFFSET_MINUTES = 30 * 24 * 60

/** The owner-only settings: connection status, rule timing and the test send. */
export function ReminderSettings() {
  return (
    <div className="reminder-settings">
      <ProviderCard />
      <RulesCard />
      <TestMessageCard />
    </div>
  )
}

function ProviderCard() {
  const provider = useAsync(() => getReminderProvider(), [])

  return (
    <section className="tab-section" aria-label="WhatsApp connection">
      <div className="tab-section-head">
        <h4>WhatsApp connection</h4>
        {provider.data !== undefined && (
          <span className={provider.data.isConfigured ? 'pill pill-active' : 'pill pill-inactive'}>
            {provider.data.isConfigured ? 'Connected' : 'Not configured'}
          </span>
        )}
      </div>

      {provider.failure !== undefined && (
        <ErrorState message={provider.failure} onRetry={provider.reload} />
      )}

      {provider.data === undefined && provider.failure === undefined && (
        <LoadingState label="Checking WhatsApp…" />
      )}

      {provider.data !== undefined && (
        <>
          <p>{provider.data.description}</p>
          <p className="reminder-note">
            Channel: {provider.data.channel}. Credentials are configuration, never data — nothing
            on this screen reads or returns them.
          </p>
        </>
      )}
    </section>
  )
}

function RulesCard() {
  const rules = useAsync(() => getReminderRules(), [])
  const [actionFailure, setActionFailure] = useState<string>()

  return (
    <section className="tab-section" aria-label="Reminder rules">
      <div className="tab-section-head">
        <h4>Reminder rules</h4>
      </div>
      <p className="reminder-note">
        Timed reminders go out before the appointment; the event notices fire as soon as the
        appointment is confirmed, cancelled or rescheduled.
      </p>

      {rules.failure !== undefined && <ErrorState message={rules.failure} onRetry={rules.reload} />}
      {actionFailure !== undefined && (
        <p className="banner" role="alert">
          {actionFailure}
        </p>
      )}

      {rules.data === undefined && rules.failure === undefined && (
        <LoadingState label="Loading reminder rules…" />
      )}

      {rules.data !== undefined && (
        <table className="grid">
          <thead>
            <tr>
              <th scope="col">Notice</th>
              <th scope="col">Timing</th>
              <th scope="col">State</th>
              <th scope="col">Actions</th>
            </tr>
          </thead>
          <tbody>
            {rules.data.map(rule => (
              <RuleRow key={rule.id} rule={rule} reload={rules.reload} onFailure={setActionFailure} />
            ))}
          </tbody>
        </table>
      )}
    </section>
  )
}

function RuleRow({
  rule,
  reload,
  onFailure,
}: {
  rule: ReminderRule
  reload: () => void
  onFailure: (message: string | undefined) => void
}) {
  const [busy, setBusy] = useState(false)
  const [draft, setDraft] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<string[]>()

  // Only the timed reminders have a moment to configure; the event notices
  // fire on the change itself.
  const isTimed = rule.reminderType === 'Reminder'
  const inputValue = draft ?? String(rule.offsetMinutes)
  const preview = Number(inputValue)

  function toggle() {
    setBusy(true)
    onFailure(undefined)

    updateReminderRule(rule.id, { isEnabled: !rule.isEnabled })
      .then(reload)
      .catch((error: unknown) => onFailure(describeFailure(error)))
      .finally(() => setBusy(false))
  }

  function saveTiming() {
    if (!Number.isInteger(preview) || preview < MIN_OFFSET_MINUTES || preview > MAX_OFFSET_MINUTES) {
      setFieldErrors([
        `Enter between ${MIN_OFFSET_MINUTES} and ${MAX_OFFSET_MINUTES} minutes (30 days).`,
      ])
      return
    }

    setBusy(true)
    setFieldErrors(undefined)
    onFailure(undefined)

    updateReminderRule(rule.id, { offsetMinutes: preview })
      .then(() => {
        setDraft(null)
        reload()
      })
      .catch((error: unknown) => {
        setFieldErrors(
          error instanceof ApiError
            ? (error.errors.offsetMinutes ?? [describeFailure(error)])
            : [describeFailure(error)],
        )
      })
      .finally(() => setBusy(false))
  }

  return (
    <tr>
      <th scope="row">{rule.reminderType}</th>
      <td>
        {isTimed ? (
          <>
            <div className="reminder-timing">
              <input
                type="number"
                aria-label={`Timing in minutes for the ${rule.reminderType} notice`}
                min={MIN_OFFSET_MINUTES}
                max={MAX_OFFSET_MINUTES}
                value={inputValue}
                disabled={busy}
                onChange={event => setDraft(event.target.value)}
              />
              <span className="reminder-note">
                {Number.isInteger(preview) && preview >= MIN_OFFSET_MINUTES && preview <= MAX_OFFSET_MINUTES
                  ? `${formatDuration(preview)} before`
                  : 'minutes before'}
              </span>
              <button
                type="button"
                className="button"
                onClick={saveTiming}
                disabled={busy || draft === null}
              >
                Save timing
              </button>
            </div>
            <FieldErrors messages={fieldErrors} />
          </>
        ) : (
          <span className="reminder-note">Sent right away</span>
        )}
      </td>
      <td>
        <StatusPill isActive={rule.isEnabled} />
      </td>
      <td className="actions">
        <button type="button" className="button" onClick={toggle} disabled={busy}>
          {rule.isEnabled ? 'Disable' : 'Enable'}
        </button>
      </td>
    </tr>
  )
}

function TestMessageCard() {
  const [phone, setPhone] = useState('')
  const [confirming, setConfirming] = useState(false)
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<SendTestMessageResult>()
  const [failure, setFailure] = useState<string>()

  function send() {
    setConfirming(false)
    setBusy(true)
    setResult(undefined)
    setFailure(undefined)

    sendTestMessage(phone.trim())
      .then(setResult)
      .catch((error: unknown) => setFailure(describeFailure(error)))
      .finally(() => setBusy(false))
  }

  return (
    <section className="tab-section" aria-label="Send test message">
      <div className="tab-section-head">
        <h4>Send test message</h4>
      </div>
      <p className="reminder-note">
        Sends a clearly marked test through the configured provider to the number you enter. It
        never touches customer reminder rows or their opt-in state.
      </p>

      <div className="reminder-phone-row">
        <label className="field">
          <span>Phone number</span>
          <input
            name="phoneNumber"
            type="tel"
            placeholder="+14155552671"
            value={phone}
            onChange={event => setPhone(event.target.value)}
          />
        </label>
        <button
          type="button"
          className="button"
          disabled={phone.trim() === '' || busy}
          onClick={() => setConfirming(true)}
        >
          {busy ? 'Sending…' : 'Send test message'}
        </button>
      </div>

      {failure !== undefined && (
        <p className="banner" role="alert">
          {failure}
        </p>
      )}

      {result !== undefined && result.success && (
        <p className="banner banner-success" role="status">
          {result.providerMessageId === null
            ? 'Test message sent.'
            : `Test message sent — provider message id ${result.providerMessageId}.`}
        </p>
      )}

      {result !== undefined && !result.success && (
        <p className="banner" role="alert">
          {result.failureReason ?? 'The test message could not be sent.'}
        </p>
      )}

      {confirming && (
        <ConfirmationDialog
          title="Send test WhatsApp"
          confirmLabel="Send test"
          busy={busy}
          onCancel={() => setConfirming(false)}
          onConfirm={send}
        >
          <p>
            Send a test WhatsApp message to <strong>{phone.trim()}</strong> through the configured
            provider? It is clearly marked as a test and no customer reminder is involved.
          </p>
        </ConfirmationDialog>
      )}
    </section>
  )
}
