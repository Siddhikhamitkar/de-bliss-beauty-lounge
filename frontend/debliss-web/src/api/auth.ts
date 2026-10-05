import { ApiError, request } from './client.ts'

/**
 * The safe user projection the auth endpoints return (and the only user shape
 * the SPA ever sees — no password material, no entity internals).
 */
export type RoleName = 'OWNER' | 'RECEPTIONIST' | 'STAFF'

export interface AuthUser {
  id: string
  firstName: string
  lastName: string
  email: string
  roles: RoleName[]
  staffId: string | null
}

export function login(email: string, password: string): Promise<AuthUser> {
  return request<AuthUser>('POST', '/auth/login', { email, password })
}

/**
 * The current session, or null when there is none. A 401 is a normal answer
 * here (no/expired cookie), not an error worth surfacing.
 */
export async function fetchCurrentUser(): Promise<AuthUser | null> {
  try {
    return await request<AuthUser>('GET', '/auth/me')
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) {
      return null
    }

    throw error
  }
}

export function logout(): Promise<void> {
  return request<void>('POST', '/auth/logout')
}
