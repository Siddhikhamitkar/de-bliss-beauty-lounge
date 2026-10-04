import { useEffect, useMemo, useRef, useState } from 'react'
import { describeFailure } from '../api/client.ts'
import type { Appointment, UpdateScheduleRequest } from '../api/contracts.ts'
import { getCalendar, updateAppointmentSchedule } from '../api/scheduling.ts'
import { EmptyState } from '../components/EmptyState.tsx'
import { ErrorState } from '../components/ErrorState.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { useAsync } from '../useAsync.ts'
import { CalendarHeader } from './CalendarHeader.tsx'
import { MonthView } from './MonthView.tsx'
import { TimeGrid } from './TimeGrid.tsx'
import type { SlotTarget } from './TimeGrid.tsx'
import { AppointmentDetailsPanel } from './AppointmentDetailsPanel.tsx'
import { BookingModal } from './BookingModal.tsx'
import { addDays, addMonths, rangeForView, startOfDay, viewRangeLabel } from './calendarTime.ts'
import type { CalendarView } from './calendarTime.ts'

/**
 * What the booking dialog is doing: creating from a slot (or bare day) or
 * editing an existing booking. One piece of state covers both directions.
 */
export type BookingState =
  | { kind: 'create'; day: Date; staffId: string | null; startMinutes: number | null }
  | { kind: 'edit'; appointment: Appointment }
  | null

/**
 * The scheduling workspace: the operational heart of the salon app. The grid
 * fills the viewport, drags revalidate against the backend, and the booking
 * dialog plus details panel stay on this page — nothing navigates away.
 */
export function CalendarPage() {
  const [view, setView] = useState<CalendarView>('week')
  const [anchor, setAnchor] = useState(() => new Date())
  const [search, setSearch] = useState('')
  const [appliedSearch, setAppliedSearch] = useState('')
  const [staffFilter, setStaffFilter] = useState('all')
  const [statusFilter, setStatusFilter] = useState('All')
  const [serviceFilter, setServiceFilter] = useState('all')
  const [actionFailure, setActionFailure] = useState<string>()
  const [detailsId, setDetailsId] = useState<string | null>(null)
  const [booking, setBooking] = useState<BookingState>(null)
  const [now, setNow] = useState(() => new Date())

  const dragBusy = useRef(false)

  const range = useMemo(() => rangeForView(view, anchor), [view, anchor])
  const calendar = useAsync(
    () => getCalendar(range.from, range.to),
    [range.from.getTime(), range.to.getTime()],
  )

  // The now-line only needs minute precision; a slow tick keeps it honest.
  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), 60_000)
    return () => clearInterval(timer)
  }, [])

  function navigate(direction: -1 | 1) {
    setAnchor(current =>
      view === 'day'
        ? addDays(current, direction)
        : view === 'three-day'
          ? addDays(current, direction * 3)
          : view === 'week'
            ? addDays(current, direction * 7)
            : addMonths(current, direction),
    )
  }

  function openSlot(slot: SlotTarget) {
    setBooking({ kind: 'create', day: slot.day, staffId: slot.staffId, startMinutes: slot.startMinutes })
  }

  /**
   * A drag or resize commits only after the backend accepts it. The grid is
   * never mutated locally, so a rejection's revert is simply not moving.
   */
  function reschedule(appointment: Appointment, patch: UpdateScheduleRequest) {
    if (dragBusy.current) {
      return
    }

    dragBusy.current = true
    setActionFailure(undefined)

    updateAppointmentSchedule(appointment.id, patch)
      .then(() => calendar.reload())
      .catch((error: unknown) => setActionFailure(describeFailure(error)))
      .finally(() => {
        dragBusy.current = false
      })
  }

  const data = calendar.data

  const staffOptions = useMemo(
    () =>
      (data?.staff ?? []).map(member => ({
        id: member.id,
        name: `${member.firstName} ${member.lastName}`,
      })),
    [data],
  )

  const serviceOptions = useMemo(() => {
    const names = new Set<string>()

    for (const appointment of data?.appointments ?? []) {
      for (const line of appointment.services) {
        names.add(line.serviceName)
      }
    }

    return [...names].sort((left, right) => left.localeCompare(right))
  }, [data])

  const visibleStaff = useMemo(
    () =>
      staffFilter === 'all'
        ? (data?.staff ?? [])
        : (data?.staff ?? []).filter(member => member.id === staffFilter),
    [data, staffFilter],
  )

  const filteredAppointments = useMemo(
    () =>
      (data?.appointments ?? []).filter(appointment => {
        if (statusFilter !== 'All' && appointment.status !== statusFilter) {
          return false
        }

        if (
          serviceFilter !== 'all' &&
          !appointment.services.some(line => line.serviceName === serviceFilter)
        ) {
          return false
        }

        if (appliedSearch !== '') {
          const needle = appliedSearch.toLowerCase()
          const haystack = `${appointment.customerName} ${appointment.customerPhone ?? ''}`.toLowerCase()

          if (!haystack.includes(needle)) {
            return false
          }
        }

        return true
      }),
    [data, statusFilter, serviceFilter, appliedSearch],
  )

  const filteredMarkers = useMemo(
    () =>
      staffFilter === 'all'
        ? (data?.markers ?? [])
        : (data?.markers ?? []).filter(
            marker => marker.staffId === null || marker.staffId === staffFilter,
          ),
    [data, staffFilter],
  )

  return (
    <section className="cal-workspace" aria-label="Appointment calendar">
      <CalendarHeader
        view={view}
        rangeLabel={viewRangeLabel(view, anchor)}
        search={search}
        staffOptions={staffOptions}
        staffFilter={staffFilter}
        statusFilter={statusFilter}
        serviceOptions={serviceOptions}
        serviceFilter={serviceFilter}
        onNavigate={navigate}
        onToday={() => setAnchor(new Date())}
        onViewChange={setView}
        onSearchChange={setSearch}
        onSearchApply={() => setAppliedSearch(search.trim())}
        onStaffFilterChange={setStaffFilter}
        onStatusFilterChange={setStatusFilter}
        onServiceFilterChange={setServiceFilter}
        onNewAppointment={() =>
          setBooking({ kind: 'create', day: startOfDay(anchor), staffId: null, startMinutes: null })
        }
      />

      {actionFailure !== undefined && (
        <p className="banner" role="alert">{actionFailure}</p>
      )}

      {calendar.failure !== undefined && (
        <ErrorState message={calendar.failure} onRetry={calendar.reload} />
      )}

      {calendar.data === undefined && calendar.failure === undefined && (
        <LoadingState label="Loading calendar…" />
      )}

      {calendar.data !== undefined && view !== 'month' && visibleStaff.length === 0 && (
        <EmptyState>No staff members to show — the Staff section has no one to schedule yet.</EmptyState>
      )}

      {calendar.data !== undefined && view !== 'month' && visibleStaff.length > 0 && (
        <TimeGrid
          days={range.days}
          staff={visibleStaff}
          appointments={filteredAppointments}
          markers={filteredMarkers}
          now={now}
          onSlotClick={openSlot}
          onAppointmentClick={appointment => setDetailsId(appointment.id)}
          onReschedule={reschedule}
        />
      )}

      {calendar.data !== undefined && view === 'month' && (
        <MonthView
          days={range.days}
          anchorMonth={anchor.getMonth()}
          appointments={filteredAppointments}
          markers={filteredMarkers}
          onSelectDay={day => {
            setAnchor(day)
            setView('day')
          }}
          onAppointmentClick={appointment => setDetailsId(appointment.id)}
        />
      )}

      {detailsId !== null && (
        <AppointmentDetailsPanel
          appointmentId={detailsId}
          onClose={() => setDetailsId(null)}
          onChanged={calendar.reload}
          onEdit={appointment => {
            setDetailsId(null)
            setBooking({ kind: 'edit', appointment })
          }}
        />
      )}

      {booking !== null && (
        <BookingModal
          booking={booking}
          onClose={() => setBooking(null)}
          onSaved={() => {
            setBooking(null)
            calendar.reload()
          }}
        />
      )}
    </section>
  )
}
