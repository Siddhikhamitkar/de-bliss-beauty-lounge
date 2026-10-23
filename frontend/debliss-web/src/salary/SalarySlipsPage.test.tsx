import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/client.ts'
import type { PagedResult, SalarySlipDto, SalarySlipSummaryDto, StaffSalaryDetails, StaffSummary } from '../api/contracts.ts'
import { getSalarySlip, getSalarySlipPdf, getStaffSalary, previewSalarySlip, saveSalarySlip, searchSalarySlips } from '../api/salary.ts'
import { searchStaff } from '../api/staff.ts'
import { SalarySlipsPage } from './SalarySlipsPage.tsx'
import { localDate } from './slipDraft.ts'

vi.mock('../api/staff.ts', () => ({ searchStaff: vi.fn() }))
vi.mock('../api/salary.ts', async importOriginal => ({
  ...await importOriginal<typeof import('../api/salary.ts')>(),
  getStaffSalary: vi.fn(), getSalarySlip: vi.fn(), getSalarySlipPdf: vi.fn(),
  previewSalarySlip: vi.fn(), saveSalarySlip: vi.fn(), searchSalarySlips: vi.fn(),
}))

const member: StaffSummary = { id: 's1', firstName: 'Test', lastName: 'Member', jobTitle: null, calendarColor: null, isActive: true, serviceCount: 0, sortOrder: 0, createdAt: '2026-10-01T00:00:00Z', updatedAt: null }
const salary: StaffSalaryDetails = { totalSalary: 2500, basicSalary: 2000, otherAllowances: 500, bankName: 'Test bank', bankAccountNumber: '0012345678', ibanNumber: '00 AE 12345678' }
const slip: SalarySlipDto = {
  id: 'p1', staffId: 's1', employeeName: 'Test Member', employeeId: null,
  salaryMonth: 10, salaryYear: 2026, payDate: '2026-10-31', workingDays: 0, totalLeave: 0, leaveWithoutPay: 0, lateMarks: 0,
  agreedTotalSalary: 2500, basicSalary: 2000, houseRentAllowance: 0, otherAllowance: 500, commission: 0,
  leaveDeduction: 0, otherDeductions: 0, totalEarnings: 2500, totalDeductions: 0, netSalary: 2500,
  bankName: salary.bankName, bankAccountNumber: salary.bankAccountNumber, ibanNumber: salary.ibanNumber, accountName: 'Test Member',
  createdAt: '2026-10-31T12:00:00Z', createdBy: 'owner-id', templateVersion: '1',
}
const pdfBase64 = btoa('%PDF-1.7\nTest PDF')
function paged<T>(items: T[], page = 1, totalCount = items.length, pageSize = 20): PagedResult<T> {
  return { items, totalCount, page, pageSize, totalPages: Math.ceil(totalCount / pageSize), hasPreviousPage: page > 1, hasNextPage: page * pageSize < totalCount }
}
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(done => { resolve = done })
  return { promise, resolve }
}
const createUrl = vi.fn()
const revokeUrl = vi.fn()

beforeEach(() => {
  vi.resetAllMocks()
  let urlNumber = 0
  createUrl.mockImplementation(() => `blob:salary-${++urlNumber}`)
  vi.stubGlobal('URL', class extends URL { static createObjectURL = createUrl; static revokeObjectURL = revokeUrl })
  vi.mocked(searchStaff).mockResolvedValue(paged([member, { ...member, id: 's2', firstName: 'Second' }]))
  vi.mocked(getStaffSalary).mockResolvedValue(salary)
  vi.mocked(previewSalarySlip).mockResolvedValue({ slip, pdfBase64, previewToken: 'token-one' })
  vi.mocked(saveSalarySlip).mockResolvedValue(slip)
  vi.mocked(getSalarySlip).mockResolvedValue(slip)
  vi.mocked(getSalarySlipPdf).mockResolvedValue(new Blob(['%PDF-1.7\nSaved text PDF'], { type: 'application/pdf' }))
  vi.mocked(searchSalarySlips).mockResolvedValue(paged<SalarySlipSummaryDto>([slip]))
})
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

async function selectEmployee(user: ReturnType<typeof userEvent.setup>, id = 's1') {
  await screen.findByRole('option', { name: 'Test Member' })
  await user.selectOptions(screen.getByLabelText('Employee'), id)
}
async function readyForm(user: ReturnType<typeof userEvent.setup>) {
  await selectEmployee(user)
  await screen.findByLabelText('Basic salary (AED)')
}
async function generate(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', { name: 'Generate PDF preview' }))
  await screen.findByRole('heading', { name: 'Review salary slip' })
}

describe('SalarySlipsPage', () => {
  it('prefills salary only after employee selection, keeps banks masked and uses local date defaults', async () => {
    const user = userEvent.setup()
    const storage = vi.spyOn(Storage.prototype, 'setItem')
    render(<SalarySlipsPage />)
    expect(getStaffSalary).not.toHaveBeenCalled()
    await readyForm(user)
    expect(getStaffSalary).toHaveBeenCalledWith('s1')
    expect(screen.getByLabelText('Basic salary (AED)')).toHaveValue('2000')
    expect(screen.getByLabelText('Other allowance (AED)')).toHaveValue('500')
    expect(screen.getByLabelText('House rent allowance (AED)')).toHaveValue('0')
    expect(screen.getByLabelText('Employee ID (optional)')).toHaveValue('')
    const now = new Date()
    expect(screen.getByLabelText('Salary month')).toHaveValue(String(now.getMonth() + 1))
    expect(screen.getByLabelText('Salary year')).toHaveValue(String(now.getFullYear()))
    expect(screen.getByLabelText('Pay date')).toHaveValue(localDate(now))
    expect(screen.getByLabelText('Working days')).toHaveValue('0')
    expect(screen.getByText('Test bank')).toBeInTheDocument()
    expect(screen.queryByText(salary.bankAccountNumber!)).not.toBeInTheDocument()
    expect(screen.queryByText(salary.ibanNumber!)).not.toBeInTheDocument()
    expect(storage).not.toHaveBeenCalled()
  })

  it('pages through more than 200 active staff and supports searching the whole list', async () => {
    const user = userEvent.setup()
    const first = Array.from({ length: 200 }, (_, index) => ({ ...member, id: `first-${index}`, firstName: `Person ${index}` }))
    vi.mocked(searchStaff).mockImplementation(async query => query.page === 1 ? paged(first, 1, 201, 200) : paged([{ ...member, id: 'last', firstName: 'Last' }], 2, 201, 200))
    render(<SalarySlipsPage />)
    await screen.findByRole('option', { name: 'Last Member' })
    expect(searchStaff).toHaveBeenNthCalledWith(2, { status: 'Active', page: 2, pageSize: 200 })
    await user.type(screen.getByLabelText('Find employee'), 'Last')
    expect(screen.queryByRole('option', { name: 'Person 0 Member' })).not.toBeInTheDocument()
    await user.selectOptions(screen.getByLabelText('Employee'), 'last')
    await waitFor(() => expect(getStaffSalary).toHaveBeenCalledWith('last'))
  })

  it('disables generation while loading and ignores a stale employee response', async () => {
    const user = userEvent.setup()
    const first = deferred<StaffSalaryDetails>()
    vi.mocked(getStaffSalary).mockImplementation(id => id === 's1' ? first.promise : Promise.resolve({ ...salary, basicSalary: 0, otherAllowances: null, bankName: 'Second bank' }))
    render(<SalarySlipsPage />)
    await selectEmployee(user)
    expect(screen.getByRole('button', { name: 'Generate PDF preview' })).toBeDisabled()
    await user.selectOptions(screen.getByLabelText('Employee'), 's2')
    expect(await screen.findByLabelText('Basic salary (AED)')).toHaveValue('0')
    await act(async () => first.resolve(salary))
    expect(screen.getByLabelText('Basic salary (AED)')).toHaveValue('0')
    expect(screen.getByLabelText('Other allowance (AED)')).toHaveValue('0')
    expect(screen.getByText('Second bank')).toBeInTheDocument()
  })

  it('blocks generation after failed prefill, with a working retry', async () => {
    const user = userEvent.setup()
    vi.mocked(getStaffSalary).mockRejectedValueOnce(new Error('Salary unavailable.'))
    render(<SalarySlipsPage />)
    await selectEmployee(user)
    expect(await screen.findByRole('alert')).toHaveTextContent('Salary unavailable.')
    expect(screen.getByRole('button', { name: 'Generate PDF preview' })).toBeDisabled()
    expect(previewSalarySlip).not.toHaveBeenCalled()
    await user.click(screen.getByRole('button', { name: 'Try again' }))
    expect(await screen.findByLabelText('Basic salary (AED)')).toHaveValue('2000')
  })

  it('validates attendance, period, decimal syntax and net before an API call', async () => {
    const user = userEvent.setup()
    render(<SalarySlipsPage />)
    await readyForm(user)
    fireEvent.change(screen.getByLabelText('Salary month'), { target: { value: '2' } })
    fireEvent.change(screen.getByLabelText('Salary year'), { target: { value: '2026' } })
    fireEvent.change(screen.getByLabelText('Working days'), { target: { value: '29' } })
    fireEvent.change(screen.getByLabelText('Leave without pay'), { target: { value: '1' } })
    fireEvent.change(screen.getByLabelText('Late marks'), { target: { value: '0.5' } })
    fireEvent.change(screen.getByLabelText('Commission (AED)'), { target: { value: '1e2' } })
    await user.click(screen.getByRole('button', { name: 'Generate PDF preview' }))
    expect(screen.getByText(/Working days must be from 0 to 28/)).toBeInTheDocument()
    expect(screen.getByText(/Leave without pay cannot exceed/)).toBeInTheDocument()
    expect(screen.getByText(/Late marks must be from 0 to 28 as a whole number/)).toBeInTheDocument()
    expect(screen.getByText(/Enter an AED amount/)).toBeInTheDocument()
    expect(previewSalarySlip).not.toHaveBeenCalled()
    fireEvent.change(screen.getByLabelText(/^Commission \(AED\)/), { target: { value: '0' } })
    fireEvent.change(screen.getByLabelText('Other deductions (AED)'), { target: { value: '2500.01' } })
    await user.click(screen.getByRole('button', { name: 'Generate PDF preview' }))
    expect(screen.getByText(/Net salary must not be negative/)).toBeInTheDocument()
  })

  it('generates an actual downloadable PDF without saving and invalidates the token on edit', async () => {
    const user = userEvent.setup()
    const view = render(<SalarySlipsPage />)
    await readyForm(user)
    await user.type(screen.getByLabelText('Employee ID (optional)'), '001 A')
    await generate(user)
    expect(previewSalarySlip).toHaveBeenCalledWith(expect.objectContaining({ staffId: 's1', employeeId: '001 A', workingDays: 0, leaveDeduction: 0 }))
    expect(saveSalarySlip).not.toHaveBeenCalled()
    expect(screen.getByTitle('Salary slip PDF preview')).toHaveAttribute('src', 'blob:salary-1')
    expect(screen.getByRole('link', { name: 'Download preview PDF' })).toHaveAttribute('href', 'blob:salary-1')
    expect(within(screen.getByRole('region', { name: 'Salary slip details' })).getByText('Test bank')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Back to edit' }))
    expect(revokeUrl).toHaveBeenCalledWith('blob:salary-1')
    expect(screen.queryByRole('button', { name: 'Save salary slip' })).not.toBeInTheDocument()
    expect(screen.getByLabelText('Employee ID (optional)')).toHaveValue('001 A')
    fireEvent.change(screen.getByLabelText('Commission (AED)'), { target: { value: '20' } })
    vi.mocked(previewSalarySlip).mockResolvedValue({ slip, pdfBase64, previewToken: 'token-two' })
    await generate(user)
    expect(previewSalarySlip).toHaveBeenLastCalledWith(expect.objectContaining({ commission: 20 }))
    await user.click(screen.getByRole('button', { name: 'Save salary slip' }))
    expect(saveSalarySlip).toHaveBeenCalledExactlyOnceWith('token-two')
    expect(await screen.findByRole('heading', { name: 'Saved salary slip' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Download PDF' })).toHaveAttribute('download', 'salary-slip-2026-10.pdf')
    expect(screen.queryByRole('button', { name: 'Back to edit' })).not.toBeInTheDocument()
    expect(getSalarySlipPdf).toHaveBeenCalledWith('p1')
    view.unmount()
    expect(revokeUrl).toHaveBeenCalledWith('blob:salary-3')
  })

  it('sends only one token while saving and disables back, employee and page actions', async () => {
    const user = userEvent.setup()
    const saving = deferred<SalarySlipDto>()
    vi.mocked(saveSalarySlip).mockReturnValue(saving.promise)
    render(<SalarySlipsPage />)
    await readyForm(user)
    await generate(user)
    await user.dblClick(screen.getByRole('button', { name: 'Save salary slip' }))
    expect(saveSalarySlip).toHaveBeenCalledExactlyOnceWith('token-one')
    for (const name of ['Back to edit', 'History', 'New salary slip', 'Saving…']) expect(screen.getByRole('button', { name })).toBeDisabled()
    expect(screen.getByLabelText('Employee')).toBeDisabled()
    await act(async () => saving.resolve(slip))
    expect(await screen.findByRole('heading', { name: 'Saved salary slip' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'New salary slip' }))
    expect(await screen.findByLabelText('Employee')).toHaveValue('')
    expect(screen.queryByRole('link', { name: 'Download PDF' })).not.toBeInTheDocument()
  })

  it.each([400, 409])('shows %s expired/duplicate save problems and lets the owner edit and regenerate', async status => {
    const user = userEvent.setup()
    vi.mocked(saveSalarySlip).mockRejectedValue(new ApiError(status, { detail: status === 409 ? 'A slip already exists for this period.' : 'Preview has expired.' }))
    render(<SalarySlipsPage />)
    await readyForm(user)
    await generate(user)
    await user.click(screen.getByRole('button', { name: 'Save salary slip' }))
    expect(await screen.findByRole('alert')).toHaveTextContent(status === 409 ? 'A slip already exists' : 'Preview has expired')
    expect(screen.getByRole('button', { name: 'Save salary slip' })).toBeDisabled()
    await user.click(screen.getByRole('button', { name: 'Back to edit' }))
    await generate(user)
    expect(previewSalarySlip).toHaveBeenCalledTimes(2)
    expect(screen.getByRole('button', { name: 'Save salary slip' })).toBeEnabled()
  })

  it('shows preview API field errors without losing the editable draft', async () => {
    const user = userEvent.setup()
    vi.mocked(previewSalarySlip).mockRejectedValue(new ApiError(400, { detail: 'Review the amounts.', errors: { basicSalary: ['Basic salary rejected.'] } }))
    render(<SalarySlipsPage />)
    await readyForm(user)
    await user.click(screen.getByRole('button', { name: 'Generate PDF preview' }))
    expect(await screen.findByText('Basic salary rejected.')).toBeInTheDocument()
    expect(screen.getByText('Review the amounts.')).toBeInTheDocument()
    expect(screen.getByLabelText(/^Basic salary/)).toHaveValue('2000')
    expect(screen.getByLabelText('Employee')).toBeEnabled()
    expect(saveSalarySlip).not.toHaveBeenCalled()
  })

  it('filters and pages history, reopens an immutable snapshot and redownloads its PDF', async () => {
    const user = userEvent.setup()
    vi.mocked(searchSalarySlips).mockImplementation(async query => paged([{ ...slip, id: query.page === 2 ? 'p2' : 'p1' }], query.page, 21))
    render(<SalarySlipsPage />)
    await user.click(screen.getByRole('button', { name: 'History' }))
    await screen.findByRole('button', { name: /^Open salary slip/ })
    await screen.findByRole('option', { name: 'Test Member' })
    await user.selectOptions(screen.getByLabelText('Filter by employee'), 's1')
    await user.type(screen.getByLabelText('Filter by year'), '2026')
    await user.click(screen.getByRole('button', { name: 'Apply filters' }))
    await waitFor(() => expect(searchSalarySlips).toHaveBeenLastCalledWith({ staffId: 's1', year: 2026, page: 1, pageSize: 20 }))
    await user.click(await screen.findByRole('button', { name: 'Next' }))
    await waitFor(() => expect(searchSalarySlips).toHaveBeenLastCalledWith({ staffId: 's1', year: 2026, page: 2, pageSize: 20 }))
    await user.click(await screen.findByRole('button', { name: /^Open salary slip/ }))
    expect(await screen.findByRole('heading', { name: 'Saved salary slip' })).toBeInTheDocument()
    expect(getSalarySlip).toHaveBeenCalledWith('p2')
    expect(getSalarySlipPdf).toHaveBeenCalledWith('p2')
    expect(screen.getByRole('link', { name: 'Download PDF' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Save salary slip' })).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Basic salary (AED)')).not.toBeInTheDocument()
  })

  it('keeps saved text available when PDF download fails and retries the PDF', async () => {
    const user = userEvent.setup()
    vi.mocked(getSalarySlipPdf).mockRejectedValueOnce(new ApiError(500, { detail: 'PDF unavailable.' }))
    render(<SalarySlipsPage />)
    await user.click(screen.getByRole('button', { name: 'History' }))
    await user.click(await screen.findByRole('button', { name: /^Open salary slip/ }))
    expect(await screen.findByRole('alert')).toHaveTextContent('PDF unavailable.')
    expect(screen.getByRole('region', { name: 'Salary slip details' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Try again' }))
    expect(await screen.findByRole('link', { name: 'Download PDF' })).toBeInTheDocument()
  })

  it('shows history errors and empty states without an unusable record action', async () => {
    const user = userEvent.setup()
    vi.mocked(searchSalarySlips).mockRejectedValueOnce(new Error('History unavailable.')).mockResolvedValue(paged([]))
    render(<SalarySlipsPage />)
    await user.click(screen.getByRole('button', { name: 'History' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('History unavailable.')
    await user.click(screen.getByRole('button', { name: 'Try again' }))
    expect(await screen.findByText('No saved salary slips match these filters.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Open salary slip/ })).not.toBeInTheDocument()
  })
})
