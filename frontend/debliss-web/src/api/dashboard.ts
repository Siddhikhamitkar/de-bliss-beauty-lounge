import { request } from './client.ts'
import type { ReceptionistDashboard } from './contracts.ts'

/**
 * The receptionist's read-only snapshot of today. One round trip keeps the
 * dashboard cheap to refresh; the endpoint carries appointment workload only,
 * never money.
 */
export function getReceptionistDashboard(): Promise<ReceptionistDashboard> {
  return request('GET', '/dashboard/receptionist')
}
