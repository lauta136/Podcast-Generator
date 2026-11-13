import type { ReactNode } from 'react'

export function ErrorNotice({ error, onRetry }: { error: Error; onRetry?: () => void }) {
  return (
    <div className="notice notice-error" role="alert">
      <p>{error.message}</p>
      {onRetry && (
        <button type="button" className="button" onClick={onRetry}>
          Try again
        </button>
      )}
    </div>
  )
}

export function Loading({ children = 'Loading…' }: { children?: ReactNode }) {
  return (
    <p className="loading" role="status">
      {children}
    </p>
  )
}
