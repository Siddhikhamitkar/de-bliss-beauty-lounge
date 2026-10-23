import { useRef, useState } from 'react'
import { describeFailure } from '../api/client.ts'
import type { ImportPreview, ImportResult, ImportRowStatus } from '../api/contracts.ts'
import {
  downloadImportErrorReport,
  downloadImportTemplate,
  executeImport,
  ImportTemplateHeaders,
  importTemplateName,
  previewImport,
  saveBlob,
  type ImportKind,
} from '../api/imports.ts'

/** One workbook flavour the dialog can work with. */
export interface ImportFlavour {
  kind: ImportKind
  label: string
  description: string
}

interface ImportDialogProps {
  /** One flavour, or the retail/salon-use pair the inventory page offers. */
  flavours: ImportFlavour[]
  title: string
  onClose: () => void
  /** Called when the import created rows, so the list behind re-reads. */
  onImported?: () => void
}

type ImportStep = 'select' | 'preview' | 'result'

const StatusLabels: Record<ImportRowStatus, string> = {
  Valid: 'Ready',
  Invalid: 'Invalid',
  DuplicateInFile: 'Duplicate in file',
  AlreadyExists: 'Already exists',
}

/**
 * The Excel import conversation: choose a file, see exactly what would happen
 * — counts, errors, the first rows, who would be skipped — and only then
 * decide. Closing the dialog at any point before the confirm click leaves the
 * database untouched, because nothing is written until the import step.
 */
export function ImportDialog({ flavours, title, onClose, onImported }: ImportDialogProps) {
  const [kind, setKind] = useState<ImportKind>(flavours[0].kind)
  const [step, setStep] = useState<ImportStep>('select')
  const [file, setFile] = useState<File | null>(null)
  const [preview, setPreview] = useState<ImportPreview | null>(null)
  const [result, setResult] = useState<ImportResult | null>(null)
  const [importDuplicates, setImportDuplicates] = useState(false)
  const [activity, setActivity] = useState<'preview' | 'import' | 'download' | null>(null)
  const inFlight = useRef(false)
  const [failure, setFailure] = useState<string>()
  const busy = activity !== null

  const noun = kind === 'customers' ? 'customer' : kind === 'services' ? 'service' : 'product'
  const allowsDuplicates = kind === 'customers'

  async function run(operation: NonNullable<typeof activity>, action: () => Promise<void>) {
    if (inFlight.current) return
    inFlight.current = true
    setActivity(operation)
    setFailure(undefined)

    try {
      await action()
    } catch (error: unknown) {
      setFailure(describeFailure(error))
    } finally {
      inFlight.current = false
      setActivity(null)
    }
  }

  const close = () => {
    if (!inFlight.current) onClose()
  }

  const handlePreview = () =>
    run('preview', async () => {
      if (!file) {
        return
      }

      setPreview(await previewImport(kind, file))
      setStep('preview')
    })

  const handleImport = () =>
    run('import', async () => {
      if (!file || !preview || preview.validRows === 0) {
        return
      }

      const importResult = await executeImport(kind, file, importDuplicates)

      setResult(importResult)
      setStep('result')

      if (importResult.succeeded && importResult.created > 0) {
        onImported?.()
      }
    })

  const handleTemplate = () =>
    run('download', async () => {
      saveBlob(await downloadImportTemplate(kind), importTemplateName(kind))
    })

  const handleErrorReport = () =>
    run('download', async () => {
      if (file) {
        saveBlob(
          await downloadImportErrorReport(kind, file),
          `${file.name.replace(/\.xlsx$/i, '')}-errors.xlsx`,
        )
      }
    })

  const chooseFile = (chosen: File | null) => {
    if (inFlight.current) return
    setFile(null)
    setPreview(null)
    setResult(null)
    setImportDuplicates(false)
    setFailure(undefined)

    // Convenience feedback only; the server validates the workbook and every row.
    if (chosen && !/\.xlsx$/i.test(chosen.name)) {
      setFailure('Choose an Excel .xlsx workbook. Other file types are not supported.')
    } else if (chosen && chosen.size > 5 * 1024 * 1024) {
      setFailure('The workbook is too large. Choose an .xlsx file up to 5 MB.')
    } else {
      setFile(chosen)
    }
  }

  const backToSelect = () => {
    if (inFlight.current) return
    chooseFile(null)
    setStep('select')
  }

  return (
    <div className="dialog-backdrop" onClick={close}>
      <div
        className={`dialog import-dialog${kind === 'services' ? ' import-dialog-services' : ''}`}
        role="dialog"
        aria-modal="true"
        aria-busy={busy}
        aria-labelledby="import-dialog-title"
        onClick={event => event.stopPropagation()}
        onKeyDown={event => {
          if (event.key === 'Escape') {
            event.stopPropagation()
            close()
          }
        }}
      >
        <h3 id="import-dialog-title">{step === 'result' ? 'Import finished' : title}</h3>

        <div className="dialog-body import-body">
          {step === 'select' && (
            <SelectStep
              flavours={flavours}
              kind={kind}
              onKind={chosen => {
                chooseFile(null)
                setKind(chosen)
              }}
              noun={noun}
              busy={busy}
              file={file}
              onFile={chooseFile}
            />
          )}

          {step === 'preview' && preview && (
            <PreviewStep
              preview={preview}
              noun={noun}
              isServices={kind === 'services'}
              busy={busy}
              allowsDuplicates={allowsDuplicates}
              importDuplicates={importDuplicates}
              onDuplicatesChange={setImportDuplicates}
            />
          )}

          {step === 'result' && result && <ResultStep result={result} noun={noun} />}

          {busy && (
            <p className="import-hint" role="status">
              {activity === 'import'
                ? `Importing ${noun}s and saving changes… Keep this dialog open.`
                : activity === 'preview'
                  ? 'Checking the workbook… Nothing is being saved.'
                  : 'Preparing your download…'}
            </p>
          )}

          {failure && (
            <p className="banner" role="alert">
              {failure}
            </p>
          )}
        </div>

        <div className="dialog-actions">
          {step === 'select' && (
            <>
              <button type="button" className="button" onClick={close} disabled={busy}>
                Cancel
              </button>
              <button type="button" className="button" onClick={handleTemplate} disabled={busy}>
                {activity === 'download' ? 'Downloading…' : 'Download template'}
              </button>
              <button
                type="button"
                className="button button-primary"
                onClick={handlePreview}
                disabled={busy || file === null}
              >
                {activity === 'preview' ? 'Reading…' : 'Preview import'}
              </button>
            </>
          )}

          {step === 'preview' && preview && (
            <>
              <button type="button" className="button" onClick={close} disabled={busy}>
                Cancel
              </button>
              <button type="button" className="button" onClick={backToSelect} disabled={busy}>
                Choose another file
              </button>
              {preview.errors.length > 0 && (
                <button type="button" className="button" onClick={handleErrorReport} disabled={busy}>
                  Download error report
                </button>
              )}
              <button
                type="button"
                className="button button-primary"
                onClick={handleImport}
                disabled={busy || preview.validRows === 0}
              >
                {activity === 'import'
                  ? 'Importing…'
                  : `Import ${preview.validRows} ${noun}${preview.validRows === 1 ? '' : 's'}`}
              </button>
            </>
          )}

          {step === 'result' && result && (
            <>
              {result.errors.length > 0 && (
                <button type="button" className="button" onClick={handleErrorReport} disabled={busy}>
                  Download error report
                </button>
              )}
              <button type="button" className="button button-primary" onClick={close} disabled={busy}>
                Done
              </button>
            </>
          )}
        </div>
      </div>
    </div>
  )
}

function SelectStep({
  flavours,
  kind,
  onKind,
  noun,
  busy,
  file,
  onFile,
}: {
  flavours: ImportFlavour[]
  kind: ImportKind
  onKind: (kind: ImportKind) => void
  noun: string
  busy: boolean
  file: File | null
  onFile: (file: File | null) => void
}) {
  const [dragging, setDragging] = useState(false)

  return (
    <>
      <p className="import-lede">
        Import {noun}s from an Excel workbook. Download the template first — it names the exact
        columns, and its Example sheet explains each one without ever being imported.
      </p>

      {flavours.length > 1 && (
        <fieldset className="import-flavours">
          <legend>What kind of products?</legend>
          {flavours.map(flavour => (
            <label key={flavour.kind} className="import-flavour">
              <input
                type="radio"
                name="import-flavour"
                checked={kind === flavour.kind}
                onChange={() => onKind(flavour.kind)}
                disabled={busy}
              />
              <span>
                <strong>{flavour.label}</strong>
                <br />
                {flavour.description}
              </span>
            </label>
          ))}
        </fieldset>
      )}

      {kind === 'services' && (
        <>
          <p className="import-hint">
            Columns, in order: {ImportTemplateHeaders.services.join(', ')}.
          </p>
          <ServicesImportRules />
        </>
      )}

      <div
        className={`import-dropzone${dragging ? ' is-dragging' : ''}`}
        role="group"
        aria-label="Workbook upload"
        aria-disabled={busy}
        onDragOver={event => {
          event.preventDefault()
          if (!busy) setDragging(true)
        }}
        onDragLeave={() => setDragging(false)}
        onDrop={event => {
          event.preventDefault()
          setDragging(false)
          if (!busy) onFile(event.dataTransfer.files[0] ?? null)
        }}
      >
        <label className="import-file-picker">
          <span>Choose .xlsx file</span>
          <input
            className="import-file-input"
            type="file"
            accept=".xlsx"
            aria-label="Workbook (.xlsx)"
            aria-describedby="import-file-hint"
            onChange={event => {
              onFile(event.target.files?.[0] ?? null)
              // The File state is shared with drop; allow reselecting the same workbook.
              event.target.value = ''
            }}
            disabled={busy}
          />
        </label>
        <span>or drag and drop it here</span>
        <p className="import-selected-file">{file ? file.name : 'No workbook selected.'}</p>
      </div>
      <p className="import-hint" id="import-file-hint">
        Excel .xlsx only, up to 5 MB and 1000 rows. Every row is checked against the same rules as
        the {noun} form before anything is saved. The server makes the final validation decision.
      </p>
    </>
  )
}

function ServicesImportRules() {
  return (
    <ul className="import-summary">
      <li>Categories must already exist. Unknown categories are invalid; none are created.</li>
      <li>
        Eligible staff: separate existing full names with semicolons. Each name must match exactly
        one staff member, ignoring case; unknown or ambiguous names are invalid.
      </li>
      <li>
        Active defaults to Yes when blank and requires at least one active eligible staff member.
        Use No to import an unassigned service for later configuration.
      </li>
      <li>
        Duplicate service names within the same category are skipped, both in the file and in
        existing services. Nothing is overwritten and there is no duplicate override.
      </li>
      <li>Online booking is off and display order is 0 for imported services.</li>
    </ul>
  )
}

const ServicePreviewColumns = [
  { index: 1, label: 'Category' },
  { index: 2, label: 'Price (AED)' },
  { index: 3, label: 'Duration (minutes)' },
  { index: 5, label: 'Active' },
  { index: 6, label: 'Eligible staff' },
]

function PreviewStep({
  preview,
  noun,
  isServices,
  busy,
  allowsDuplicates,
  importDuplicates,
  onDuplicatesChange,
}: {
  preview: ImportPreview
  noun: string
  isServices: boolean
  busy: boolean
  allowsDuplicates: boolean
  importDuplicates: boolean
  onDuplicatesChange: (value: boolean) => void
}) {
  return (
    <>
      <p className="import-lede">
        <strong>{preview.fileName}</strong> — {preview.totalRows}{' '}
        {preview.totalRows === 1 ? 'row' : 'rows'} read. Nothing is saved until you confirm.
      </p>

      <div className="import-counts">
        <Count value={preview.validRows} label="Ready to import" />
        <Count value={preview.invalidRows} label="Invalid" problem={preview.invalidRows > 0} />
        <Count
          value={preview.duplicateRows}
          label="Duplicate in file"
          problem={preview.duplicateRows > 0}
        />
        <Count
          value={preview.existingRows}
          label="Already exists"
          problem={preview.existingRows > 0}
        />
        {preview.openingStockRows > 0 && (
          <Count value={preview.openingStockRows} label="Opening-stock movements" />
        )}
      </div>

      {isServices && <ServicesImportRules />}

      {preview.rows.length > 0 && (
        <div className="import-table-scroll" role="region" aria-label="Import preview rows" tabIndex={0}>
          <table className="import-table">
            <thead>
              <tr>
                <th scope="col">Row</th>
                <th scope="col">{isServices ? 'Service name' : noun}</th>
                {isServices && ServicePreviewColumns.map(column => (
                  <th scope="col" key={column.index}>{column.label}</th>
                ))}
                <th scope="col">Verdict</th>
              </tr>
            </thead>
            <tbody>
              {preview.rows.map(row => (
                <tr key={row.rowNumber}>
                  <td>{row.rowNumber}</td>
                  <td>{row.label}</td>
                  {isServices && ServicePreviewColumns.map(column => (
                    <td key={column.index}>
                      {row.fields[column.index]?.trim() || (column.index === 5 ? 'Yes (default)' : '—')}
                    </td>
                  ))}
                  <td>
                    <span className={`import-status import-status-${row.status.toLowerCase()}`}>
                      {StatusLabels[row.status]}
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {preview.rows.length < preview.totalRows && (
        <p className="import-hint">
          Showing the first {preview.rows.length} of {preview.totalRows} rows.
        </p>
      )}

      {preview.errors.length > 0 && (
        <div className="import-errors" aria-label="Row problems">
          {preview.errors.map((error, index) => (
            <p className="import-error-row" key={index}>
              <strong>Row {error.rowNumber}</strong>
              <span className="import-error-field">{error.field}</span>
              <span>{error.message}</span>
            </p>
          ))}
        </div>
      )}

      {allowsDuplicates && (preview.duplicateRows > 0 || preview.existingRows > 0) && (
        <label className="import-option">
          <input
            type="checkbox"
            checked={importDuplicates}
            onChange={event => onDuplicatesChange(event.target.checked)}
            disabled={busy}
          />
          <span>
            Import duplicates anyway — tick this only if the people sharing a number are genuinely
            different customers. Existing details are never overwritten either way.
          </span>
        </label>
      )}

      {preview.invalidRows > 0 && (
        <p className="import-hint">
          Invalid rows are never imported; the valid ones can go in and you can fix the rest from
          the error report afterwards.
        </p>
      )}
    </>
  )
}

function ResultStep({ result, noun }: { result: ImportResult; noun: string }) {
  if (!result.succeeded) {
    return (
      <>
        <p className="banner" role="alert">
          {result.message ?? 'The import failed and nothing was saved.'}
        </p>
        <p className="import-hint">
          No {noun}s were created{noun === 'service' ? '.' : ' and no stock was moved.'} Fix the
          problem and start the import again.
        </p>
      </>
    )
  }

  return (
    <>
      <p className="import-lede">
        {result.created === 0
          ? 'Nothing needed creating.'
          : `Created ${result.created} ${noun}${result.created === 1 ? '' : 's'}.`}
      </p>
      <ul className="import-summary">
        {result.skippedInvalid > 0 && (
          <li>{result.skippedInvalid} invalid rows were not imported.</li>
        )}
        {result.skippedExisting > 0 && (
          <li>
            {result.skippedExisting} {result.skippedExisting === 1 ? 'row was' : 'rows were'}{' '}
            skipped because it already exists — nothing was overwritten.
          </li>
        )}
        {result.openingStockMovements > 0 && (
          <li>
            {result.openingStockMovements} opening-stock{' '}
            {result.openingStockMovements === 1 ? 'movement was' : 'movements were'} recorded, each
            visible in the product's stock history.
          </li>
        )}
        {result.errors.length > 0 && (
          <li>
            {result.errors.length} validation {result.errors.length === 1 ? 'issue was' : 'issues were'} reported.
          </li>
        )}
      </ul>
      {noun === 'service' && result.errors.length > 0 && (
        <p className="import-hint">
          The error report rechecks the workbook against current data. Newly imported services may
          appear as already existing; the summary above describes this import's outcome.
        </p>
      )}
    </>
  )
}

function Count({
  value,
  label,
  problem = false,
}: {
  value: number
  label: string
  problem?: boolean
}) {
  return (
    <div className={`import-count${problem ? ' is-problem' : ''}`}>
      <strong>{value}</strong>
      {label}
    </div>
  )
}

