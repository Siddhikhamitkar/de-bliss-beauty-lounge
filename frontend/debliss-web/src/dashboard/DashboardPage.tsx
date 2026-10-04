import { useEffect, useState } from 'react'
import { getReceptionistDashboard } from '../api/dashboard.ts'
import type {
  DashboardAppointment,
  DashboardStaffMember,
  ReceptionistDashboard,
} from '../api/contracts.ts'
import { Avatar } from '../components/Avatar.tsx'
import { EmptyState } from '../components/EmptyState.tsx'
import { ErrorState } from '../components/ErrorState.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { formatDate, formatTime } from '../format.ts'
import { useAsync } from '../useAsync.ts'
import { greetingFor, relativeSince, relativeUntil } from './dashboardTime.ts'
import {
  CalendarIcon,
  CalendarPlusIcon,
  CardIcon,
  CheckCircleIcon,
  ChevronRightIcon,
  ClockIcon,
  GiftIcon,
  NowIcon,
  NoteIcon,
  RefreshIcon,
  SearchIcon,
  SparkleIcon,
  UserPlusIcon,
  WalkInIcon,
  XCircleIcon,
} from './icons.tsx'
import type { ComponentType } from 'react'

interface DashboardPageProps {
  /** Bumped after a checkout elsewhere so the snapshot reloads on next show. */
  revision?: number
  /** Row click: opens the appointment's details on the calendar. */
  onOpenAppointment: (appointmentId: string) => void
  onNewAppointment: () => void
  onAddCustomer: () => void
  onFindCustomer: () => void
  onOpenBilling: () => void
  /** A completed visit's Checkout: the app's existing checkout workspace. */
  onCheckout: (appointmentId: string) => void
  onViewAllAppointments: () => void
}

/**
 * The receptionist's landing: one aggregated snapshot of today. Every number
 * is appointment workload — the endpoint deliberately carries no money, so
 * nothing owner-level can leak onto the front desk.
 */
export function DashboardPage({
  revision = 0,
  onOpenAppointment,
  onNewAppointment,
  onAddCustomer,
  onFindCustomer,
  onOpenBilling,
  onCheckout,
  onViewAllAppointments,
}: DashboardPageProps) {
  const dashboard = useAsync(() => getReceptionistDashboard(), [revision])
  // One slow tick keeps the greeting, "in N min" labels and staff statuses
  // honest while the page stays open.
  const [now, setNow] = useState(() => new Date())

  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), 60_000)
    return () => clearInterval(timer)
  }, [])

  return (
    <section className="dash" aria-label="Receptionist dashboard">
      <header className="dash-hero">
        <div className="dash-hero-text">
          <p className="dash-hero-eyebrow">
            {dashboard.data === undefined
              ? 'De Bliss Beauty Lounge'
              : `${formatDate(dashboard.data.today.date)} · De Bliss Beauty Lounge`}
          </p>
          <h1 className="dash-greeting">
            {greetingFor(now.getHours())}{' '}
            <span className="dash-wave" aria-hidden="true">
              👋
            </span>
          </h1>
          <p className="dash-subline">Here&rsquo;s what&rsquo;s happening at De Bliss today.</p>
        </div>
        <button type="button" className="button dash-refresh" onClick={dashboard.reload}>
          <RefreshIcon /> Refresh
        </button>
      </header>

      {dashboard.failure !== undefined && (
        <ErrorState message={dashboard.failure} onRetry={dashboard.reload} />
      )}

      {dashboard.data === undefined && dashboard.failure === undefined && (
        <LoadingState label="Loading dashboard…" />
      )}

      {dashboard.data !== undefined && (
        <DashboardBody
          data={dashboard.data}
          now={now}
          onOpenAppointment={onOpenAppointment}
          onNewAppointment={onNewAppointment}
          onAddCustomer={onAddCustomer}
          onFindCustomer={onFindCustomer}
          onOpenBilling={onOpenBilling}
          onCheckout={onCheckout}
          onViewAllAppointments={onViewAllAppointments}
        />
      )}
    </section>
  )
}

interface DashboardBodyProps {
  data: ReceptionistDashboard
  now: Date
  onOpenAppointment: (appointmentId: string) => void
  onNewAppointment: () => void
  onAddCustomer: () => void
  onFindCustomer: () => void
  onOpenBilling: () => void
  onCheckout: (appointmentId: string) => void
  onViewAllAppointments: () => void
}

interface KpiCard {
  label: string
  value: number
  tone: 'champagne' | 'rose' | 'sage' | 'dust' | 'clay' | 'mauve'
  icon: ComponentType
  foot: string
  live?: boolean
}

function DashboardBody({
  data,
  now,
  onOpenAppointment,
  onNewAppointment,
  onAddCustomer,
  onFindCustomer,
  onOpenBilling,
  onCheckout,
  onViewAllAppointments,
}: DashboardBodyProps) {
  const cards: KpiCard[] = [
    { label: 'Appointments', value: data.today.total, tone: 'champagne', icon: CalendarIcon, foot: 'booked today' },
    { label: 'Upcoming', value: data.today.upcoming, tone: 'rose', icon: ClockIcon, foot: 'still to come' },
    { label: 'In progress', value: data.today.inProgress, tone: 'sage', icon: NowIcon, foot: 'in the chair now', live: true },
    { label: 'Completed', value: data.today.completed, tone: 'dust', icon: CheckCircleIcon, foot: 'checked out' },
    { label: 'No-shows', value: data.today.noShow, tone: 'clay', icon: XCircleIcon, foot: "didn't arrive" },
    { label: 'Walk-ins', value: data.today.walkIns, tone: 'mauve', icon: WalkInIcon, foot: 'without a booking' },
  ]

  return (
    <>
      <div className="dash-cards">
        {cards.map(card => (
          <div key={card.label} className={`dash-card dash-card--${card.tone}`}>
            <span className="dash-card-icon">
              <card.icon />
            </span>
            <span className="dash-card-value">{card.value}</span>
            <span className="dash-card-label">{card.label}</span>
            <span className="dash-card-foot">
              {card.live === true && card.value > 0 && (
                <span className="dash-live-dot" aria-hidden="true" />
              )}
              {card.foot}
            </span>
          </div>
        ))}
      </div>

      <div className="dash-grid">
        <div className="dash-col">
          <section className="panel" aria-label="Today's schedule">
            <div className="panel-header">
              <h2>Today&rsquo;s schedule</h2>
              <button type="button" className="button dash-ghost" onClick={onViewAllAppointments}>
                View all appointments <ChevronRightIcon className="dash-inline-icon" />
              </button>
            </div>
            {data.schedule.length === 0 ? (
              <EmptyState>No appointments on the books yet today.</EmptyState>
            ) : (
              <ul className="dash-rows">
                {data.schedule.map(appointment => (
                  <ScheduleRow
                    key={appointment.id}
                    appointment={appointment}
                    onOpen={() => onOpenAppointment(appointment.id)}
                    onCheckout={
                      appointment.canCheckout ? () => onCheckout(appointment.id) : undefined
                    }
                  />
                ))}
              </ul>
            )}
          </section>

          <section className="panel" aria-label="Team right now">
            <div className="panel-header">
              <h2>Team right now</h2>
            </div>
            {data.staff.length === 0 ? (
              <EmptyState>No active staff yet — add the team in the Staff section.</EmptyState>
            ) : (
              <ul className="dash-staff">
                {data.staff.map(member => (
                  <StaffRow key={member.id} member={member} />
                ))}
              </ul>
            )}
          </section>
        </div>

        <div className="dash-col">
          <section className="panel" aria-label="Quick actions">
            <div className="panel-header">
              <h2>Quick actions</h2>
            </div>
            <div className="dash-actions">
              <button
                type="button"
                className="dash-action dash-action--primary"
                onClick={onNewAppointment}
              >
                <span className="dash-action-icon">
                  <CalendarPlusIcon />
                </span>
                <span className="dash-action-title">New appointment</span>{' '}
                <span className="dash-action-hint">Book a visit on the calendar</span>
              </button>
              <button type="button" className="dash-action" onClick={onAddCustomer}>
                <span className="dash-action-icon">
                  <UserPlusIcon />
                </span>
                <span className="dash-action-title">Add customer</span>{' '}
                <span className="dash-action-hint">Register a new guest</span>
              </button>
              <button type="button" className="dash-action" onClick={onFindCustomer}>
                <span className="dash-action-icon">
                  <SearchIcon />
                </span>
                <span className="dash-action-title">Find customer</span>{' '}
                <span className="dash-action-hint">Search profiles and notes</span>
              </button>
              <button type="button" className="dash-action" onClick={onOpenBilling}>
                <span className="dash-action-icon">
                  <CardIcon />
                </span>
                <span className="dash-action-title">Billing &amp; checkout</span>{' '}
                <span className="dash-action-hint">Invoices and payments</span>
              </button>
            </div>
          </section>

          <section className="panel" aria-label="Up next">
            <div className="panel-header">
              <h2>Up next</h2>
            </div>
            {data.upcoming.length === 0 ? (
              <EmptyState>Nothing else booked after right now — the rest of today is clear.</EmptyState>
            ) : (
              <ul className="dash-rows">
                {data.upcoming.map(appointment => (
                  <li key={appointment.id} className={`dash-row status-${appointment.status}`}>
                    <span className="dash-row-when">
                      <span className="dash-row-when-in">{relativeUntil(appointment.startsAt, now)}</span>
                      <span className="dash-row-time-end">{formatTime(appointment.startsAt)}</span>
                    </span>
                    <span className="dash-row-avatar">
                      <CustomerAvatar name={appointment.customerName} seed={appointment.customerId ?? appointment.customerName} />
                    </span>
                    <button
                      type="button"
                      className="dash-row-main"
                      onClick={() => onOpenAppointment(appointment.id)}
                    >
                      <span className="dash-row-title">{appointment.customerName}</span>{' '}
                      <span className="dash-row-sub">{appointment.servicesLabel}</span>
                    </button>
                    <span className={`pill status-pill status-${appointment.status}`}>
                      {appointment.status}
                    </span>
                    <span className="dash-row-chevron">
                      <ChevronRightIcon />
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </section>

          {data.popularServices.length > 0 && (
            <section className="panel" aria-label="Popular today">
              <div className="panel-header">
                <h2>Popular today</h2>
              </div>
              <ul className="dash-popular">
                {data.popularServices.map(service => (
                  <li key={service.name} className="dash-popular-row">
                    <span>{service.name}</span>
                    <span className="dash-popular-count">&times;{service.count}</span>
                  </li>
                ))}
              </ul>
            </section>
          )}

          <AlertsPanel data={data} now={now} />
        </div>
      </div>
    </>
  )
}

function ScheduleRow({
  appointment,
  onOpen,
  onCheckout,
}: {
  appointment: DashboardAppointment
  onOpen: () => void
  onCheckout?: () => void
}) {
  return (
    <li className={`dash-row status-${appointment.status}`}>
      <span className="dash-row-time">
        <span>{formatTime(appointment.startsAt)}</span>
        <span className="dash-row-time-end">{formatTime(appointment.endsAt)}</span>
      </span>
      <span className="dash-row-avatar">
        <CustomerAvatar
          name={appointment.customerName}
          seed={appointment.customerId ?? appointment.customerName}
        />
      </span>
      <button type="button" className="dash-row-main" onClick={onOpen}>
        <span className="dash-row-title">{appointment.customerName}</span>{' '}
        <span className="dash-row-sub">
          {appointment.servicesLabel}
          {appointment.staffName !== null ? ` · ${appointment.staffName}` : ''}
        </span>
      </button>
      <span className={`pill status-pill status-${appointment.status}`}>
        {appointment.status}
      </span>
      {onCheckout !== undefined && (
        <button type="button" className="button dash-row-checkout" onClick={onCheckout}>
          Checkout
        </button>
      )}
      <span className="dash-row-chevron">
        <ChevronRightIcon />
      </span>
    </li>
  )
}

/** Blush/champagne discs only — a deterministic tint per real customer seed. */
const CUSTOMER_TONES = ['#c99aa2', '#c9a86a', '#a98bb0', '#b5766b', '#9d8aa8', '#bd9266']

function customerTone(seed: string): string {
  let hash = 0
  for (let i = 0; i < seed.length; i += 1) {
    hash = (hash * 31 + seed.charCodeAt(i)) >>> 0
  }
  return CUSTOMER_TONES[hash % CUSTOMER_TONES.length]
}

function CustomerAvatar({ name, seed }: { name: string; seed: string }) {
  const parts = name.trim().split(/\s+/)
  const firstName = parts[0] ?? ''
  const lastName = parts.length > 1 ? parts[parts.length - 1] : ''

  return <Avatar firstName={firstName} lastName={lastName} color={customerTone(seed)} />
}

/** "WithClient" arrives as the enum name; the chip reads better with a space. */
function staffStatusLabel(status: string): string {
  return status === 'WithClient' ? 'With client' : status
}

function StaffRow({ member }: { member: DashboardStaffMember }) {
  return (
    <li className="dash-staff-row">
      <Avatar firstName={member.firstName} lastName={member.lastName} color={member.color} />
      <span className="dash-staff-id">
        <span className="dash-row-title">
          {member.firstName} {member.lastName}
        </span>{' '}
        {member.jobTitle !== null && <span className="dash-row-sub">{member.jobTitle}</span>}
      </span>
      <span className="dash-staff-side">
        <span className={`dash-status-chip dash-status-${member.status.toLowerCase()}`}>
          <span className="dash-status-dot" aria-hidden="true" />
          {staffStatusLabel(member.status)}
        </span>
        {member.statusNote !== null && (
          <span className="dash-status-note">{member.statusNote}</span>
        )}
      </span>
    </li>
  )
}

function AlertsPanel({ data, now }: { data: ReceptionistDashboard; now: Date }) {
  const { alerts } = data

  return (
    <section className="panel" aria-label="Customer alerts">
      <div className="panel-header">
        <h2>Customer alerts</h2>
      </div>
      <div className="dash-alerts">
        <div className="dash-alert">
          <span className="dash-alert-icon">
            <GiftIcon />
          </span>
          <div className="dash-alert-body">
            <h3 className="dash-alert-title">Birthdays</h3>
            {alerts.birthdaysToday > 0 ? (
              <p>
                Wish a happy birthday to {joinNames(alerts.birthdayNamesToday)}.
              </p>
            ) : alerts.birthdayNamesLaterThisWeek.length > 0 ? (
              <p>
                No birthdays today. Coming up this week:{' '}
                {joinNames(alerts.birthdayNamesLaterThisWeek)}.
              </p>
            ) : (
              <p className="dash-quiet">No birthdays in the next seven days.</p>
            )}
          </div>
        </div>

        <div className="dash-alert">
          <span className="dash-alert-icon">
            <SparkleIcon />
          </span>
          <div className="dash-alert-body">
            <h3 className="dash-alert-title">First visits</h3>
            {alerts.firstVisitsToday > 0 ? (
              <p>
                {alerts.firstVisitsToday}{' '}
                {alerts.firstVisitsToday === 1 ? 'first-time customer' : 'first-time customers'}{' '}
                today — make them feel welcome.
              </p>
            ) : (
              <p className="dash-quiet">No first-time customers today.</p>
            )}
          </div>
        </div>

        <div className="dash-alert">
          <span className="dash-alert-icon">
            <NoteIcon />
          </span>
          <div className="dash-alert-body">
            <h3 className="dash-alert-title">Latest notes</h3>
            {alerts.recentNotes.length === 0 ? (
              <p className="dash-quiet">No reception notes yet.</p>
            ) : (
              <ul className="dash-notes">
                {alerts.recentNotes.map(note => (
                  <li
                    key={`${note.customerId}-${note.createdAt}`}
                    className="dash-note"
                  >
                    <p className="dash-note-text">{note.note}</p>
                    <p className="dash-note-meta">
                      {note.customerName} · {relativeSince(note.createdAt, now)}
                    </p>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </div>
      </div>
    </section>
  )
}

function joinNames(names: string[]): string {
  if (names.length <= 1) {
    return names[0] ?? ''
  }

  return `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`
}
