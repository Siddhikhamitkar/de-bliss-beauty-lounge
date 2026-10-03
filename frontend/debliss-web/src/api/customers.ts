import { request, toQueryString } from './client.ts'
import type {
  CustomerAppointmentItem,
  CustomerNote,
  CustomerProfile,
  CustomerStatusFilter,
  CustomerSummary,
  PagedResult,
  SaveCustomerNoteRequest,
  SaveCustomerRequest,
} from './contracts.ts'

/** Everything the customer list can be narrowed by. Omitted keys are not sent. */
export interface CustomerSearch {
  search?: string
  status?: CustomerStatusFilter
  page?: number
  pageSize?: number
}

export function searchCustomers(search: CustomerSearch): Promise<PagedResult<CustomerSummary>> {
  return request(
    'GET',
    `/customers${toQueryString({
      search: search.search,
      status: search.status,
      page: search.page,
      pageSize: search.pageSize,
    })}`,
  )
}

export function getCustomer(id: string): Promise<CustomerProfile> {
  return request('GET', `/customers/${id}`)
}

export function createCustomer(body: SaveCustomerRequest): Promise<CustomerProfile> {
  return request('POST', '/customers', body)
}

export function updateCustomer(id: string, body: SaveCustomerRequest): Promise<CustomerProfile> {
  return request('PUT', `/customers/${id}`, body)
}

export function setCustomerStatus(id: string, isActive: boolean): Promise<CustomerProfile> {
  return request('PATCH', `/customers/${id}/status`, { isActive })
}

export function listCustomerNotes(customerId: string): Promise<CustomerNote[]> {
  return request('GET', `/customers/${customerId}/notes`)
}

export function createCustomerNote(
  customerId: string,
  body: SaveCustomerNoteRequest,
): Promise<CustomerNote> {
  return request('POST', `/customers/${customerId}/notes`, body)
}

export function updateCustomerNote(
  customerId: string,
  noteId: string,
  body: SaveCustomerNoteRequest,
): Promise<CustomerNote> {
  return request('PUT', `/customers/${customerId}/notes/${noteId}`, body)
}

/**
 * Past and upcoming appointments for the profile. The list is empty until the
 * appointment module starts writing bookings; nothing here invents rows.
 */
export function listCustomerAppointments(customerId: string): Promise<CustomerAppointmentItem[]> {
  return request('GET', `/customers/${customerId}/appointments`)
}
