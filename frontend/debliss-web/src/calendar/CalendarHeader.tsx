import { APPOINTMENT_STATUSES } from '../api/contracts.ts'
import type { CalendarView } from './calendarTime.ts'

interface CalendarHeaderProps {
  view: CalendarView
  rangeLabel: string
  search: string
  staffOptions: Array<{ id: string; name: string }>
  staffFilter: string
  statusFilter: string
  serviceOptions: string[]
  serviceFilter: string
  onNavigate: (direction: -1 | 1) => void
  onToday: () => void
  onViewChange: (view: CalendarView) => void
  onSearchChange: (value: string) => void
  onSearchApply: () => void
  onStaffFilterChange: (value: string) => void
  onStatusFilterChange: (value: string) => void
  onServiceFilterChange: (value: string) => void
  onNewAppointment: () => void
}

const VIEWS: Array<{ value: CalendarView; label: string }> = [
  { value: 'day', label: 'Day' },
  { value: 'three-day', label: '3 Days' },
  { value: 'week', label: 'Week' },
  { value: 'month', label: 'Month' },
]

/**
 * The workspace toolbar. Every control is compact and operational: navigation,
 * the current range, the view switch and the filters share one dense strip so
 * the grid below keeps the viewport.
 */
export function CalendarHeader({
  view,
  rangeLabel,
  search,
  staffOptions,
  staffFilter,
  statusFilter,
  serviceOptions,
  serviceFilter,
  onNavigate,
  onToday,
  onViewChange,
  onSearchChange,
  onSearchApply,
  onStaffFilterChange,
  onStatusFilterChange,
  onServiceFilterChange,
  onNewAppointment,
}: CalendarHeaderProps) {
  return (
    <div className="cal-toolbar">
      <div className="cal-toolbar-row">
        <div className="cal-nav" aria-label="Date navigation">
          <button
            type="button"
            className="button"
            aria-label="Previous date"
            onClick={() => onNavigate(-1)}
          >
            ‹
          </button>
          <button type="button" className="button" onClick={onToday}>
            Today
          </button>
          <button
            type="button"
            className="button"
            aria-label="Next date"
            onClick={() => onNavigate(1)}
          >
            ›
          </button>
        </div>

        <h2 className="cal-range-label">{rangeLabel}</h2>

        <div className="cal-view-switch" role="group" aria-label="Calendar view">
          {VIEWS.map(option => (
            <button
              key={option.value}
              type="button"
              className="cal-view-button"
              aria-current={view === option.value ? 'true' : undefined}
              onClick={() => onViewChange(option.value)}
            >
              {option.label}
            </button>
          ))}
        </div>

        <button
          type="button"
          className="button button-primary cal-new"
          onClick={onNewAppointment}
        >
          + New Appointment
        </button>
      </div>

      <div className="cal-toolbar-row cal-toolbar-filters">
        <label className="cal-filter">
          <span>Search</span>
          <input
            type="search"
            value={search}
            placeholder="Customer name or phone"
            onChange={event => onSearchChange(event.target.value)}
            onKeyDown={event => {
              if (event.key === 'Enter') {
                onSearchApply()
              }
            }}
          />
        </label>

        <label className="cal-filter">
          <span>Staff</span>
          <select value={staffFilter} onChange={event => onStaffFilterChange(event.target.value)}>
            <option value="all">All staff</option>
            {staffOptions.map(option => (
              <option key={option.id} value={option.id}>
                {option.name}
              </option>
            ))}
          </select>
        </label>

        <label className="cal-filter">
          <span>Status</span>
          <select value={statusFilter} onChange={event => onStatusFilterChange(event.target.value)}>
            <option value="All">All statuses</option>
            {APPOINTMENT_STATUSES.map(status => (
              <option key={status} value={status}>
                {status}
              </option>
            ))}
          </select>
        </label>

        <label className="cal-filter">
          <span>Service</span>
          <select
            value={serviceFilter}
            onChange={event => onServiceFilterChange(event.target.value)}
          >
            <option value="all">All services</option>
            {serviceOptions.map(name => (
              <option key={name} value={name}>
                {name}
              </option>
            ))}
          </select>
        </label>
      </div>
    </div>
  )
}
