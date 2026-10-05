import { request, toQueryString } from './client.ts'

export type ReminderType = 'Confirmation' | 'Reminder' | 'Cancellation' | 'Rescheduled'
export type ReminderStatus = 'Pending' | 'Sent' | 'Failed' | 'Cancelled' | 'Skipped'

export interface AppointmentReminder {
  id: string
  appointmentId: string
  customerId: string
  customerName: string
  appointmentStartsAt: string | null
  reminderType: ReminderType
  channel: string
  status: ReminderStatus
  scheduledAt: string
  sentAt: string | null
  attemptCount: number
  lastAttemptAt: string | null
  providerMessageId: string | null
  failureReason: string | null
  createdAt: string
}

export interface ReminderRule {
  id: string
  reminderType: ReminderType
  offsetMinutes: number
  isEnabled: boolean
}

export interface ReminderProviderStatus {
  isConfigured: boolean
  description: string
  channel: string
}

export interface SendTestMessageResult {
  success: boolean
  providerMessageId: string | null
  failureReason: string | null
  providerStatus: string | null
}

/** Everything the history list can be narrowed by. Omitted keys are not sent. */
export interface ReminderSearch {
  status?: ReminderStatus
  reminderType?: ReminderType
  appointmentId?: string
  customerId?: string
  take?: number
}

export function searchReminders(search: ReminderSearch): Promise<AppointmentReminder[]> {
  return request(
    'GET',
    `/reminders/history${toQueryString({
      status: search.status,
      reminderType: search.reminderType,
      appointmentId: search.appointmentId,
      customerId: search.customerId,
      take: search.take,
    })}`,
  )
}

/** The reminder rows of one appointment, oldest scheduled first. */
export function getAppointmentReminders(appointmentId: string): Promise<AppointmentReminder[]> {
  return request('GET', `/appointments/${appointmentId}/reminders`)
}

export function getReminderRules(): Promise<ReminderRule[]> {
  return request('GET', '/reminders/rules')
}

/** Partial edit: only the supplied fields change. */
export function updateReminderRule(
  id: string,
  body: { isEnabled?: boolean; offsetMinutes?: number },
): Promise<ReminderRule> {
  return request('PUT', `/reminders/rules/${id}`, body)
}

export function getReminderProvider(): Promise<ReminderProviderStatus> {
  return request('GET', '/reminders/provider')
}

/** Owner-only operator test send; never touches a customer's reminder rows. */
export function sendTestMessage(phoneNumber: string): Promise<SendTestMessageResult> {
  return request('POST', '/reminders/test', { phoneNumber })
}
