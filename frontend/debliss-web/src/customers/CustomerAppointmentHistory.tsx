import { TERMINAL_STATUSES } from '../api/contracts.ts'
import type { CustomerAppointmentItem } from '../api/contracts.ts'
import { listCustomerAppointments } from '../api/customers.ts'
import { EmptyState } from '../components/EmptyState.tsx'
import { ErrorState } from '../components/ErrorState.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { formatDate } from '../format.ts'
import { useAsync } from '../useAsync.ts'

interface CustomerAppointmentHistoryProps {
  customerId: string
}

interface AppointmentGroups {
  upcoming: CustomerAppointmentItem[]
  history: CustomerAppointmentItem[]
  total: number
}

function formatTime(iso: string): string {
  return new Date(iso).toLocaleTimeString(undefined, { timeStyle: 'short' })
}

/**
 * Read-only view over the scheduling tables, split into what is still coming
 * up and what is already past (or finished early). Both halves read through
 * the real API, so the calendar's bookings show up here without a second
 * source of truth.
 */
export function CustomerAppointmentHistory({ customerId }: CustomerAppointmentHistoryProps) {
  const { data, failure, reload } = useAsync<AppointmentGroups>(
    async () => {
      const appointments = await listCustomerAppointments(customerId)
      // The split is frozen per load so the two halves stay stable while the
      // drawer is open, instead of re-classifying on every render.
      const now = Date.now()
      const upcoming = appointments
        .filter(
          appointment =>
            new Date(appointment.startsAt).getTime() >= now &&
            !TERMINAL_STATUSES.includes(appointment.status),
        )
        .sort((a, b) => new Date(a.startsAt).getTime() - new Date(b.startsAt).getTime())
      const upcomingIds = new Set(upcoming.map(appointment => appointment.id))
      const history = appointments.filter(appointment => !upcomingIds.has(appointment.id))
      return { upcoming, history, total: appointments.length }
    },
    [customerId],
  )

  if (failure !== undefined) {
    return (
      <section className="tab-section" aria-label="Appointments">
        <div className="tab-section-head">
          <h4>Appointments</h4>
        </div>
        <ErrorState message={failure} onRetry={reload} />
      </section>
    )
  }

  if (data === undefined) {
    return (
      <section className="tab-section" aria-label="Appointments">
        <div className="tab-section-head">
          <h4>Appointments</h4>
        </div>
        <LoadingState label="Loading appointments…" />
      </section>
    )
  }

  if (data.total === 0) {
    return (
      <section className="tab-section" aria-label="Appointments">
        <div className="tab-section-head">
          <h4>Appointments</h4>
        </div>
        <EmptyState>No appointments yet.</EmptyState>
      </section>
    )
  }

  return (
    <>
      <section className="tab-section" aria-label="Upcoming appointments">
        <div className="tab-section-head">
          <h4>Upcoming</h4>
        </div>
        {data.upcoming.length === 0 ? (
          <EmptyState>Nothing scheduled.</EmptyState>
        ) : (
          <AppointmentList appointments={data.upcoming} />
        )}
      </section>

      <section className="tab-section" aria-label="Past appointments">
        <div className="tab-section-head">
          <h4>Past</h4>
        </div>
        {data.history.length === 0 ? (
          <EmptyState>No past appointments yet.</EmptyState>
        ) : (
          <AppointmentList appointments={data.history} />
        )}
      </section>
    </>
  )
}

function AppointmentList({ appointments }: { appointments: CustomerAppointmentItem[] }) {
  return (
    <ul className="appt-list">
      {appointments.map(appointment => (
        <li key={appointment.id} className="appt-card">
          <div className="appt-when">
            <span className="appt-date">{formatDate(appointment.startsAt)}</span>
            <span className="appt-time">
              {formatTime(appointment.startsAt)} – {formatTime(appointment.endsAt)}
            </span>
          </div>
          <div className="appt-what">
            <span className="appt-services">{appointment.serviceNames.join(', ')}</span>
            {appointment.staffNames.length > 0 && (
              <span className="appt-staff">{appointment.staffNames.join(', ')}</span>
            )}
          </div>
          <span className={`pill status-pill status-${appointment.status}`}>
            {appointment.status}
          </span>
        </li>
      ))}
    </ul>
  )
}
