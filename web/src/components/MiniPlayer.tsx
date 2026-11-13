import { Link, useLocation } from 'react-router'
import { formatTime } from '../lib/format'
import { usePlayer } from '../player/usePlayer'
import { PauseIcon, PlayIcon } from './Icons'

/** Shown on every page except the episode's own listening page, so audio keeps going while you browse. */
export function MiniPlayer() {
  const player = usePlayer()
  const location = useLocation()
  if (!player.episode || location.pathname === `/episodes/${player.episode.id}`) return null

  const share = player.duration > 0 ? (player.currentTime / player.duration) * 100 : 0
  return (
    <section className="mini-player" aria-label="Now playing">
      <button type="button" className="play" onClick={player.toggle} aria-label={player.playing ? 'Pause' : 'Play'}>
        {player.playing ? <PauseIcon /> : <PlayIcon />}
      </button>
      <Link className="title" to={`/episodes/${player.episode.id}`}>
        {player.episode.title}
      </Link>
      <span className="time">
        {formatTime(player.currentTime)} / {formatTime(player.duration)}
      </span>
      <div className="mini-bar" aria-hidden="true">
        <div style={{ width: `${share}%` }} />
      </div>
    </section>
  )
}
