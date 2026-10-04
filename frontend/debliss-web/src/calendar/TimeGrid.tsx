import { useEffect, useMemo, useRef, useState } from 'react'
import type { PointerEvent as ReactPointerEvent } from 'react'
import type {
  Appointment,
  CalendarMarker,
  CalendarStaff,
  UpdateScheduleRequest,
} from '../api/contracts.ts'
import { AppointmentBlock } from './AppointmentBlock.tsx'
import {
  assignLanes,
  buildColumns,
  DAY_MINUTES,
  DRAG_CLICK_TOLERANCE_PX,
  formatClock,
  parseDayKey,
  PIXELS_PER_MINUTE,
  placeMarkers,
  placeAppointment,
  resolveMoveStart,
  resolveResizeDuration,
  setMinutesOfDay,
  SLOT_SNAP_MINUTES,
  dayKeyOf,
} from './calendarTime.ts'
import type { GridColumn, PlacedMarker, PlacedSegment } from './calendarTime.ts'

export interface SlotTarget {
  day: Date
  staffId: string
  startMinutes: number
}

interface TimeGridProps {
  days: Date[]
  staff: CalendarStaff[]
  appointments: Appointment[]
  markers: CalendarMarker[]
  now: Date
  onSlotClick: (slot: SlotTarget) => void
  onAppointmentClick: (appointment: Appointment) => void
  onReschedule: (appointment: Appointment, patch: UpdateScheduleRequest) => void
}

interface DragOrigin {
  dayKey: string
  staffId: string
  startMinutes: number
  durationMinutes: number
}

interface DragState {
  segmentKey: string
  appointment: Appointment
  mode: 'move' | 'resize'
  origin: DragOrigin
  proposed: DragOrigin
  startClientX: number
  startClientY: number
  clientX: number
  clientY: number
  moved: boolean
  /** Where inside the card it was grabbed, so the preview keeps the grip. */
  grabOffsetX: number
  grabOffsetY: number
  cardWidth: number
  cardHeight: number
  staffColor: string | null
}

const GRID_HEIGHT = DAY_MINUTES * PIXELS_PER_MINUTE
const HOURS = Array.from({ length: 24 }, (_, hour) => hour)

/**
 * The staff-column time grid behind the Day, 3-Day and Week views. Column
 * headers stick while the day scrolls; appointments sit at the height their
 * duration demands. Once the pointer moves a few pixels the booking lifts
 * into a card-shaped preview that follows it; the API is only touched on
 * drop — a rejected PATCH leaves the grid untouched, which is the revert.
 */
export function TimeGrid({
  days,
  staff,
  appointments,
  markers,
  now,
  onSlotClick,
  onAppointmentClick,
  onReschedule,
}: TimeGridProps) {
  const scrollRef = useRef<HTMLDivElement>(null)
  const dragRef = useRef<DragState | null>(null)
  const suppressClickRef = useRef(false)
  const [drag, setDrag] = useState<DragState | null>(null)

  const columns = useMemo(() => buildColumns(days, staff), [days, staff])
  const visibleKeys = useMemo(() => new Set(columns.map(column => column.key)), [columns])

  const segmentsByColumn = useMemo(() => {
    const grouped = new Map<string, PlacedSegment[]>()

    for (const appointment of appointments) {
      for (const segment of placeAppointment(appointment, visibleKeys)) {
        const list = grouped.get(segment.columnKey) ?? []
        list.push(segment)
        grouped.set(segment.columnKey, list)
      }
    }

    for (const [key, list] of grouped) {
      grouped.set(key, assignLanes(list))
    }

    return grouped
  }, [appointments, visibleKeys])

  const markersByColumn = useMemo(() => {
    const grouped = new Map<string, PlacedMarker[]>()

    for (const placed of placeMarkers(markers, columns)) {
      const list = grouped.get(placed.columnKey) ?? []
      list.push(placed)
      grouped.set(placed.columnKey, list)
    }

    return grouped
  }, [markers, columns])

  const todayKey = dayKeyOf(now)
  const nowMinutes = now.getHours() * 60 + now.getMinutes()

  // Open the day at 07:00; reception schedules forward, not at midnight.
  useEffect(() => {
    const scroller = scrollRef.current
    if (scroller) {
      scroller.scrollTop = 7 * 60 * PIXELS_PER_MINUTE
    }
  }, [days])

  function columnFromPoint(clientX: number, clientY: number): {
    key: string
    top: number
  } | null {
    const hovered = document
      .elementFromPoint(clientX, clientY)
      ?.closest<HTMLElement>('[data-column-key]')

    if (hovered === null || hovered === undefined) {
      return null
    }

    return { key: hovered.dataset.columnKey ?? '', top: hovered.getBoundingClientRect().top }
  }

  function beginDrag(
    event: ReactPointerEvent<HTMLElement>,
    segment: PlacedSegment,
    mode: DragState['mode'],
  ) {
    if (event.button !== 0) {
      return
    }

    event.preventDefault()
    event.currentTarget.setPointerCapture(event.pointerId)

    const rect = event.currentTarget.getBoundingClientRect()
    const [dayKey, staffId] = segment.columnKey.split('|')
    const origin: DragOrigin = {
      dayKey,
      staffId,
      startMinutes: segment.startMinutes,
      durationMinutes: segment.durationMinutes,
    }

    dragRef.current = {
      segmentKey: `${segment.appointment.id}|${segment.lineIndex}`,
      appointment: segment.appointment,
      mode,
      origin,
      proposed: origin,
      startClientX: event.clientX,
      startClientY: event.clientY,
      clientX: event.clientX,
      clientY: event.clientY,
      moved: false,
      grabOffsetX: event.clientX - rect.left,
      grabOffsetY: event.clientY - rect.top,
      cardWidth: rect.width,
      cardHeight: rect.height,
      staffColor: segment.line.staffColor,
    }
    setDrag(dragRef.current)
  }

  function continueDrag(event: ReactPointerEvent<HTMLElement>) {
    const active = dragRef.current

    if (active === null) {
      return
    }

    const deltaY = event.clientY - active.startClientY

    if (
      Math.abs(deltaY) > DRAG_CLICK_TOLERANCE_PX ||
      Math.abs(event.clientX - active.startClientX) > DRAG_CLICK_TOLERANCE_PX
    ) {
      active.moved = true
    }

    if (active.mode === 'resize') {
      active.proposed = {
        ...active.proposed,
        durationMinutes: resolveResizeDuration(active.origin.durationMinutes, deltaY),
      }
    } else {
      const target = columnFromPoint(event.clientX, event.clientY)

      if (target !== null && target.key !== '') {
        const [dayKey, staffId] = target.key.split('|')
        // The vertical delta is measured from the grab point, so the booking
        // keeps its grip offset — grabbing a card by its middle does not
        // shift its start. resolveMoveStart snaps to the 5-minute grid.
        active.proposed = {
          dayKey,
          staffId,
          startMinutes: resolveMoveStart(active.origin.startMinutes, deltaY),
          durationMinutes: active.origin.durationMinutes,
        }
      }
    }

    active.clientX = event.clientX
    active.clientY = event.clientY
    setDrag({ ...active })
  }

  function endDrag() {
    const active = dragRef.current
    dragRef.current = null
    setDrag(null)

    if (active === null) {
      return
    }

    if (!active.moved) {
      // No drag: the click that follows the pointer-up opens the details.
      return
    }

    suppressClickRef.current = true

    const patch: UpdateScheduleRequest = {}

    if (
      active.proposed.dayKey !== active.origin.dayKey ||
      active.proposed.startMinutes !== active.origin.startMinutes
    ) {
      patch.startsAt = setMinutesOfDay(
        parseDayKey(active.proposed.dayKey),
        active.proposed.startMinutes,
      ).toISOString()
    }

    if (active.proposed.staffId !== active.origin.staffId) {
      patch.staffId = active.proposed.staffId
    }

    if (
      active.mode === 'resize' &&
      active.proposed.durationMinutes !== active.origin.durationMinutes
    ) {
      patch.durationMinutes = active.proposed.durationMinutes
    }

    if (patch.startsAt !== undefined || patch.staffId !== undefined || patch.durationMinutes !== undefined) {
      onReschedule(active.appointment, patch)
    }
  }

  function handleSlotClick(event: React.MouseEvent<HTMLDivElement>, column: GridColumn) {
    if (suppressClickRef.current) {
      suppressClickRef.current = false
      return
    }

    if ((event.target as HTMLElement).closest('.cal-event') !== null) {
      return
    }

    const rect = event.currentTarget.getBoundingClientRect()
    const rawMinutes = (event.clientY - rect.top) / PIXELS_PER_MINUTE
    const snapped = Math.floor(rawMinutes / SLOT_SNAP_MINUTES) * SLOT_SNAP_MINUTES

    onSlotClick({ day: column.day, staffId: column.staff.id, startMinutes: snapped })
  }

  const resizeGhostLabel =
    drag !== null && drag.mode === 'resize' ? `${drag.proposed.durationMinutes} min` : ''

  const dropTargetKey =
    drag !== null && drag.moved && drag.mode === 'move'
      ? `${drag.proposed.dayKey}|${drag.proposed.staffId}`
      : null

  return (
    <div className="cal-scroll" ref={scrollRef}>
      <div
        className="cal-grid"
        style={{ gridTemplateColumns: `3.5rem repeat(${columns.length}, minmax(10rem, 1fr))` }}
      >
        <div className="cal-corner" />

        {columns.map(column => (
          <div key={`head-${column.key}`} className="cal-column-head" data-column-key={column.key}>
            {days.length > 1 && (
              <span className="cal-column-day">
                {column.day.toLocaleDateString(undefined, { weekday: 'short', day: 'numeric' })}
              </span>
            )}
            <span
              className="cal-stamp"
              style={
                column.staff.calendarColor === null
                  ? {}
                  : { background: column.staff.calendarColor }
              }
            >
              {column.staff.firstName.charAt(0)}
              {column.staff.lastName.charAt(0)}
            </span>
            <span className="cal-staff-name">
              {column.staff.firstName} {column.staff.lastName}
            </span>
            <span className="cal-staff-status">
              {column.staff.isActive
                ? column.intervals.length > 0
                  ? `Working ${column.intervals.map(interval => `${interval.start}–${interval.end}`).join(', ')}`
                  : 'Off'
                : 'Inactive'}
            </span>
          </div>
        ))}

        <div className="cal-gutter" style={{ height: GRID_HEIGHT }}>
          {HOURS.map(hour => (
            <span key={hour} className="cal-gutter-hour" style={{ top: hour * 60 * PIXELS_PER_MINUTE }}>
              {formatClock(hour * 60)}
            </span>
          ))}
        </div>

        {columns.map(column => (
          <div
            key={column.key}
            className={[
              'cal-column',
              column.dayKey === todayKey ? 'is-today' : '',
              dropTargetKey === column.key ? 'is-drop-target' : '',
              column.staff.isActive ? '' : 'is-inactive',
            ]
              .filter(Boolean)
              .join(' ')}
            style={{ height: GRID_HEIGHT }}
            data-column-key={column.key}
            onClick={event => handleSlotClick(event, column)}
          >
            {closedZones(column.intervals).map(([start, end]) => (
              <div
                key={`${start}-${end}`}
                className="cal-closed"
                style={{
                  top: start * PIXELS_PER_MINUTE,
                  height: (end - start) * PIXELS_PER_MINUTE,
                }}
              />
            ))}

            {(markersByColumn.get(column.key) ?? []).map(placed => (
              <div
                key={`${placed.marker.id}-${placed.startMinutes}`}
                className={`cal-marker marker-${placed.marker.kind}`}
                style={{
                  top: placed.startMinutes * PIXELS_PER_MINUTE,
                  height: placed.durationMinutes * PIXELS_PER_MINUTE,
                }}
                title={`${placed.marker.type ?? placed.marker.kind}${placed.marker.label === null ? '' : ` — ${placed.marker.label}`}`}
              >
                <span className="cal-marker-label">
                  {placed.marker.label ?? placed.marker.type ?? placed.marker.kind}
                </span>
              </div>
            ))}

            {(segmentsByColumn.get(column.key) ?? []).map(segment => (
              <AppointmentBlock
                key={`${segment.appointment.id}|${segment.lineIndex}`}
                segment={segment}
                isDragging={drag?.segmentKey === `${segment.appointment.id}|${segment.lineIndex}`}
                onPointerDownMove={(event, target) => beginDrag(event, target, 'move')}
                onPointerDownResize={(event, target) => beginDrag(event, target, 'resize')}
                onPointerMove={continueDrag}
                onPointerUp={endDrag}
                onPointerCancel={endDrag}
                onActivate={appointment => {
                  if (suppressClickRef.current) {
                    suppressClickRef.current = false
                    return
                  }
                  onAppointmentClick(appointment)
                }}
              />
            ))}

            {column.dayKey === todayKey && (
              <div className="cal-now" style={{ top: nowMinutes * PIXELS_PER_MINUTE }} />
            )}
          </div>
        ))}
      </div>

      {drag !== null && drag.moved && drag.mode === 'move' && (
        <div
          className={`cal-drag-card status-${drag.appointment.status}`}
          style={{
            left: drag.clientX - drag.grabOffsetX,
            top: drag.clientY - drag.grabOffsetY,
            width: drag.cardWidth,
            minHeight: drag.cardHeight,
            ...(drag.staffColor === null
              ? {}
              : { background: `color-mix(in srgb, ${drag.staffColor} 18%, var(--raised))` }),
          }}
        >
          <span className="cal-event-time">
            {formatClock(drag.proposed.startMinutes)}–
            {formatClock(drag.proposed.startMinutes + drag.proposed.durationMinutes)}
          </span>
          <span className="cal-event-name">{drag.appointment.customerName}</span>
        </div>
      )}

      {drag !== null && drag.moved && drag.mode === 'resize' && (
        <div className="cal-drag-ghost" style={{ left: drag.clientX + 14, top: drag.clientY + 14 }}>
          {resizeGhostLabel}
        </div>
      )}
    </div>
  )
}

/**
 * The spans outside the member's working intervals: before the first, between
 * them (breaks) and after the last. Rendered shaded so unavailable time reads
 * at a glance and clicks still land on the column for validation.
 */
function closedZones(intervals: GridColumn['intervals']): Array<[number, number]> {
  if (intervals.length === 0) {
    return [[0, DAY_MINUTES]]
  }

  const sorted = [...intervals].sort((left, right) => toMinutes(left.start) - toMinutes(right.start))
  const zones: Array<[number, number]> = []
  let cursor = 0

  for (const interval of sorted) {
    const start = toMinutes(interval.start)
    const end = toMinutes(interval.end)

    if (start > cursor) {
      zones.push([cursor, start])
    }

    cursor = Math.max(cursor, end)
  }

  if (cursor < DAY_MINUTES) {
    zones.push([cursor, DAY_MINUTES])
  }

  return zones
}

function toMinutes(hhmm: string): number {
  const [hours, minutes] = hhmm.split(':').map(Number)
  return hours * 60 + minutes
}
