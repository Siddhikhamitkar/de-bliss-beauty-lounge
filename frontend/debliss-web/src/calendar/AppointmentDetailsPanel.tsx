import { useState } from 'react'
import { describeFailure } from '../api/client.ts'
import type { Appointment } from '../api/contracts.ts'
import { getAppointment, setAppointmentStatus } from '../api/scheduling.ts'
import { ErrorState } from '../components/ErrorState.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { formatDuration, formatPrice } from '../format.ts'
import { useAsync } from '../useAsync.ts'
import { formatClock, minutesOfDay, parseDayKey, dayKeyOf } from './calendarTime.ts'

interface AppointmentDetailsPanelProps {
  appointmentId: string
  onClose: () => void
  /** The calendar refreshes after any action lands. */
  onChanged: () => void
  /** Edit (change services/staff/time in full) opens the booking dialog. */
  onEdit: (appointment: Appointment) => void
  /** Checkout opens the billing screen for a completed visit. */
  onCheckout: (appointment: Appointment) => void
}

/** Target status → the verb a receptionist would say. */
const STATUS_ACTION_LABELS: Record<string, string> = {
  Confirmed: 'Confirm',
  CheckedIn: 'Check in',
  InProgress: 'Start',
  Completed: 'Complete',
  Cancelled: 'Cancel',
  NoShow: 'No show',
}

/** The transitions the backend allows out of each status; anything else is not offered. */
const ALLOWED_TRANSITIONS: Record<string, readonly string[]> = {
  Pending: ['Confirmed', 'Cancelled'],
  Confirmed: ['CheckedIn', 'Cancelled', 'NoShow'],
  CheckedIn: ['InProgress', 'Cancelled'],
  InProgress: ['Completed'],
  Waitlisted: ['Confirmed', 'Cancelled'],
}

/**
 * The booking's side panel: who, what, when, how much, and the quick actions
 * that move the visit along its day. It slides over the grid — details never
 * navigate away from the schedule.
 */
export function AppointmentDetailsPanel({
  appointmentId,
  onClose,
  onChanged,
  onEdit,
  onCheckout,
}: AppointmentDetailsPanelProps) {
  const appointment = useAsync(() => getAppointment(appointmentId), [appointmentId])
  const [actionFailure, setActionFailure] = useState<string>()
  const [busy, setBusy] = useState(false)
  const [reasonFor, setReasonFor] = useState<string | null>(null)
  const [reason, setReason] = useState('')

  function transition(target: string) {
    if (appointment.data === undefined) {
      return
    }

    setBusy(true)
    setActionFailure(undefined)

    setAppointmentStatus(appointment.data.id, {
      status: target,
      reason: target === 'Cancelled' || target === 'NoShow' ? reason || null : null,
    })
      .then(() => {
        setReasonFor(null)
        setReason('')
        appointment.reload()
        onChanged()
      })
      .catch((error: unknown) => setActionFailure(describeFailure(error)))
      .finally(() => setBusy(false))
  }

  const current = appointment.data
  const bundleName = current?.bundleName ?? null
  const bundlePrice = current?.bundlePrice ?? null

  return (
    <div className="cal-backdrop" onClick={onClose}>
      <aside
        className="cal-details"
        role="dialog"
        aria-modal="true"
        aria-label="Appointment details"
        onClick={event => event.stopPropagation()}
      >
        {appointment.failure !== undefined && (
          <ErrorState message={appointment.failure} onRetry={appointment.reload} />
        )}

        {current === undefined && appointment.failure === undefined && (
          <LoadingState label="Loading appointment…" />
        )}

        {current !== undefined && (
          <>
            <div className="cal-details-head">
              <div>
                <h3>{current.customerName}</h3>
                <span className={`pill status-pill status-${current.status}`}>{current.status}</span>
              </div>
              <button type="button" className="button" aria-label="Close details" onClick={onClose}>
                ×
              </button>
            </div>

            {current.customerPhone !== null && (
              <p className="cal-details-phone">{current.customerPhone}</p>
            )}

            <dl className="detail-grid">
              <dt>Date</dt>
              <dd>
                {parseDayKey(dayKeyOf(new Date(current.startsAt))).toLocaleDateString(undefined, {
                  weekday: 'long',
                  day: 'numeric',
                  month: 'long',
                  year: 'numeric',
                })}
              </dd>
              <dt>Time</dt>
              <dd>
                {formatClock(minutesOfDay(new Date(current.startsAt)))} –{' '}
                {formatClock(minutesOfDay(new Date(current.endsAt)))}
              </dd>
              <dt>Duration</dt>
              <dd>{formatDuration(current.totalDurationMinutes)}</dd>
              <dt>Price</dt>
              <dd>
                {bundleName !== null && bundlePrice !== null ? (
                  <>
                    {formatPrice(bundlePrice)}
                    <span className="was-price">{formatPrice(current.totalPrice)}</span>
                  </>
                ) : (
                  formatPrice(current.totalPrice)
                )}
              </dd>
              <dt>Source</dt>
              <dd>{current.source}</dd>
            </dl>

            <div className="cal-details-services">
              <h4>{bundleName === null ? 'Services' : `Bundle · ${bundleName}`}</h4>
              <ul>
                {current.services.map((line, index) => (
                  <li key={`${line.serviceId}-${index}`}>
                    <span className="cal-details-service-name">
                      {line.serviceName}
                      <em>
                        {formatDuration(line.durationMinutes)} · {formatPrice(line.unitPrice)}
                      </em>
                    </span>
                    <span className="cal-details-service-staff">
                      <i
                        className="cal-color-dot"
                        style={line.staffColor === null ? {} : { background: line.staffColor }}
                      />
                      {line.staffFirstName} {line.staffLastName}
                    </span>
                  </li>
                ))}
              </ul>
              {bundleName !== null && bundlePrice !== null && current.totalPrice > bundlePrice && (
                <p className="cal-details-saving">
                  Bundle saving {formatPrice(current.totalPrice - bundlePrice)} — the services above
                  are still performed and billed as one visit.
                </p>
              )}
            </div>

            {current.notes !== null && current.notes !== '' && (
              <div className="cal-details-notes">
                <h4>Notes</h4>
                <p>{current.notes}</p>
              </div>
            )}

            {current.cancellationReason !== null && current.cancellationReason !== '' && (
              <div className="cal-details-notes">
                <h4>Cancellation reason</h4>
                <p>{current.cancellationReason}</p>
              </div>
            )}

            {actionFailure !== undefined && (
              <p className="banner" role="alert">{actionFailure}</p>
            )}

            <div className="cal-details-actions">
              {(ALLOWED_TRANSITIONS[current.status] ?? []).map(target =>
                target === 'Cancelled' || target === 'NoShow' ? (
                  <button
                    key={target}
                    type="button"
                    className="button"
                    disabled={busy}
                    onClick={() => setReasonFor(reasonFor === target ? null : target)}
                  >
                    {STATUS_ACTION_LABELS[target]}
                  </button>
                ) : (
                  <button
                    key={target}
                    type="button"
                    className={`button${target === 'Completed' || target === 'CheckedIn' ? ' button-primary' : ''}`}
                    disabled={busy}
                    onClick={() => transition(target)}
                  >
                    {STATUS_ACTION_LABELS[target]}
                  </button>
                ),
              )}

              {current.status === 'Completed' && (
                <button
                  type="button"
                  className="button button-primary"
                  disabled={busy}
                  onClick={() => onCheckout(current)}
                >
                  Checkout
                </button>
              )}

              <button type="button" className="button" disabled={busy} onClick={() => onEdit(current)}>
                Edit
              </button>
            </div>

            {reasonFor !== null && (
              <div className="cal-details-reason">
                <label className="field">
                  <span>Reason for {STATUS_ACTION_LABELS[reasonFor].toLowerCase()}</span>
                  <textarea
                    rows={2}
                    value={reason}
                    onChange={event => setReason(event.target.value)}
                  />
                </label>
                <div className="dialog-actions">
                  <button type="button" className="button" onClick={() => setReasonFor(null)}>
                    Back
                  </button>
                  <button
                    type="button"
                    className="button button-primary"
                    disabled={busy}
                    onClick={() => transition(reasonFor)}
                  >
                    {STATUS_ACTION_LABELS[reasonFor]}
                  </button>
                </div>
              </div>
            )}

            {(ALLOWED_TRANSITIONS[current.status] ?? []).length > 0 && (
              <p className="cal-details-hint">
                Reschedule by dragging the booking on the grid, or edit to change services and
                staff.
              </p>
            )}
          </>
        )}
      </aside>
    </div>
  )
}
