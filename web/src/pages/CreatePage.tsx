import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router'
import { ApiError, api } from '../api/client'
import { LEVELS, STYLES, type CefrLevel, type EpisodeStyle, type Estimate, type Preferences } from '../api/types'
import { ErrorNotice, Loading } from '../components/Feedback'
import { formatRate, styleLabel } from '../lib/labels'
import { useApi, useDebounced, type ApiState } from '../lib/useApi'

const RATES = Array.from({ length: 14 }, (_, i) => Math.round((0.6 + i * 0.05) * 100) / 100)

export function CreatePage() {
  const prefs = useApi((signal) => api.getPreferences(signal), [])
  return (
    <div className="page page-narrow">
      <h1>New episode</h1>
      <p className="lede">Pick a topic. The episode is written for your level and read aloud, usually within a few minutes.</p>
      {prefs.error && <ErrorNotice error={prefs.error} onRetry={prefs.reload} />}
      {prefs.loading && !prefs.data && <Loading />}
      {prefs.data && <CreateForm prefs={prefs.data} />}
    </div>
  )
}

function CreateForm({ prefs }: { prefs: Preferences }) {
  const navigate = useNavigate()
  const [topic, setTopic] = useState('')
  const [surprise, setSurprise] = useState(false)
  const [level, setLevel] = useState<CefrLevel>(prefs.defaultLevel)
  const [duration, setDuration] = useState(prefs.defaultDurationMin)
  const [style, setStyle] = useState<EpisodeStyle>('Conversational')
  const [rate, setRate] = useState<number | null>(null) // null: preferences, then the level default
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
  const blocked = estimate.data !== undefined && !estimate.data.fitsBudget

  async function submit(e: FormEvent) {
    e.preventDefault()
    setSubmitting(true)
    try {
      const episode = await api.createEpisode({
        topic: surprise ? undefined : topic,
        surpriseMe: surprise,
        level,
        durationMin: duration,
        style,
        speakingRate: rate ?? undefined,
      })
      navigate(`/episodes/${episode.id}`)
    } catch (err) {
      setError(err as Error)
      setSubmitting(false)
    }
  }

  return (
    <form onSubmit={submit} noValidate>
      <div className="field">
        <label htmlFor="topic">Topic</label>
        <textarea
          id="topic"
          className="textarea"
          value={surprise ? '' : topic}
          onChange={(e) => edit(setTopic)(e.target.value)}
          disabled={surprise}
          maxLength={500}
          placeholder="How the Berlin Wall fell, why Germans love bread, the rules of Skat…"
        />
        <label className="row" style={{ gap: '0.5rem', fontWeight: 400 }}>
          <input type="checkbox" checked={surprise} onChange={(e) => edit(setSurprise)(e.target.checked)} />
          Surprise me, using my interests
        </label>
        {fieldError('topic') && <p className="field-error">{fieldError('topic')}</p>}
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
        <label htmlFor="duration">Length</label>
        <div className="row">
          <input
            id="duration"
            className="input"
            style={{ maxWidth: '7rem' }}
            type="number"
            min={5}
            max={60}
            value={duration}
            onChange={(e) => edit(setDuration)(Number(e.target.value))}
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
          onChange={(e) => edit(setRate)(e.target.value === '' ? null : Number(e.target.value))}
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

      <EstimatePanel estimate={estimate} />

      {error && !fieldError('topic') && !fieldError('durationMin') && !fieldError('speakingRate') && (
        <ErrorNotice error={error} />
      )}
      <button
        type="submit"
        className="button button-primary"
        disabled={submitting || blocked || (!surprise && topic.trim() === '')}
      >
        {submitting ? 'Creating…' : 'Create episode'}
      </button>
    </form>
  )
}

function EstimatePanel({ estimate }: { estimate: ApiState<Estimate> }) {
  if (estimate.error) {
    return (
      <div className="notice notice-error" role="status">
        <p>{estimate.error.message}</p>
      </div>
    )
  }
  const e = estimate.data
  if (!e) return null

  const share = e.monthlyBudget > 0 ? (e.estimatedChars / e.monthlyBudget) * 100 : 100
  return (
    <div className={`notice ${e.fitsBudget ? '' : 'notice-error'}`} role="status" aria-live="polite">
      <p>
        About {e.targetWords.toLocaleString('en')} words, read at {formatRate(e.speakingRate)}. Uses{' '}
        {e.estimatedChars.toLocaleString('en')} characters, {share < 1 ? 'under 1' : Math.round(share)}% of this month's
        free voice allowance.
      </p>
      {!e.fitsBudget && (
        <p>
          Only {e.budgetRemaining.toLocaleString('en')} characters are left this month. Choose a shorter episode or the
          other voice quality in your profile.
        </p>
      )}
    </div>
  )
}
