import type { PointerEvent } from 'react'
import type { Appointment } from '../api/contracts.ts'
import { TERMINAL_STATUSES } from '../api/contracts.ts'
import { formatClock, PIXELS_PER_MINUTE } from './calendarTime.ts'
import type { PlacedSegment } from './calendarTime.ts'

interface AppointmentBlockProps {
  segment: PlacedSegment
  /** Fades and ignores the pointer while its drag is in flight. */
  isDragging: boolean
  onPointerDownMove: (event: PointerEvent<HTMLElement>, segment: PlacedSegment) => void
  onPointerDownResize: (event: PointerEvent<HTMLElement>, segment: PlacedSegment) => void
  onPointerMove: (event: PointerEvent<HTMLElement>) => void
  onPointerUp: (event: PointerEvent<HTMLElement>) => void
  onPointerCancel: (event: PointerEvent<HTMLElement>) => void
  onActivate: (appointment: Appointment) => void
}

/**
 * One service line of a booking on the grid. Height follows duration, so a
 * 30-minute line is half the size of an hour — the grid stays proportional.
 * The left stripe is the booking's status; the body tints toward the staff
 * member's calendar colour. Terminal bookings render as history: no handles.
 */
export function AppointmentBlock({
  segment,
  isDragging,
  onPointerDownMove,
  onPointerDownResize,
  onPointerMove,
  onPointerUp,
  onPointerCancel,
  onActivate,
}: AppointmentBlockProps) {
  const { appointment, line, startMinutes, durationMinutes, lineIndex, lineCount, laneIndex, laneCount } = segment
  const top = startMinutes * PIXELS_PER_MINUTE
  const height = durationMinutes * PIXELS_PER_MINUTE
  const isTerminal = TERMINAL_STATUSES.includes(appointment.status)
  const isLastLine = lineIndex === lineCount - 1
  const serviceLabel =
    lineCount === 1 ? line.serviceName : `${line.serviceName} · ${lineIndex + 1}/${lineCount}`

  const color = line.staffColor

  return (
    <button
      type="button"
      className={[
        'cal-event',
        `status-${appointment.status}`,
        isDragging ? 'is-dragging' : '',
        isTerminal ? 'is-terminal' : '',
        height < 42 ? 'is-compact' : '',
      ]
        .filter(Boolean)
        .join(' ')}
      style={{
        top,
        height,
        ...(laneCount > 1
          ? {
              left: `${(laneIndex * 100) / laneCount}%`,
              width: `calc(${100 / laneCount}% - 4px)`,
              right: 'auto',
            }
          : {}),
        ...(color === null
          ? {}
          : { background: `color-mix(in srgb, ${color} 18%, var(--raised))` }),
      }}
      title={`${appointment.customerName} — ${serviceLabel}\n${formatClock(startMinutes)} – ${formatClock(startMinutes + durationMinutes)} · ${appointment.status}`}
      onPointerDown={event => onPointerDownMove(event, segment)}
      onPointerMove={onPointerMove}
      onPointerUp={onPointerUp}
      onPointerCancel={onPointerCancel}
      onClick={() => onActivate(appointment)}
    >
      <span className="cal-event-time">
        {formatClock(startMinutes)}–{formatClock(startMinutes + durationMinutes)}
      </span>
      <span className="cal-event-name">{appointment.customerName}</span>
      <span className="cal-event-service">{serviceLabel}</span>
      {!isTerminal && isLastLine && (
        <span
          className="cal-event-resize"
          aria-hidden="true"
          onPointerDown={event => {
            // Without this the card's move handler fires later in the same
            // bubble and turns every resize into a move.
            event.stopPropagation()
            onPointerDownResize(event, segment)
          }}
        />
      )}
    </button>
  )
}
