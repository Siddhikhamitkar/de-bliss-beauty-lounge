import { request, toQueryString } from './client.ts'
import type { CustomerLoyaltySummary, LoyaltySettings, LoyaltyTransaction, PagedResult } from './contracts.ts'

export function getLoyaltySettings(): Promise<LoyaltySettings> {
  return request('GET', '/loyalty/settings')
}

export function updateLoyaltySettings(body: LoyaltySettings): Promise<LoyaltySettings> {
  return request('PUT', '/loyalty/settings', body)
}

export function getCustomerLoyalty(customerId: string): Promise<CustomerLoyaltySummary> {
  return request('GET', `/customers/${customerId}/loyalty`)
}

export function getLoyaltyTransactions(customerId: string, page = 1, pageSize = 50): Promise<PagedResult<LoyaltyTransaction>> {
  return request('GET', `/customers/${customerId}/loyalty/transactions${toQueryString({ page, pageSize })}`)
}
