import type { SalarySlipRequest, StaffSalaryDetails } from '../api/contracts.ts'
import { MONEY_ERROR, parseCents } from './money.ts'

export const earningFields = [
  ['basicSalary', 'Basic salary (AED)'],
  ['houseRentAllowance', 'House rent allowance (AED)'],
  ['otherAllowance', 'Other allowance (AED)'],
  ['commission', 'Commission (AED)'],
] as const
export const deductionFields = [
  ['leaveDeduction', 'Leave deduction (AED)'],
  ['otherDeductions', 'Other deductions (AED)'],
] as const
export const attendanceFields = [
  ['workingDays', 'Working days'], ['totalLeave', 'Total leave'],
  ['leaveWithoutPay', 'Leave without pay'], ['lateMarks', 'Late marks'],
] as const
export const months = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December']
export type SlipDraft = Record<Exclude<keyof SalarySlipRequest, 'staffId'>, string>

export function localDate(now: Date): string {
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`
}

export function newSlipDraft(salary: StaffSalaryDetails, now = new Date()): SlipDraft {
  return {
    salaryMonth: String(now.getMonth() + 1), salaryYear: String(now.getFullYear()), payDate: localDate(now),
    employeeId: '', workingDays: '0', totalLeave: '0', leaveWithoutPay: '0', lateMarks: '0',
    basicSalary: String(salary.basicSalary ?? 0), houseRentAllowance: '0',
    otherAllowance: String(salary.otherAllowances ?? 0), commission: '0', leaveDeduction: '0', otherDeductions: '0',
  }
}

export function daysInMonth(year: number, month: number): number {
  if (month === 2) return year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0) ? 29 : 28
  return [4, 6, 9, 11].includes(month) ? 30 : 31
}

// Match the salary API's supported salary years (pay dates may extend into 2101).
export function validYear(raw: string): boolean {
  return /^\d{4}$/.test(raw) && Number(raw) >= 2000 && Number(raw) <= 2100
}

export function slipTotals(draft: SlipDraft): { earnings: number; deductions: number; net: number } | undefined {
  const earnings = earningFields.map(([field]) => parseCents(draft[field]))
  const deductions = deductionFields.map(([field]) => parseCents(draft[field]))
  if (earnings.some(value => value === undefined) || deductions.some(value => value === undefined)) return undefined
  const totalEarnings = (earnings as number[]).reduce((sum, value) => sum + value, 0)
  const totalDeductions = (deductions as number[]).reduce((sum, value) => sum + value, 0)
  return { earnings: totalEarnings, deductions: totalDeductions, net: totalEarnings - totalDeductions }
}

export function validateSlipDraft(staffId: string, draft: SlipDraft): {
  errors: Record<string, string[]>
  request?: SalarySlipRequest
} {
  const errors: Record<string, string[]> = {}
  const salaryMonth = Number(draft.salaryMonth)
  const salaryYear = Number(draft.salaryYear)
  if (!/^\d{1,2}$/.test(draft.salaryMonth) || salaryMonth < 1 || salaryMonth > 12) errors.salaryMonth = ['Choose a salary month.']
  if (!validYear(draft.salaryYear)) errors.salaryYear = ['Enter a salary year from 2000 to 2100.']
  const date = /^(\d{4})-(\d{2})-(\d{2})$/.exec(draft.payDate)
  if (!date || Number(date[1]) < 2000 || Number(date[1]) > 2101 || Number(date[2]) < 1 || Number(date[2]) > 12 || Number(date[3]) < 1 || Number(date[3]) > daysInMonth(Number(date[1]), Number(date[2]))) {
    errors.payDate = ['Enter a valid pay date between 2000 and 2101.']
  }
  if (draft.employeeId.length > 100) errors.employeeId = ['Employee ID must be 100 characters or fewer.']
  const limit = !errors.salaryMonth && !errors.salaryYear ? daysInMonth(salaryYear, salaryMonth) : 31
  for (const [field, label] of attendanceFields) {
    const pattern = field === 'lateMarks' ? /^\d+$/ : /^\d+(?:\.\d)?$/
    if (!pattern.test(draft[field]) || Number(draft[field]) > limit) {
      errors[field] = [`${label} must be from 0 to ${limit}${field === 'lateMarks' ? ' as a whole number' : ' with at most 1 decimal place'}.`]
    }
  }
  if (!errors.leaveWithoutPay && !errors.totalLeave && Number(draft.leaveWithoutPay) > Number(draft.totalLeave)) {
    errors.leaveWithoutPay = ['Leave without pay cannot exceed total leave.']
  }
  for (const [field] of [...earningFields, ...deductionFields]) {
    if (parseCents(draft[field]) === undefined) errors[field] = [MONEY_ERROR]
  }
  const totals = slipTotals(draft)
  if (totals && totals.net < 0) errors.netSalary = ['Deductions cannot exceed earnings. Net salary must not be negative.']
  if (!staffId) errors.staffId = ['Choose an active staff member.']
  if (Object.keys(errors).length) return { errors }
  return {
    errors,
    request: {
      staffId, salaryMonth, salaryYear, payDate: draft.payDate,
      employeeId: draft.employeeId.trim() === '' ? null : draft.employeeId,
      workingDays: Number(draft.workingDays), totalLeave: Number(draft.totalLeave),
      leaveWithoutPay: Number(draft.leaveWithoutPay), lateMarks: Number(draft.lateMarks),
      // All six amounts have been validated; no invalid string is coerced to zero.
      basicSalary: parseCents(draft.basicSalary)! / 100,
      houseRentAllowance: parseCents(draft.houseRentAllowance)! / 100,
      otherAllowance: parseCents(draft.otherAllowance)! / 100,
      commission: parseCents(draft.commission)! / 100,
      leaveDeduction: parseCents(draft.leaveDeduction)! / 100,
      otherDeductions: parseCents(draft.otherDeductions)! / 100,
    },
  }
}
