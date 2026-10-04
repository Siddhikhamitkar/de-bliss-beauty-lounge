import { useMemo } from 'react'
import type { Appointment, CalendarMarker } from '../api/contracts.ts'
import { dayKeyOf, formatClock, minutesOfDay } from './calendarTime.ts'

interface MonthViewProps {
  days: Date[]
  anchorMonth: number
  appointments: Appointment[]
  markers: CalendarMarker[]
  onSelectDay: (day: Date) => void
  onAppointmentClick: (appointment: Appointment) => void
}

const WEEKDAY_LABELS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'] as const

/**
 * The month overview: six Monday-first weeks, one chip per booking and a line
 * per marker. It is a triage view — clicking a day drops into the day grid,
 * clicking a chip opens the booking.
 */
export function MonthView({
  days,
  anchorMonth,
  appointments,
  markers,
  onSelectDay,
  onAppointmentClick,
}: MonthViewProps) {
  const byDay = useMemo(() => {
    const grouped = new Map<string, Appointment[]>()

    for (const appointment of appointments) {
      const key = dayKeyOf(new Date(appointment.startsAt))
      const list = grouped.get(key) ?? []
      list.push(appointment)
      grouped.set(key, list)
    }

    for (const list of grouped.values()) {
      list.sort((left, right) => left.startsAt.localeCompare(right.startsAt))
    }

    return grouped
  }, [appointments])

  const markersByDay = useMemo(() => {
    const grouped = new Map<string, CalendarMarker[]>()

    for (const marker of markers) {
      for (const day of days) {
        const start = new Date(marker.startsAt).getTime()
        const end = new Date(marker.endsAt).getTime()
        const dayStart = day.getTime()
        const dayEnd = dayStart + 24 * 60 * 60 * 1000

        if (end > dayStart && start < dayEnd) {
          const key = dayKeyOf(day)
          const list = grouped.get(key) ?? []
          list.push(marker)
          grouped.set(key, list)
        }
      }
    }

    return grouped
  }, [markers, days])

  return (
    <div className="cal-month" role="grid" aria-label="Month overview">
      {WEEKDAY_LABELS.map(label => (
        <div key={label} className="cal-month-weekday" role="columnheader">
          {label}
        </div>
      ))}

      {days.map(day => {
        const key = dayKeyOf(day)
        const dayAppointments = byDay.get(key) ?? []
        const dayMarkers = markersByDay.get(key) ?? []

        return (
          <div
            key={key}
            role="gridcell"
            className={[
              'cal-month-cell',
              day.getMonth() === anchorMonth ? '' : 'is-outside',
              dayAppointments.length === 0 && dayMarkers.length === 0 ? 'is-quiet' : '',
            ]
              .filter(Boolean)
              .join(' ')}
          >
            <button type="button" className="cal-month-day" onClick={() => onSelectDay(day)}>
              {day.getDate()}
            </button>

            {dayMarkers.slice(0, 1).map(marker => (
              <div key={marker.id} className={`cal-month-marker marker-${marker.kind}`}>
                {marker.label ?? marker.type ?? marker.kind}
              </div>
            ))}

            {dayAppointments.map(appointment => (
              <button
                key={appointment.id}
                type="button"
                className={`cal-month-event status-${appointment.status}`}
                style={
                  appointment.services[0]?.staffColor === null ||
                  appointment.services[0] === undefined
                    ? {}
                    : { borderLeftColor: appointment.services[0].staffColor }
                }
                onClick={() => onAppointmentClick(appointment)}
              >
                <span className="cal-month-event-time">
                  {formatClock(minutesOfDay(new Date(appointment.startsAt)))}
                </span>
                <span className="cal-month-event-name">{appointment.customerName}</span>
                <span className="cal-month-event-service">
                  {appointment.services.map(line => line.serviceName).join(' + ')}
                </span>
              </button>
            ))}
          </div>
        )
      })}
    </div>
  )
}
