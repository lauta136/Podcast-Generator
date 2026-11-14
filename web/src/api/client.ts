import type {
  Budget,
  CreateEpisodeRequest,
  DifficultyRating,
  Episode,
  EpisodePage,
  EpisodeStatusInfo,
  Estimate,
  Preferences,
  PreferencesUpdate,
  Progress,
  Transcript,
  User,
  Voice,
} from './types'

/** Fired on window when a request outside /api/auth is answered with 401: the session has ended. */
export const SIGNED_OUT_EVENT = 'podcast:signed-out'

/** A failed request, with the API's ProblemDetails turned into something a form can show. */
export class ApiError extends Error {
  readonly status: number
  /** Field name (camelCase, as the form uses it) → messages. */
  readonly fieldErrors: Record<string, string[]>

  constructor(status: number, message: string, fieldErrors: Record<string, string[]> = {}) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.fieldErrors = fieldErrors
  }
}

interface ProblemDetails {
  title?: string
  detail?: string
  errors?: Record<string, string[]>
}

const camel = (key: string) => (key.length > 0 ? key[0].toLowerCase() + key.slice(1) : key)

export async function toApiError(response: Response): Promise<ApiError> {
  let problem: ProblemDetails = {}
  try {
    problem = (await response.json()) as ProblemDetails
  } catch {
    // not JSON (e.g. a proxy error page)
  }

  const fieldErrors = Object.fromEntries(Object.entries(problem.errors ?? {}).map(([k, v]) => [camel(k), v]))
  const message =
    problem.detail ??
    Object.values(fieldErrors).flat()[0] ??
    problem.title ??
    (response.status === 429
      ? 'Too many attempts. Wait a minute, then try again.'
      : response.status >= 500
        ? 'The server ran into a problem. Try again in a moment.'
        : `Request failed (${response.status}).`)
  return new ApiError(response.status, message, fieldErrors)
}

async function request<T>(method: string, url: string, body?: unknown, signal?: AbortSignal): Promise<T> {
  let response: Response
  try {
    response = await fetch(url, {
      method,
      signal,
      headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    })
  } catch (e) {
    if (e instanceof DOMException && e.name === 'AbortError') throw e
    throw new ApiError(0, 'Cannot reach the server. Check that the API is running.')
  }

  if (response.status === 401 && !url.startsWith('/api/auth/')) window.dispatchEvent(new Event(SIGNED_OUT_EVENT))
  if (!response.ok) throw await toApiError(response)
  if (response.status === 204 || response.status === 202) {
    const text = await response.text()
    return (text ? JSON.parse(text) : undefined) as T
  }
  return (await response.json()) as T
}

const query = (params: Record<string, string | number | undefined | null>) => {
  const q = new URLSearchParams()
  for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== null && v !== '') q.set(k, String(v))
  const s = q.toString()
  return s ? `?${s}` : ''
}

export const api = {
  me: (signal?: AbortSignal) => request<User>('GET', '/api/auth/me', undefined, signal),
  register: (email: string, password: string) => request<User>('POST', '/api/auth/register', { email, password }),
  login: (email: string, password: string) => request<User>('POST', '/api/auth/login', { email, password }),
  logout: () => request<void>('POST', '/api/auth/logout'),

  getPreferences: (signal?: AbortSignal) => request<Preferences>('GET', '/api/preferences', undefined, signal),
  savePreferences: (p: PreferencesUpdate) => request<Preferences>('PUT', '/api/preferences', p),
  getVoices: (signal?: AbortSignal) => request<Voice[]>('GET', '/api/voices?language=de', undefined, signal),
  getBudget: (signal?: AbortSignal) => request<Budget[]>('GET', '/api/tts/budget', undefined, signal),

  estimate: (p: { level?: string; durationMin?: number; speakingRate?: number }, signal?: AbortSignal) =>
    request<Estimate>('GET', `/api/episodes/estimate${query(p)}`, undefined, signal),
  createEpisode: (r: CreateEpisodeRequest) => request<Episode>('POST', '/api/episodes', r),
  listEpisodes: (
    p: { search?: string; level?: string; status?: string; page?: number; pageSize?: number },
    signal?: AbortSignal,
  ) => request<EpisodePage>('GET', `/api/episodes${query(p)}`, undefined, signal),
  getEpisode: (id: string, signal?: AbortSignal) => request<Episode>('GET', `/api/episodes/${id}`, undefined, signal),
  getStatus: (id: string, signal?: AbortSignal) =>
    request<EpisodeStatusInfo>('GET', `/api/episodes/${id}/status`, undefined, signal),
  getTranscript: (id: string, signal?: AbortSignal) =>
    request<Transcript>('GET', `/api/episodes/${id}/transcript`, undefined, signal),
  retry: (id: string) => request<void>('POST', `/api/episodes/${id}/retry`),
  deleteEpisode: (id: string) => request<void>('DELETE', `/api/episodes/${id}`),
  rate: (id: string, rating: DifficultyRating) => request<void>('PUT', `/api/episodes/${id}/rating`, { rating }),
  getProgress: (id: string, signal?: AbortSignal) =>
    request<Progress>('GET', `/api/episodes/${id}/progress`, undefined, signal),
  saveProgress: (id: string, positionSec: number, completed: boolean) =>
    request<Progress>('PUT', `/api/episodes/${id}/progress`, { positionSec, completed }),
  audioUrl: (id: string) => `/api/episodes/${id}/audio`,
}
