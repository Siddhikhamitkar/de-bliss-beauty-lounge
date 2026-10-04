import type { CustomerAppointmentSummary } from '../api/contracts.ts'
import { formatDateTime } from '../format.ts'

interface CustomerSummaryProps {
  summary: CustomerAppointmentSummary
}

/**
 * The visit counts a receptionist quotes at a glance, as a row of tiles:
 * four counters plus when the customer was last in. Spending, packages and
 * loyalty will slot in beside these later.
 */
export function CustomerSummary({ summary }: CustomerSummaryProps) {
  return (
    <section className="tab-section" aria-label="Customer summary">
      <div className="tab-section-head">
        <h4>Summary</h4>
      </div>
      <div className="stat-grid">
        <div className="stat-tile">
          <span className="stat-value">{summary.totalAppointments}</span>
          <span className="stat-label">Appointments</span>
        </div>
        <div className="stat-tile">
          <span className="stat-value">{summary.completedAppointments}</span>
          <span className="stat-label">Completed</span>
        </div>
        <div className="stat-tile">
          <span className="stat-value">{summary.cancelledAppointments}</span>
          <span className="stat-label">Cancelled</span>
        </div>
        <div className="stat-tile">
          <span className="stat-value">{summary.noShowAppointments}</span>
          <span className="stat-label">No-shows</span>
        </div>
        <div className="stat-tile stat-tile-wide">
          <span className="stat-label">Last visit</span>
          <span className="stat-value stat-value-date">
            {summary.lastVisitAt === null ? '—' : formatDateTime(summary.lastVisitAt)}
          </span>
        </div>
      </div>
    </section>
  )
}
