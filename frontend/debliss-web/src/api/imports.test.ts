import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  downloadImportErrorReport,
  downloadImportTemplate,
  executeImport,
  ImportTemplateHeaders,
  importTemplateName,
  previewImport,
  type ImportKind,
} from './imports.ts'

const file = new File(['workbook'], 'services.xlsx')

afterEach(() => vi.unstubAllGlobals())

describe('Excel import API', () => {
  it('defines the exact seven service columns and template name', () => {
    expect(ImportTemplateHeaders.services).toEqual([
      'Service name', 'Category name', 'Price (AED)', 'Duration (minutes)',
      'Description', 'Active', 'Eligible staff',
    ])
    expect(importTemplateName('services')).toBe('services-import-template.xlsx')
  })

  it.each([
    ['services', '/api/imports/services'],
    ['customers', '/api/imports/customers'],
    ['retail', '/api/imports/inventory/retail'],
    ['salon-use', '/api/imports/inventory/salon-use'],
  ] satisfies [ImportKind, string][])('keeps %s template, preview, execution and error-report contracts', async (kind, route) => {
    const response = { type: kind === 'services' ? 'Services' : kind, fileName: file.name }
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(
      new Response(JSON.stringify(response), { status: 200 }),
    ))
    vi.stubGlobal('fetch', fetchMock)

    await downloadImportTemplate(kind)
    expect(fetchMock).toHaveBeenLastCalledWith(`${route}/template`, {
      method: 'GET', credentials: 'same-origin', body: undefined,
    })

    expect(await previewImport(kind, file)).toEqual(response)
    expect(fetchMock).toHaveBeenLastCalledWith(`${route}/preview`, {
      method: 'POST', credentials: 'same-origin', body: expect.any(FormData),
    })
    const previewForm = fetchMock.mock.calls[1][1].body as FormData
    expect(previewForm.get('file')).toBe(file)
    expect([...previewForm.keys()]).toEqual(['file'])

    expect(await executeImport(kind, file, true)).toEqual(response)
    expect(fetchMock).toHaveBeenLastCalledWith(route, {
      method: 'POST', credentials: 'same-origin', body: expect.any(FormData),
    })
    const importForm = fetchMock.mock.calls[2][1].body as FormData
    expect(importForm.get('file')).toBe(file)
    expect(importForm.get('importDuplicates')).toBe(kind === 'customers' ? 'true' : null)

    await downloadImportErrorReport(kind, file)
    expect(fetchMock).toHaveBeenLastCalledWith(`${route}/error-report`, {
      method: 'POST', credentials: 'same-origin', body: expect.any(FormData),
    })
    const reportForm = fetchMock.mock.calls[3][1].body as FormData
    expect(reportForm.get('file')).toBe(file)
    expect([...reportForm.keys()]).toEqual(['file'])
  })

  it('surfaces service workbook validation failures from the server', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      errors: { file: ['Invalid workbook headers.'] },
    }), { status: 400 })))

    await expect(previewImport('services', file)).rejects.toThrow('Invalid workbook headers.')
  })
})
