import { request, toQueryString } from './client.ts'
import type {
  Bundle,
  PagedResult,
  SaveBundleRequest,
  SaveServiceCategoryRequest,
  SaveServiceRequest,
  Service,
  ServiceCategory,
  ServiceStatusFilter,
  ServiceSummary,
  StaffOption,
} from './contracts.ts'

/** Everything the service list can be narrowed by. Omitted keys are not sent. */
export interface ServiceSearch {
  search?: string
  categoryId?: string
  status?: ServiceStatusFilter
  bookableOnly?: boolean
  page?: number
  pageSize?: number
}

export function searchServices(search: ServiceSearch): Promise<PagedResult<ServiceSummary>> {
  return request(
    'GET',
    `/services${toQueryString({
      search: search.search,
      categoryId: search.categoryId,
      status: search.status,
      bookableOnly: search.bookableOnly,
      page: search.page,
      pageSize: search.pageSize,
    })}`,
  )
}

/**
 * `searchServices` returns services and bundles interleaved, which is what the
 * menu wants but not what a picker of individual services wants: a bundle has
 * no staff of its own and cannot be a component of another bundle. Every
 * caller that offers services to assign, compose or book one at a time goes
 * through here. The page counts still describe the merged set, so this is only
 * correct for the single full-page lookups those pickers make.
 */
export function searchIndividualServices(
  search: ServiceSearch,
): Promise<PagedResult<ServiceSummary>> {
  return searchServices(search).then(page => ({
    ...page,
    items: page.items.filter(item => item.itemKind === 'Service'),
  }))
}

export function getService(id: string): Promise<Service> {
  return request('GET', `/services/${id}`)
}

export function createService(body: SaveServiceRequest): Promise<Service> {
  return request('POST', '/services', body)
}

export function updateService(id: string, body: SaveServiceRequest): Promise<Service> {
  return request('PUT', `/services/${id}`, body)
}

export function setServiceStatus(id: string, isActive: boolean): Promise<Service> {
  return request('PATCH', `/services/${id}/status`, { isActive })
}

/**
 * The bundles the booking flow may offer. Management screens list bundles
 * through `searchServices`, which merges them into the same page.
 */
export function listBookableBundles(): Promise<Bundle[]> {
  return request('GET', '/service-bundles/bookable')
}

export function getBundle(id: string): Promise<Bundle> {
  return request('GET', `/service-bundles/${id}`)
}

export function createBundle(body: SaveBundleRequest): Promise<Bundle> {
  return request('POST', '/service-bundles', body)
}

export function updateBundle(id: string, body: SaveBundleRequest): Promise<Bundle> {
  return request('PUT', `/service-bundles/${id}`, body)
}

export function setBundleStatus(id: string, isActive: boolean): Promise<Bundle> {
  return request('PATCH', `/service-bundles/${id}/status`, { isActive })
}

export function listCategories(status?: ServiceStatusFilter): Promise<ServiceCategory[]> {
  return request('GET', `/service-categories${toQueryString({ status })}`)
}

export function getServiceCategory(id: string): Promise<ServiceCategory> {
  return request('GET', `/service-categories/${id}`)
}

export function createCategory(body: SaveServiceCategoryRequest): Promise<ServiceCategory> {
  return request('POST', '/service-categories', body)
}

export function updateCategory(
  id: string,
  body: SaveServiceCategoryRequest,
): Promise<ServiceCategory> {
  return request('PUT', `/service-categories/${id}`, body)
}

export function setCategoryStatus(id: string, isActive: boolean): Promise<ServiceCategory> {
  return request('PATCH', `/service-categories/${id}/status`, { isActive })
}

/**
 * Inactive staff are included because a service may still be assigned to
 * someone who has since left. The form has to show that assignment; if the
 * lookup hid it, the next save would silently drop it.
 */
export function listStaffOptions(): Promise<StaffOption[]> {
  return request('GET', '/staff-members?includeInactive=true')
}
