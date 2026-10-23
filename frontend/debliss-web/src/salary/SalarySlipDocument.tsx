import { useEffect, useRef } from 'react'
import type { SalarySlipDto, StaffSalaryDetails } from '../api/contracts.ts'
import { maskBankNumber, money } from './money.ts'
import { attendanceFields, deductionFields, earningFields, months } from './slipDraft.ts'

export function BankDetails({ salary, accountName }: {
  salary: Pick<StaffSalaryDetails, 'bankName' | 'bankAccountNumber' | 'ibanNumber'>
  accountName: string | null
}) {
  return (
    <section className="salary-bank" aria-label="Bank details">
      <h3>Bank details</h3>
      <p className="panel-lede">Read-only from the staff record. Account identifiers are masked here and complete in the PDF.</p>
      <dl className="salary-facts">
        <div><dt>Bank name</dt><dd>{salary.bankName || 'Not set'}</dd></div>
        <div><dt>Account name</dt><dd>{accountName || 'Not set'}</dd></div>
        <div><dt>Bank account number</dt><dd>{maskBankNumber(salary.bankAccountNumber)}</dd></div>
        <div><dt>IBAN</dt><dd>{maskBankNumber(salary.ibanNumber)}</dd></div>
      </dl>
    </section>
  )
}

/** Text equivalent remains available to screen readers and without a PDF viewer. */
export function SalarySlipText({ slip }: { slip: SalarySlipDto }) {
  return (
    <section className="salary-text-preview" aria-label="Salary slip details">
      <div className="panel-header">
        <div><h3>{slip.employeeName}</h3><p>{months[slip.salaryMonth - 1]} {slip.salaryYear} · Pay date {slip.payDate}</p></div>
        <span className="pill">Salary slip</span>
      </div>
      <dl className="salary-facts">
        <div><dt>Employee ID</dt><dd>{slip.employeeId || 'Not set'}</dd></div>
        <div><dt>Agreed salary reference</dt><dd>{money(slip.agreedTotalSalary)}</dd></div>
        {attendanceFields.map(([field, label]) => <div key={field}><dt>{label}</dt><dd>{slip[field]}</dd></div>)}
        {[...earningFields, ...deductionFields].map(([field, label]) => <div key={field}><dt>{label}</dt><dd>{money(slip[field])}</dd></div>)}
      </dl>
      <div className="salary-totals" aria-label="Salary totals">
        <div><span>Total earnings</span><strong>{money(slip.totalEarnings)}</strong></div>
        <div><span>Total deductions</span><strong>{money(slip.totalDeductions)}</strong></div>
        <div className="salary-net"><span>Net salary</span><strong>{money(slip.netSalary)}</strong></div>
      </div>
      <BankDetails salary={slip} accountName={slip.accountName} />
      <p className="panel-lede">Template {slip.templateVersion} · Created {slip.createdAt} · Created by {slip.createdBy}</p>
    </section>
  )
}

export function SalaryPdf({ blob, slip, preview = false }: { blob: Blob; slip: SalarySlipDto; preview?: boolean }) {
  const download = useRef<HTMLAnchorElement>(null)
  const viewer = useRef<HTMLIFrameElement>(null)
  useEffect(() => {
    const url = URL.createObjectURL(blob)
    const link = download.current
    const frame = viewer.current
    if (link) link.href = url
    if (frame) frame.src = url
    return () => {
      link?.removeAttribute('href')
      frame?.removeAttribute('src')
      URL.revokeObjectURL(url)
    }
  }, [blob])
  return (
    <section className="salary-pdf" aria-label="Salary slip PDF">
      <div className="panel-header">
        <p className="panel-lede">If the PDF viewer is unavailable, use the download link.</p>
        <a ref={download} className="button" download={`salary-slip-${slip.salaryYear}-${String(slip.salaryMonth).padStart(2, '0')}${preview ? '-preview' : ''}.pdf`}>
          {preview ? 'Download preview PDF' : 'Download PDF'}
        </a>
      </div>
      <iframe ref={viewer} title={preview ? 'Salary slip PDF preview' : 'Saved salary slip PDF'} />
    </section>
  )
}
