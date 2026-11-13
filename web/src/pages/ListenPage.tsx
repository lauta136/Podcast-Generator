import { Fragment, useEffect, useMemo, useRef, useState } from 'react'
import { Link, useParams } from 'react-router'
import { api } from '../api/client'
import type { DifficultyRating, Episode, EpisodeStatusInfo, Progress, Transcript } from '../api/types'
import { ErrorNotice, Loading } from '../components/Feedback'
import { PauseIcon, PlayIcon } from '../components/Icons'
import { formatTime } from '../lib/format'
import { formatRate, isGenerating, ratingLabel, statusLabel, styleLabel } from '../lib/labels'
import { activeSegmentIndex, timeline } from '../lib/transcript'
import { useApi } from '../lib/useApi'
import { SPEEDS, usePlayer } from '../player/usePlayer'
import './listen.css'

const POLL_MS = 2500

export function ListenPage() {
  const { id = '' } = useParams()
  const episode = useApi((signal) => api.getEpisode(id, signal), [id])

  if (episode.error) {
    return (
      <div className="page page-narrow">
        <ErrorNotice error={episode.error} onRetry={episode.reload} />
        <Link to="/library">Back to the library</Link>
      </div>
    )
  }
  if (!episode.data) {
    return (
      <div className="page">
        <Loading />
      </div>
    )
  }
  if (episode.data.status !== 'Ready') {
    return <GenerationView episode={episode.data} onReady={episode.reload} />
  }
  if (episode.data.audioDurationSec === null) {
    // Episodes made before audio generation existed are Ready without a recording.
    return (
      <div className="page page-narrow">
        <h1>{episode.data.title ?? episode.data.topic}</h1>
        <div className="notice" role="status">
          <p>This episode has no recording. Create a new episode on the same topic to listen to it.</p>
        </div>
        <Link className="button" to="/new">
          New episode
        </Link>
      </div>
    )
  }
  return <ReadyView key={episode.data.id} episode={episode.data} onRated={episode.setData} />
}

/** Progress while the episode is made; polls the status endpoint. Failed and paused episodes can be retried. */
function GenerationView({ episode, onReady }: { episode: Episode; onReady: () => void }) {
  const [status, setStatus] = useState<EpisodeStatusInfo>()
  const [error, setError] = useState<Error>()
  const [retrying, setRetrying] = useState(false)
  const current = status?.status ?? episode.status
  // Poll until the first answer, then only while generating. Depends on this flag, not on each new status,
  // so a response doesn't restart the timer.
  const polling = status === undefined || isGenerating(status.status)

  useEffect(() => {
    if (!polling) return
    const controller = new AbortController()
    const tick = () =>
      api
        .getStatus(episode.id, controller.signal)
        .then((s) => {
          setStatus(s)
          setError(undefined)
          if (s.status === 'Ready') onReady()
        })
        .catch((e: unknown) => {
          if (!controller.signal.aborted) setError(e as Error)
        })
    void tick()
    const timer = setInterval(() => void tick(), POLL_MS)
    return () => {
      controller.abort()
      clearInterval(timer)
    }
  }, [episode.id, polling, onReady])

  async function retry() {
    setRetrying(true)
    try {
      await api.retry(episode.id)
      setStatus((s) => (s ? { ...s, status: 'Pending', lastError: null } : s))
    } catch (e) {
      setError(e as Error)
    } finally {
      setRetrying(false)
    }
  }

  const steps = [
    { label: 'Plan the episode', done: (status?.sectionsTotal ?? 0) > 0, active: current === 'GeneratingOutline' || current === 'Pending' },
    {
      label: status && status.sectionsTotal > 0 ? `Write ${status.sectionsCompleted} of ${status.sectionsTotal} ${status.sectionsTotal === 1 ? 'part' : 'parts'}` : 'Write the parts',
      done: status !== undefined && status.sectionsTotal > 0 && status.sectionsCompleted === status.sectionsTotal,
      active: current === 'GeneratingSections',
    },
    {
      label: status && status.audioTotal > 0 ? `Record ${status.audioCompleted} of ${status.audioTotal} ${status.audioTotal === 1 ? 'paragraph' : 'paragraphs'}` : 'Record the audio',
      done: current === 'Ready',
      active: current === 'GeneratingAudio',
    },
  ]

  return (
    <div className="page page-narrow">
      <h1>{status?.title ?? episode.title ?? episode.topic}</h1>
      <p className="lede">
        {styleLabel[episode.style]} at {episode.level}, about {episode.durationMin} minutes
      </p>

      {isGenerating(current) && (
        <ol className="steps" aria-live="polite">
          {steps.map((s) => (
            <li key={s.label} data-state={s.done ? 'done' : s.active ? 'active' : 'waiting'}>
              {s.label}
            </li>
          ))}
        </ol>
      )}

      {current === 'Failed' && (
        <div className="notice notice-error" role="alert">
          <p>Generation stopped: {status?.lastError ?? episode.lastError}</p>
          <p>Retry continues where it stopped. Finished parts are kept.</p>
          <button type="button" className="button button-primary" onClick={retry} disabled={retrying}>
            Retry
          </button>
        </div>
      )}
      {current === 'PausedForBudget' && (
        <div className="notice" role="status">
          <p>{statusLabel.PausedForBudget}. Nothing was charged.</p>
          <p>Retry once next month's allowance starts, or switch voice quality in your profile.</p>
          <button type="button" className="button" onClick={retry} disabled={retrying}>
            Retry
          </button>
        </div>
      )}
      {error && <ErrorNotice error={error} />}
      {isGenerating(current) && <p className="hint">You can leave this page; generation continues in the background.</p>}
    </div>
  )
}

function ReadyView({ episode, onRated }: { episode: Episode; onRated: (e: Episode) => void }) {
  const data = useApi(
    async (signal): Promise<[Transcript, Progress]> =>
      Promise.all([api.getTranscript(episode.id, signal), api.getProgress(episode.id, signal)]),
    [episode.id],
  )
  const player = usePlayer()
  const { load } = player
  const title = episode.title ?? episode.topic

  useEffect(() => {
    if (data.data) load({ id: episode.id, title }, data.data[1].positionSec)
  }, [data.data, episode.id, title, load])

  if (data.error) return <div className="page"><ErrorNotice error={data.error} onRetry={data.reload} /></div>
  if (!data.data) return <div className="page"><Loading /></div>

  return (
    <div className="listen">
      <aside className="console">
        <h1 className="console-title">{title}</h1>
        <p className="console-meta">
          {styleLabel[episode.style]} at {episode.level}, read at {formatRate(episode.speakingRate)}
        </p>
        <Controls />
        <SectionNav transcript={data.data[0]} />
        <Rating episode={episode} onRated={onRated} />
      </aside>
      <ReadAlong transcript={data.data[0]} episodeId={episode.id} />
    </div>
  )
}

function Controls() {
  const p = usePlayer()
  const loaded = p.duration > 0
  return (
    <div className="controls">
      <div className="transport">
        <button type="button" className="button button-quiet" onClick={() => p.skip(-15)} disabled={!loaded} aria-label="Back 15 seconds">
          −15
        </button>
        <button type="button" className="play play-large" onClick={p.toggle} disabled={!loaded} aria-label={p.playing ? 'Pause' : 'Play'}>
          {p.playing ? <PauseIcon /> : <PlayIcon />}
        </button>
        <button type="button" className="button button-quiet" onClick={() => p.skip(15)} disabled={!loaded} aria-label="Forward 15 seconds">
          +15
        </button>
      </div>
      <input
        className="seek"
        type="range"
        min={0}
        max={Math.max(1, Math.floor(p.duration))}
        step={1}
        value={Math.floor(p.currentTime)}
        onChange={(e) => p.seek(Number(e.target.value))}
        disabled={!loaded}
        aria-label="Position"
        aria-valuetext={`${formatTime(p.currentTime)} of ${formatTime(p.duration)}`}
      />
      <div className="times">
        <span>{formatTime(p.currentTime)}</span>
        <span>{formatTime(p.duration)}</span>
      </div>
      <div className="row">
        <label className="compact">
          Speed
          <select className="select" value={p.rate} onChange={(e) => p.setRate(Number(e.target.value))}>
            {SPEEDS.map((s) => (
              <option key={s} value={s}>
                {formatRate(s)}
              </option>
            ))}
          </select>
        </label>
        <label className="compact">
          Volume
          <input type="range" min={0} max={1} step={0.05} value={p.volume} onChange={(e) => p.setVolume(Number(e.target.value))} />
        </label>
      </div>
    </div>
  )
}

function SectionNav({ transcript }: { transcript: Transcript }) {
  const p = usePlayer()
  const segments = useMemo(() => timeline(transcript.sections), [transcript])
  const active = activeSegmentIndex(segments, p.currentTime)
  const activeSection = active >= 0 ? segments[active].sectionIndex : 0
  if (transcript.sections.length < 2) return null
  return (
    <nav aria-label="Parts" className="parts">
      <h2>Parts</h2>
      <ol>
        {transcript.sections.map((s, i) => {
          const start = s.segments[0]?.startSec
          return (
            <li key={s.id}>
              <button
                type="button"
                className="part"
                aria-current={i === activeSection ? 'true' : undefined}
                onClick={() => start !== null && start !== undefined && p.seek(start)}
              >
                {s.title}
              </button>
            </li>
          )
        })}
      </ol>
    </nav>
  )
}

function Rating({ episode, onRated }: { episode: Episode; onRated: (e: Episode) => void }) {
  const [error, setError] = useState<Error>()
  const rate = async (rating: DifficultyRating) => {
    try {
      await api.rate(episode.id, rating)
      onRated({ ...episode, difficultyRating: rating })
      setError(undefined)
    } catch (e) {
      setError(e as Error)
    }
  }
  return (
    <fieldset className="rating">
      <legend className="field-label">How was the difficulty?</legend>
      <div className="keys">
        {(Object.keys(ratingLabel) as DifficultyRating[]).map((r) => (
          <label key={r}>
            <input type="radio" name="rating" checked={episode.difficultyRating === r} onChange={() => void rate(r)} />
            <span>{ratingLabel[r]}</span>
          </label>
        ))}
      </div>
      <p className="hint">Future {episode.level} episodes adjust to this. Your level never changes by itself.</p>
      {error && <p className="field-error">{error.message}</p>}
    </fieldset>
  )
}

/** The transcript: click a sentence to jump there; the sentence being spoken is highlighted and kept in view. */
function ReadAlong({ transcript, episodeId }: { transcript: Transcript; episodeId: string }) {
  const p = usePlayer()
  const segments = useMemo(() => timeline(transcript.sections), [transcript])
  const playingThis = p.episode?.id === episodeId
  const active = playingThis ? activeSegmentIndex(segments, p.currentTime) : -1
  const activeId = active >= 0 ? segments[active].id : undefined
  const [follow, setFollow] = useState(true)
  const activeRef = useRef<HTMLSpanElement>(null)
  const seekTo = (start: number | null) => {
    if (start !== null) p.seek(start)
  }

  useEffect(() => {
    if (!follow || !p.playing || !activeRef.current) return
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches
    activeRef.current.scrollIntoView({ block: 'center', behavior: reduced ? 'auto' : 'smooth' })
  }, [activeId, follow, p.playing])

  return (
    <article className="read-along" aria-label="Transcript">
      <label className="follow">
        <input type="checkbox" checked={follow} onChange={(e) => setFollow(e.target.checked)} />
        Keep the current sentence in view
      </label>
      {transcript.sections.map((section) => (
        <section key={section.id} className="read-section">
          <h2>{section.title}</h2>
          {groupParagraphs(section.segments).map((paragraph) => (
            <p key={paragraph[0].id}>
              {paragraph.map((s, i) => (
                // A <span>, not a <button>: buttons render inline-block, so sentences couldn't flow as one paragraph.
                <Fragment key={s.id}>
                  {i > 0 && ' '}
                  <span
                    ref={s.id === activeId ? activeRef : undefined}
                    role="button"
                    tabIndex={0}
                    className="transcript-sentence"
                    data-active={s.id === activeId || undefined}
                    aria-current={s.id === activeId ? 'true' : undefined}
                    onClick={() => seekTo(s.startSec)}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter' || e.key === ' ') {
                        e.preventDefault()
                        seekTo(s.startSec)
                      }
                    }}
                  >
                    {s.text}
                  </span>
                </Fragment>
              ))}
            </p>
          ))}
        </section>
      ))}
    </article>
  )
}

function groupParagraphs<T extends { paragraph: number }>(segments: T[]): T[][] {
  const groups: T[][] = []
  for (const s of segments) {
    const last = groups.at(-1)
    if (last && last[0].paragraph === s.paragraph) last.push(s)
    else groups.push([s])
  }
  return groups
}
