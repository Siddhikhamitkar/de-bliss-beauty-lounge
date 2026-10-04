import { request, toQueryString } from './client.ts'
import type {
  Appointment,
  CalendarData,
  SaveAppointmentRequest,
  SetAppointmentStatusRequest,
  UpdateScheduleRequest,
} from './contracts.ts'

/**
 * Everything the grid renders for [from, to). The server caps the span at 62
 * days, and every view the workspace offers stays well under that.
 */
export function getCalendar(from: Date, to: Date): Promise<CalendarData> {
  return request(
    'GET',
    `/calendar${toQueryString({ from: from.toISOString(), to: to.toISOString() })}`,
  )
}

export function getAppointment(id: string): Promise<Appointment> {
  return request('GET', `/appointments/${id}`)
}

/** Creates a booking; the 201 body is the stored appointment with its computed end. */
export function createAppointment(body: SaveAppointmentRequest): Promise<Appointment> {
  return request('POST', '/appointments', body)
}

/** Full edit: replaces the service lines wholesale and recomputes the end. */
export function updateAppointment(id: string, body: SaveAppointmentRequest): Promise<Appointment> {
  return request('PUT', `/appointments/${id}`, body)
}

/**
 * One calendar drag or resize. The server re-validates availability for the
 * resulting window before committing; a rejection here is the caller's cue to
 * revert and surface the message.
 */
export function updateAppointmentSchedule(
  id: string,
  body: UpdateScheduleRequest,
): Promise<Appointment> {
  return request('PATCH', `/appointments/${id}/schedule`, body)
}

/** Moves the booking along its status pipeline; illegal jumps come back as 409. */
export function setAppointmentStatus(
  id: string,
  body: SetAppointmentStatusRequest,
): Promise<Appointment> {
  return request('PATCH', `/appointments/${id}/status`, body)
}
