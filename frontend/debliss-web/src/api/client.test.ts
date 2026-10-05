import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, describeFailure, request, toQueryString } from './client.ts'

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('toQueryString', () => {
  it('drops undefined and empty values', () => {
    expect(
      toQueryString({ search: 'gel', status: undefined, categoryId: '', page: 2 }),
    ).toBe('?search=gel&page=2')
  })

  it('returns an empty string when nothing survives', () => {
    expect(toQueryString({ search: undefined, page: undefined })).toBe('')
  })

  it('encodes reserved characters', () => {
    expect(toQueryString({ search: 'cut & blow' })).toBe('?search=cut+%26+blow')
  })
})

describe('ApiError', () => {
  it('prefers the problem detail', () => {
    const error = new ApiError(404, { status: 404, detail: 'Service not found.' })
    expect(error.message).toBe('Service not found.')
    expect(error.errors).toEqual({})
  })

  it('falls back to the first field message when there is no detail', () => {
    const error = new ApiError(400, {
      status: 400,
      title: 'One or more validation errors occurred.',
      errors: { name: ['Category name is required.'], sortOrder: ['Too big.'] },
    })
    expect(error.message).toBe('Category name is required.')
    expect(error.errors.name).toEqual(['Category name is required.'])
  })

  it('falls back to the title when there is neither detail nor field errors', () => {
    const error = new ApiError(409, { status: 409, title: 'Conflict.' })
    expect(error.message).toBe('Conflict.')
  })
})

describe('describeFailure', () => {
  it('returns the ApiError message as-is', () => {
    const error = new ApiError(404, { detail: 'Gone.' })
    expect(describeFailure(error)).toBe('Gone.')
  })

  it('explains a TypeError as the API being unreachable', () => {
    expect(describeFailure(new TypeError('fetch failed'))).toBe(
      'Cannot reach the De Bliss API. Is the backend running?',
    )
  })

  it('passes through any other error message', () => {
    expect(describeFailure(new Error('boom'))).toBe('boom')
  })

  it('has a last-resort sentence for non-error values', () => {
    expect(describeFailure('weird')).toBe('Something went wrong.')
  })
})

describe('request', () => {
  it('prefixes the path with /api and parses the JSON body', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ id: '1' }), { status: 200 }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const result = await request<{ id: string }>('GET', '/services/1')

    expect(result).toEqual({ id: '1' })
    expect(fetchMock).toHaveBeenCalledWith('/api/services/1', {
      method: 'GET',
      credentials: 'same-origin',
      headers: undefined,
      body: undefined,
    })
  })

  it('resolves with undefined when a success has no body, as a 204 delete does', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(request('DELETE', '/staff/s1/time-off/t1')).resolves.toBeUndefined()
  })

  it('sends a JSON body when one is given', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response('{}', { status: 200 }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await request('POST', '/services', { name: 'Gel Manicure' })

    expect(fetchMock).toHaveBeenCalledWith('/api/services', {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name: 'Gel Manicure' }),
    })
  })

  it('throws an ApiError carrying the field errors on a non-2xx answer', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          status: 400,
          title: 'One or more validation errors occurred.',
          errors: { name: ['Category name is required.'] },
        }),
        { status: 400 },
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    const failure = await request('POST', '/service-categories', {}).catch(
      (error: unknown) => error,
    )

    expect(failure).toBeInstanceOf(ApiError)
    expect((failure as ApiError).status).toBe(400)
    expect((failure as ApiError).errors).toEqual({
      name: ['Category name is required.'],
    })
  })

  it('still reports the status when the failure body is not JSON', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response('<html>proxy error</html>', { status: 502 }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const failure = await request('GET', '/services').catch((error: unknown) => error)

    expect(failure).toBeInstanceOf(ApiError)
    expect((failure as ApiError).status).toBe(502)
  })
})
