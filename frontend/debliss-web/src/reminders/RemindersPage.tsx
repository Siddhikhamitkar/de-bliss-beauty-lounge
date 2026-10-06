import { useState } from 'react'
import type { KeyboardEvent } from 'react'
import { searchReminders } from '../api/reminders.ts'
import type { AppointmentReminder, ReminderStatus, ReminderType } from '../api/reminders.ts'
import { useAuth } from '../auth/AuthContext.tsx'
import { EmptyState } from '../components/EmptyState.tsx'
import { ErrorState } from '../components/ErrorState.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { formatDateTime } from '../format.ts'
import { useAsync } from '../useAsync.ts'
import { ReminderSettings } from './ReminderSettings.tsx'

const TABS = [
  { id: 'history', label: 'History' },
  { id: 'settings', label: 'Settings' },
] as const

type TabId = (typeof TABS)[number]['id']

const STATUS_FILTERS: readonly (ReminderStatus | 'All')[] = [
  'All',
  'Pending',
  'Sent',
  'Failed',
  'Cancelled',
  'Skipped',
]

const TYPE_FILTERS: readonly (ReminderType | 'All')[] = [
  'All',
  'Confirmation',
  'Reminder',
  'Cancellation',
  'Rescheduled',
]

/**
 * The reminder workspace: the operational history everyone in operations
 * monitors, and — for the owner alone — the settings that decide how WhatsApp
 * reminders behave. The API is the real gate; the role check here only keeps
 * the settings tab from ever being drawn for a receptionist.
 */
export function RemindersPage() {
  const { user } = useAuth()
  const [activeTab, setActiveTab] = useState<TabId>('history')

  const isOwner = user?.roles.includes('OWNER') ?? false
  const visibleTabs = isOwner ? TABS : TABS.filter(tab => tab.id !== 'settings')

  function handleTabKeys(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') {
      return
    }

    event.preventDefault()
    const step = event.key === 'ArrowRight' ? 1 : -1
    const index = visibleTabs.findIndex(tab => tab.id === activeTab)
    const next = visibleTabs[(index + step + visibleTabs.length) % visibleTabs.length]
    setActiveTab(next.id)
    document.getElementById(`reminders-tab-${next.id}`)?.focus()
  }

  return (
    <section className="panel" aria-label="Reminders">
      <div className="panel-header">
        <div className="panel-heading">
          <h2>Reminders</h2>
          <p className="panel-lede">
            Appointment reminders over WhatsApp — delivery history and settings.
          </p>
        </div>
      </div>

      <div className="page-tabs" role="tablist" aria-label="Reminder views" onKeyDown={handleTabKeys}>
        {visibleTabs.map(tab => (
          <button
            key={tab.id}
            type="button"
            role="tab"
            id={`reminders-tab-${tab.id}`}
            aria-selected={activeTab === tab.id}
            aria-controls="reminders-tabpanel"
            tabIndex={activeTab === tab.id ? 0 : -1}
            className="page-tab"
            onClick={() => setActiveTab(tab.id)}
          >
            {tab.label}
          </button>
        ))}
      </div>

      <div role="tabpanel" id="reminders-tabpanel" aria-labelledby={`reminders-tab-${activeTab}`}>
        {activeTab === 'history' ? <ReminderHistory /> : <ReminderSettings />}
      </div>
    </section>
  )
}

/** The reminder log every operations user watches for failed sends. */
function ReminderHistory() {
  const [status, setStatus] = useState<ReminderStatus | 'All'>('All')
  const [reminderType, setReminderType] = useState<ReminderType | 'All'>('All')

  const reminders = useAsync(
    () =>
      searchReminders({
        status: status === 'All' ? undefined : status,
        reminderType: reminderType === 'All' ? undefined : reminderType,
      }),
    [status, reminderType],
  )

  return (
    <>
      <div className="filters">
        <label className="field">
          <span>Status</span>
          <select
            name="status"
            value={status}
            onChange={event => setStatus(event.target.value as ReminderStatus | 'All')}
          >
            {STATUS_FILTERS.map(value => (
              <option key={value} value={value}>
                {value}
              </option>
            ))}
          </select>
        </label>
        <label className="field">
          <span>Type</span>
          <select
            name="type"
            value={reminderType}
            onChange={event => setReminderType(event.target.value as ReminderType | 'All')}
          >
            {TYPE_FILTERS.map(value => (
              <option key={value} value={value}>
                {value}
              </option>
            ))}
          </select>
        </label>
      </div>

      {reminders.failure !== undefined && (
        <ErrorState message={reminders.failure} onRetry={reminders.reload} />
      )}

      {reminders.data === undefined && reminders.failure === undefined && (
        <LoadingState label="Loading reminders…" />
      )}

      {reminders.data !== undefined && reminders.data.length === 0 && (
        <EmptyState>No reminders in this view.</EmptyState>
      )}

      {reminders.data !== undefined && reminders.data.length > 0 && (
        <table className="grid">
          <thead>
            <tr>
              <th scope="col">Scheduled</th>
              <th scope="col">Appointment</th>
              <th scope="col">Customer</th>
              <th scope="col">Type</th>
              <th scope="col">Status</th>
              <th scope="col">Attempts</th>
              <th scope="col">Sent</th>
              <th scope="col">Failure</th>
            </tr>
          </thead>
          <tbody>
            {reminders.data.map(reminder => (
              <ReminderRow key={reminder.id} reminder={reminder} />
            ))}
          </tbody>
        </table>
      )}
    </>
  )
}

function ReminderRow({ reminder }: { reminder: AppointmentReminder }) {
  return (
    <tr>
      <td>{formatDateTime(reminder.scheduledAt)}</td>
      <td>
        {reminder.appointmentStartsAt === null ? '—' : formatDateTime(reminder.appointmentStartsAt)}
      </td>
      <td>{reminder.customerName}</td>
      <td>{reminder.reminderType}</td>
      <td>
        <span className={`pill status-pill status-${reminder.status}`}>{reminder.status}</span>
      </td>
      <td className="numeric">{reminder.attemptCount}</td>
      <td>{reminder.sentAt === null ? '—' : formatDateTime(reminder.sentAt)}</td>
      <td className={reminder.failureReason === null ? undefined : 'reminder-failure'}>
        {reminder.failureReason ?? '—'}
      </td>
    </tr>
  )
}
