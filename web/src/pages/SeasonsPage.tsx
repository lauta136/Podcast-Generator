import { Link } from 'react-router'
import { api } from '../api/client'
import { ErrorNotice, Loading } from '../components/Feedback'
import { useApi } from '../lib/useApi'
import './library.css'
import './seasons.css'

export function SeasonsPage() {
  const seasons = useApi((signal) => api.listSeasons(signal), [])
  return (
    <div className="page">
      <div className="library-head">
        <h1>Seasons</h1>
        <Link className="button button-primary" to="/seasons/new">
          New season
        </Link>
      </div>
      <p className="lede">
        A season is a series of episodes on one big topic. Each episode picks up where the last one ended, so the story
        builds as you listen.
      </p>

      {seasons.error && <ErrorNotice error={seasons.error} onRetry={seasons.reload} />}
      {seasons.loading && !seasons.data && <Loading />}
      {seasons.data && seasons.data.length === 0 && (
        <div className="empty">
          <h2>No seasons yet</h2>
          <p>
            Pick a big topic, such as the Hanseatic League or how Berlin grew, and listen to it part by part.
          </p>
          <Link className="button button-primary" to="/seasons/new">
            New season
          </Link>
        </div>
      )}
      {seasons.data && seasons.data.length > 0 && (
        <ul className="episodes">
          {seasons.data.map((s) => (
            <li key={s.id} className="episode">
              <div className="episode-main">
                <Link className="episode-title" to={`/seasons/${s.id}`}>
                  {s.title}
                </Link>
                <p className="episode-meta">{s.planned ? (s.description ?? s.topic) : 'Planning the season…'}</p>
                <div className="episode-progress">
                  <div className="mini-bar" aria-hidden="true">
                    <div style={{ width: `${(s.episodesFinished / s.episodeCount) * 100}%` }} />
                  </div>
                  <span className="hint">
                    {s.episodesFinished} of {s.episodeCount} parts listened, {s.episodesReady} ready
                  </span>
                </div>
              </div>
              <div className="episode-actions">
                <Link className="button" to={`/seasons/${s.id}`}>
                  Open
                </Link>
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
