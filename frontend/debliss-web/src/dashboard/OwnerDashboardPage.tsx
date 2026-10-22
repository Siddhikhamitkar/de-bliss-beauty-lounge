import { useState } from 'react'
import { getOwnerDashboard } from '../api/dashboard.ts'
import type { OwnerDashboardRange } from '../api/dashboard.ts'
import type {
  OwnerDailyPoint,
  OwnerDashboard,
  OwnerMonthlyPoint,
} from '../api/contracts.ts'
import { Avatar } from '../components/Avatar.tsx'
import { EmptyState } from '../components/EmptyState.tsx'
import { ErrorState } from '../components/ErrorState.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { formatMoney, formatTime } from '../format.ts'
import { useAsync } from '../useAsync.ts'
import {
  CardIcon,
  CheckCircleIcon,
  ChevronRightIcon,
  GiftIcon,
  NoteIcon,
  RefreshIcon,
  SparkleIcon,
  XCircleIcon,
} from './icons.tsx'
import type { ComponentType, ReactNode } from 'react'

interface OwnerDashboardPageProps {
  /** Bumped after a checkout so money reloads on next show. */
  revision?: number
  /** The signed-in owner's first name, folded into the greeting. */
  firstName?: string
  /** Every action card and section link lands in an existing module. */
  onNavigate: (path: string) => void
  /** Row click: opens the appointment's details on the calendar. */
  onOpenAppointment: (appointmentId: string) => void
  /** A completed visit's Checkout: the app's existing checkout workspace. */
  onCheckout: (appointmentId: string) => void
}

const RANGES: { value: OwnerDashboardRange; label: string }[] = [
  { value: 'Today', label: 'Today' },
  { value: 'Yesterday', label: 'Yesterday' },
  { value: 'Last7Days', label: 'Last 7 days' },
  { value: 'ThisMonth', label: 'This month' },
  { value: 'LastMonth', label: 'Last month' },
  { value: 'Custom', label: 'Custom' },
]

/** The owner's money view: the same floor the receptionist sees, with the
 * financial layer the front desk never gets. Every figure is real — an empty
 * salon reads as zeros, never as invented data. */
export function OwnerDashboardPage({
  revision = 0,
  firstName,
  onNavigate,
  onOpenAppointment,
  onCheckout,
}: OwnerDashboardPageProps) {
  const [range, setRange] = useState<OwnerDashboardRange>('Today')
  // Custom dates are salon-local ISO days; defaults cover the last week.
  const [customFrom, setCustomFrom] = useState(() => isoDayOffset(-6))
  const [customTo, setCustomTo] = useState(() => isoDayOffset(0))

  const dashboard = useAsync(
    () =>
      getOwnerDashboard(
        range,
        ...(range === 'Custom' ? [customFrom, customTo] : []),
      ),
    [range, range === 'Custom' ? customFrom : '', range === 'Custom' ? customTo : '', revision],
  )

  return (
    <section className="dash" aria-label="Owner dashboard">
      <header className="dash-hero">
        <div className="dash-hero-text">
          <p className="dash-hero-eyebrow">De Bliss Beauty Lounge · Owner</p>
          <h1 className="dash-greeting">
            {firstName !== undefined && firstName !== '' ? `Welcome back, ${firstName}` : 'Welcome back'}{' '}
            <span className="dash-wave" aria-hidden="true">
              ✨
            </span>
          </h1>
          <p className="dash-subline">The lounge&rsquo;s money and operations in one view.</p>
        </div>
        <button type="button" className="button dash-refresh" onClick={dashboard.reload}>
          <RefreshIcon /> Refresh
        </button>
      </header>

      <div className="own-range" role="group" aria-label="Reporting range">
        {RANGES.map(option => (
          <button
            key={option.value}
            type="button"
            className={`own-range-chip${range === option.value ? ' own-range-chip--on' : ''}`}
            aria-pressed={range === option.value}
            onClick={() => setRange(option.value)}
          >
            {option.label}
          </button>
        ))}
        {range === 'Custom' && (
          <span className="own-custom">
            <label>
              From{' '}
              <input
                type="date"
                aria-label="Custom range start"
                value={customFrom}
                max={customTo}
                onChange={event => setCustomFrom(event.target.value)}
              />
            </label>
            <label>
              to{' '}
              <input
                type="date"
                aria-label="Custom range end"
                value={customTo}
                min={customFrom}
                onChange={event => setCustomTo(event.target.value)}
              />
            </label>
          </span>
        )}
      </div>

      {dashboard.failure !== undefined && (
        <ErrorState message={dashboard.failure} onRetry={dashboard.reload} />
      )}

      {dashboard.data === undefined && dashboard.failure === undefined && (
        <LoadingState label="Loading your numbers…" />
      )}

      {dashboard.data !== undefined && (
        <OwnerBody
          data={dashboard.data}
          onNavigate={onNavigate}
          onOpenAppointment={onOpenAppointment}
          onCheckout={onCheckout}
        />
      )}
    </section>
  )
}

interface OwnerBodyProps {
  data: OwnerDashboard
  onNavigate: (path: string) => void
  onOpenAppointment: (appointmentId: string) => void
  onCheckout: (appointmentId: string) => void
}

function OwnerBody({ data, onNavigate, onOpenAppointment, onCheckout }: OwnerBodyProps) {
  const { kpis, revenue, operations, inventory, actions } = data

  return (
    <>
      <div className="dash-cards">
        <KpiCard
          tone="champagne"
          icon={CardIcon}
          label="Net revenue · today"
          value={formatMoney(kpis.currency, kpis.netRevenueToday)}
          foot={`vs yesterday ${formatMoney(kpis.currency, kpis.netRevenueYesterday)}`}
        />
        <KpiCard
          tone="sage"
          icon={SparkleIcon}
          label="Net revenue · month to date"
          value={formatMoney(kpis.currency, kpis.netRevenueMonthToDate)}
          foot={
            <>
              vs same days last month{' '}
              <DeltaChip current={kpis.netRevenueMonthToDate} previous={kpis.netRevenueSamePeriodLastMonth} />
            </>
          }
        />
        <KpiCard
          tone="dust"
          icon={CheckCircleIcon}
          label="Completed today"
          value={String(kpis.completedAppointmentsToday)}
          foot={`${operations.today.upcoming} upcoming still to come`}
        />
        <KpiCard
          tone="rose"
          icon={NoteIcon}
          label="Avg transaction"
          value={formatMoney(kpis.currency, revenue.averageTransactionValue)}
          foot={`${revenue.paidInvoiceCount} paid ${revenue.paidInvoiceCount === 1 ? 'invoice' : 'invoices'} · ${revenue.rangeLabel.toLowerCase()}`}
        />
        <KpiCard
          tone="clay"
          icon={XCircleIcon}
          label="Outstanding"
          value={formatMoney(kpis.currency, kpis.outstandingAmount)}
          foot={`${kpis.outstandingInvoiceCount} ${kpis.outstandingInvoiceCount === 1 ? 'bill' : 'bills'} awaiting payment`}
        />
        <KpiCard
          tone="mauve"
          icon={GiftIcon}
          label="Retail · range"
          value={formatMoney(kpis.currency, kpis.retailRevenue)}
          foot={`services ${formatMoney(kpis.currency, kpis.serviceRevenue)}`}
        />
      </div>

      <div className="dash-grid">
        <div className="dash-col">
          <section className="panel" aria-label={`Revenue · ${revenue.rangeLabel}`}>
            <div className="panel-header">
              <h2>Revenue · {revenue.rangeLabel}</h2>
            </div>
            <p className="own-big">
              {formatMoney(kpis.currency, revenue.netRevenue)}
              <DeltaChip current={revenue.netRevenue} previous={revenue.previousNetRevenue} />
            </p>
            <p className="own-big-sub">
              net of VAT and tips · collected {formatMoney(kpis.currency, revenue.collected)}
            </p>

            <div className="own-split" aria-label="Service and retail split">
              <div className="own-split-bar" aria-hidden="true">
                <span
                  className="own-split-services"
                  style={{ width: `${splitShare(revenue.serviceRevenue, revenue.retailRevenue)}%` }}
                />
              </div>
              <p className="own-split-legend">
                Services {formatMoney(kpis.currency, revenue.serviceRevenue)} · Retail{' '}
                {formatMoney(kpis.currency, revenue.retailRevenue)}
              </p>
            </div>

            <dl className="own-stats">
              <div>
                <dt>VAT collected</dt>
                <dd>{formatMoney(kpis.currency, revenue.vatTotal)}</dd>
              </div>
              <div>
                <dt>Tips</dt>
                <dd>{formatMoney(kpis.currency, revenue.tipTotal)}</dd>
              </div>
              <div>
                <dt>Discounts given</dt>
                <dd>{formatMoney(kpis.currency, revenue.discountTotal)}</dd>
              </div>
              <div>
                <dt>Avg transaction</dt>
                <dd>{formatMoney(kpis.currency, revenue.averageTransactionValue)}</dd>
              </div>
            </dl>
          </section>

          <section className="panel" aria-label="Revenue trends">
            <div className="panel-header">
              <h2>Daily net revenue</h2>
            </div>
            <DailyBars points={revenue.daily} currency={kpis.currency} />
            <div className="panel-header own-trends-header">
              <h2>Monthly net revenue</h2>
            </div>
            <MonthlyBars points={revenue.monthly} currency={kpis.currency} />
          </section>

          <section className="panel" aria-label="Best performers">
            <div className="panel-header">
              <h2>What earns the most · {revenue.rangeLabel.toLowerCase()}</h2>
            </div>
            {revenue.topServices.length === 0 && revenue.retailProducts.length === 0 ? (
              <EmptyState>No paid services or products in this range yet.</EmptyState>
            ) : (
              <div className="own-performers">
                <NamedList title="Top services" rows={revenue.topServices} currency={kpis.currency} />
                <NamedList title="By category" rows={revenue.categories} currency={kpis.currency} />
                <NamedList title="Retail products" rows={revenue.retailProducts} currency={kpis.currency} />
              </div>
            )}
          </section>
        </div>

        <div className="dash-col">
          <section className="panel" aria-label="Needs attention">
            <div className="panel-header">
              <h2>Needs attention</h2>
            </div>
            {actions.length === 0 ? (
              <EmptyState>Nothing needs your attention right now.</EmptyState>
            ) : (
              <ul className="own-actions">
                {actions.map(action => (
                  <li key={action.message} className={`own-action own-action--${action.severity}`}>
                    <span className="own-action-severity">{action.severity}</span>
                    <span className="own-action-body">
                      {action.message}
                      {action.count > 1 && <span className="own-action-count"> ×{action.count}</span>}
                    </span>
                    <button type="button" className="button dash-ghost" onClick={() => onNavigate(action.link)}>
                      Open <ChevronRightIcon className="dash-inline-icon" />
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </section>

          <section className="panel" aria-label="Operations today">
            <div className="panel-header">
              <h2>Operations today</h2>
            </div>
            <ul className="own-status-row" aria-label="Appointment outcomes today">
              <li>
                <span className="own-status-num">{operations.today.total}</span> booked
              </li>
              <li>
                <span className="own-status-num">{operations.today.completed}</span> completed
              </li>
              <li>
                <span className="own-status-num">{operations.today.inProgress}</span> in the chair
              </li>
              <li>
                <span className="own-status-num">{operations.today.upcoming}</span> upcoming
              </li>
              <li>
                <span className="own-status-num">{operations.today.cancelled}</span> cancelled
              </li>
              <li>
                <span className="own-status-num">{operations.today.noShow}</span> no-shows
              </li>
            </ul>

            {operations.conflicts.length > 0 ? (
              <ul className="own-conflicts">
                {operations.conflicts.map(conflict => (
                  <li key={`${conflict.staffId}-${conflict.startsAt}`} className="own-conflict">
                    <strong>{conflict.staffName}</strong> is double-booked{' '}
                    {formatTime(conflict.startsAt)}–{formatTime(conflict.endsAt)} ({conflict.appointmentCount}{' '}
                    visits).
                  </li>
                ))}
              </ul>
            ) : (
              <p className="own-quiet">No schedule conflicts today.</p>
            )}

            {operations.awaitingCheckout > 0 && (
              <p className="own-quiet">
                {operations.awaitingCheckout} completed {operations.awaitingCheckout === 1 ? 'visit awaits' : 'visits await'}{' '}
                checkout.
              </p>
            )}

            <div className="panel-header own-trends-header">
              <h2>Today&rsquo;s floor</h2>
            </div>
            {operations.schedule.length === 0 ? (
              <EmptyState>No appointments on the books yet today.</EmptyState>
            ) : (
              <ul className="own-sched">
                {operations.schedule.map(appointment => (
                  <li key={appointment.id} className={`own-sched-row status-${appointment.status}`}>
                    <span className="own-sched-time">{formatTime(appointment.startsAt)}</span>
                    <button
                      type="button"
                      className="own-sched-main"
                      onClick={() => onOpenAppointment(appointment.id)}
                    >
                      <span className="dash-row-title">{appointment.customerName}</span>{' '}
                      <span className="dash-row-sub">{appointment.servicesLabel}</span>
                    </button>
                    <span className={`pill status-pill status-${appointment.status}`}>
                      {appointment.status}
                    </span>
                    {appointment.canCheckout && (
                      <button
                        type="button"
                        className="button dash-row-checkout"
                        onClick={() => onCheckout(appointment.id)}
                      >
                        Checkout
                      </button>
                    )}
                  </li>
                ))}
              </ul>
            )}
          </section>

          <section className="panel" aria-label="Team today">
            <div className="panel-header">
              <h2>Team today</h2>
            </div>
            {operations.staff.length === 0 ? (
              <EmptyState>No active staff yet — add the team in the Staff section.</EmptyState>
            ) : (
              <ul className="dash-staff">
                {operations.staff.map(member => (
                  <li key={member.id} className="dash-staff-row">
                    <Avatar firstName={member.firstName} lastName={member.lastName} color={member.color} />
                    <span className="dash-staff-id">
                      <span className="dash-row-title">
                        {member.firstName} {member.lastName}
                      </span>
                    </span>
                    <span className="dash-staff-side">
                      <span className={`dash-status-chip dash-status-${member.status.toLowerCase()}`}>
                        <span className="dash-status-dot" aria-hidden="true" />
                        {member.status === 'WithClient' ? 'With client' : member.status}
                      </span>
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </section>

          <section className="panel" aria-label="Stock needing attention">
            <div className="panel-header">
              <h2>Stock needing attention</h2>
              <button type="button" className="button dash-ghost" onClick={() => onNavigate('/inventory')}>
                Open inventory <ChevronRightIcon className="dash-inline-icon" />
              </button>
            </div>
            {inventory.items.length === 0 ? (
              <EmptyState>Stock levels are healthy.</EmptyState>
            ) : (
              <>
                <p className="own-quiet">
                  {inventory.outOfStockCount} out of stock · {inventory.lowStockCount} running low
                </p>
                <ul className="own-stock">
                  {inventory.items.map(item => (
                    <li key={item.productId} className="own-stock-row">
                      <span className="dash-row-title">{item.name}</span>
                      <span className="own-stock-qty">
                        {item.currentStock} / {item.minimumStock} {item.unitOfMeasure}
                      </span>
                      <span
                        className={`own-stock-pill ${item.status === 'OutOfStock' ? 'own-stock-pill--out' : 'own-stock-pill--low'}`}
                      >
                        {item.status === 'OutOfStock' ? 'Out of stock' : 'Running low'}
                      </span>
                    </li>
                  ))}
                </ul>
              </>
            )}
          </section>
        </div>
      </div>
    </>
  )
}

function KpiCard({
  tone,
  icon: Icon,
  label,
  value,
  foot,
}: {
  tone: 'champagne' | 'rose' | 'sage' | 'dust' | 'clay' | 'mauve'
  icon: ComponentType
  label: string
  value: string
  foot: ReactNode
}) {
  return (
    <div className={`dash-card dash-card--${tone}`}>
      <span className="dash-card-icon">
        <Icon />
      </span>
      <span className="dash-card-value">{value}</span>
      <span className="dash-card-label">{label}</span>
      <span className="dash-card-foot own-kpi-foot">{foot}</span>
    </div>
  )
}

/** Percent change vs the comparable prior window; honest when there is none. */
function DeltaChip({ current, previous }: { current: number; previous: number }) {
  if (previous === 0) {
    return <span className="own-delta own-delta--flat">no prior period</span>
  }

  const percent = Math.round(((current - previous) / previous) * 1000) / 10

  return (
    <span className={`own-delta ${percent >= 0 ? 'own-delta--up' : 'own-delta--down'}`}>
      {percent >= 0 ? '▲' : '▼'} {Math.abs(percent)}%
    </span>
  )
}

function splitShare(services: number, retail: number): number {
  const total = services + retail

  return total === 0 ? 0 : Math.round((services / total) * 100)
}

function DailyBars({ points, currency }: { points: OwnerDailyPoint[]; currency: string }) {
  if (points.length === 0) {
    return <EmptyState>No days in this range.</EmptyState>
  }

  const max = Math.max(...points.map(point => point.netRevenue))

  return (
    <div className="own-bars">
      {points.map(point => (
        <div
          key={point.date}
          className="own-bar-col"
          title={`${point.date} · ${formatMoney(currency, point.netRevenue)} · ${point.completed} completed, ${point.cancelled} cancelled`}
        >
          <div className="own-bar-track">
            <div
              className={`own-bar${point.netRevenue > 0 ? '' : ' own-bar--zero'}`}
              style={{
                height:
                  max > 0 && point.netRevenue > 0
                    ? `${Math.max((point.netRevenue / max) * 100, 3)}%`
                    : '3%',
              }}
            />
          </div>
          <span className="own-bar-label">{dayLabel(point.date)}</span>
        </div>
      ))}
    </div>
  )
}

function MonthlyBars({ points, currency }: { points: OwnerMonthlyPoint[]; currency: string }) {
  if (points.length === 0) {
    return <EmptyState>No months to chart yet.</EmptyState>
  }

  const max = Math.max(...points.map(point => point.netRevenue))

  return (
    <div className="own-bars own-bars--monthly">
      {points.map(point => (
        <div
          key={point.monthStart}
          className="own-bar-col"
          title={`${point.monthStart} · ${formatMoney(currency, point.netRevenue)}`}
        >
          <div className="own-bar-track">
            <div
              className={`own-bar own-bar--month${point.netRevenue > 0 ? '' : ' own-bar--zero'}`}
              style={{
                height:
                  max > 0 && point.netRevenue > 0
                    ? `${Math.max((point.netRevenue / max) * 100, 3)}%`
                    : '3%',
              }}
            />
          </div>
          <span className="own-bar-label">
            {new Date(`${point.monthStart}T00:00:00`).toLocaleString(undefined, { month: 'short' })}
          </span>
        </div>
      ))}
    </div>
  )
}

function NamedList({
  title,
  rows,
  currency,
}: {
  title: string
  rows: { name: string; revenue: number; count: number }[]
  currency: string
}) {
  return (
    <div className="own-named">
      <h3 className="own-named-title">{title}</h3>
      {rows.length === 0 ? (
        <p className="own-quiet">None in this range.</p>
      ) : (
        <ul className="own-named-rows">
          {rows.map(row => (
            <li key={row.name} className="own-named-row">
              <span className="own-named-name">{row.name}</span>
              <span className="own-named-count">×{row.count}</span>
              <span className="own-named-amount">{formatMoney(currency, row.revenue)}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

/** An ISO date's local midnights, shifted by whole days from today. */
function isoDayOffset(days: number): string {
  const date = new Date()
  date.setDate(date.getDate() + days)
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')

  return `${date.getFullYear()}-${month}-${day}`
}

/** Bars label the day of the month; a new month spells its name. */
function dayLabel(iso: string): string {
  const date = new Date(`${iso}T00:00:00`)

  return date.getDate() === 1
    ? date.toLocaleString(undefined, { month: 'short' })
    : String(date.getDate())
}
