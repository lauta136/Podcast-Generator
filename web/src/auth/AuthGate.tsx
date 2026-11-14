import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { ApiError, SIGNED_OUT_EVENT, api } from '../api/client'
import type { User } from '../api/types'
import { ErrorNotice, Loading } from '../components/Feedback'
import { SignInPage } from '../pages/SignInPage'
import { AuthContext } from './useAuth'

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
