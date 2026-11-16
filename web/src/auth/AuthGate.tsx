import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { ApiError, SIGNED_OUT_EVENT, api, refreshSession } from '../api/client'
import type { User } from '../api/types'
import { ErrorNotice, Loading } from '../components/Feedback'
import { SignInPage } from '../pages/SignInPage'
import { AuthContext } from './useAuth'

/** Renew a few minutes before the 15-minute access token expires. */
const RENEW_EVERY_MS = 10 * 60 * 1000

type State = { status: 'checking' } | { status: 'signed-out' } | { status: 'signed-in'; user: User } | { status: 'error'; error: Error }

/**
 * Shows the app to a signed-in user and the sign-in screen to everyone else. The session lives in an HttpOnly
 * cookie the page can't read, so "am I signed in?" is a question for the API (GET /api/auth/me).
 */
export function AuthGate({ children }: { children: ReactNode }) {
  const [state, setState] = useState<State>({ status: 'checking' })

  const check = useCallback(
    () =>
      api
        .me()
        // An expired access token (e.g. after a night away) is renewed from the refresh cookie before giving up.
        .catch(async (error: unknown) => {
          if (error instanceof ApiError && error.status === 401 && (await refreshSession())) return api.me()
          throw error
        })
        .then((user) => setState({ status: 'signed-in', user }))
        .catch((error: unknown) =>
          setState(error instanceof ApiError && error.status === 401 ? { status: 'signed-out' } : { status: 'error', error: error as Error }),
        ),
    [],
  )

  useEffect(() => void check(), [check])

  const retry = () => {
    setState({ status: 'checking' })
    void check()
  }

  // Any request answered with 401 (the session ran out) brings the sign-in screen back.
  useEffect(() => {
    const onSignedOut = () => setState({ status: 'signed-out' })
    window.addEventListener(SIGNED_OUT_EVENT, onSignedOut)
    return () => window.removeEventListener(SIGNED_OUT_EVENT, onSignedOut)
  }, [])

  // Keep the access token fresh while signed in. The audio element can't renew a token itself, so a long
  // episode would otherwise stop when the 15 minutes run out. Also renew on return to a tab that slept.
  const signedIn = state.status === 'signed-in'
  useEffect(() => {
    if (!signedIn) return
    let last = Date.now()
    const renew = () => {
      last = Date.now()
      void refreshSession().then((ok) => {
        if (!ok) setState({ status: 'signed-out' })
      })
    }
    const timer = setInterval(renew, RENEW_EVERY_MS)
    const onVisible = () => {
      if (document.visibilityState === 'visible' && Date.now() - last > RENEW_EVERY_MS) renew()
    }
    document.addEventListener('visibilitychange', onVisible)
    return () => {
      clearInterval(timer)
      document.removeEventListener('visibilitychange', onVisible)
    }
  }, [signedIn])

  const signOut = useCallback(async () => {
    await api.logout().catch(() => undefined) // the screen changes either way; the cookie expires by itself
    setState({ status: 'signed-out' })
  }, [])

  const user = state.status === 'signed-in' ? state.user : null
  const auth = useMemo(() => (user ? { user, signOut } : null), [user, signOut])

  if (state.status === 'checking') {
    return (
      <div className="page">
        <Loading />
      </div>
    )
  }
  if (state.status === 'error') {
    return (
      <div className="page page-narrow">
        <ErrorNotice error={state.error} onRetry={retry} />
      </div>
    )
  }
  if (!auth) return <SignInPage onSignedIn={(u) => setState({ status: 'signed-in', user: u })} />
  return <AuthContext.Provider value={auth}>{children}</AuthContext.Provider>
}
