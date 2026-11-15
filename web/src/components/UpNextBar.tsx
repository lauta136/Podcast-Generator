import { useEffect } from 'react'
import { Link, useLocation, useNavigate } from 'react-router'
import { usePlayer } from '../player/usePlayer'

/**
 * After a season episode ends: a countdown to the next part (Play now / Cancel), or a note that the next part
 * hasn't been made yet. When the next part starts, the page of the finished one follows to the new one.
 */
export function UpNextBar() {
  const player = usePlayer()
  const { upNext, lastAdvance } = player
  const location = useLocation()
  const navigate = useNavigate()

  useEffect(() => {
    if (lastAdvance && location.pathname === `/episodes/${lastAdvance.from}`) {
      navigate(`/episodes/${lastAdvance.to}`, { replace: false })
    }
    // Only react to a new advance, not to every navigation.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [lastAdvance])

  if (!upNext) return null
  return (
    <section className="up-next" aria-label="Up next" aria-live="polite">
      {upNext.kind === 'countdown' ? (
        <>
          <p className="up-next-text">
            Up next: part {upNext.order}, <strong>{upNext.title}</strong>, in {upNext.secondsLeft} s
          </p>
          <div className="up-next-actions">
            <button type="button" className="button button-primary" onClick={player.playUpNext}>
              Play now
            </button>
            <button type="button" className="button button-quiet" onClick={player.cancelUpNext}>
              Cancel
            </button>
          </div>
        </>
      ) : (
        <>
          <p className="up-next-text">
            Part {upNext.order}, <strong>{upNext.title}</strong>, isn't made yet.
          </p>
          <div className="up-next-actions">
            <Link className="button button-primary" to={`/seasons/${upNext.seasonId}`} onClick={player.cancelUpNext}>
              Open the season
            </Link>
            <button type="button" className="button button-quiet" onClick={player.cancelUpNext}>
              Dismiss
            </button>
          </div>
        </>
      )}
    </section>
  )
}
