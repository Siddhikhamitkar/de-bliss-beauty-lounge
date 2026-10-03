/**
 * The JSON that crosses the wire, mirrored field for field from the records in
 * backend/DeBliss.Application/ServiceCatalogue/Dtos. Names are camelCase
 * because that is ASP.NET Core's default serializer policy.
 *
 * These describe what the API sends; the `Save*` types below are the only thing
 * this app ever posts back.
 */

/** How a list is narrowed by record state. Sent by name, as the API binds it. */
export type ServiceStatusFilter = 'All' | 'Active' | 'Inactive'

/** One page of a larger result set, with enough metadata to render a pager. */
export interface PagedResult<T> {
  items: T[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
  hasPreviousPage: boolean
  hasNextPage: boolean
}

/** A row in the service list: enough to recognise and triage a service. */
export interface ServiceSummary {
  id: string
  name: string
  categoryId: string | null
  categoryName: string | null
  durationMinutes: number
  price: number
  isActive: boolean
  isOnlineBookable: boolean
  /**
   * Active, in an active category, with at least one active staff member able
   * to perform it. The API computes this; the UI only reports it.
   */
  isBookable: boolean
  eligibleStaffCount: number
  sortOrder: number
  createdAt: string
  updatedAt: string | null
}

/** A staff member already attached to a service. */
export interface EligibleStaff {
  staffId: string
  firstName: string
  lastName: string
  jobTitle: string | null
  isActive: boolean
}

/** The full record behind one service, including who may perform it. */
export interface Service {
  id: string
  name: string
  description: string | null
  categoryId: string | null
  categoryName: string | null
  isCategoryActive: boolean
  durationMinutes: number
  price: number
  isActive: boolean
  isOnlineBookable: boolean
  isBookable: boolean
  sortOrder: number
  eligibleStaff: EligibleStaff[]
  createdAt: string
  updatedAt: string | null
}

/** A service menu category. */
export interface ServiceCategory {
  id: string
  name: string
  description: string | null
  sortOrder: number
  isActive: boolean
  serviceCount: number
  createdAt: string
  updatedAt: string | null
}

/** A staff member as offered by the eligible-staff selector. */
export interface StaffOption {
  id: string
  firstName: string
  lastName: string
  jobTitle: string | null
  isActive: boolean
  displayName: string
}

/**
 * Payload for creating and editing a service. `eligibleStaffIds` replaces the
 * whole assignment set, which is exactly what the form submits.
 */
export interface SaveServiceRequest {
  name: string | null
  categoryId: string | null
  description: string | null
  durationMinutes: number
  price: number
  isActive: boolean
  isOnlineBookable: boolean
  sortOrder: number
  eligibleStaffIds: string[]
}

/** Payload for creating and editing a category. */
export interface SaveServiceCategoryRequest {
  name: string | null
  description: string | null
  sortOrder: number
  isActive: boolean
}

/**
 * An RFC 9457 problem document. `errors` appears only on a 400 from a save
 * endpoint and is keyed by the request field each message belongs to.
 */
export interface Problem {
  type?: string | null
  title?: string | null
  status?: number | null
  detail?: string | null
  instance?: string | null
  errors?: Record<string, string[]>
}
