import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AuthUser } from '../api/auth.ts'
import {
  getReminderProvider,
  getReminderRules,
  searchReminders,
  sendTestMessage,
  updateReminderRule,
} from '../api/reminders.ts'
import type { AppointmentReminder, ReminderRule } from '../api/reminders.ts'
import { useAuth } from '../auth/AuthContext.tsx'
import { RemindersPage } from './RemindersPage.tsx'

vi.mock('../api/reminders.ts', () => ({
  searchReminders: vi.fn(),
  getAppointmentReminders: vi.fn(),
  getReminderRules: vi.fn(),
  updateReminderRule: vi.fn(),
  getReminderProvider: vi.fn(),
  sendTestMessage: vi.fn(),
}))

vi.mock('../auth/AuthContext.tsx', () => ({
  useAuth: vi.fn(),
}))

const ownerUser: AuthUser = {
  id: 'u1',
  firstName: 'Omar',
  lastName: 'Owner',
  email: 'owner@debliss.dev',
  roles: ['OWNER'],
  staffId: null,
}

const receptionistUser: AuthUser = {
  id: 'u2',
  firstName: 'Nadia',
  lastName: 'FrontDesk',
  email: 'receptionist@debliss.dev',
  roles: ['RECEPTIONIST'],
  staffId: null,
}

function mockUser(user: AuthUser) {
  vi.mocked(useAuth).mockReturnValue({
    user,
    loading: false,
    login: vi.fn(),
    logout: vi.fn(),
  })
}

function reminder(overrides: Partial<AppointmentReminder> = {}): AppointmentReminder {
  return {
    id: 'r1',
    appointmentId: 'a1',
    customerId: 'c1',
    customerName: 'Lina Mansour',
    appointmentStartsAt: '2026-10-04T09:00:00Z',
    reminderType: 'Reminder',
    channel: 'WhatsApp',
    status: 'Pending',
    scheduledAt: '2026-10-03T06:00:00Z',
    sentAt: null,
    attemptCount: 0,
    lastAttemptAt: null,
    providerMessageId: null,
    failureReason: null,
    createdAt: '2026-10-01T06:00:00Z',
    ...overrides,
  }
}

const rules: ReminderRule[] = [
  { id: 'rule-1', reminderType: 'Reminder', offsetMinutes: 1440, isEnabled: true },
  { id: 'rule-2', reminderType: 'Confirmation', offsetMinutes: 0, isEnabled: true },
]

async function openSettings(user: ReturnType<typeof userEvent.setup>) {
  render(<RemindersPage />)
  await user.click(await screen.findByRole('tab', { name: 'Settings' }))
}

describe('RemindersPage', () => {
  beforeEach(() => {
    mockUser(ownerUser)
    vi.mocked(searchReminders).mockResolvedValue([reminder()])
    vi.mocked(getReminderRules).mockResolvedValue(rules)
    vi.mocked(getReminderProvider).mockResolvedValue({
      isConfigured: true,
      description: 'Twilio WhatsApp sender for this salon.',
      channel: 'WhatsApp',
    })
    vi.mocked(updateReminderRule).mockResolvedValue({
      id: 'rule-1',
      reminderType: 'Reminder',
      offsetMinutes: 1440,
      isEnabled: false,
    })
    vi.mocked(sendTestMessage).mockResolvedValue({
      success: true,
      providerMessageId: 'SM123',
      failureReason: null,
      providerStatus: 'queued',
    })
  })

  it('loads the reminder history that the operations team monitors', async () => {
    render(<RemindersPage />)

    expect(await screen.findByText('Lina Mansour')).toBeInTheDocument()
    expect(screen.getByText('Pending', { selector: 'span.pill' })).toBeInTheDocument()
    // 'Reminder' also appears as a Type filter option — match the table cell.
    expect(screen.getByText('Reminder', { selector: 'td' })).toBeInTheDocument()
    expect(searchReminders).toHaveBeenCalledWith({ status: undefined, reminderType: undefined })
  })

  it('shows a failed send with its failure reason and attempt count', async () => {
    vi.mocked(searchReminders).mockResolvedValue([
      reminder({
        status: 'Failed',
        attemptCount: 2,
        lastAttemptAt: '2026-10-03T07:00:00Z',
        failureReason: 'Twilio error 63016 — outside the messaging window.',
      }),
    ])
    render(<RemindersPage />)

    expect(
      await screen.findByText('Twilio error 63016 — outside the messaging window.'),
    ).toBeInTheDocument()
    expect(screen.getByText('Failed', { selector: 'span.pill' })).toBeInTheDocument()
    expect(screen.getByText('2')).toBeInTheDocument()
  })

  it('narrows the history with the status and type filters', async () => {
    const user = userEvent.setup()
    render(<RemindersPage />)
    await screen.findByText('Lina Mansour')

    await user.selectOptions(screen.getByLabelText('Status'), 'Failed')
    await waitFor(() =>
      expect(searchReminders).toHaveBeenLastCalledWith({
        status: 'Failed',
        reminderType: undefined,
      }),
    )

    await user.selectOptions(screen.getByLabelText('Type'), 'Reminder')
    await waitFor(() =>
      expect(searchReminders).toHaveBeenLastCalledWith({
        status: 'Failed',
        reminderType: 'Reminder',
      }),
    )
  })

  it('shows the empty state when nothing matches the filters', async () => {
    vi.mocked(searchReminders).mockResolvedValue([])
    render(<RemindersPage />)

    expect(await screen.findByText('No reminders in this view.')).toBeInTheDocument()
  })

  it('shows a failed load with a working retry', async () => {
    const user = userEvent.setup()
    vi.mocked(searchReminders).mockRejectedValueOnce(new Error('boom'))
    vi.mocked(searchReminders).mockResolvedValue([reminder()])
    render(<RemindersPage />)

    expect(await screen.findByRole('alert')).toHaveTextContent('boom')

    await user.click(screen.getByRole('button', { name: 'Try again' }))

    expect(await screen.findByText('Lina Mansour')).toBeInTheDocument()
  })

  it('keeps the settings tab out of a receptionist’s reach', async () => {
    mockUser(receptionistUser)
    render(<RemindersPage />)

    expect(await screen.findByRole('tab', { name: 'History' })).toBeInTheDocument()
    expect(screen.queryByRole('tab', { name: 'Settings' })).not.toBeInTheDocument()
  })

  it('gives the owner the connection status, the rules and the test send', async () => {
    const user = userEvent.setup()
    await openSettings(user)

    const connection = await screen.findByRole('region', { name: 'WhatsApp connection' })
    expect(within(connection).getByText('Connected')).toBeInTheDocument()
    expect(within(connection).getByText(/Channel: WhatsApp/)).toBeInTheDocument()

    const rulesCard = await screen.findByRole('region', { name: 'Reminder rules' })
    expect(within(rulesCard).getByRole('rowheader', { name: 'Reminder' })).toBeInTheDocument()
    expect(
      within(rulesCard).getByLabelText('Timing in minutes for the Reminder notice'),
    ).toHaveValue(1440)
    // The event notices have no timing to set.
    expect(within(rulesCard).getByRole('rowheader', { name: 'Confirmation' })).toBeInTheDocument()
    expect(within(rulesCard).getByText('Sent right away')).toBeInTheDocument()

    expect(screen.getByRole('region', { name: 'Send test message' })).toBeInTheDocument()
  })

  it('reports a connection the salon has not configured yet', async () => {
    const user = userEvent.setup()
    vi.mocked(getReminderProvider).mockResolvedValue({
      isConfigured: false,
      description: 'Twilio WhatsApp is not configured for this environment.',
      channel: 'WhatsApp',
    })
    await openSettings(user)

    const connection = await screen.findByRole('region', { name: 'WhatsApp connection' })
    expect(within(connection).getByText('Not configured')).toBeInTheDocument()
  })

  it('toggles a rule through the API and reloads the list', async () => {
    const user = userEvent.setup()
    await openSettings(user)

    const rulesCard = await screen.findByRole('region', { name: 'Reminder rules' })
    const row = within(rulesCard).getByRole('rowheader', { name: 'Reminder' }).closest('tr')
    expect(row).not.toBeNull()

    await user.click(within(row as HTMLElement).getByRole('button', { name: 'Disable' }))

    await waitFor(() =>
      expect(updateReminderRule).toHaveBeenCalledWith('rule-1', { isEnabled: false }),
    )
    await waitFor(() => expect(getReminderRules).toHaveBeenCalledTimes(2))
  })

  it('saves a new timing for the timed notice', async () => {
    const user = userEvent.setup()
    await openSettings(user)

    const rulesCard = await screen.findByRole('region', { name: 'Reminder rules' })
    const row = within(rulesCard).getByRole('rowheader', { name: 'Reminder' }).closest('tr')
    const input = within(row as HTMLElement).getByLabelText('Timing in minutes for the Reminder notice')

    await user.clear(input)
    await user.type(input, '120')
    await user.click(within(row as HTMLElement).getByRole('button', { name: 'Save timing' }))

    await waitFor(() =>
      expect(updateReminderRule).toHaveBeenCalledWith('rule-1', { offsetMinutes: 120 }),
    )
  })

  it('refuses a timing outside the allowed window without calling the API', async () => {
    const user = userEvent.setup()
    await openSettings(user)

    const rulesCard = await screen.findByRole('region', { name: 'Reminder rules' })
    const row = within(rulesCard).getByRole('rowheader', { name: 'Reminder' }).closest('tr')
    const input = within(row as HTMLElement).getByLabelText('Timing in minutes for the Reminder notice')

    await user.clear(input)
    await user.click(within(row as HTMLElement).getByRole('button', { name: 'Save timing' }))

    expect(
      await screen.findByText('Enter between 1 and 43200 minutes (30 days).'),
    ).toBeInTheDocument()
    expect(updateReminderRule).not.toHaveBeenCalled()
  })

  it('sends a test message only after the confirmation and shows the provider id', async () => {
    const user = userEvent.setup()
    await openSettings(user)

    const card = await screen.findByRole('region', { name: 'Send test message' })
    const sendButton = within(card).getByRole('button', { name: 'Send test message' })
    expect(sendButton).toBeDisabled()

    await user.type(within(card).getByPlaceholderText('+14155552671'), '+14155552671')
    expect(sendButton).toBeEnabled()
    await user.click(sendButton)

    // The dialog stands between the operator and any send.
    expect(sendTestMessage).not.toHaveBeenCalled()
    const dialog = await screen.findByRole('dialog', { name: 'Send test WhatsApp' })
    expect(within(dialog).getByText(/\+14155552671/)).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Send test' }))

    await waitFor(() => expect(sendTestMessage).toHaveBeenCalledWith('+14155552671'))
    expect(
      await screen.findByText('Test message sent — provider message id SM123.'),
    ).toHaveAttribute('role', 'status')
  })
})
