import { request } from './client.ts'
import type { OwnerDashboard, ReceptionistDashboard } from './contracts.ts'

/**
 * The receptionist's read-only snapshot of today. One round trip keeps the
 * dashboard cheap to refresh; the endpoint carries appointment workload only,
 * never money.
 */
export function getReceptionistDashboard(): Promise<ReceptionistDashboard> {
  return request('GET', '/dashboard/receptionist')
}

export type OwnerDashboardRange =
  | 'Today'
  | 'Yesterday'
  | 'Last7Days'
  | 'ThisMonth'
  | 'LastMonth'
  | 'Custom'

/**
 * The owner's financial snapshot for a reporting range. Dates are salon-local
 * ISO days, accepted only together with the Custom range.
 */
export function getOwnerDashboard(
  range: OwnerDashboardRange,
  from?: string,
  to?: string,
): Promise<OwnerDashboard> {
  const params = new URLSearchParams({ range })

  if (from !== undefined) {
    params.set('from', from)
  }

  if (to !== undefined) {
    params.set('to', to)
  }

  return request('GET', `/dashboard/owner?${params.toString()}`)
}
