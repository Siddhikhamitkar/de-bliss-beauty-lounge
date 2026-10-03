import { request, toQueryString } from './client.ts'
import type {
  DaySchedule,
  PagedResult,
  SaveStaffRequest,
  SaveStaffTimeOffRequest,
  StaffAvailabilityCheck,
  StaffProfile,
  StaffServiceItem,
  StaffStatusFilter,
  StaffSummary,
  StaffTimeOff,
  WeekSchedule,
} from './contracts.ts'

/** Everything the staff list can be narrowed by. Omitted keys are not sent. */
export interface StaffSearch {
  search?: string
  status?: StaffStatusFilter
  page?: number
  pageSize?: number
}

export function searchStaff(search: StaffSearch): Promise<PagedResult<StaffSummary>> {
  return request(
    'GET',
    `/staff${toQueryString({
      search: search.search,
      status: search.status,
      page: search.page,
      pageSize: search.pageSize,
    })}`,
  )
}

export function getStaff(id: string): Promise<StaffProfile> {
  return request('GET', `/staff/${id}`)
}

export function createStaff(body: SaveStaffRequest): Promise<StaffProfile> {
  return request('POST', '/staff', body)
}

export function updateStaff(id: string, body: SaveStaffRequest): Promise<StaffProfile> {
  return request('PUT', `/staff/${id}`, body)
}

export function setStaffStatus(id: string, isActive: boolean): Promise<StaffProfile> {
  return request('PATCH', `/staff/${id}/status`, { isActive })
}

export function getStaffServices(staffId: string): Promise<StaffServiceItem[]> {
  return request('GET', `/staff/${staffId}/services`)
}

/** Replaces the whole assignment set, mirroring how the service form submits. */
export function setStaffServices(
  staffId: string,
  serviceIds: string[],
): Promise<StaffServiceItem[]> {
  return request('PUT', `/staff/${staffId}/services`, { serviceIds })
}

export function getWeekSchedule(staffId: string): Promise<WeekSchedule> {
  return request('GET', `/staff/${staffId}/working-hours`)
}

/** Replaces the member's whole weekly schedule with the submitted pattern. */
export function saveWeekSchedule(staffId: string, days: DaySchedule[]): Promise<WeekSchedule> {
  return request('PUT', `/staff/${staffId}/working-hours`, { days })
}

export function listTimeOff(staffId: string): Promise<StaffTimeOff[]> {
  return request('GET', `/staff/${staffId}/time-off`)
}

export function createTimeOff(
  staffId: string,
  body: SaveStaffTimeOffRequest,
): Promise<StaffTimeOff> {
  return request('POST', `/staff/${staffId}/time-off`, body)
}

export function updateTimeOff(
  staffId: string,
  timeOffId: string,
  body: SaveStaffTimeOffRequest,
): Promise<StaffTimeOff> {
  return request('PUT', `/staff/${staffId}/time-off/${timeOffId}`, body)
}

/**
 * Only future entries can be removed; anything that has started comes back as
 * a 409, which the caller surfaces as a banner.
 */
export function deleteTimeOff(staffId: string, timeOffId: string): Promise<void> {
  return request('DELETE', `/staff/${staffId}/time-off/${timeOffId}`)
}

/**
 * Answers "can this staff member take this slot?". The window must sit inside
 * one day and end after it starts; the server validates both.
 */
export function checkStaffAvailability(
  staffId: string,
  start: Date,
  end: Date,
): Promise<StaffAvailabilityCheck> {
  return request(
    'GET',
    `/staff/availability${toQueryString({ staffId, start: start.toISOString(), end: end.toISOString() })}`,
  )
}
