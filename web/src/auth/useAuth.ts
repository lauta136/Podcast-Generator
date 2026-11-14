import { createContext, useContext } from 'react'
import type { User } from '../api/types'

export interface Auth {
  user: User
  signOut: () => Promise<void>
}

export const AuthContext = createContext<Auth | null>(null)

/** The signed-in user. Only rendered below <AuthGate>, so there always is one. */
export function useAuth(): Auth {
  const auth = useContext(AuthContext)
  if (!auth) throw new Error('useAuth must be used inside <AuthGate>')
  return auth
}
