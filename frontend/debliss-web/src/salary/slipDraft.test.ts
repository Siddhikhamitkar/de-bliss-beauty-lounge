import { describe, expect, it } from 'vitest'
import type { StaffSalaryDetails } from '../api/contracts.ts'
import { parseCents } from './money.ts'
import { daysInMonth, localDate, newSlipDraft, slipTotals, validateSlipDraft } from './slipDraft.ts'

const salary: StaffSalaryDetails = { totalSalary: null, basicSalary: null, otherAllowances: null, bankName: null, bankAccountNumber: null, ibanNumber: null }

describe('salary decimal and draft validation', () => {
  it.each(['-1', '1e3', '1E3', 'NaN', 'Infinity', '1.234', '1,000', '.', '+1', '1000000000', '999999999.999', '', ' '])('rejects invalid amount %j rather than coercing it to zero', raw => {
    expect(parseCents(raw)).toBeUndefined()
  })

  it('keeps exact cents, the upper bound and explicit zero', () => {
    expect(parseCents('0')).toBe(0)
    expect(parseCents('0001.01')).toBe(101)
    expect(parseCents('999999999.99')).toBe(99_999_999_999)
    const draft = { ...newSlipDraft(salary), basicSalary: '0.1', otherAllowance: '0.2', otherDeductions: '0.3' }
    expect(slipTotals(draft)).toEqual({ earnings: 30, deductions: 30, net: 0 })
    expect(validateSlipDraft('s1', draft).request).not.toHaveProperty('netSalary')
  })

  it('uses local calendar defaults and no sample identifiers', () => {
    const now = new Date(2026, 0, 1, 0, 5)
    const draft = newSlipDraft(salary, now)
    expect(localDate(now)).toBe('2026-01-01')
    expect(draft).toMatchObject({ salaryYear: '2026', salaryMonth: '1', payDate: '2026-01-01', employeeId: '', basicSalary: '0', houseRentAllowance: '0', workingDays: '0', lateMarks: '0' })
    expect(validateSlipDraft('s1', draft).request?.employeeId).toBeNull()
  })

  it('prefills only salary components, not the agreed reference or a bank-derived ID', () => {
    expect(newSlipDraft({ ...salary, totalSalary: 5000, basicSalary: 0, otherAllowances: 300 })).toMatchObject({ basicSalary: '0', otherAllowance: '300', houseRentAllowance: '0', employeeId: '' })
  })

  it.each([
    ['salaryMonth', '13'], ['salaryYear', '2e3'], ['salaryYear', '0000'], ['salaryYear', '1999'], ['salaryYear', '2101'],
    ['payDate', '2026-02-30'], ['payDate', '1999-12-31'], ['payDate', '2102-01-01'],
    ['workingDays', '28.1'], ['totalLeave', '-1'], ['totalLeave', '1.01'], ['lateMarks', '0.5'], ['lateMarks', '29'],
    ['commission', ''], ['leaveDeduction', '1e1'], ['otherDeductions', '-1'],
  ])('blocks invalid %s=%s before generating', (field, value) => {
    const draft = { ...newSlipDraft(salary), salaryMonth: '2', salaryYear: '2026', [field]: value }
    const result = validateSlipDraft('s1', draft)
    expect(result.request).toBeUndefined()
    expect(result.errors[field]).toBeDefined()
  })

  it('uses calendar month limits, supports tenths and does not infer deductions', () => {
    expect(daysInMonth(2028, 2)).toBe(29)
    expect(daysInMonth(2100, 2)).toBe(28)
    const draft = { ...newSlipDraft(salary), salaryMonth: '2', salaryYear: '2028', workingDays: '29', totalLeave: '1.5', leaveWithoutPay: '1.5', lateMarks: '2' }
    expect(validateSlipDraft('s1', draft).request).toMatchObject({ workingDays: 29, totalLeave: 1.5, leaveWithoutPay: 1.5, leaveDeduction: 0 })
  })

  it('rejects leave without pay over total leave and negative net', () => {
    const result = validateSlipDraft('s1', { ...newSlipDraft(salary), totalLeave: '1', leaveWithoutPay: '2', basicSalary: '10', leaveDeduction: '10.01' })
    expect(result.request).toBeUndefined()
    expect(result.errors).toHaveProperty('leaveWithoutPay')
    expect(result.errors).toHaveProperty('netSalary')
  })
})
