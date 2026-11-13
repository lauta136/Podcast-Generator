import { Link } from 'react-router'
import type { EpisodeListItem } from '../api/types'
import { formatTime } from '../lib/format'
import { isGenerating, statusLabel, styleLabel } from '../lib/labels'

/** Library rows: title, what it is, and where you stopped. */
export function EpisodeList({ items, onDelete }: { items: EpisodeListItem[]; onDelete?: (item: EpisodeListItem) => void }) {
  return (
    <ul className="episodes">
      {items.map((e) => (
        <EpisodeRow key={e.id} item={e} onDelete={onDelete} />
      ))}
    </ul>
  )
}

function EpisodeRow({ item: e, onDelete }: { item: EpisodeListItem; onDelete?: (item: EpisodeListItem) => void }) {
  const length = e.audioDurationSec ?? e.durationMin * 60
  const position = e.progress?.completed ? length : (e.progress?.positionSec ?? 0)
  const share = length > 0 ? Math.min(100, (position / length) * 100) : 0
  const started = position > 0 && !e.progress?.completed

  return (
    <li className="episode">
      <div className="episode-main">
        <Link className="episode-title" to={`/episodes/${e.id}`}>
          {e.title ?? e.topic}
        </Link>
        <p className="episode-meta">
          {styleLabel[e.style]} at {e.level}, {e.audioDurationSec ? formatTime(e.audioDurationSec) : `about ${e.durationMin} min`}
          {e.status !== 'Ready' && (
            <span className="episode-status" data-status={isGenerating(e.status) ? 'working' : e.status}>
              {statusLabel[e.status]}
            </span>
          )}
        </p>
        {e.status === 'Ready' && (
          <div className="episode-progress">
            <div className="mini-bar" aria-hidden="true">
              <div style={{ width: `${share}%` }} />
            </div>
            <span className="hint">
              {e.progress?.completed ? 'Finished' : started ? `Stopped at ${formatTime(position)}` : 'Not started'}
            </span>
          </div>
        )}
      </div>
      <div className="episode-actions">
        {e.status === 'Ready' && (
          <Link className="button" to={`/episodes/${e.id}`}>
            {started ? 'Resume' : 'Listen'}
          </Link>
        )}
        {onDelete && !isGenerating(e.status) && (
          <button type="button" className="button button-quiet button-danger" onClick={() => onDelete(e)}>
            Delete<span className="visually-hidden"> {e.title ?? e.topic}</span>
          </button>
        )}
      </div>
    </li>
  )
}
