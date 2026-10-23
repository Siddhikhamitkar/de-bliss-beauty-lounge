import { requestBlob, requestForm } from './client.ts'
import type { ImportPreview, ImportResult } from './contracts.ts'

/** Which workbook flavour a dialog or call talks about; matches the API routes. */
export type ImportKind = 'customers' | 'retail' | 'salon-use' | 'services'

/** Column headers per flavour, for rendering the preview grid's fields. */
export const ImportTemplateHeaders: Record<ImportKind, string[]> = {
  customers: [
    'First name',
    'Last name',
    'Mobile',
    'Email',
    'Date of birth',
    'Gender',
    'Address',
    'Notes',
    'Active',
  ],
  retail: [
    'Product name',
    'Brand',
    'SKU',
    'Category',
    'Cost price',
    'Selling price',
    'Minimum stock',
    'Opening stock',
    'Active',
  ],
  'salon-use': [
    'Product name',
    'Brand',
    'SKU',
    'Category',
    'Unit of measure',
    'Minimum stock',
    'Opening stock',
    'Active',
  ],
  services: [
    'Service name',
    'Category name',
    'Price (AED)',
    'Duration (minutes)',
    'Description',
    'Active',
    'Eligible staff',
  ],
}

const TemplateFileNames: Record<ImportKind, string> = {
  customers: 'customer-import-template.xlsx',
  retail: 'retail-products-import-template.xlsx',
  'salon-use': 'salon-use-products-import-template.xlsx',
  services: 'services-import-template.xlsx',
}

export function importTemplateName(kind: ImportKind): string {
  return TemplateFileNames[kind]
}

function route(kind: ImportKind): string {
  return kind === 'customers' || kind === 'services'
    ? `/imports/${kind}`
    : `/imports/inventory/${kind}`
}

export function downloadImportTemplate(kind: ImportKind): Promise<Blob> {
  return requestBlob('GET', `${route(kind)}/template`)
}

export function previewImport(kind: ImportKind, file: File): Promise<ImportPreview> {
  return requestForm('POST', `${route(kind)}/preview`, toForm(file))
}

export function executeImport(
  kind: ImportKind,
  file: File,
  importDuplicates: boolean,
): Promise<ImportResult> {
  const form = toForm(file)

  if (kind === 'customers') {
    form.set('importDuplicates', String(importDuplicates))
  }

  return requestForm('POST', route(kind), form)
}

export function downloadImportErrorReport(kind: ImportKind, file: File): Promise<Blob> {
  return requestBlob('POST', `${route(kind)}/error-report`, toForm(file))
}

function toForm(file: File): FormData {
  const form = new FormData()

  form.append('file', file)

  return form
}

export function saveBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')

  anchor.href = url
  anchor.download = fileName
  document.body.append(anchor)
  anchor.click()
  anchor.remove()
  URL.revokeObjectURL(url)
}
