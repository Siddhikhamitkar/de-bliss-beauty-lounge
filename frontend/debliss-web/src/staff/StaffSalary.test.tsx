import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/client.ts'
import type { StaffProfile, StaffSalaryDetails } from '../api/contracts.ts'
import { getStaffSalary } from '../api/salary.ts'
import { createStaff, getStaff, searchStaff, updateStaff } from '../api/staff.ts'
import { StaffEditor } from './StaffForm.tsx'
import { StaffPage } from './StaffPage.tsx'

vi.mock('../api/staff.ts', () => ({ getStaff: vi.fn(), createStaff: vi.fn(), updateStaff: vi.fn(), searchStaff: vi.fn() }))
vi.mock('../api/salary.ts', () => ({ getStaffSalary: vi.fn() }))

const profile: StaffProfile = {
  id: 's1', firstName: 'Test', lastName: 'Member', email: null, phoneNumber: null, jobTitle: null,
  calendarColor: null, isActive: true, sortOrder: 0, services: [], createdAt: '2026-10-01T00:00:00Z', updatedAt: null,
}
const unset: StaffSalaryDetails = { totalSalary: null, basicSalary: null, otherAllowances: null, ibanNumber: null, bankName: null, bankAccountNumber: null }
const bank: StaffSalaryDetails = { totalSalary: 1000, basicSalary: 800, otherAllowances: 200, ibanNumber: '00 AE-12 3456', bankName: 'Test bank', bankAccountNumber: '0012-34 0056' }

beforeEach(() => {
  vi.resetAllMocks()
  vi.mocked(getStaff).mockResolvedValue(profile)
  vi.mocked(getStaffSalary).mockResolvedValue(bank)
  vi.mocked(createStaff).mockResolvedValue(profile)
  vi.mocked(updateStaff).mockResolvedValue(profile)
  vi.mocked(searchStaff).mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 50, totalPages: 0, hasNextPage: false, hasPreviousPage: false })
})

describe('owner staff salary editing', () => {
  it('passes the owner capability from StaffPage and starts all six fields blank', async () => {
    const user = userEvent.setup()
    render(<StaffPage canManageSalary />)
    await user.click(screen.getByRole('button', { name: 'New staff member' }))
    expect(await screen.findByLabelText('Agreed total salary (AED)')).toHaveValue('')
    for (const label of ['Basic salary (AED)', 'Other allowances (AED)', 'IBAN', 'Bank name', 'Bank account number']) expect(screen.getByLabelText(label)).toHaveValue('')
    await user.type(screen.getByLabelText('First name'), 'Test')
    await user.type(screen.getByLabelText('Last name'), 'Member')
    await user.click(screen.getByRole('button', { name: 'Save staff member' }))
    await waitFor(() => expect(createStaff).toHaveBeenCalledWith(expect.objectContaining({ salary: unset })))
    expect(getStaffSalary).not.toHaveBeenCalled()
  })

  it('loads separate finance, preserves identifier formatting, and reloads explicit zeros', async () => {
    const user = userEvent.setup()
    let savedSalary = bank
    vi.mocked(getStaffSalary).mockImplementation(async () => savedSalary)
    vi.mocked(updateStaff).mockImplementation(async (_id, request) => { savedSalary = request.salary!; return profile })
    const onSaved = vi.fn()
    const view = render(<StaffEditor staffId="s1" canManageSalary onSaved={onSaved} onCancel={() => {}} />)
    expect(await screen.findByLabelText('Agreed total salary (AED)')).toHaveValue('1000')
    expect(getStaff).toHaveBeenCalledWith('s1')
    expect(getStaffSalary).toHaveBeenCalledWith('s1')
    expect(screen.getByLabelText('Bank account number')).toHaveValue('0012-34 0056')
    for (const label of ['Agreed total salary (AED)', 'Basic salary (AED)', 'Other allowances (AED)']) fireEvent.change(screen.getByLabelText(label), { target: { value: '0' } })
    await user.click(screen.getByRole('button', { name: 'Save staff member' }))
    await waitFor(() => expect(onSaved).toHaveBeenCalledOnce())
    expect(updateStaff).toHaveBeenCalledWith('s1', expect.objectContaining({ salary: { ...bank, totalSalary: 0, basicSalary: 0, otherAllowances: 0 } }))
    view.unmount()
    render(<StaffEditor staffId="s1" canManageSalary onSaved={() => {}} onCancel={() => {}} />)
    expect(await screen.findByLabelText('Agreed total salary (AED)')).toHaveValue('0')
    expect(screen.getByLabelText('Basic salary (AED)')).toHaveValue('0')
    expect(screen.getByLabelText('IBAN')).toHaveValue(bank.ibanNumber)
  })

  it('compares decimal components in cents and sends cleared amounts as null', async () => {
    const user = userEvent.setup()
    vi.mocked(getStaffSalary).mockResolvedValue({ ...bank, totalSalary: 0.3, basicSalary: 0.1, otherAllowances: 0.2 })
    render(<StaffEditor staffId="s1" canManageSalary onSaved={() => {}} onCancel={() => {}} />)
    await screen.findByLabelText('Agreed total salary (AED)')
    expect(screen.queryByText(/do not add up/)).not.toBeInTheDocument()
    await user.clear(screen.getByLabelText('Agreed total salary (AED)'))
    await user.clear(screen.getByLabelText('Basic salary (AED)'))
    await user.clear(screen.getByLabelText('Other allowances (AED)'))
    await user.click(screen.getByRole('button', { name: 'Save staff member' }))
    await waitFor(() => expect(updateStaff).toHaveBeenCalledWith('s1', expect.objectContaining({ salary: { ...bank, totalSalary: null, basicSalary: null, otherAllowances: null } })))
  })

  it('warns without overwriting the agreed total and treats null components as zero', async () => {
    const user = userEvent.setup()
    vi.mocked(getStaffSalary).mockResolvedValue({ ...unset, totalSalary: 1000 })
    render(<StaffEditor staffId="s1" canManageSalary onSaved={() => {}} onCancel={() => {}} />)
    expect(await screen.findByText(/do not add up to the agreed total/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Save staff member' }))
    await waitFor(() => expect(updateStaff).toHaveBeenCalledWith('s1', expect.objectContaining({ salary: { ...unset, totalSalary: 1000 } })))
  })

  it.each(['-1', '1e3', 'wrong', '1.001', '1000000000'])('blocks invalid owner salary %s', async value => {
    const user = userEvent.setup()
    render(<StaffEditor staffId="s1" canManageSalary onSaved={() => {}} onCancel={() => {}} />)
    fireEvent.change(await screen.findByLabelText('Agreed total salary (AED)'), { target: { value } })
    await user.click(screen.getByRole('button', { name: 'Save staff member' }))
    expect(await screen.findByText(/Enter an AED amount/)).toBeInTheDocument()
    expect(updateStaff).not.toHaveBeenCalled()
  })

  it('shows dotted backend field errors and exact text limits', async () => {
    const user = userEvent.setup()
    vi.mocked(updateStaff).mockRejectedValue(new ApiError(400, { errors: { 'salary.totalSalary': ['Salary rejected.'], 'salary.ibanNumber': ['IBAN rejected.'] } }))
    render(<StaffEditor staffId="s1" canManageSalary onSaved={() => {}} onCancel={() => {}} />)
    expect(await screen.findByLabelText('IBAN')).toHaveAttribute('maxlength', '50')
    expect(screen.getByLabelText('Bank name')).toHaveAttribute('maxlength', '200')
    expect(screen.getByLabelText('Bank account number')).toHaveAttribute('maxlength', '100')
    await user.click(screen.getByRole('button', { name: 'Save staff member' }))
    expect(await screen.findByText('Salary rejected.')).toBeInTheDocument()
    expect(screen.getByText('IBAN rejected.')).toBeInTheDocument()
  })

  it('does not allow editing with failed protected finance loading', async () => {
    vi.mocked(getStaffSalary).mockRejectedValue(new ApiError(403, { detail: 'Owner access required.' }))
    render(<StaffEditor staffId="s1" canManageSalary onSaved={() => {}} onCancel={() => {}} />)
    expect(await screen.findByRole('alert')).toHaveTextContent('Owner access required.')
    expect(screen.queryByRole('button', { name: 'Save staff member' })).not.toBeInTheDocument()
  })

  it('never loads, displays, or submits financial data for operational editors', async () => {
    const user = userEvent.setup()
    render(<StaffEditor staffId="s1" onSaved={() => {}} onCancel={() => {}} />)
    await screen.findByLabelText('First name')
    expect(screen.queryByLabelText('IBAN')).not.toBeInTheDocument()
    expect(screen.queryByText(/Salary & bank details/)).not.toBeInTheDocument()
    await user.type(screen.getByLabelText('Role'), 'Stylist')
    await user.click(screen.getByRole('button', { name: 'Save staff member' }))
    await waitFor(() => expect(updateStaff).toHaveBeenCalledOnce())
    expect(getStaffSalary).not.toHaveBeenCalled()
    expect(vi.mocked(updateStaff).mock.calls[0][1]).not.toHaveProperty('salary')
  })
})
