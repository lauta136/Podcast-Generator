import { Link } from 'react-router'
import { api } from '../api/client'
import { EpisodeList } from '../components/EpisodeList'
import { ErrorNotice, Loading } from '../components/Feedback'
import { formatTime } from '../lib/format'
import { useApi } from '../lib/useApi'
import './library.css'

export function HomePage() {
  const recent = useApi((signal) => api.listEpisodes({ pageSize: 5 }, signal), [])
  const items = recent.data?.items ?? []
  // The most recent episode you started and didn't finish.
  const resume = items.find((e) => e.status === 'Ready' && (e.progress?.positionSec ?? 0) > 0 && !e.progress?.completed)

  return (
    <div className="page">
      <section className="home-intro">
        <h1>Hören, mitlesen, verstehen.</h1>
        <p className="lede">
          Listen to German podcasts written for your level, about topics you choose, and read along as you listen.
        </p>
        <div className="row">
          <Link className="button button-primary" to="/new">
            New episode
          </Link>
          <Link className="button" to="/library">
            Open the library
          </Link>
        </div>
      </section>

      {recent.error && <ErrorNotice error={recent.error} onRetry={recent.reload} />}
      {recent.loading && !recent.data && <Loading />}

      {resume && (
        <section className="resume" aria-labelledby="resume-title">
          <h2 id="resume-title">Continue listening</h2>
          <p>
            <Link to={`/episodes/${resume.id}`} className="resume-link">
              {resume.title ?? resume.topic}
            </Link>
            <span className="hint"> You stopped at {formatTime(resume.progress?.positionSec ?? 0)}.</span>
          </p>
        </section>
      )}

      {recent.data && items.length > 0 && (
        <section aria-labelledby="recent-title">
          <h2 id="recent-title">Recent episodes</h2>
          <EpisodeList items={items} />
        </section>
      )}
    </div>
  )
}
