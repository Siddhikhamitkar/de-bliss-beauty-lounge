import type { Problem } from './contracts.ts'

/**
 * A non-2xx answer, already decoded into the problem document the API always
 * returns. `errors` is keyed by request field, which is what lets a form mark
 * the individual inputs that failed instead of showing one vague message.
 */
export class ApiError extends Error {
  readonly status: number
  readonly errors: Record<string, string[]>

  constructor(status: number, problem: Problem) {
    super(summarize(status, problem))
    this.name = 'ApiError'
    this.status = status
    this.errors = problem.errors ?? {}
  }
}

/**
 * The most specific sentence available. A 400 carries no `detail` — only a
 * generic title and a map of field messages — so without this fallback an
 * action outside a form, such as the status toggle on the service list, would
 * tell the user "One or more validation errors occurred." and nothing more.
 */
function summarize(status: number, problem: Problem): string {
  if (problem.detail) {
    return problem.detail
  }

  const firstFieldMessage = Object.values(problem.errors ?? {})[0]?.[0]

  if (firstFieldMessage) {
    return firstFieldMessage
  }

  return problem.title ?? `The API answered ${status}.`
}

/** Turns anything thrown into a sentence worth showing a person. */
export function describeFailure(error: unknown): string {
  if (error instanceof ApiError) {
    return error.message
  }

  if (error instanceof TypeError) {
    // fetch rejects with a TypeError when the request never reached the API at
    // all — the backend is stopped, or the dev-server proxy has no target.
    return 'Cannot reach the De Bliss API. Is the backend running?'
  }

  if (error instanceof Error) {
    return error.message
  }

  return 'Something went wrong.'
}

type QueryValue = string | number | boolean | undefined

/**
 * Builds a query string, dropping the keys a caller left undefined so the URL
 * carries only the filters that are actually narrowing the result.
 */
export function toQueryString(values: Record<string, QueryValue>): string {
  const search = new URLSearchParams()

  for (const [key, value] of Object.entries(values)) {
    if (value !== undefined && value !== '') {
      search.set(key, String(value))
    }
  }

  const text = search.toString()

  return text.length === 0 ? '' : `?${text}`
}

/**
 * Every call goes through here so the relative `/api` prefix — and with it the
 * dev-server proxy that makes the browser's requests same-origin — is written
 * down in exactly one place.
 */
export async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const hasBody = body !== undefined

  const response = await fetch(`/api${path}`, {
    method,
    credentials: 'same-origin',
    headers: hasBody ? { 'Content-Type': 'application/json' } : undefined,
    body: hasBody ? JSON.stringify(body) : undefined,
  })

  if (!response.ok) {
    // A 401 outside the auth endpoints themselves means the session expired or
    // was revoked: the auth context listens for this and returns to the login
    // page. Login's own 401 (bad credentials) must not trigger that.
    if (response.status === 401 && !path.startsWith('/auth/')) {
      window.dispatchEvent(new CustomEvent('debliss:unauthorized'))
    }

    throw new ApiError(response.status, await readProblem(response))
  }

  // A 204 (the time-off delete, for example) has no body to parse.
  const text = await response.text()

  return (text ? (JSON.parse(text) as T) : undefined) as T
}

/**
 * POSTs multipart form data — an uploaded workbook — and parses the JSON
 * answer, with the same 401 and problem-document handling as `request`.
 */
export async function requestForm<T>(method: string, path: string, form: FormData): Promise<T> {
  const response = await fetch(`/api${path}`, {
    method,
    credentials: 'same-origin',
    body: form,
  })

  if (!response.ok) {
    if (response.status === 401 && !path.startsWith('/auth/')) {
      window.dispatchEvent(new CustomEvent('debliss:unauthorized'))
    }

    throw new ApiError(response.status, await readProblem(response))
  }

  const text = await response.text()

  return (text ? (JSON.parse(text) as T) : undefined) as T
}

/**
 * Fetches a file — an import template or error report — as bytes for the
 * browser to save. Multipart form is optional so template downloads need no
 * body; errors decode as problems exactly like every other call.
 */
export async function requestBlob(method: string, path: string, form?: FormData): Promise<Blob> {
  const response = await fetch(`/api${path}`, {
    method,
    credentials: 'same-origin',
    body: form,
  })

  if (!response.ok) {
    if (response.status === 401 && !path.startsWith('/auth/')) {
      window.dispatchEvent(new CustomEvent('debliss:unauthorized'))
    }

    throw new ApiError(response.status, await readProblem(response))
  }

  return response.blob()
}

/**
 * A body that is not JSON means something other than the API answered — a proxy
 * error page, typically. That is reported as an empty problem so the caller
 * still gets the status code rather than a parse failure masking it.
 */
async function readProblem(response: Response): Promise<Problem> {
  try {
    return (await response.json()) as Problem
  } catch {
    return { status: response.status }
  }
}
