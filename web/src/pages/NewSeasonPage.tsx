import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router'
import { ApiError, api } from '../api/client'
import { LEVELS, STYLES, type CefrLevel, type EpisodeStyle, type Preferences } from '../api/types'
import { ErrorNotice, Loading } from '../components/Feedback'
import { formatRate, styleLabel } from '../lib/labels'
import { useApi, useDebounced } from '../lib/useApi'

const RATES = Array.from({ length: 14 }, (_, i) => Math.round((0.6 + i * 0.05) * 100) / 100)
const COUNTS = Array.from({ length: 11 }, (_, i) => i + 2) // 2 … 12

export function NewSeasonPage() {
  const prefs = useApi((signal) => api.getPreferences(signal), [])
  return (
    <div className="page page-narrow">
      <h1>New season</h1>
      <p className="lede">
        Name a big topic. The app plans the whole series and makes the first episode. You make each next episode when
        you're ready for it.
      </p>
      {prefs.error && <ErrorNotice error={prefs.error} onRetry={prefs.reload} />}
      {prefs.loading && !prefs.data && <Loading />}
      {prefs.data && <SeasonForm prefs={prefs.data} />}
    </div>
  )
}

function SeasonForm({ prefs }: { prefs: Preferences }) {
  const navigate = useNavigate()
  const [topic, setTopic] = useState('')
  const [count, setCount] = useState(5)
  const [level, setLevel] = useState<CefrLevel>(prefs.defaultLevel)
  const [duration, setDuration] = useState(prefs.defaultDurationMin)
  const [style, setStyle] = useState<EpisodeStyle>('Storytelling')
  const [rate, setRate] = useState<number | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<Error>()

  const estimateInput = useDebounced({ level, duration, rate }, 300)
  const estimate = useApi(
    (signal) =>
      api.estimate(
        { level: estimateInput.level, durationMin: estimateInput.duration, speakingRate: estimateInput.rate ?? undefined },
        signal,
      ),
    [estimateInput.level, estimateInput.duration, estimateInput.rate],
  )

  /** Wraps a setter so editing any field clears the last submit error. */
  const edit =
    <T,>(setter: (v: T) => void) =>
    (v: T) => {
      setter(v)
      setError(undefined)
    }
  const fieldError = (name: string) => (error instanceof ApiError ? error.fieldErrors[name]?.[0] : undefined)
  const shownInFields = ['topic', 'episodeCount', 'durationMin', 'speakingRate'].some((f) => fieldError(f))

  async function submit(e: FormEvent) {
    e.preventDefault()
    setSubmitting(true)
    try {
      const first = await api.createSeason({
        topic,
        episodeCount: count,
        level,
        durationMin: duration,
        style,
        speakingRate: rate ?? undefined,
      })
      navigate(`/seasons/${first.seasonId}`)
    } catch (err) {
      setError(err as Error)
      setSubmitting(false)
    }
  }

  const e = estimate.data
  const share = e && e.monthlyBudget > 0 ? (e.estimatedChars * count * 100) / e.monthlyBudget : 0

  return (
    <form onSubmit={submit} noValidate>
      <div className="field">
        <label htmlFor="topic">Topic</label>
        <textarea
          id="topic"
          className="textarea"
          value={topic}
          onChange={(ev) => edit(setTopic)(ev.target.value)}
          maxLength={500}
          placeholder="The Hanseatic League, the history of Berlin, how the German language grew…"
        />
        {fieldError('topic') && <p className="field-error">{fieldError('topic')}</p>}
      </div>

      <div className="field">
        <label htmlFor="count">Episodes</label>
        <select
          id="count"
          className="select"
          style={{ maxWidth: '10rem' }}
          value={count}
          onChange={(ev) => edit(setCount)(Number(ev.target.value))}
        >
          {COUNTS.map((c) => (
            <option key={c} value={c}>
              {c} episodes
            </option>
          ))}
        </select>
        {fieldError('episodeCount') && <p className="field-error">{fieldError('episodeCount')}</p>}
      </div>

      <fieldset>
        <legend className="field-label">Level</legend>
        <div className="keys">
          {LEVELS.map((l) => (
            <label key={l}>
              <input type="radio" name="level" checked={level === l} onChange={() => edit(setLevel)(l)} />
              <span>{l}</span>
            </label>
          ))}
        </div>
      </fieldset>

      <div className="field">
        <label htmlFor="duration">Length of each episode</label>
        <div className="row">
          <input
            id="duration"
            className="input"
            style={{ maxWidth: '7rem' }}
            type="number"
            min={5}
            max={60}
            value={duration}
            onChange={(ev) => edit(setDuration)(Number(ev.target.value))}
          />
          <span className="hint">minutes, 5 to 60</span>
        </div>
        {fieldError('durationMin') && <p className="field-error">{fieldError('durationMin')}</p>}
      </div>

      <fieldset>
        <legend className="field-label">Style</legend>
        <div className="keys">
          {STYLES.map((s) => (
            <label key={s}>
              <input type="radio" name="style" checked={style === s} onChange={() => edit(setStyle)(s)} />
              <span>{styleLabel[s]}</span>
            </label>
          ))}
        </div>
      </fieldset>

      <div className="field">
        <label htmlFor="rate">Speaking speed</label>
        <select
          id="rate"
          className="select"
          value={rate ?? ''}
          onChange={(ev) => edit(setRate)(ev.target.value === '' ? null : Number(ev.target.value))}
        >
          <option value="">{prefs.speakingRate === null ? 'Level default' : 'My usual speed'}</option>
          {RATES.map((r) => (
            <option key={r} value={r}>
              {formatRate(r)}
            </option>
          ))}
        </select>
        {fieldError('speakingRate') && <p className="field-error">{fieldError('speakingRate')}</p>}
      </div>

      {estimate.error && (
        <div className="notice notice-error" role="status">
          <p>{estimate.error.message}</p>
        </div>
      )}
      {e && (
        <div className={`notice ${e.fitsBudget ? '' : 'notice-error'}`} role="status" aria-live="polite">
          <p>
            Each episode: about {e.targetWords.toLocaleString('en')} words, read at {formatRate(e.speakingRate)}. The
            whole season uses about {(e.estimatedChars * count).toLocaleString('en')} characters,{' '}
            {share < 1 ? 'under 1' : Math.round(share)}% of a month's free voice allowance, spent episode by episode as
            you make them.
          </p>
          {!e.fitsBudget && <p>Not even the first episode fits what's left this month. Choose shorter episodes.</p>}
        </div>
      )}

      {error && !shownInFields && <ErrorNotice error={error} />}
      <button
        type="submit"
        className="button button-primary"
        disabled={submitting || topic.trim() === '' || (e !== undefined && !e.fitsBudget)}
      >
        {submitting ? 'Creating…' : 'Create season'}
      </button>
    </form>
  )
}
