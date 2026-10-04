import { describe, expect, it } from 'vitest'
import type { Appointment, CalendarMarker, CalendarStaff } from '../api/contracts.ts'
import {
  addDays,
  addMonths,
  assignLanes,
  buildColumns,
  dayKeyOf,
  formatClock,
  minutesFromPointer,
  minutesOfDay,
  parseDayKey,
  placeAppointment,
  placeMarkers,
  PIXELS_PER_MINUTE,
  rangeForView,
  resolveMoveStart,
  resolveResizeDuration,
  setMinutesOfDay,
  startOfDay,
  startOfWeek,
  viewRangeLabel,
} from './calendarTime.ts'

function at(year: number, month: number, day: number, hours = 0, minutes = 0): Date {
  return new Date(year, month - 1, day, hours, minutes, 0, 0)
}

describe('day arithmetic', () => {
  it('startOfDay zeroes the time of day', () => {
    expect(startOfDay(at(2026, 10, 4, 15, 42))).toEqual(at(2026, 10, 4))
  })

  it('addDays crosses month boundaries without overflowing', () => {
    expect(addDays(at(2026, 10, 31), 1)).toEqual(at(2026, 11, 1))
    expect(addDays(at(2026, 1, 1), -1)).toEqual(at(2025, 12, 31))
  })

  it('addMonths clamps the day instead of overflowing into the next month', () => {
    expect(addMonths(at(2026, 1, 31), 1)).toEqual(at(2026, 2, 28))
    expect(addMonths(at(2026, 3, 31), -1)).toEqual(at(2026, 2, 28))
    expect(addMonths(at(2026, 11, 30), 1)).toEqual(at(2026, 12, 30))
  })

  it('startOfWeek lands on the Monday of the same week', () => {
    expect(startOfWeek(at(2026, 10, 7, 18, 0))).toEqual(at(2026, 10, 5)) // Wednesday
    expect(startOfWeek(at(2026, 10, 5))).toEqual(at(2026, 10, 5)) // Monday stays
    expect(startOfWeek(at(2026, 10, 4))).toEqual(at(2026, 9, 28)) // Sunday belongs to the past week
  })
})

describe('day keys and clocks', () => {
  it('dayKeyOf pads to a stable local yyyy-mm-dd', () => {
    expect(dayKeyOf(at(2026, 3, 5))).toBe('2026-03-05')
    expect(dayKeyOf(at(2026, 12, 31))).toBe('2026-12-31')
  })

  it('parseDayKey reverses dayKeyOf', () => {
    expect(parseDayKey('2026-03-05')).toEqual(at(2026, 3, 5))
    expect(parseDayKey(dayKeyOf(at(2026, 7, 19)))).toEqual(at(2026, 7, 19))
  })

  it('minutesOfDay reads the local clock', () => {
    expect(minutesOfDay(at(2026, 10, 4, 10, 15))).toBe(615)
    expect(minutesOfDay(at(2026, 10, 4, 0, 0))).toBe(0)
  })

  it('setMinutesOfDay replaces the time and stays inside the day', () => {
    expect(setMinutesOfDay(at(2026, 10, 4, 22, 30), 600)).toEqual(at(2026, 10, 4, 10, 0))
    expect(setMinutesOfDay(at(2026, 10, 4), -50)).toEqual(at(2026, 10, 4, 0, 0))
    expect(setMinutesOfDay(at(2026, 10, 4), 24 * 60)).toEqual(at(2026, 10, 4, 23, 59))
  })

  it('formatClock renders HH:mm across the day', () => {
    expect(formatClock(0)).toBe('00:00')
    expect(formatClock(615)).toBe('10:15')
    expect(formatClock(1439)).toBe('23:59')
  })
})

describe('rangeForView', () => {
  it('day view covers exactly the anchor day', () => {
    const anchor = at(2026, 10, 7, 15, 0)
    const range = rangeForView('day', anchor)

    expect(range.days).toHaveLength(1)
    expect(range.from).toEqual(at(2026, 10, 7))
    expect(range.to).toEqual(at(2026, 10, 8))
  })

  it('three-day view starts on the anchor and spans three days', () => {
    const range = rangeForView('three-day', at(2026, 10, 30))

    expect(range.days).toHaveLength(3)
    expect(range.days[0]).toEqual(at(2026, 10, 30))
    expect(range.days[2]).toEqual(at(2026, 11, 1))
  })

  it('week view starts on Monday even when the anchor is mid-week', () => {
    const range = rangeForView('week', at(2026, 10, 7, 12, 0)) // Wednesday

    expect(range.days).toHaveLength(7)
    expect(range.days[0]).toEqual(at(2026, 10, 5))
    expect(range.days[6]).toEqual(at(2026, 10, 11))
  })

  it('month view always asks for six Monday-first weeks', () => {
    const range = rangeForView('month', at(2026, 10, 15))

    expect(range.days).toHaveLength(42)
    expect(range.days[0]).toEqual(startOfWeek(at(2026, 10, 1)))
    expect(range.to).toEqual(addDays(range.from, 42))
    // 1 Oct 2026 is a Thursday, so the grid opens in September and closes in November.
    expect(range.days[0].getMonth()).toBe(8)
    expect(range.days[41].getMonth()).toBe(10)
  })

  it('every view stays under the API 62-day cap', () => {
    expect(rangeForView('month', at(2026, 2, 15)).days).toHaveLength(42)
  })
})

describe('viewRangeLabel', () => {
  it('day view leads with the weekday', () => {
    const anchor = at(2026, 10, 7)
    expect(viewRangeLabel('day', anchor)).toBe(
      anchor.toLocaleDateString(undefined, {
        weekday: 'long',
        day: 'numeric',
        month: 'long',
        year: 'numeric',
      }),
    )
  })

  it('month view shows month and year only', () => {
    expect(viewRangeLabel('month', at(2026, 10, 7))).toBe(
      at(2026, 10, 7).toLocaleDateString(undefined, { month: 'long', year: 'numeric' }),
    )
  })

  it('week and three-day views show both endpoints', () => {
    expect(viewRangeLabel('week', at(2026, 10, 7))).toContain('–')
    expect(viewRangeLabel('three-day', at(2026, 10, 7))).toContain('–')
  })

  it('week view labels the Monday-first range, not the anchor day', () => {
    const long: Intl.DateTimeFormatOptions = { day: 'numeric', month: 'long', year: 'numeric' }
    expect(viewRangeLabel('week', at(2026, 10, 7))).toBe(
      `${at(2026, 10, 5).toLocaleDateString(undefined, long)} – ${at(2026, 10, 11).toLocaleDateString(undefined, long)}`,
    )
  })
})

function staff(id: string, firstName: string, workingDays: CalendarStaff['workingHours']['days']): CalendarStaff {
  return {
    id,
    firstName,
    lastName: 'Test',
    jobTitle: null,
    calendarColor: null,
    sortOrder: 0,
    workingHours: { days: workingDays },
    isActive: true,
  }
}

describe('buildColumns', () => {
  const members = [
    staff('s1', 'Ava', [
      { dayOfWeek: 'Monday', isWorkingDay: true, intervals: [{ start: '09:00', end: '17:00' }] },
    ]),
    staff('s2', 'Noah', []),
  ]

  it('emits day-major columns keyed day|staff with the weekday pattern', () => {
    const columns = buildColumns([at(2026, 10, 5), at(2026, 10, 6)], members)

    expect(columns.map(column => column.key)).toEqual([
      '2026-10-05|s1',
      '2026-10-05|s2',
      '2026-10-06|s1',
      '2026-10-06|s2',
    ])
    expect(columns[0].intervals).toEqual([{ start: '09:00', end: '17:00' }])
    expect(columns[1].intervals).toEqual([])
  })

  it('falls back to empty intervals when the member has no pattern for that weekday', () => {
    const columns = buildColumns([at(2026, 10, 6)], [members[0]]) // Tuesday
    expect(columns[0].intervals).toEqual([])
  })
})

function appointment(services: Appointment['services'], startsAt: string): Appointment {
  return {
    id: 'a1',
    customerId: 'c1',
    customerName: 'Lina Mansour',
    customerPhone: '+971 50 555 0100',
    startsAt,
    endsAt: startsAt,
    status: 'Confirmed',
    source: 'Phone',
    notes: null,
    cancellationReason: null,
    services,
    totalPrice: services.reduce((sum, line) => sum + line.unitPrice, 0),
    totalDurationMinutes: services.reduce((sum, line) => sum + line.durationMinutes, 0),
    createdAt: startsAt,
    updatedAt: null,
  }
}

function line(serviceId: string, staffId: string, durationMinutes: number): Appointment['services'][number] {
  return {
    serviceId,
    serviceName: serviceId,
    durationMinutes,
    unitPrice: 100,
    staffId,
    staffFirstName: 'Ava',
    staffLastName: 'Test',
    staffColor: null,
  }
}

describe('placeAppointment', () => {
  const booking = appointment(
    [line('cut', 's1', 45), line('dry', 's2', 30)],
    new Date(2026, 9, 5, 10, 0).toISOString(),
  )

  it('runs service lines back to back from the booking start', () => {
    const visible = new Set(['2026-10-05|s1', '2026-10-05|s2'])
    const segments = placeAppointment(booking, visible)

    expect(segments).toHaveLength(2)
    expect(segments[0].startMinutes).toBe(600)
    expect(segments[0].durationMinutes).toBe(45)
    expect(segments[1].startMinutes).toBe(645)
    expect(segments[1].durationMinutes).toBe(30)
    expect(segments.map(segment => segment.columnKey)).toEqual([
      '2026-10-05|s1',
      '2026-10-05|s2',
    ])
    expect(segments[0].lineCount).toBe(2)
    expect(segments[1].lineIndex).toBe(1)
  })

  it('drops lines whose column is not visible', () => {
    const visible = new Set(['2026-10-05|s2'])
    const segments = placeAppointment(booking, visible)

    expect(segments).toHaveLength(1)
    expect(segments[0].line.staffId).toBe('s2')
  })
})

describe('assignLanes', () => {
  const columnKey = '2026-10-05|s1'
  const booking = appointment([line('cut', 's1', 45)], new Date(2026, 9, 5, 10, 0).toISOString())

  function segment(startMinutes: number, durationMinutes: number) {
    return {
      appointment: booking,
      line: booking.services[0],
      columnKey,
      startMinutes,
      durationMinutes,
      lineIndex: 0,
      lineCount: 1,
      laneIndex: 0,
      laneCount: 1,
    }
  }

  it('splits genuinely overlapping bookings into side-by-side lanes', () => {
    const placed = assignLanes([segment(780, 60), segment(810, 45)])

    expect(placed.map(entry => [entry.laneIndex, entry.laneCount])).toEqual([
      [0, 2],
      [1, 2],
    ])
  })

  it('keeps back-to-back bookings at full width', () => {
    const placed = assignLanes([segment(600, 75), segment(675, 30)])

    expect(placed.map(entry => [entry.laneIndex, entry.laneCount])).toEqual([
      [0, 1],
      [0, 1],
    ])
  })

  it('narrows only the overlapping group, not the whole column', () => {
    const placed = assignLanes([segment(780, 45), segment(810, 30), segment(960, 30)])

    expect(placed.map(entry => [entry.laneIndex, entry.laneCount])).toEqual([
      [0, 2],
      [1, 2],
      [0, 1],
    ])
  })

  it('packs a third overlapping booking into the first lane once it is free', () => {
    const placed = assignLanes([segment(600, 30), segment(610, 30), segment(635, 30)])

    expect(placed.map(entry => [entry.laneIndex, entry.laneCount])).toEqual([
      [0, 2],
      [1, 2],
      [0, 2],
    ])
  })
})

describe('placeMarkers', () => {
  const columns = buildColumns(
    [at(2026, 10, 5), at(2026, 10, 6)],
    [staff('s1', 'Ava', []), staff('s2', 'Noah', [])],
  )

  const timeOff = (id: string, staffId: string | null, startsAt: string, endsAt: string): CalendarMarker => ({
    id,
    staffId,
    startsAt,
    endsAt,
    kind: 'TimeOff',
    type: 'Holiday',
    label: 'Holiday',
  })

  it('clips a marker that spans midnight onto both days', () => {
    const placed = placeMarkers(
      [timeOff('m1', 's1', new Date(2026, 9, 5, 22, 0).toISOString(), new Date(2026, 9, 6, 2, 0).toISOString())],
      columns,
    )

    const dayOne = placed.find(entry => entry.columnKey === '2026-10-05|s1')
    const dayTwo = placed.find(entry => entry.columnKey === '2026-10-06|s1')

    expect(dayOne).toMatchObject({ startMinutes: 22 * 60, durationMinutes: 120 })
    expect(dayTwo).toMatchObject({ startMinutes: 0, durationMinutes: 120 })
    expect(placed.filter(entry => entry.columnKey.endsWith('|s2'))).toHaveLength(0)
  })

  it('repeats a salon-wide marker on every column of the overlapping day', () => {
    const placed = placeMarkers(
      [timeOff('m1', null, new Date(2026, 9, 5, 12, 0).toISOString(), new Date(2026, 9, 5, 13, 0).toISOString())],
      columns,
    )

    expect(placed).toHaveLength(2)
    expect(placed.every(entry => entry.startMinutes === 720 && entry.durationMinutes === 60)).toBe(true)
  })

  it('ignores markers with no overlap', () => {
    const placed = placeMarkers(
      [timeOff('m1', 's1', new Date(2026, 9, 12).toISOString(), new Date(2026, 9, 13).toISOString())],
      columns,
    )

    expect(placed).toHaveLength(0)
  })
})

describe('drag math', () => {
  it('resolveMoveStart converts pixels to snapped minutes', () => {
    expect(resolveMoveStart(600, 30 * PIXELS_PER_MINUTE)).toBe(630) // +30 min
    expect(resolveMoveStart(600, 7 * PIXELS_PER_MINUTE)).toBe(605) // sub-snap rounds to the 5-min grid
    expect(resolveMoveStart(600, -6 * PIXELS_PER_MINUTE)).toBe(595)
  })

  it('resolveMoveStart snaps an off-grid origin back onto the grid', () => {
    // A row saved off-grid (a legacy 17:23 start) returns to the grid on the
    // next drag instead of staying off-grid forever.
    expect(resolveMoveStart(1043, 0)).toBe(1045) // 17:23 -> 17:25
    expect(resolveMoveStart(1043, 30 * PIXELS_PER_MINUTE)).toBe(1075)
  })

  it('resolveMoveStart clamps inside the day and preserves the duration floor', () => {
    expect(resolveMoveStart(10, -120 * PIXELS_PER_MINUTE)).toBe(0)
    expect(resolveMoveStart(1430, 120 * PIXELS_PER_MINUTE)).toBe(24 * 60 - 15)
  })

  it('resolveResizeDuration never drops below the floor or past midnight', () => {
    expect(resolveResizeDuration(30, -1200 * PIXELS_PER_MINUTE)).toBe(15)
    expect(resolveResizeDuration(30, 1500 * PIXELS_PER_MINUTE)).toBe(24 * 60)
    expect(resolveResizeDuration(60, 30 * PIXELS_PER_MINUTE)).toBe(90)
  })

  it('minutesFromPointer maps a client Y to clamped minutes', () => {
    expect(minutesFromPointer(100, 100 + 120 * PIXELS_PER_MINUTE)).toBe(120)
    expect(minutesFromPointer(100, 0)).toBe(0)
    expect(minutesFromPointer(100, 99999)).toBe(24 * 60 - 15)
  })
})
