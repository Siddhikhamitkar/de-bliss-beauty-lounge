import type { CustomerAppointmentSummary } from '../api/contracts.ts'
import { formatDateTime } from '../format.ts'

interface CustomerSummaryProps {
  summary: CustomerAppointmentSummary
}

/**
 * The visit counts a receptionist quotes at a glance. Spending, packages and
 * loyalty will slot into this panel later; the shape already carries them.
 */
export function CustomerSummary({ summary }: CustomerSummaryProps) {
  return (
    <section className="panel" aria-label="Customer summary">
      <div className="panel-header">
        <h2>Customer Summary</h2>
      </div>
      <dl className="detail-grid">
        <dt>Total appointments</dt>
        <dd>{summary.totalAppointments}</dd>
        <dt>Completed</dt>
        <dd>{summary.completedAppointments}</dd>
        <dt>Cancelled</dt>
        <dd>{summary.cancelledAppointments}</dd>
        <dt>Last visit</dt>
        <dd>{summary.lastVisitAt === null ? '—' : formatDateTime(summary.lastVisitAt)}</dd>
      </dl>
    </section>
  )
}
