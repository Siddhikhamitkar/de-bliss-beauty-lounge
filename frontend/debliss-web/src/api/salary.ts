import { request, requestBlob, toQueryString } from './client.ts'
import type {
  PagedResult, SalarySlipDto, SalarySlipPreview, SalarySlipRequest,
  SalarySlipSummaryDto, StaffSalaryDetails,
} from './contracts.ts'

export function getStaffSalary(staffId: string): Promise<StaffSalaryDetails> {
  return request('GET', `/staff/${encodeURIComponent(staffId)}/salary`)
}

export interface SalarySlipSearch {
  staffId?: string
  year?: number
  page?: number
  pageSize?: number
}

export function searchSalarySlips(search: SalarySlipSearch): Promise<PagedResult<SalarySlipSummaryDto>> {
  return request('GET', `/salary-slips${toQueryString({ ...search })}`)
}

export function getSalarySlip(id: string): Promise<SalarySlipDto> {
  return request('GET', `/salary-slips/${encodeURIComponent(id)}`)
}

export function previewSalarySlip(body: SalarySlipRequest): Promise<SalarySlipPreview> {
  return request('POST', '/salary-slips/preview', body)
}

/** Save only the protected preview token, never a browser-editable snapshot. */
export function saveSalarySlip(previewToken: string): Promise<SalarySlipDto> {
  return request('POST', '/salary-slips', { previewToken })
}

export function getSalarySlipPdf(id: string): Promise<Blob> {
  return requestBlob('GET', `/salary-slips/${encodeURIComponent(id)}/pdf`)
}

export function decodeSalaryPdf(base64: string): Blob {
  try {
    const binary = atob(base64)
    if (!binary.startsWith('%PDF-')) throw new Error('Not a PDF')
    const bytes = new Uint8Array(binary.length)
    for (let index = 0; index < binary.length; index += 1) bytes[index] = binary.charCodeAt(index)
    return new Blob([bytes], { type: 'application/pdf' })
  } catch {
    throw new Error('The PDF preview could not be read. Please generate it again.')
  }
}
