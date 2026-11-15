import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { api } from '../api/client'
import type { EpisodeListItem, SeasonDetail } from '../api/types'
import { ErrorNotice, Loading } from '../components/Feedback'
import { formatTime } from '../lib/format'
import { isGenerating, statusLabel, styleLabel } from '../lib/labels'
import { partStates, seasonIsBusy } from '../lib/seasons'
import { useApi } from '../lib/useApi'
import { usePlayer } from '../player/usePlayer'
import './library.css'
import './seasons.css'

const POLL_MS = 3000

export function SeasonPage() {
  const { id = '' } = useParams()
  const season = useApi((signal) => api.getSeason(id, signal), [id])
  const busy = season.data ? seasonIsBusy(season.data) : false
  const { reload } = season

  // While the plan or an episode is being made, refresh so the page moves on by itself.
  useEffect(() => {
    if (!busy) return
    const timer = setInterval(reload, POLL_MS)
    return () => clearInterval(timer)
  }, [busy, reload])

  if (season.error && !season.data) {
    return (
      <div className="page page-narrow">
        <ErrorNotice error={season.error} onRetry={season.reload} />
        <Link to="/seasons">Back to seasons</Link>
      </div>
    )
  }
  if (!season.data) {
    return (
      <div className="page">
        <Loading />
      </div>
    )
  }
  return <SeasonView season={season.data} onChanged={season.reload} />
}

function SeasonView({ season, onChanged }: { season: SeasonDetail; onChanged: () => void }) {
  const navigate = useNavigate()
  const player = usePlayer()
  const [starting, setStarting] = useState(false)
  const [error, setError] = useState<Error>()
  const states = partStates(season)
  const first = season.episodes[0]
  const generating = season.episodes.some((e) => isGenerating(e.status))

  async function makeNext() {
    setStarting(true)
    setError(undefined)
    try {
      await api.createNextEpisode(season.id)
      onChanged()
    } catch (e) {
      setError(e as Error)
    } finally {
      setStarting(false)
    }
  }

  async function remove() {
    if (season.episodes.some((e) => e.id === player.episode?.id)) {
      setError(new Error('An episode of this season is in the player. Play something else before deleting the season.'))
      return
    }
    const what = season.episodes.length === 1 ? 'its episode' : `all ${season.episodes.length} episodes`
    if (!window.confirm(`Delete "${season.title}" with ${what} and the audio? This can't be undone.`)) return
    try {
      await api.deleteSeason(season.id)
      navigate('/seasons')
    } catch (e) {
      setError(e as Error)
    }
  }

  return (
    <div className="page page-narrow">
      <p className="hint">
        <Link to="/seasons">Seasons</Link>
      </p>
      <h1>{season.title}</h1>
      <p className="lede">
        {season.description ? `${season.description} ` : ''}
        {styleLabel[season.style]} at {season.level}, {season.episodeCount} episodes of about {season.durationMin}{' '}
        minutes.
      </p>

      {!season.planned && first && (
        <div className="notice" role="status" aria-live="polite">
          {first.status === 'Failed' || first.status === 'PausedForBudget' ? (
            <p>
              Planning stopped together with episode 1. <Link to={`/episodes/${first.id}`}>Open episode 1</Link> to see
              why and retry.
            </p>
          ) : (
            <p>Planning the season and writing episode 1. This takes a few minutes; the page updates by itself.</p>
          )}
        </div>
      )}

      {error && <ErrorNotice error={error} />}

      {season.planned && (
        <ol className="parts-list">
          {season.parts.map((part, i) => {
            const state = states[i]
            const e = part.episode
            return (
              <li key={part.order} className="part-row" data-state={state.kind}>
                <span className="part-number" aria-hidden="true">
                  {part.order}
                </span>
                <div className="part-body">
                  <h2 className="part-title">
                    {e?.status === 'Ready' ? <Link to={`/episodes/${e.id}`}>{e.title ?? part.title}</Link> : (e?.title ?? part.title)}
                  </h2>
                  {state.kind === 'episode' && e && <PartProgress episode={e} />}
                  {state.kind === 'next' && (
                    <button type="button" className="button button-primary" disabled={starting} onClick={() => void makeNext()}>
                      {starting ? 'Starting…' : `Make episode ${part.order}`}
                    </button>
                  )}
                  {state.kind === 'waiting' && <p className="hint">{state.reason}</p>}
                </div>
              </li>
            )
          })}
        </ol>
      )}

      <div className="season-footer">
        <button type="button" className="button button-quiet button-danger" onClick={() => void remove()} disabled={generating}>
          Delete season
        </button>
        {generating && <p className="hint">You can delete the season once the episode being made is finished.</p>}
      </div>
    </div>
  )
}

function PartProgress({ episode: e }: { episode: EpisodeListItem }) {
  if (e.status !== 'Ready') {
    return (
      <p className="hint">
        {statusLabel[e.status]}
        {isGenerating(e.status) ? '…' : '. '}
        {!isGenerating(e.status) && <Link to={`/episodes/${e.id}`}>Open it to retry</Link>}
      </p>
    )
  }
  const length = e.audioDurationSec ?? e.durationMin * 60
  const position = e.progress?.completed ? length : (e.progress?.positionSec ?? 0)
  return (
    <div className="episode-progress">
      <div className="mini-bar" aria-hidden="true">
        <div style={{ width: `${Math.min(100, (position / length) * 100)}%` }} />
      </div>
      <span className="hint">
        {e.progress?.completed
          ? 'Finished'
          : position > 0
            ? `Stopped at ${formatTime(position)}`
            : `${formatTime(length)}, not started`}
      </span>
    </div>
  )
}
