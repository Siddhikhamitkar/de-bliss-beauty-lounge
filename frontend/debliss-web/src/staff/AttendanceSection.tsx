import { useState } from 'react'
import { listStaffAttendance } from '../api/staff.ts'
import type { StaffAttendance } from '../api/contracts.ts'
import { formatDate, formatTime } from '../format.ts'
import { useAsync } from '../useAsync.ts'
import { EmptyState } from '../components/EmptyState.tsx'
import { ErrorState } from '../components/ErrorState.tsx'
import { LoadingState } from '../components/LoadingState.tsx'

// Friendly labels over the wire names; an unknown future name still renders.
const AttendanceStatusLabels: Record<string, string> = {
  CheckedIn: 'Checked in',
  CheckedOut: 'Checked out',
}

function attendanceStatusLabel(name: string): string {
  return AttendanceStatusLabels[name] ?? name
}

/** The shift the owner's weekly pattern planned for that weekday, e.g. "09:00–17:00". */
function scheduledShiftLabel(record: StaffAttendance): string {
  if (record.scheduledIntervals.length === 0) {
    return 'Not scheduled'
  }

  return record.scheduledIntervals.map(({ start, end }) => `${start}–${end}`).join(', ')
}

/**
 * The member's clock-in/clock-out history, read-only. Records are written by the
 * member's own check-in and check-out through the future staff mobile app — this
 * view never edits them, and it shows timestamps only, never a worked-hours total.
 */
export function AttendanceSection({ staffId }: { staffId: string }) {
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const attendance = useAsync(
    () =>
      listStaffAttendance(
        staffId,
        from === '' ? undefined : from,
        to === '' ? undefined : to,
      ),
    [staffId, from, to],
  )

  return (
    <section className="tab-section" aria-label="Attendance">
      <div className="tab-section-head">
        <h4>Attendance</h4>
      </div>

      <div className="filters">
        <label className="field">
          From
          <input
            type="date"
            value={from}
            onChange={event => setFrom(event.target.value)}
          />
        </label>
        <label className="field">
          To
          <input
            type="date"
            value={to}
            onChange={event => setTo(event.target.value)}
          />
        </label>
      </div>

      {attendance.failure !== undefined && (
        <ErrorState message={attendance.failure} onRetry={attendance.reload} />
      )}

      {attendance.failure === undefined && attendance.data === undefined && (
        <LoadingState label="Loading attendance…" />
      )}

      {attendance.failure === undefined && attendance.data !== undefined && (
        attendance.data.length === 0 ? (
          <EmptyState>
            {from === '' && to === ''
              ? 'No attendance records yet.'
              : 'No attendance records in this range.'}
          </EmptyState>
        ) : (
          <table className="grid">
            <thead>
              <tr>
                <th scope="col">Date</th>
                <th scope="col">Scheduled shift</th>
                <th scope="col">Check-in</th>
                <th scope="col">Check-out</th>
                <th scope="col">Status</th>
              </tr>
            </thead>
            <tbody>
              {attendance.data.map(record => (
                <tr key={record.id}>
                  <th scope="row">{formatDate(record.workDate)}</th>
                  <td>{scheduledShiftLabel(record)}</td>
                  <td>{formatTime(record.checkInAt)}</td>
                  <td>{record.checkOutAt === null ? '—' : formatTime(record.checkOutAt)}</td>
                  <td>{attendanceStatusLabel(record.status)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )
      )}
    </section>
  )
}
