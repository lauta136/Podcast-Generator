import { useState, type FormEvent } from 'react'
import { ApiError, api } from '../api/client'
import type { User } from '../api/types'
import './signin.css'

type Mode = 'sign-in' | 'register'

export function SignInPage({ onSignedIn }: { onSignedIn: (user: User) => void }) {
  const [mode, setMode] = useState<Mode>('sign-in')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<Error>()

  const registering = mode === 'register'
  const fieldError = (name: string) => (error instanceof ApiError ? error.fieldErrors[name]?.[0] : undefined)
  const formError = error && !fieldError('email') && !fieldError('password') ? error.message : undefined

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(undefined)
    try {
      onSignedIn(await (registering ? api.register(email, password) : api.login(email, password)))
    } catch (err) {
      setError(err as Error)
      setBusy(false)
    }
  }

  const switchMode = (next: Mode) => {
    setMode(next)
    setError(undefined)
  }

  return (
    <main className="signin">
      <p className="brand signin-brand">Podcast Generator</p>
      <h1>{registering ? 'Create your account' : 'Sign in'}</h1>
      <p className="lede">
        {registering
          ? 'Your episodes, level and listening position are saved to your account.'
          : 'Listen to German podcasts written for your level, and read along.'}
      </p>

      <form onSubmit={submit} noValidate>
        <div className="field">
          <label htmlFor="email">Email</label>
          <input
            id="email"
            className="input"
            type="email"
            autoComplete="username"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            aria-invalid={fieldError('email') ? true : undefined}
            aria-describedby={fieldError('email') ? 'email-error' : undefined}
            required
          />
          {fieldError('email') && (
            <p id="email-error" className="field-error">
              {fieldError('email')}
            </p>
          )}
        </div>

        <div className="field">
          <label htmlFor="password">Password</label>
          <input
            id="password"
            className="input"
            type="password"
            autoComplete={registering ? 'new-password' : 'current-password'}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            aria-invalid={fieldError('password') ? true : undefined}
            aria-describedby="password-hint"
            required
          />
          {registering && !fieldError('password') && (
            <p id="password-hint" className="hint">
              8 characters or more. A few unrelated words work well.
            </p>
          )}
          {fieldError('password') && (
            <p id="password-hint" className="field-error">
              {fieldError('password')}
            </p>
          )}
        </div>

        {formError && (
          <div className="notice notice-error" role="alert">
            <p>{formError}</p>
          </div>
        )}

        <button type="submit" className="button button-primary signin-submit" disabled={busy || email.trim() === '' || password === ''}>
          {busy ? 'One moment…' : registering ? 'Create account' : 'Sign in'}
        </button>
      </form>

      <p className="signin-switch">
        {registering ? 'Already have an account?' : 'New here?'}{' '}
        <button type="button" className="link" onClick={() => switchMode(registering ? 'sign-in' : 'register')}>
          {registering ? 'Sign in' : 'Create an account'}
        </button>
      </p>
    </main>
  )
}
