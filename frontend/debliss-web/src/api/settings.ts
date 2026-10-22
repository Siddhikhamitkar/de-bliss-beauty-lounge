import { request } from './client.ts'
import type { SaveTaxSettingsRequest, TaxSettings } from './contracts.ts'

/** The salon's tax configuration; every role reads it, only the owner writes it. */
export function getTaxSettings(): Promise<TaxSettings> {
  return request('GET', '/settings/tax')
}

/** Saves the tax configuration; the API rejects non-owner sessions with 403. */
export function updateTaxSettings(body: SaveTaxSettingsRequest): Promise<TaxSettings> {
  return request('PUT', '/settings/tax', body)
}
