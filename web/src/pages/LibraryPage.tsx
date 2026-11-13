import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { api } from '../api/client'
import { LEVELS, type EpisodeListItem } from '../api/types'
import { EpisodeList } from '../components/EpisodeList'
import { ErrorNotice, Loading } from '../components/Feedback'
import { usePlayer } from '../player/usePlayer'
import { useApi, useDebounced } from '../lib/useApi'
import './library.css'

const PAGE_SIZE = 20
const STATUS_FILTERS = [
  { value: '', label: 'Any status' },
  { value: 'Ready', label: 'Ready' },
  { value: 'GeneratingSections', label: 'Being written' },
  { value: 'GeneratingAudio', label: 'Being recorded' },
  { value: 'Failed', label: 'Failed' },
  { value: 'PausedForBudget', label: 'Paused' },
]

export function LibraryPage() {
  // Filters live in the URL, so back/forward and reloads keep them.
  const [params, setParams] = useSearchParams()
  const search = params.get('q') ?? ''
  const level = params.get('level') ?? ''
  const status = params.get('status') ?? ''
  const page = Math.max(1, Number(params.get('page') ?? '1') || 1)
  const [searchText, setSearchText] = useState(search)
  const debouncedSearch = useDebounced(searchText, 300)
  const player = usePlayer()
  const [deleteError, setDeleteError] = useState<Error>()

  const update = (changes: Record<string, string>) =>
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        for (const [k, v] of Object.entries(changes)) {
          if (v) next.set(k, v)
          else next.delete(k)
        }
        if (!('page' in changes)) next.delete('page')
        return next
      },
      { replace: true },
    )

  useEffect(() => {
    if (debouncedSearch === (params.get('q') ?? '')) return
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        if (debouncedSearch) next.set('q', debouncedSearch)
        else next.delete('q')
        next.delete('page')
        return next
      },
      { replace: true },
    )
  }, [debouncedSearch, params, setParams])

  const list = useApi(
    (signal) => api.listEpisodes({ search, level, status, page, pageSize: PAGE_SIZE }, signal),
    [search, level, status, page],
  )

  async function remove(item: EpisodeListItem) {
    if (!window.confirm(`Delete "${item.title ?? item.topic}" and its audio? This can't be undone.`)) return
    try {
      await api.deleteEpisode(item.id)
      setDeleteError(undefined)
      list.reload()
    } catch (e) {
      setDeleteError(e as Error)
    }
  }

  const filtered = search !== '' || level !== '' || status !== ''
  const pages = list.data ? Math.max(1, Math.ceil(list.data.total / PAGE_SIZE)) : 1

  return (
    <div className="page">
      <div className="library-head">
        <h1>Library</h1>
        <Link className="button button-primary" to="/new">
          New episode
        </Link>
      </div>

      <div className="filters" role="search">
        <label className="visually-hidden" htmlFor="search">
          Search by title or topic
        </label>
        <input
          id="search"
          className="input"
          type="search"
          placeholder="Search by title or topic"
          value={searchText}
          onChange={(e) => setSearchText(e.target.value)}
        />
        <label className="visually-hidden" htmlFor="level-filter">
          Level
        </label>
        <select id="level-filter" className="select" value={level} onChange={(e) => update({ level: e.target.value })}>
          <option value="">Any level</option>
          {LEVELS.map((l) => (
            <option key={l} value={l}>
              {l}
            </option>
          ))}
        </select>
        <label className="visually-hidden" htmlFor="status-filter">
          Status
        </label>
        <select id="status-filter" className="select" value={status} onChange={(e) => update({ status: e.target.value })}>
          {STATUS_FILTERS.map((s) => (
            <option key={s.value} value={s.value}>
              {s.label}
            </option>
          ))}
        </select>
      </div>

      {deleteError && <ErrorNotice error={deleteError} />}
      {list.error && <ErrorNotice error={list.error} onRetry={list.reload} />}
      {list.loading && !list.data && <Loading />}

      {list.data && list.data.items.length === 0 && (
        <div className="empty">
          {filtered ? (
            <>
              <p>No episodes match these filters.</p>
              <button type="button" className="button" onClick={() => { setSearchText(''); setParams({}, { replace: true }) }}>
                Clear filters
              </button>
            </>
          ) : (
            <>
              <h2>Your library is empty</h2>
              <p>Create your first episode: pick a topic you'd enjoy hearing about, in German at your level.</p>
              <Link className="button button-primary" to="/new">
                New episode
              </Link>
            </>
          )}
        </div>
      )}

      {list.data && list.data.items.length > 0 && (
        <>
          <p className="hint" aria-live="polite">
            {list.data.total === 1 ? '1 episode' : `${list.data.total} episodes`}
          </p>
          <EpisodeList items={list.data.items} onDelete={(e) => (player.episode?.id === e.id ? setDeleteError(new Error('Stop playing this episode before deleting it.')) : void remove(e))} />
          {pages > 1 && (
            <nav className="pager" aria-label="Pages">
              <button type="button" className="button" disabled={page <= 1} onClick={() => update({ page: String(page - 1) })}>
                Previous
              </button>
              <span>
                Page {page} of {pages}
              </span>
              <button type="button" className="button" disabled={page >= pages} onClick={() => update({ page: String(page + 1) })}>
                Next
              </button>
            </nav>
          )}
        </>
      )}
    </div>
  )
}
