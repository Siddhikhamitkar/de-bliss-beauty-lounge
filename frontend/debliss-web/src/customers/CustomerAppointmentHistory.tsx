import { listCustomerAppointments } from '../api/customers.ts'
import { EmptyState } from '../components/EmptyState.tsx'
import { ErrorState } from '../components/ErrorState.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { formatDateTime } from '../format.ts'
import { useAsync } from '../useAsync.ts'

interface CustomerAppointmentHistoryProps {
  customerId: string
}

/**
 * Read-only view over the scheduling tables, newest first. The appointment
 * module does not exist yet, so for now this only ever shows the empty state
 * — but it loads through the real API, ready for the calendar to populate.
 */
export function CustomerAppointmentHistory({ customerId }: CustomerAppointmentHistoryProps) {
  const { data, failure, reload } = useAsync(
    () => listCustomerAppointments(customerId),
    [customerId],
  )

  return (
    <section className="panel">
      <div className="panel-header">
        <h2>Appointment History</h2>
      </div>
      {failure !== undefined ? (
        <ErrorState message={failure} onRetry={reload} />
      ) : data === undefined ? (
        <LoadingState />
      ) : data.length === 0 ? (
        <EmptyState>No appointments yet.</EmptyState>
      ) : (
        <table className="grid">
          <thead>
            <tr>
              <th scope="col">Date</th>
              <th scope="col">Services</th>
              <th scope="col">Staff</th>
              <th scope="col">Status</th>
            </tr>
          </thead>
          <tbody>
            {data.map(appointment => (
              <tr key={`${appointment.startsAt}`}>
                <th scope="row">{formatDateTime(appointment.startsAt)}</th>
                <td>{appointment.serviceNames.join(', ')}</td>
                <td>{appointment.staffNames.join(', ')}</td>
                <td>{appointment.status}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  )
}
