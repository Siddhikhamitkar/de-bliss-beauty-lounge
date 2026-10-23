import { act, fireEvent, render, screen, within } from '@testing-library/react'
import userEvent, { type UserEvent } from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ImportPreview, ImportResult } from '../api/contracts.ts'
import {
  downloadImportErrorReport,
  downloadImportTemplate,
  executeImport,
  importTemplateName,
  previewImport,
} from '../api/imports.ts'
import { ImportDialog } from './ImportDialog.tsx'

vi.mock('../api/imports.ts', async importOriginal => ({
  ...await importOriginal<typeof import('../api/imports.ts')>(),
  downloadImportErrorReport: vi.fn(),
  downloadImportTemplate: vi.fn(),
  executeImport: vi.fn(),
  importTemplateName: vi.fn((kind: string) => `${kind}-import-template.xlsx`),
  previewImport: vi.fn(),
}))

function previewData(overrides: Partial<ImportPreview> = {}): ImportPreview {
  return {
    type: 'Customers',
    fileName: 'customers.xlsx',
    totalRows: 3,
    validRows: 2,
    invalidRows: 1,
    duplicateRows: 0,
    existingRows: 0,
    openingStockRows: 0,
    rows: [
      { rowNumber: 2, label: 'Amelia Fernandes', status: 'Valid', fields: [] },
      { rowNumber: 3, label: 'R 3', status: 'Invalid', fields: [] },
      { rowNumber: 4, label: 'Bella Cruz', status: 'Valid', fields: [] },
    ],
    errors: [
      { rowNumber: 3, field: 'Mobile', message: 'Enter a valid mobile number.', rawValue: '12' },
    ],
    ...overrides,
  }
}

function resultData(overrides: Partial<ImportResult> = {}): ImportResult {
  return {
    type: 'Customers',
    fileName: 'customers.xlsx',
    succeeded: true,
    created: 2,
    skippedInvalid: 1,
    skippedExisting: 0,
    failed: 0,
    openingStockMovements: 0,
    message: null,
    errors: [],
    ...overrides,
  }
}

const workbook = new File(
  ['workbook bytes'],
  'customers.xlsx',
  { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' },
)

function renderCustomersDialog(overrides: { onClose?: () => void; onImported?: () => void } = {}) {
  const onClose = overrides.onClose ?? vi.fn()
  const onImported = overrides.onImported ?? vi.fn()

  render(
    <ImportDialog
      flavours={[{ kind: 'customers', label: 'Customers', description: 'Name, mobile, email and profile details.' }]}
      title="Import customers"
      onClose={onClose}
      onImported={onImported}
    />,
  )

  return { onClose, onImported }
}

async function previewWorkbook(user: UserEvent, preview: ImportPreview) {
  vi.mocked(previewImport).mockResolvedValue(preview)

  await user.upload(screen.getByLabelText('Workbook (.xlsx)'), workbook)
  await user.click(screen.getByRole('button', { name: 'Preview import' }))

  await screen.findByText(/rows? read/)
}

const serviceWorkbook = new File(['services workbook'], 'services.XLSX')

function servicePreview(overrides: Partial<ImportPreview> = {}): ImportPreview {
  return previewData({
    type: 'Services',
    fileName: serviceWorkbook.name,
    totalRows: 5,
    validRows: 2,
    invalidRows: 1,
    duplicateRows: 1,
    existingRows: 1,
    rows: [
      { rowNumber: 2, label: 'Gel Manicure', status: 'Valid', fields: ['Gel Manicure', 'Nails', '120.50', '45', 'Long-lasting finish', '', 'Ana Bianchi; Mona Farouk'] },
      { rowNumber: 3, label: 'Future service', status: 'Valid', fields: ['Future service', 'Hair', '80', '30', '', 'No', ''] },
      { rowNumber: 4, label: 'Invalid service', status: 'Invalid', fields: ['Invalid service', 'Unknown', '100', '60', '', 'Yes', 'Unknown Person'] },
      { rowNumber: 5, label: 'gel manicure', status: 'DuplicateInFile', fields: ['gel manicure', 'Nails', '120.50', '45', '', 'Yes', 'Ana Bianchi'] },
      { rowNumber: 6, label: 'Existing service', status: 'AlreadyExists', fields: ['Existing service', 'Hair', '80', '30', '', 'No', ''] },
    ],
    errors: [
      { rowNumber: 4, field: 'Category name', message: 'Category not found.', rawValue: 'Unknown' },
      { rowNumber: 4, field: 'Eligible staff', message: 'Staff name must match one existing staff member.', rawValue: 'Unknown Person' },
    ],
    ...overrides,
  })
}

function renderServicesDialog() {
  const onClose = vi.fn()
  const onImported = vi.fn()
  render(
    <ImportDialog
      flavours={[{ kind: 'services', label: 'Services', description: 'Services and eligible staff.' }]}
      title="Import Services"
      onClose={onClose}
      onImported={onImported}
    />,
  )
  return { onClose, onImported }
}

async function selectServiceFile(user: UserEvent, method: 'select' | 'drop', file = serviceWorkbook) {
  if (method === 'select') {
    await user.upload(screen.getByLabelText('Workbook (.xlsx)'), file)
  } else {
    fireEvent.drop(screen.getByRole('group', { name: 'Workbook upload' }), {
      dataTransfer: { files: [file] },
    })
  }
}

describe('ImportDialog', () => {
  const realCreateObjectURL = URL.createObjectURL
  const realRevokeObjectURL = URL.revokeObjectURL

  beforeEach(() => {
    URL.createObjectURL = vi.fn(() => 'blob:download')
    URL.revokeObjectURL = vi.fn()
  })

  afterEach(() => {
    URL.createObjectURL = realCreateObjectURL
    URL.revokeObjectURL = realRevokeObjectURL
  })

  it('previews the workbook with counts, verdicts and field errors before saving anything', async () => {
    const user = userEvent.setup()
    renderCustomersDialog()

    expect(screen.getByRole('button', { name: 'Preview import' })).toBeDisabled()

    await previewWorkbook(user, previewData())

    expect(previewImport).toHaveBeenCalledExactlyOnceWith('customers', workbook)
    expect(executeImport).not.toHaveBeenCalled()

    const counts = screen.getByText('Ready to import').closest<HTMLElement>('.import-counts')!

    expect(within(counts).getByText('Invalid')).toBeInTheDocument()
    expect(within(counts).getByText('Duplicate in file')).toBeInTheDocument()
    expect(within(counts).getByText('Already exists')).toBeInTheDocument()

    const table = screen.getByRole('table')

    expect(within(table).getByText('Amelia Fernandes')).toBeInTheDocument()
    // Two valid rows, so the verdict pill "Ready" appears twice.
    expect(within(table).getAllByText('Ready')).toHaveLength(2)
    expect(within(table).getByText('Invalid')).toBeInTheDocument()

    expect(screen.getByText('Enter a valid mobile number.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Import 2 customers' })).toBeInTheDocument()
  })

  it('hides the duplicates opt-in while nothing looks duplicated', async () => {
    const user = userEvent.setup()
    renderCustomersDialog()

    await previewWorkbook(user, previewData())

    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument()
  })

  it('lets the operator choose to import duplicates deliberately', async () => {
    const user = userEvent.setup()
    const { onImported } = renderCustomersDialog()
    vi.mocked(executeImport).mockResolvedValue(resultData({ created: 2 }))

    await previewWorkbook(
      user,
      previewData({
        duplicateRows: 1,
        rows: [
          { rowNumber: 2, label: 'Amelia Fernandes', status: 'Valid', fields: [] },
          { rowNumber: 3, label: 'R 3', status: 'DuplicateInFile', fields: [] },
        ],
      }),
    )

    const duplicates = screen.getByRole('checkbox', { name: /Import duplicates anyway/ })

    expect(duplicates).not.toBeChecked()

    await user.click(duplicates)
    await user.click(screen.getByRole('button', { name: 'Import 2 customers' }))

    expect(await screen.findByText('Created 2 customers.')).toBeInTheDocument()
    expect(executeImport).toHaveBeenCalledExactlyOnceWith('customers', workbook, true)
    expect(onImported).toHaveBeenCalledOnce()
  })

  it('reports skips, existing rows and opening stock in the summary', async () => {
    const user = userEvent.setup()
    vi.mocked(executeImport).mockResolvedValue(
      resultData({
        created: 1,
        skippedInvalid: 1,
        skippedExisting: 2,
        openingStockMovements: 1,
        errors: [{ rowNumber: 5, field: 'SKU', message: 'Same SKU as row 2.', rawValue: null }],
      }),
    )

    render(
      <ImportDialog
        flavours={[{ kind: 'retail', label: 'Retail products', description: 'Priced products.' }]}
        title="Import products"
        onClose={vi.fn()}
        onImported={vi.fn()}
      />,
    )

    await previewWorkbook(
      user,
      previewData({
        type: 'RetailProducts',
        fileName: 'retail.xlsx',
        validRows: 1,
        openingStockRows: 1,
        rows: [{ rowNumber: 2, label: 'LUM-KER-250', status: 'Valid', fields: [] }],
        errors: [],
      }),
    )

    await user.click(screen.getByRole('button', { name: 'Import 1 product' }))

    expect(await screen.findByText('Created 1 product.')).toBeInTheDocument()
    expect(screen.getByText('1 invalid rows were not imported.')).toBeInTheDocument()
    expect(
      screen.getByText('2 rows were skipped because it already exists — nothing was overwritten.'),
    ).toBeInTheDocument()
    expect(
      screen.getByText(/opening-stock movement was recorded, each visible in the product's stock history/),
    ).toBeInTheDocument()
    expect(screen.getByText('Download error report')).toBeInTheDocument()
  })

  it('cannot confirm a workbook with nothing valid in it', async () => {
    const user = userEvent.setup()
    renderCustomersDialog()

    await previewWorkbook(
      user,
      previewData({
        validRows: 0,
        invalidRows: 3,
        rows: [
          { rowNumber: 2, label: 'R 2', status: 'Invalid', fields: [] },
          { rowNumber: 3, label: 'R 3', status: 'Invalid', fields: [] },
          { rowNumber: 4, label: 'R 4', status: 'Invalid', fields: [] },
        ],
      }),
    )

    expect(screen.getByRole('button', { name: 'Import 0 customers' })).toBeDisabled()
    expect(executeImport).not.toHaveBeenCalled()
  })

  it('cancelling at the preview closes the dialog without importing', async () => {
    const user = userEvent.setup()
    const { onClose } = renderCustomersDialog()

    await previewWorkbook(user, previewData())
    await user.click(screen.getByRole('button', { name: 'Cancel' }))

    expect(onClose).toHaveBeenCalledOnce()
    expect(executeImport).not.toHaveBeenCalled()
  })

  it('choosing another file returns to the start without importing', async () => {
    const user = userEvent.setup()
    const { onClose } = renderCustomersDialog()

    await previewWorkbook(user, previewData())
    await user.click(screen.getByRole('button', { name: 'Choose another file' }))

    expect(screen.getByLabelText('Workbook (.xlsx)')).toBeInTheDocument()
    expect(onClose).not.toHaveBeenCalled()
    expect(executeImport).not.toHaveBeenCalled()
  })

  it('a failed import explains that nothing was saved', async () => {
    const user = userEvent.setup()
    const { onImported } = renderCustomersDialog()
    vi.mocked(executeImport).mockResolvedValue(
      resultData({
        succeeded: false,
        created: 0,
        failed: 1,
        message: 'The import failed and nothing was saved. Resolve the problem and try again.',
      }),
    )

    await previewWorkbook(user, previewData())
    await user.click(screen.getByRole('button', { name: 'Import 2 customers' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'The import failed and nothing was saved.',
    )
    expect(screen.getByText(/No customers were created and no stock was moved/)).toBeInTheDocument()
    expect(onImported).not.toHaveBeenCalled()
    expect(screen.queryByRole('button', { name: 'Download error report' })).not.toBeInTheDocument()
  })

  it('downloads the error report for the same workbook when rows had problems', async () => {
    const user = userEvent.setup()
    const blob = new Blob(['report'])
    vi.mocked(downloadImportErrorReport).mockResolvedValue(blob)
    vi.mocked(executeImport).mockResolvedValue(
      resultData({ errors: [{ rowNumber: 3, field: 'Mobile', message: 'Bad', rawValue: null }] }),
    )

    renderCustomersDialog()

    await previewWorkbook(user, previewData())
    await user.click(screen.getByRole('button', { name: 'Import 2 customers' }))
    await user.click(await screen.findByRole('button', { name: 'Download error report' }))

    expect(downloadImportErrorReport).toHaveBeenCalledExactlyOnceWith('customers', workbook)
    expect(URL.createObjectURL).toHaveBeenCalledWith(blob)
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:download')
  })

  it('downloads the template for the flavour under import', async () => {
    const user = userEvent.setup()
    const blob = new Blob(['template'])
    vi.mocked(downloadImportTemplate).mockResolvedValue(blob)

    renderCustomersDialog()

    await user.click(screen.getByRole('button', { name: 'Download template' }))

    expect(downloadImportTemplate).toHaveBeenCalledExactlyOnceWith('customers')
    expect(importTemplateName).toHaveBeenCalledWith('customers')
    expect(URL.createObjectURL).toHaveBeenCalledWith(blob)
  })

  it('switches between the retail and salon-use flavours before previewing', async () => {
    const user = userEvent.setup()

    render(
      <ImportDialog
        flavours={[
          { kind: 'retail', label: 'Retail products', description: 'Priced products.' },
          { kind: 'salon-use', label: 'Salon-use products', description: 'Counted in ml or pcs.' },
        ]}
        title="Import products"
        onClose={vi.fn()}
        onImported={vi.fn()}
      />,
    )

    expect(screen.getByText(/Import products from an Excel workbook/)).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: /Retail products/ })).toBeChecked()

    await user.click(screen.getByRole('radio', { name: /Salon-use products/ }))
    await previewWorkbook(
      user,
      previewData({
        type: 'SalonUseProducts',
        fileName: 'salon.xlsx',
        validRows: 1,
        totalRows: 1,
        invalidRows: 0,
        rows: [{ rowNumber: 2, label: 'SB-H2O2-1L', status: 'Valid', fields: [] }],
        errors: [],
      }),
    )

    expect(previewImport).toHaveBeenCalledExactlyOnceWith('salon-use', workbook)
    expect(screen.getByRole('button', { name: 'Import 1 product' })).toBeInTheDocument()
  })

  it('shows an API failure as a banner and stays on the step', async () => {
    const user = userEvent.setup()
    vi.mocked(previewImport).mockRejectedValue(new Error('boom'))

    renderCustomersDialog()

    await user.upload(screen.getByLabelText('Workbook (.xlsx)'), workbook)
    await user.click(screen.getByRole('button', { name: 'Preview import' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('boom')
    expect(screen.getByRole('button', { name: 'Preview import' })).toBeInTheDocument()
  })

  describe('Services', () => {
    it.each(['select', 'drop'] as const)('previews a service workbook via %s with service fields, counts and errors', async method => {
      const user = userEvent.setup()
      renderServicesDialog()
      vi.mocked(previewImport).mockResolvedValue(servicePreview())

      expect(screen.getByText(/Columns, in order:/)).toHaveTextContent(
        'Service name, Category name, Price (AED), Duration (minutes), Description, Active, Eligible staff',
      )
      expect(screen.getByText(/Categories must already exist/)).toBeInTheDocument()
      expect(screen.getByText(/Online booking is off and display order is 0/)).toBeInTheDocument()
      expect(screen.getByText(/Each name must match exactly one staff member, ignoring case/)).toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Preview import' })).toBeDisabled()

      await selectServiceFile(user, method)
      expect(screen.getByText(serviceWorkbook.name)).toBeInTheDocument()
      await user.click(screen.getByRole('button', { name: 'Preview import' }))

      expect(previewImport).toHaveBeenCalledExactlyOnceWith('services', serviceWorkbook)
      const table = await screen.findByRole('table')
      expect(within(table).getAllByRole('columnheader').map(cell => cell.textContent)).toEqual([
        'Row', 'Service name', 'Category', 'Price (AED)', 'Duration (minutes)', 'Active', 'Eligible staff', 'Verdict',
      ])
      const row = within(table).getByRole('row', { name: /2 Gel Manicure/ })
      expect(within(row).getAllByRole('cell').map(cell => cell.textContent)).toEqual([
        '2', 'Gel Manicure', 'Nails', '120.50', '45', 'Yes (default)', 'Ana Bianchi; Mona Farouk', 'Ready',
      ])
      const inactive = within(table).getByRole('row', { name: /3 Future service/ })
      expect(within(inactive).getByText('No')).toBeInTheDocument()
      expect(within(inactive).getByText('—')).toBeInTheDocument()
      expect(within(table).getByText('Duplicate in file')).toBeInTheDocument()
      expect(within(table).getByText('Already exists')).toBeInTheDocument()
      for (const [label, count] of [['Ready to import', 2], ['Invalid', 1], ['Duplicate in file', 1], ['Already exists', 1]] as const) {
        expect(screen.getByText(label, { selector: '.import-count' })).toHaveTextContent(String(count))
      }
      expect(screen.getByLabelText('Row problems')).toHaveTextContent('Row 4Category nameCategory not found.')
      expect(screen.getByText('Staff name must match one existing staff member.')).toBeInTheDocument()
      expect(screen.queryByRole('checkbox')).not.toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Import 2 services' })).toBeEnabled()
      expect(executeImport).not.toHaveBeenCalled()
    })

    it('downloads a preview error report without importing even when no rows are valid', async () => {
      const user = userEvent.setup()
      renderServicesDialog()
      vi.mocked(previewImport).mockResolvedValue(servicePreview({ validRows: 0, invalidRows: 3 }))
      const report = new Blob(['service errors'])
      vi.mocked(downloadImportErrorReport).mockResolvedValue(report)
      const download = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})

      await selectServiceFile(user, 'select')
      await user.click(screen.getByRole('button', { name: 'Preview import' }))
      expect(screen.getByRole('button', { name: 'Import 0 services' })).toBeDisabled()
      await user.click(screen.getByRole('button', { name: 'Download error report' }))

      expect(downloadImportErrorReport).toHaveBeenCalledExactlyOnceWith('services', serviceWorkbook)
      expect(URL.createObjectURL).toHaveBeenCalledWith(report)
      expect(download.mock.instances[0]).toHaveAttribute('download', 'services-errors.xlsx')
      expect(executeImport).not.toHaveBeenCalled()
      download.mockRestore()
    })

    it('confirms services once, reports skips and notifies the list only after creation', async () => {
      const user = userEvent.setup()
      const { onImported, onClose } = renderServicesDialog()
      vi.mocked(previewImport).mockResolvedValue(servicePreview())
      vi.mocked(executeImport).mockResolvedValue(resultData({
        type: 'Services', fileName: serviceWorkbook.name, skippedExisting: 2, errors: servicePreview().errors,
      }))

      await selectServiceFile(user, 'drop')
      await user.click(screen.getByRole('button', { name: 'Preview import' }))
      expect(onImported).not.toHaveBeenCalled()
      await user.click(screen.getByRole('button', { name: 'Import 2 services' }))

      expect(await screen.findByText('Created 2 services.')).toBeInTheDocument()
      expect(executeImport).toHaveBeenCalledExactlyOnceWith('services', serviceWorkbook, false)
      expect(screen.getByText(/2 rows were skipped/)).toBeInTheDocument()
      expect(screen.getByText('1 invalid rows were not imported.')).toBeInTheDocument()
      expect(onImported).toHaveBeenCalledOnce()
      expect(screen.getByText(/2 validation issues were reported/)).toBeInTheDocument()
      expect(screen.getByText(/The error report rechecks the workbook against current data/)).toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Download error report' })).toBeInTheDocument()
      await user.click(screen.getByRole('button', { name: 'Done' }))
      expect(onClose).toHaveBeenCalledOnce()
    })

    it.each([true, false])('does not request reload when zero services were created (succeeded=%s)', async succeeded => {
      const user = userEvent.setup()
      const { onImported } = renderServicesDialog()
      vi.mocked(previewImport).mockResolvedValue(servicePreview())
      vi.mocked(executeImport).mockResolvedValue(resultData({ type: 'Services', succeeded, created: 0 }))
      await selectServiceFile(user, 'select')
      await user.click(screen.getByRole('button', { name: 'Preview import' }))
      await user.click(screen.getByRole('button', { name: 'Import 2 services' }))
      await screen.findByRole('heading', { name: 'Import finished' })
      expect(onImported).not.toHaveBeenCalled()
      if (!succeeded) expect(screen.getByText(/No services were created/)).not.toHaveTextContent('stock')
    })

    it.each(['select', 'preview'] as const)('cancel from %s never executes the import', async step => {
      const user = userEvent.setup()
      const { onClose, onImported } = renderServicesDialog()
      vi.mocked(previewImport).mockResolvedValue(servicePreview())
      await selectServiceFile(user, 'select')
      if (step === 'preview') await user.click(screen.getByRole('button', { name: 'Preview import' }))
      await user.click(screen.getByRole('button', { name: 'Cancel' }))
      expect(onClose).toHaveBeenCalledOnce()
      expect(onImported).not.toHaveBeenCalled()
      expect(executeImport).not.toHaveBeenCalled()
    })

    it('resets the workbook and stale preview when choosing another file', async () => {
      const user = userEvent.setup()
      renderServicesDialog()
      vi.mocked(previewImport).mockResolvedValue(servicePreview())
      await selectServiceFile(user, 'drop')
      await user.click(screen.getByRole('button', { name: 'Preview import' }))
      await user.click(screen.getByRole('button', { name: 'Choose another file' }))

      expect(screen.getByText('No workbook selected.')).toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Preview import' })).toBeDisabled()
      expect(screen.queryByLabelText('Row problems')).not.toBeInTheDocument()
      expect(screen.queryByRole('table')).not.toBeInTheDocument()
      const nextFile = new File(['replacement'], 'replacement.xlsx')
      await selectServiceFile(user, 'select', nextFile)
      await user.click(screen.getByRole('button', { name: 'Preview import' }))
      expect(previewImport).toHaveBeenLastCalledWith('services', nextFile)
      expect(executeImport).not.toHaveBeenCalled()
    })

    it.each([
      ['select', 'unsupported'], ['drop', 'unsupported'], ['select', 'oversize'], ['drop', 'oversize'],
    ] as const)('rejects %s of an %s file, clears stale selection and allows correction', async (method, problem) => {
      const user = userEvent.setup({ applyAccept: false })
      renderServicesDialog()
      await selectServiceFile(user, 'select')
      const rejected = problem === 'unsupported'
        ? new File(['csv'], 'services.csv')
        : new File([new Uint8Array(5 * 1024 * 1024 + 1)], 'large.xlsx')
      await selectServiceFile(user, method, rejected)

      expect(screen.getByRole('alert')).toHaveTextContent(problem === 'unsupported' ? 'Other file types are not supported' : 'too large')
      expect(screen.getByText('No workbook selected.')).toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Preview import' })).toBeDisabled()
      expect(previewImport).not.toHaveBeenCalled()
      expect(executeImport).not.toHaveBeenCalled()
      await selectServiceFile(user, method)
      expect(screen.queryByRole('alert')).not.toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Preview import' })).toBeEnabled()
    })

    it('keeps server validation authoritative for an accepted extension and size', async () => {
      const user = userEvent.setup()
      renderServicesDialog()
      vi.mocked(previewImport).mockRejectedValue(new Error('The workbook has invalid headers.'))
      await selectServiceFile(user, 'select')
      await user.click(screen.getByRole('button', { name: 'Preview import' }))
      expect(await screen.findByRole('alert')).toHaveTextContent('The workbook has invalid headers.')
      expect(screen.getByText(serviceWorkbook.name)).toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Preview import' })).toBeEnabled()
      expect(executeImport).not.toHaveBeenCalled()
    })

    it('blocks dismissal, double confirmation and file changes while importing', async () => {
      const user = userEvent.setup()
      const { onClose, onImported } = renderServicesDialog()
      vi.mocked(previewImport).mockResolvedValue(servicePreview())
      let finish!: (result: ImportResult) => void
      vi.mocked(executeImport).mockReturnValue(new Promise(resolve => { finish = resolve }))
      await selectServiceFile(user, 'select')
      await user.click(screen.getByRole('button', { name: 'Preview import' }))
      await user.click(screen.getByRole('button', { name: 'Import 2 services' }))

      const dialog = screen.getByRole('dialog')
      expect(dialog).toHaveAttribute('aria-busy', 'true')
      expect(screen.getByRole('status')).toHaveTextContent('Importing services and saving changes')
      for (const button of within(dialog).getAllByRole('button')) expect(button).toBeDisabled()
      await user.click(screen.getByRole('button', { name: 'Importing…' }))
      await user.click(screen.getByRole('button', { name: 'Cancel' }))
      await user.click(screen.getByRole('button', { name: 'Choose another file' }))
      fireEvent.click(dialog.parentElement!)
      fireEvent.keyDown(dialog, { key: 'Escape' })
      expect(onClose).not.toHaveBeenCalled()
      expect(onImported).not.toHaveBeenCalled()
      expect(executeImport).toHaveBeenCalledOnce()

      await act(async () => finish(resultData({ type: 'Services' })))
      expect(await screen.findByText('Created 2 services.')).toBeInTheDocument()
      expect(dialog).toHaveAttribute('aria-busy', 'false')
      expect(onImported).toHaveBeenCalledOnce()
      expect(screen.getByRole('button', { name: 'Done' })).toBeEnabled()
    })

    it('blocks replacement drops and dismissal while preview processing is in flight', async () => {
      const user = userEvent.setup()
      const { onClose } = renderServicesDialog()
      let finish!: (preview: ImportPreview) => void
      vi.mocked(previewImport).mockReturnValue(new Promise(resolve => { finish = resolve }))
      await selectServiceFile(user, 'select')
      await user.click(screen.getByRole('button', { name: 'Preview import' }))
      expect(screen.getByRole('status')).toHaveTextContent('Checking the workbook')
      expect(screen.getByLabelText('Workbook (.xlsx)')).toBeDisabled()
      await selectServiceFile(user, 'drop', new File(['other'], 'other.xlsx'))
      fireEvent.click(screen.getByRole('dialog').parentElement!)
      expect(screen.getByText(serviceWorkbook.name)).toBeInTheDocument()
      expect(onClose).not.toHaveBeenCalled()
      await act(async () => finish(servicePreview()))
      expect(screen.getByRole('button', { name: 'Import 2 services' })).toBeEnabled()
      expect(executeImport).not.toHaveBeenCalled()
    })
  })
})
