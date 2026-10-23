import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from './client.ts'
import { decodeSalaryPdf, getSalarySlip, getSalarySlipPdf, getStaffSalary, previewSalarySlip, saveSalarySlip, searchSalarySlips } from './salary.ts'
import type { SalarySlipRequest } from './contracts.ts'

const draft: SalarySlipRequest = {
  staffId: 's1', salaryMonth: 10, salaryYear: 2026, payDate: '2026-10-31', employeeId: null,
  workingDays: 0, totalLeave: 0, leaveWithoutPay: 0, lateMarks: 0,
  basicSalary: 0, houseRentAllowance: 0, otherAllowance: 0, commission: 0, leaveDeduction: 0, otherDeductions: 0,
}

afterEach(() => vi.unstubAllGlobals())

describe('salary API', () => {
  it('uses separate protected detail endpoints and token-only saving', async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(new Response('{}')))
    vi.stubGlobal('fetch', fetchMock)
    await getStaffSalary('staff/one')
    expect(fetchMock).toHaveBeenLastCalledWith('/api/staff/staff%2Fone/salary', expect.objectContaining({ method: 'GET', credentials: 'same-origin' }))
    await getSalarySlip('slip/one')
    expect(fetchMock).toHaveBeenLastCalledWith('/api/salary-slips/slip%2Fone', expect.objectContaining({ method: 'GET' }))
    await previewSalarySlip(draft)
    expect(fetchMock).toHaveBeenLastCalledWith('/api/salary-slips/preview', expect.objectContaining({ method: 'POST', body: JSON.stringify(draft) }))
    expect(fetchMock.mock.calls.filter(([path]) => path === '/api/salary-slips')).toHaveLength(0)
    await saveSalarySlip('protected-token')
    expect(fetchMock).toHaveBeenLastCalledWith('/api/salary-slips', expect.objectContaining({ method: 'POST', body: '{"previewToken":"protected-token"}' }))
  })

  it('preserves server paging metadata and sends exact history filters', async () => {
    const page = { items: [], totalCount: 42, page: 2, pageSize: 20, totalPages: 3, hasPreviousPage: true, hasNextPage: true }
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(page)))
    vi.stubGlobal('fetch', fetchMock)
    expect(await searchSalarySlips({ staffId: 's1', year: 2026, page: 2, pageSize: 20 })).toEqual(page)
    expect(fetchMock).toHaveBeenCalledWith('/api/salary-slips?staffId=s1&year=2026&page=2&pageSize=20', expect.objectContaining({ method: 'GET' }))
  })

  it('downloads PDF bytes through the shared blob client', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response('%PDF-1.7\nbody', { headers: { 'Content-Type': 'application/pdf' } }))
    vi.stubGlobal('fetch', fetchMock)
    const blob = await getSalarySlipPdf('p1')
    expect(blob.type).toBe('application/pdf')
    expect(await blob.text()).toContain('%PDF-1.7')
    expect(fetchMock).toHaveBeenCalledWith('/api/salary-slips/p1/pdf', { method: 'GET', credentials: 'same-origin', body: undefined })
  })

  it.each([400, 409])('preserves %s problem errors on save', async status => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ detail: 'Generate again.', errors: { previewToken: ['Invalid preview.'] } }), { status })))
    await expect(saveSalarySlip('old-token')).rejects.toMatchObject({ status, message: 'Generate again.', errors: { previewToken: ['Invalid preview.'] } })
  })

  it('handles PDF authorization failures as problems and signals expired sessions', async () => {
    const unauthorized = vi.fn()
    window.addEventListener('debliss:unauthorized', unauthorized)
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ detail: 'Sign in again.' }), { status: 401 })))
    try {
      await expect(getSalarySlipPdf('p1')).rejects.toBeInstanceOf(ApiError)
      expect(unauthorized).toHaveBeenCalledOnce()
    } finally { window.removeEventListener('debliss:unauthorized', unauthorized) }
  })

  it('decodes binary PDF content without a data URL and rejects malformed previews', () => {
    const blob = decodeSalaryPdf(btoa('%PDF-1.7\n\x00\xff'))
    expect(blob.type).toBe('application/pdf')
    expect(blob.size).toBe(11)
    expect(() => decodeSalaryPdf('not base64!')).toThrow('generate it again')
    expect(() => decodeSalaryPdf(btoa('<html>not a pdf</html>'))).toThrow('generate it again')
  })
})
