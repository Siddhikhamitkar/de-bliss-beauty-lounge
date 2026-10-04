import type {
  Appointment,
  AppointmentServiceLine,
  CalendarMarker,
  CalendarStaff,
  WorkingInterval,
} from '../api/contracts.ts'

/** The views the workspace switches between. */
export type CalendarView = 'day' | 'three-day' | 'week' | 'month'

/**
 * A 30-minute service renders as a 36-pixel block: height follows duration
 * instead of every appointment wearing the same card, which is what makes the
 * grid read like a schedule rather than a list.
 */
export const PIXELS_PER_MINUTE = 1.2
export const DAY_MINUTES = 24 * 60

/** Drags land on five-minute boundaries, matching the backend's minimum duration. */
export const SNAP_MINUTES = 5

/** A resize refuses to go below this; the backend floor is 5. */
export const MIN_DURATION_MINUTES = 15

/** Click-to-create snaps to quarter hours, the way receptionists quote times. */
export const SLOT_SNAP_MINUTES = 15

/** Pointers that move less than this between down and up count as a click. */
export const DRAG_CLICK_TOLERANCE_PX = 4

export const WEEKDAY_NAMES = [
  'Sunday',
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
  'Saturday',
] as const

export function startOfDay(date: Date): Date {
  const copy = new Date(date)
  copy.setHours(0, 0, 0, 0)
  return copy
}

export function addDays(date: Date, days: number): Date {
  const copy = new Date(date)
  copy.setDate(copy.getDate() + days)
  return copy
}

/** Month arithmetic that clamps the day (Jan 31 + 1 month is Feb 28, not Mar 3). */
export function addMonths(date: Date, months: number): Date {
  const copy = new Date(date)
  const day = copy.getDate()
  copy.setDate(1)
  copy.setMonth(copy.getMonth() + months)
  const lastDayOfMonth = new Date(copy.getFullYear(), copy.getMonth() + 1, 0).getDate()
  copy.setDate(Math.min(day, lastDayOfMonth))
  return copy
}

/** Monday-first, matching the weekday pattern the working-hours editor stores. */
export function startOfWeek(date: Date): Date {
  const day = startOfDay(date)
  return addDays(day, -((day.getDay() + 6) % 7))
}

/** Local yyyy-mm-dd; the stable identity a day has across the grid and the month view. */
export function dayKeyOf(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${date.getFullYear()}-${month}-${day}`
}

export function parseDayKey(key: string): Date {
  const [year, month, day] = key.split('-').map(Number)
  return new Date(year, month - 1, day)
}

export function minutesOfDay(date: Date): number {
  return date.getHours() * 60 + date.getMinutes()
}

/** The day with its time-of-day replaced; the result always stays inside the day. */
export function setMinutesOfDay(day: Date, minutes: number): Date {
  const copy = startOfDay(day)
  copy.setHours(0, clamp(minutes, 0, DAY_MINUTES - 1), 0, 0)
  return copy
}

/** HH:mm, the 24-hour label every clock label in the workspace shares. */
export function formatClock(totalMinutes: number): string {
  const hours = Math.floor(totalMinutes / 60)
  const minutes = totalMinutes % 60
  return `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}`
}

export interface CalendarRange {
  from: Date
  to: Date
  days: Date[]
}

/**
 * Days one view covers, anchored on the date the user navigated to. The month
 * view always asks for six weeks so the grid keeps a stable shape and the span
 * stays under the API's 62-day cap.
 */
export function rangeForView(view: CalendarView, anchor: Date): CalendarRange {
  if (view === 'month') {
    const firstOfMonth = new Date(anchor.getFullYear(), anchor.getMonth(), 1)
    const from = startOfWeek(firstOfMonth)
    const days = Array.from({ length: 42 }, (_, index) => addDays(from, index))
    return { from, to: addDays(from, 42), days }
  }

  const dayCount = view === 'day' ? 1 : view === 'three-day' ? 3 : 7
  const from = view === 'week' ? startOfWeek(anchor) : startOfDay(anchor)
  const days = Array.from({ length: dayCount }, (_, index) => addDays(from, index))

  return { from, to: addDays(from, dayCount), days }
}

/** The header label for the current view and anchor, e.g. "5 – 11 Oct 2026". */
export function viewRangeLabel(view: CalendarView, anchor: Date): string {
  const long: Intl.DateTimeFormatOptions = { day: 'numeric', month: 'long', year: 'numeric' }

  if (view === 'day') {
    return anchor.toLocaleDateString(undefined, { ...long, weekday: 'long' })
  }

  if (view === 'month') {
    return anchor.toLocaleDateString(undefined, { month: 'long', year: 'numeric' })
  }

  const from = view === 'week' ? startOfWeek(anchor) : anchor
  const end = view === 'week' ? addDays(from, 6) : addDays(anchor, 2)

  return `${from.toLocaleDateString(undefined, long)} – ${end.toLocaleDateString(undefined, long)}`
}

/** One column of the time grid: one staff member on one day. */
export interface GridColumn {
  key: string
  day: Date
  dayKey: string
  staff: CalendarStaff
  /** Empty when the member does not work that weekday; the gaps between entries are breaks. */
  intervals: WorkingInterval[]
}

/** Day-major order (everyone's Monday, then Tuesday…), the way schedulers scan. */
export function buildColumns(days: readonly Date[], staff: readonly CalendarStaff[]): GridColumn[] {
  return days.flatMap(day => {
    const weekday = WEEKDAY_NAMES[day.getDay()]
    const dayKey = dayKeyOf(day)

    return staff.map(member => {
      const pattern = member.workingHours.days.find(schedule => schedule.dayOfWeek === weekday)

      return {
        key: `${dayKey}|${member.id}`,
        day,
        dayKey,
        staff: member,
        intervals: pattern?.intervals ?? [],
      }
    })
  })
}

/**
 * One renderable slice of an appointment: a single service line placed on the
 * column of the staff member who performs it. Lines run back to back from the
 * booking start — that is how the backend applies the window — so each line's
 * own start is the running sum of the durations before it.
 */
export interface PlacedSegment {
  appointment: Appointment
  line: AppointmentServiceLine
  columnKey: string
  startMinutes: number
  durationMinutes: number
  lineIndex: number
  lineCount: number
  /** Horizontal lane when a column holds overlapping bookings; 0 of 1 means full width. */
  laneIndex: number
  laneCount: number
}

export function placeAppointment(
  appointment: Appointment,
  visibleColumnKeys: ReadonlySet<string>,
): PlacedSegment[] {
  const start = new Date(appointment.startsAt)
  const dayKey = dayKeyOf(start)
  let offset = minutesOfDay(start)

  return appointment.services.map((line, index) => {
    const segment = {
      appointment,
      line,
      columnKey: `${dayKey}|${line.staffId}`,
      startMinutes: offset,
      durationMinutes: line.durationMinutes,
      lineIndex: index,
      lineCount: appointment.services.length,
      laneIndex: 0,
      laneCount: 1,
    }
    offset += line.durationMinutes

    return segment
  }).filter(segment => visibleColumnKeys.has(segment.columnKey))
}

/**
 * Lays a column's segments out side by side where their times overlap, the way
 * a paper day sheet shows two clients at once. Bookings that merely touch
 * (one ends 11:15, the next starts 11:15) stay full width — only genuine
 * overlap splits the column, and only the overlapping group narrows.
 */
export function assignLanes(segments: readonly PlacedSegment[]): PlacedSegment[] {
  const placed = [...segments]
    .sort(
      (left, right) =>
        left.startMinutes - right.startMinutes ||
        right.durationMinutes - left.durationMinutes,
    )
    .map(segment => ({ ...segment, laneIndex: 0, laneCount: 1 }))

  let clusterStart = 0
  let laneEnds: number[] = []

  const settleCluster = (endExclusive: number) => {
    for (let index = clusterStart; index < endExclusive; index += 1) {
      placed[index].laneCount = laneEnds.length
    }
  }

  for (let index = 0; index < placed.length; index += 1) {
    const segment = placed[index]
    const end = segment.startMinutes + segment.durationMinutes

    if (laneEnds.length > 0 && segment.startMinutes >= Math.max(...laneEnds)) {
      settleCluster(index)
      clusterStart = index
      laneEnds = []
    }

    const lane = laneEnds.findIndex(laneEnd => laneEnd <= segment.startMinutes)

    if (lane === -1) {
      laneEnds.push(end)
      segment.laneIndex = laneEnds.length - 1
    } else {
      laneEnds[lane] = end
      segment.laneIndex = lane
    }
  }

  settleCluster(placed.length)

  return placed
}

/** One marker slice on one column; salon-wide markers repeat on every column that day. */
export interface PlacedMarker {
  marker: CalendarMarker
  columnKey: string
  startMinutes: number
  durationMinutes: number
}

export function placeMarkers(
  markers: readonly CalendarMarker[],
  columns: readonly GridColumn[],
): PlacedMarker[] {
  const placed: PlacedMarker[] = []

  for (const marker of markers) {
    const startsAt = new Date(marker.startsAt)
    const endsAt = new Date(marker.endsAt)

    for (const column of columns) {
      if (marker.staffId !== null && marker.staffId !== column.staff.id) {
        continue
      }

      const dayStart = column.day.getTime()
      const overlapStart = Math.max(startsAt.getTime(), dayStart)
      const overlapEnd = Math.min(endsAt.getTime(), addDays(column.day, 1).getTime())

      if (overlapEnd <= overlapStart) {
        continue
      }

      placed.push({
        marker,
        columnKey: column.key,
        startMinutes: Math.round((overlapStart - dayStart) / 60000),
        durationMinutes: Math.round((overlapEnd - overlapStart) / 60000),
      })
    }
  }

  return placed
}

/** The vertical drag's proposed start, snapped and clamped inside the day. */
export function resolveMoveStart(originalStartMinutes: number, deltaYPixels: number): number {
  const proposed = originalStartMinutes + deltaYPixels / PIXELS_PER_MINUTE
  return clamp(snap(proposed, SNAP_MINUTES), 0, DAY_MINUTES - MIN_DURATION_MINUTES)
}

/** The resize's proposed duration, never below the floor, never past midnight. */
export function resolveResizeDuration(originalDurationMinutes: number, deltaYPixels: number): number {
  const proposed = originalDurationMinutes + snap(deltaYPixels / PIXELS_PER_MINUTE, SNAP_MINUTES)
  return clamp(proposed, MIN_DURATION_MINUTES, DAY_MINUTES)
}

/** Minutes under a pointer inside a column body of known geometry. */
export function minutesFromPointer(columnTopPx: number, clientY: number): number {
  return clamp((clientY - columnTopPx) / PIXELS_PER_MINUTE, 0, DAY_MINUTES - MIN_DURATION_MINUTES)
}

function snap(value: number, step: number): number {
  return Math.round(value / step) * step
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max)
}
