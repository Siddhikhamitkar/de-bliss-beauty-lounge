import { request, toQueryString } from './client.ts'
import type {
  AdjustStockRequest,
  InventoryProduct,
  InventoryProductOption,
  InventoryProductStatusFilter,
  InventoryStockStatusFilter,
  InventoryTypeFilter,
  PagedResult,
  ReceiveStockRequest,
  RecordUsageRequest,
  ReverseStockMovementRequest,
  SaveProductRequest,
  StockMovement,
  StockMovementTypeFilter,
} from './contracts.ts'

/** Everything the inventory list can be narrowed by. Omitted keys are not sent. */
export interface InventorySearch {
  search?: string
  type?: InventoryTypeFilter
  status?: InventoryProductStatusFilter
  stockStatus?: InventoryStockStatusFilter
  category?: string
  page?: number
  pageSize?: number
}

export function searchInventoryProducts(
  search: InventorySearch,
): Promise<PagedResult<InventoryProduct>> {
  return request(
    'GET',
    `/inventory${toQueryString({
      search: search.search,
      type: search.type,
      status: search.status,
      stockStatus: search.stockStatus,
      category: search.category,
      page: search.page,
      pageSize: search.pageSize,
    })}`,
  )
}

export function getInventoryProduct(id: string): Promise<InventoryProduct> {
  return request('GET', `/inventory/${id}`)
}

/** The active retail catalogue for the checkout screen's product picker. */
export function getSellableProducts(search?: string): Promise<InventoryProductOption[]> {
  return request('GET', `/inventory/sellable${toQueryString({ search })}`)
}

export function createProduct(body: SaveProductRequest): Promise<InventoryProduct> {
  return request('POST', '/inventory', body)
}

export function updateProduct(id: string, body: SaveProductRequest): Promise<InventoryProduct> {
  return request('PUT', `/inventory/${id}`, body)
}

export function setProductStatus(id: string, isActive: boolean): Promise<InventoryProduct> {
  return request('PATCH', `/inventory/${id}/status`, { isActive })
}

export function receiveStock(id: string, body: ReceiveStockRequest): Promise<StockMovement> {
  return request('POST', `/inventory/${id}/stock`, body)
}

export function recordUsage(id: string, body: RecordUsageRequest): Promise<StockMovement> {
  return request('POST', `/inventory/${id}/usage`, body)
}

export function adjustStock(id: string, body: AdjustStockRequest): Promise<StockMovement> {
  return request('POST', `/inventory/${id}/adjustments`, body)
}

export function reverseStockMovement(
  productId: string,
  movementId: string,
  body: ReverseStockMovementRequest,
): Promise<StockMovement> {
  return request('POST', `/inventory/${productId}/movements/${movementId}/reversal`, body)
}

/** Everything a product's movement history can be narrowed by. */
export interface StockMovementSearch {
  type?: StockMovementTypeFilter
  page?: number
  pageSize?: number
}

export function searchStockMovements(
  productId: string,
  search: StockMovementSearch,
): Promise<PagedResult<StockMovement>> {
  return request(
    'GET',
    `/inventory/${productId}/movements${toQueryString({
      type: search.type,
      page: search.page,
      pageSize: search.pageSize,
    })}`,
  )
}
