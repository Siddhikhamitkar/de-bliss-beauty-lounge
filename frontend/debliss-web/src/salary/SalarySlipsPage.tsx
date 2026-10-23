import { useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { ApiError, describeFailure } from '../api/client.ts'
import type { SalarySlipDto, SalarySlipPreview, StaffSalaryDetails, StaffSummary } from '../api/contracts.ts'
import { decodeSalaryPdf, getSalarySlip, getSalarySlipPdf, getStaffSalary, previewSalarySlip, saveSalarySlip, searchSalarySlips } from '../api/salary.ts'
import { searchStaff } from '../api/staff.ts'
import { EmptyState } from '../components/EmptyState.tsx'
import { ErrorState } from '../components/ErrorState.tsx'
import { FieldErrors } from '../components/FieldErrors.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { Pagination } from '../components/Pagination.tsx'
import { useAsync } from '../useAsync.ts'
import { BankDetails, SalaryPdf, SalarySlipText } from './SalarySlipDocument.tsx'
import { money, moneyFromCents } from './money.ts'
import { attendanceFields, deductionFields, earningFields, months, newSlipDraft, slipTotals, validYear, validateSlipDraft } from './slipDraft.ts'
import type { SlipDraft } from './slipDraft.ts'
import './salary.css'

async function loadStaff(activeOnly: boolean): Promise<StaffSummary[]> {
  const members: StaffSummary[] = []
  let page = 1
  while (true) {
    const result = await searchStaff({ status: activeOnly ? 'Active' : undefined, page, pageSize: 200 })
    members.push(...result.items.filter(member => !activeOnly || member.isActive))
    if (result.items.length === 0 || result.page * result.pageSize >= result.totalCount) return members
    page += 1
  }
}

type View = { kind: 'create'; revision: number } | { kind: 'history' } | { kind: 'saved'; id: string }

export function SalarySlipsPage() {
  const [view, setView] = useState<View>({ kind: 'create', revision: 0 })
  const [busy, setBusy] = useState(false)
  const revision = useRef(0)
  return (
    <section className="panel salary-page" aria-label="Salary slips">
      <div className="panel-header">
        <div className="panel-heading">
          <h2>Salary slips</h2>
          <p className="panel-lede">Owner workspace · Review a PDF before saving an immutable salary record.</p>
        </div>
        <div className="panel-actions">
          <button type="button" className="button" disabled={busy} onClick={() => setView({ kind: 'history' })}>History</button>
          <button type="button" className="button button-primary" disabled={busy} onClick={() => setView({ kind: 'create', revision: ++revision.current })}>New salary slip</button>
        </div>
      </div>
      {view.kind === 'create' && <CreateSalarySlip key={view.revision} onBusy={setBusy} onSaved={slip => setView({ kind: 'saved', id: slip.id })} />}
      {view.kind === 'history' && <SalaryHistory onOpen={id => setView({ kind: 'saved', id })} />}
      {view.kind === 'saved' && <SavedSalarySlip key={view.id} id={view.id} />}
    </section>
  )
}

function CreateSalarySlip({ onBusy, onSaved }: { onBusy: (busy: boolean) => void; onSaved: (slip: SalarySlipDto) => void }) {
  const staff = useAsync(() => loadStaff(true), [])
  const [staffId, setStaffId] = useState('')
  const [locked, setLocked] = useState(false)
  const [search, setSearch] = useState('')
  const selected = staff.data?.find(member => member.id === staffId)
  const options = staff.data?.filter(member => member.id === staffId || `${member.firstName} ${member.lastName}`.toLowerCase().includes(search.toLowerCase()))
  return (
    <>
      <ol className="salary-steps" aria-label="Salary slip workflow"><li>1 · Employee & amounts</li><li>2 · Review PDF</li><li>3 · Save record</li></ol>
      <fieldset className="salary-section" disabled={locked}>
        <legend>Employee</legend>
        {staff.failure && <ErrorState message={staff.failure} onRetry={staff.reload} />}
        {!staff.data && !staff.failure && <LoadingState label="Loading active staff…" />}
        {staff.data?.length === 0 && <EmptyState>No active staff members. Add or activate a member in Staff first.</EmptyState>}
        {!!staff.data?.length && <div className="salary-grid">
          <label className="field">Find employee<input type="search" value={search} onChange={event => setSearch(event.target.value)} placeholder="Search active staff" /></label>
          <label className="field">Employee<select value={staffId} onChange={event => setStaffId(event.target.value)}>
            <option value="">Choose an active staff member</option>
            {options?.map(member => <option key={member.id} value={member.id}>{member.firstName} {member.lastName}</option>)}
          </select></label>
        </div>}
      </fieldset>
      {selected ? <StaffSalaryLoader key={selected.id} member={selected} onBusy={onBusy} onLocked={setLocked} onSaved={onSaved} /> : <p className="panel-lede">Choose an employee to load their salary and bank details securely.</p>}
    </>
  )
}

function StaffSalaryLoader({ member, onBusy, onLocked, onSaved }: {
  member: StaffSummary
  onBusy: (busy: boolean) => void
  onLocked: (locked: boolean) => void
  onSaved: (slip: SalarySlipDto) => void
}) {
  // Keyed by employee: a late response from the previous selection cannot prefill this form.
  const salary = useAsync(() => getStaffSalary(member.id), [member.id])
  if (!salary.data || salary.failure) return <>
    {salary.failure ? <ErrorState message={salary.failure} onRetry={salary.reload} /> : <LoadingState label="Loading salary details…" />}
    <button className="button button-primary" disabled>Generate PDF preview</button>
  </>
  return <SlipEditor member={member} salary={salary.data} onBusy={onBusy} onLocked={onLocked} onSaved={onSaved} />
}

function SlipEditor({ member, salary, onBusy, onLocked, onSaved }: {
  member: StaffSummary
  salary: StaffSalaryDetails
  onBusy: (busy: boolean) => void
  onLocked: (locked: boolean) => void
  onSaved: (slip: SalarySlipDto) => void
}) {
  const [draft, setDraft] = useState(() => newSlipDraft(salary))
  const [preview, setPreview] = useState<{ data: SalarySlipPreview; blob: Blob }>()
  const [failure, setFailure] = useState<string>()
  const [errors, setErrors] = useState<Record<string, string[]>>({})
  const [busy, setBusy] = useState(false)
  const [invalidToken, setInvalidToken] = useState(false)
  const submitting = useRef(false)
  const totals = slipTotals(draft)

  const update = (field: keyof SlipDraft, value: string) => setDraft(current => ({ ...current, [field]: value }))
  function startRequest() { submitting.current = true; setBusy(true); onBusy(true); onLocked(true); setFailure(undefined) }
  function finishRequest() { submitting.current = false; setBusy(false); onBusy(false) }

  async function generate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (submitting.current) return
    const result = validateSlipDraft(member.id, draft)
    setErrors(result.errors)
    setFailure(undefined)
    if (!result.request) return
    startRequest()
    try {
      const data = await previewSalarySlip(result.request)
      const blob = decodeSalaryPdf(data.pdfBase64)
      setPreview({ data, blob })
      setInvalidToken(false)
    } catch (error) {
      setFailure(describeFailure(error))
      if (error instanceof ApiError) setErrors(error.errors)
      onLocked(false)
    } finally { finishRequest() }
  }

  async function save() {
    if (submitting.current || !preview || invalidToken) return
    startRequest()
    try {
      const saved = await saveSalarySlip(preview.data.previewToken)
      onSaved(saved)
    } catch (error) {
      setFailure(describeFailure(error))
      if (error instanceof ApiError && (error.status === 400 || error.status === 409)) setInvalidToken(true)
    } finally { finishRequest() }
  }

  if (preview) return <div className="salary-review">
    <div className="panel-header"><h3>Review salary slip</h3><span className="pill">Not saved</span></div>
    <p className="panel-lede">Nothing has been saved. Review the exact PDF below. The preview expires after 30 minutes; Save records this snapshot even if the staff profile changes.</p>
    {failure && <p className="banner" role="alert">{failure}</p>}
    {invalidToken && <p className="salary-warning">Return to edit and generate a fresh preview. For a duplicate period, check History for the existing slip.</p>}
    <SalaryPdf blob={preview.blob} slip={preview.data.slip} preview />
    <SalarySlipText slip={preview.data.slip} />
    <div className="form-actions">
      <button type="button" className="button" disabled={busy} onClick={() => { setPreview(undefined); setFailure(undefined); setErrors({}); setInvalidToken(false); onLocked(false) }}>Back to edit</button>
      <button type="button" className="button button-primary" disabled={busy || invalidToken} onClick={() => void save()}>{busy ? 'Saving…' : 'Save salary slip'}</button>
    </div>
  </div>

  const input = (field: keyof SlipDraft, label: string, inputMode: 'decimal' | 'numeric' = 'decimal') => <label className="field" key={field}>
    {label}<input inputMode={inputMode} value={draft[field]} aria-invalid={!!errors[field]} onChange={event => update(field, event.target.value)} />
    <FieldErrors messages={errors[field]} />
  </label>

  return <form className="salary-form" onSubmit={event => void generate(event)} noValidate>
    {failure && <p className="banner" role="alert">{failure}</p>}
    <fieldset className="salary-section" disabled={busy}>
      <legend>Pay period</legend>
      <div className="salary-grid">
        <label className="field">Salary month<select value={draft.salaryMonth} aria-invalid={!!errors.salaryMonth} onChange={event => update('salaryMonth', event.target.value)}>{months.map((month, index) => <option key={month} value={index + 1}>{month}</option>)}</select><FieldErrors messages={errors.salaryMonth} /></label>
        {input('salaryYear', 'Salary year', 'numeric')}
        <label className="field">Pay date<input type="date" min="2000-01-01" max="2101-12-31" value={draft.payDate} aria-invalid={!!errors.payDate} onChange={event => update('payDate', event.target.value)} /><FieldErrors messages={errors.payDate} /></label>
        <label className="field">Employee ID (optional)<input type="text" autoComplete="off" maxLength={100} value={draft.employeeId} aria-invalid={!!errors.employeeId} onChange={event => update('employeeId', event.target.value)} /><FieldErrors messages={errors.employeeId} /></label>
      </div>
    </fieldset>
    <fieldset className="salary-section" disabled={busy}>
      <legend>Attendance</legend>
      <p className="panel-lede">Enter days manually for this month. No attendance or deduction policy is inferred.</p>
      <div className="salary-grid">{attendanceFields.map(([field, label]) => input(field, label, field === 'lateMarks' ? 'numeric' : 'decimal'))}</div>
    </fieldset>
    <div className="salary-reference"><span>Agreed salary reference</span><strong>{money(salary.totalSalary)}</strong><p>This is a reference, not a calculated total or automatic deduction.</p></div>
    <div className="salary-columns">
      <fieldset className="salary-section" disabled={busy}><legend>Earnings</legend><div className="salary-grid">{earningFields.map(([field, label]) => input(field, label))}</div></fieldset>
      <fieldset className="salary-section" disabled={busy}><legend>Deductions</legend><div className="salary-grid">{deductionFields.map(([field, label]) => input(field, label))}</div></fieldset>
    </div>
    <div className="salary-totals" aria-live="polite" aria-label="Calculated salary totals">
      <div><span>Total earnings</span><strong>{totals ? moneyFromCents(totals.earnings) : 'Check amounts'}</strong></div>
      <div><span>Total deductions</span><strong>{totals ? moneyFromCents(totals.deductions) : 'Check amounts'}</strong></div>
      <div className="salary-net"><span>Net salary</span><strong>{totals ? moneyFromCents(totals.net) : 'Check amounts'}</strong></div>
    </div>
    <FieldErrors messages={errors.netSalary} />
    <BankDetails salary={salary} accountName={`${member.firstName} ${member.lastName}`} />
    <div className="form-actions"><button type="submit" className="button button-primary" disabled={busy}>{busy ? 'Generating…' : 'Generate PDF preview'}</button><p className="panel-lede">Generate does not save a record.</p></div>
  </form>
}

function SalaryHistory({ onOpen }: { onOpen: (id: string) => void }) {
  const staff = useAsync(() => loadStaff(false), [])
  const [staffId, setStaffId] = useState('')
  const [year, setYear] = useState('')
  const [yearError, setYearError] = useState<string>()
  const [query, setQuery] = useState<{ staffId?: string; year?: number; page: number }>({ page: 1 })
  // Key the results, not only the request, so previous-filter rows never masquerade as current ones.
  return <section className="salary-history" aria-label="Salary slip history">
    <h3>Saved salary slips</h3>
    <form className="filters" noValidate onSubmit={event => {
      event.preventDefault()
      if (year && !validYear(year)) { setYearError('Enter a year from 2000 to 2100.'); return }
      setYearError(undefined)
      setQuery({ staffId: staffId || undefined, year: year ? Number(year) : undefined, page: 1 })
    }}>
      <label className="field">Filter by employee<select value={staffId} onChange={event => setStaffId(event.target.value)}>
        <option value="">All employees</option>{staff.data?.map(member => <option key={member.id} value={member.id}>{member.firstName} {member.lastName}{member.isActive ? '' : ' (inactive)'}</option>)}
      </select></label>
      <label className="field">Filter by year<input inputMode="numeric" value={year} onChange={event => setYear(event.target.value)} placeholder="All years" aria-invalid={!!yearError} /><FieldErrors messages={yearError ? [yearError] : undefined} /></label>
      <button className="button" type="submit">Apply filters</button>
    </form>
    {staff.failure && <ErrorState message={staff.failure} onRetry={staff.reload} />}
    <HistoryResults key={`${query.staffId}-${query.year}-${query.page}`} query={query} onPage={page => setQuery(current => ({ ...current, page }))} onOpen={onOpen} />
  </section>
}

function HistoryResults({ query, onPage, onOpen }: {
  query: { staffId?: string; year?: number; page: number }
  onPage: (page: number) => void
  onOpen: (id: string) => void
}) {
  const history = useAsync(() => searchSalarySlips({ ...query, pageSize: 20 }), [query.staffId, query.year, query.page])
  if (history.failure) return <ErrorState message={history.failure} onRetry={history.reload} />
  if (!history.data) return <LoadingState label="Loading salary slip history…" />
  return <>
    {history.data.items.length === 0 ? <EmptyState>No saved salary slips match these filters.</EmptyState> : <div className="salary-history-list">
      {history.data.items.map(slip => <article key={slip.id} className="salary-history-row">
        <div><strong>{slip.employeeName}</strong><p>{months[slip.salaryMonth - 1]} {slip.salaryYear} · Paid {slip.payDate}</p></div>
        <strong>{money(slip.netSalary)}</strong>
        <button type="button" className="button" onClick={() => onOpen(slip.id)} aria-label={`Open salary slip for ${slip.employeeName}, ${months[slip.salaryMonth - 1]} ${slip.salaryYear}`}>View & download</button>
      </article>)}
    </div>}
    <Pagination result={history.data} onChange={onPage} />
  </>
}

function SavedSalarySlip({ id }: { id: string }) {
  const slip = useAsync(() => getSalarySlip(id), [id])
  const pdf = useAsync(() => getSalarySlipPdf(id), [id])
  if (slip.failure) return <ErrorState message={slip.failure} onRetry={slip.reload} />
  if (!slip.data) return <LoadingState label="Loading saved salary slip…" />
  return <div className="salary-review">
    <div className="panel-header"><h3>Saved salary slip</h3><span className="pill">Read-only</span></div>
    <p className="panel-lede">This record and its PDF are fixed snapshots. Staff profile changes do not alter them.</p>
    {pdf.failure && <ErrorState message={pdf.failure} onRetry={pdf.reload} />}
    {!pdf.data && !pdf.failure && <LoadingState label="Loading saved PDF…" />}
    {pdf.data && <SalaryPdf blob={pdf.data} slip={slip.data} />}
    <SalarySlipText slip={slip.data} />
  </div>
}
