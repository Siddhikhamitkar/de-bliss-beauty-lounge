import { useState } from 'react'
import type { FormEvent } from 'react'
import { useAuth } from './AuthContext.tsx'

/**
 * The single door into the SPA. Email + password, a show/hide toggle, one
 * loading state on the submit button, and one generic error line — the API
 * answers wrong-password and unknown-email identically so the form cannot
 * leak which addresses exist. No role picker: the backend decides who the
 * user is; the router then lands them on their own dashboard.
 */
export function LoginPage() {
  const { login } = useAuth()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (busy) {
      return
    }

    setBusy(true)
    setError(null)

    try {
      await login(email, password)
      // The route guard in App sees the new user and moves off /login.
    } catch {
      setError('Email or password is incorrect.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="login-screen">
      <div className="login-card">
        <div className="login-brand">
          <span className="login-brand-mark" aria-hidden="true">
            DB
          </span>
          <span className="login-brand-text">
            <span className="login-brand-name">De Bliss</span>
            <span className="login-brand-sub">Beauty Lounge</span>
          </span>
        </div>

        <h1 className="login-heading">Welcome back</h1>
        <p className="login-subline">Sign in to your workspace.</p>

        <form className="login-form" onSubmit={submit}>
          <label className="field">
            <span className="field-label">Email</span>
            <input
              type="email"
              autoComplete="email"
              autoFocus
              required
              value={email}
              onChange={event => setEmail(event.target.value)}
            />
          </label>

          <label className="field">
            <span className="field-label">Password</span>
            <span className="login-password">
              <input
                type={showPassword ? 'text' : 'password'}
                autoComplete="current-password"
                required
                value={password}
                onChange={event => setPassword(event.target.value)}
              />
              <button
                type="button"
                className="login-password-toggle"
                aria-label={showPassword ? 'Hide password' : 'Show password'}
                aria-pressed={showPassword}
                onClick={() => setShowPassword(current => !current)}
              >
                {showPassword ? <EyeOffIcon /> : <EyeIcon />}
              </button>
            </span>
          </label>

          {error !== null && (
            <p className="login-error" role="alert">
              {error}
            </p>
          )}

          <button type="submit" className="button button-primary login-submit" disabled={busy}>
            {busy ? 'Signing in…' : 'Sign in'}
          </button>
        </form>
      </div>
    </div>
  )
}

function EyeIcon() {
  return (
    <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M2.5 12S6 5.8 12 5.8 21.5 12 21.5 12 18 18.2 12 18.2 2.5 12 2.5 12Z" />
      <circle cx="12" cy="12" r="3" />
    </svg>
  )
}

function EyeOffIcon() {
  return (
    <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M4 4l16 16" />
      <path d="M10.6 6.1A9 9 0 0 1 12 5.8c6 0 9.5 6.2 9.5 6.2a16 16 0 0 1-2.7 3.4M6.6 7.4A15 15 0 0 0 2.5 12S6 18.2 12 18.2a8.7 8.7 0 0 0 3.7-.8" />
      <path d="M9.9 9.9a3 3 0 0 0 4.2 4.2" />
    </svg>
  )
}
