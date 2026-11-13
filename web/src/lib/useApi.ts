import { useCallback, useEffect, useState } from 'react'

export interface ApiState<T> {
  data: T | undefined
  error: Error | undefined
  loading: boolean
  reload: () => void
  setData: (data: T) => void
}

/** Runs `load` when `deps` change, cancelling the previous request. */
export function useApi<T>(load: (signal: AbortSignal) => Promise<T>, deps: readonly unknown[]): ApiState<T> {
  const [data, setData] = useState<T>()
  const [error, setError] = useState<Error>()
  const [loading, setLoading] = useState(true)
  const [version, setVersion] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    setLoading(true)
    setError(undefined)
    load(controller.signal)
      .then(setData)
      .catch((e: unknown) => {
        if (!controller.signal.aborted) setError(e instanceof Error ? e : new Error(String(e)))
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false)
      })
    return () => controller.abort()
    // eslint-disable-next-line react-hooks/exhaustive-deps -- callers pass the real dependencies
  }, [...deps, version])

  const reload = useCallback(() => setVersion((v) => v + 1), [])
  return { data, error, loading, reload, setData }
}

/** A value that only changes after it stops changing for `ms` (for search boxes and estimates). */
export function useDebounced<T>(value: T, ms: number): T {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => {
    const t = setTimeout(() => setDebounced(value), ms)
    return () => clearTimeout(t)
  }, [value, ms])
  return debounced
}
