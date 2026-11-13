import { useState, type FormEvent } from 'react'
import { ApiError, api } from '../api/client'
import { LEVELS, type CefrLevel, type Preferences, type VoiceFamily } from '../api/types'
import { ErrorNotice, Loading } from '../components/Feedback'
import { familyLabel, formatRate } from '../lib/labels'
import { useApi } from '../lib/useApi'
import { SPEEDS } from '../player/usePlayer'

const NATIVE = [
  { code: 'en', name: 'English' },
  { code: 'es', name: 'Spanish' },
]
const RATES = Array.from({ length: 14 }, (_, i) => Math.round((0.6 + i * 0.05) * 100) / 100) // 0.60 … 1.25

export function ProfilePage() {
  const prefs = useApi((signal) => api.getPreferences(signal), [])
  return (
    <div className="page page-narrow">
      <h1>Profile</h1>
      <p className="lede">Defaults for new episodes. You can still change level, length and speed for each one.</p>
      {prefs.error && <ErrorNotice error={prefs.error} onRetry={prefs.reload} />}
      {prefs.loading && !prefs.data && <Loading />}
      {prefs.data && <PreferencesForm initial={prefs.data} onSaved={prefs.setData} />}
      <BudgetPanel />
    </div>
  )
}

function PreferencesForm({ initial, onSaved }: { initial: Preferences; onSaved: (p: Preferences) => void }) {
  const [form, setForm] = useState(initial)
  const [interests, setInterests] = useState(initial.interests.join('\n'))
  const [saving, setSaving] = useState(false)
  const [result, setResult] = useState<{ ok: true } | { ok: false; error: ApiError | Error }>()
  const voices = useApi((signal) => api.getVoices(signal), [])

  const set = <K extends keyof Preferences>(key: K, value: Preferences[K]) => {
    setForm((f) => ({ ...f, [key]: value }))
    setResult(undefined)
  }
  const familyVoices = (voices.data ?? []).filter((v) => v.modelFamily === form.voiceModelFamily)
  const fieldError = (name: string) =>
    result && !result.ok && result.error instanceof ApiError ? result.error.fieldErrors[name]?.[0] : undefined

  async function submit(e: FormEvent) {
    e.preventDefault()
    setSaving(true)
    try {
      const { updatedAt: _ignored, ...update } = form
      const saved = await api.savePreferences({
        ...update,
        interests: interests.split(/[\n,]/).map((i) => i.trim()).filter(Boolean),
      })
      onSaved(saved)
      setResult({ ok: true })
    } catch (error) {
      setResult({ ok: false, error: error as Error })
    } finally {
      setSaving(false)
    }
  }

  return (
    <form onSubmit={submit} noValidate>
      <fieldset>
        <legend className="field-label">Your level</legend>
        <div className="keys" role="radiogroup">
          {LEVELS.map((level) => (
            <label key={level}>
              <input
                type="radio"
                name="level"
                value={level}
                checked={form.defaultLevel === level}
                onChange={() => set('defaultLevel', level as CefrLevel)}
              />
              <span>{level}</span>
            </label>
          ))}
        </div>
      </fieldset>

      <div className="field">
        <label htmlFor="duration">Episode length</label>
        <div className="row">
          <input
            id="duration"
            className="input"
            style={{ maxWidth: '7rem' }}
            type="number"
            min={5}
            max={60}
            value={form.defaultDurationMin}
            onChange={(e) => set('defaultDurationMin', Number(e.target.value))}
            aria-describedby="duration-hint"
          />
          <span id="duration-hint" className="hint">
            minutes, 5 to 60
          </span>
        </div>
        {fieldError('defaultDurationMin') && <p className="field-error">{fieldError('defaultDurationMin')}</p>}
      </div>

      <div className="field">
        <label htmlFor="speaking-rate">Speaking speed</label>
        <select
          id="speaking-rate"
          className="select"
          value={form.speakingRate ?? ''}
          onChange={(e) => set('speakingRate', e.target.value === '' ? null : Number(e.target.value))}
          aria-describedby="speaking-rate-hint"
        >
          <option value="">Follow my level</option>
          {RATES.map((r) => (
            <option key={r} value={r}>
              {formatRate(r)}
            </option>
          ))}
        </select>
        <p id="speaking-rate-hint" className="hint">
          How fast the voice speaks in new episodes. Each level has its own default, slower at lower levels. Pick a
          fixed speed to practise faster speech without harder language, or the other way round.
        </p>
        {fieldError('speakingRate') && <p className="field-error">{fieldError('speakingRate')}</p>}
      </div>

      <div className="field">
        <label htmlFor="playback">Player speed</label>
        <select
          id="playback"
          className="select"
          value={form.playbackSpeed}
          onChange={(e) => set('playbackSpeed', Number(e.target.value))}
        >
          {SPEEDS.map((s) => (
            <option key={s} value={s}>
              {formatRate(s)}
            </option>
          ))}
        </select>
        <p className="hint">Where the player starts. You can change it while listening.</p>
      </div>

      <fieldset>
        <legend className="field-label">Voice quality</legend>
        <div className="keys" role="radiogroup">
          {(['WaveNet', 'Chirp3HD'] as VoiceFamily[]).map((family) => (
            <label key={family}>
              <input
                type="radio"
                name="family"
                checked={form.voiceModelFamily === family}
                onChange={() => {
                  setForm((f) => ({ ...f, voiceModelFamily: family, voiceName: null }))
                  setResult(undefined)
                }}
              />
              <span>{familyLabel[family]}</span>
            </label>
          ))}
        </div>
        <p className="hint">Each quality has its own free monthly allowance.</p>
      </fieldset>

      <div className="field">
        <label htmlFor="voice">Voice</label>
        <select
          id="voice"
          className="select"
          value={form.voiceName ?? ''}
          onChange={(e) => set('voiceName', e.target.value || null)}
          disabled={voices.loading}
        >
          <option value="">Default voice</option>
          {familyVoices.map((v) => (
            <option key={v.name} value={v.name}>
              {v.name.replace(/^de-DE-(Wavenet|Chirp3-HD)-/, '')} ({v.gender.toLowerCase()})
            </option>
          ))}
        </select>
        {voices.error && <p className="field-error">Voices could not be loaded: {voices.error.message}</p>}
        {fieldError('voiceName') && <p className="field-error">{fieldError('voiceName')}</p>}
      </div>

      <div className="field">
        <label htmlFor="interests">Interests</label>
        <textarea
          id="interests"
          className="textarea"
          value={interests}
          onChange={(e) => {
            setInterests(e.target.value)
            setResult(undefined)
          }}
          aria-describedby="interests-hint"
        />
        <p id="interests-hint" className="hint">
          One per line. "Surprise me" picks a topic from these.
        </p>
        {fieldError('interests') && <p className="field-error">{fieldError('interests')}</p>}
      </div>

      <div className="field">
        <label htmlFor="native">Your native language</label>
        <select id="native" className="select" value={form.nativeLanguage} onChange={(e) => set('nativeLanguage', e.target.value)}>
          {NATIVE.map((l) => (
            <option key={l.code} value={l.code}>
              {l.name}
            </option>
          ))}
        </select>
        <p className="hint">Episodes are in German.</p>
      </div>

      {result?.ok && (
        <div className="notice notice-ok" role="status">
          <p>Saved.</p>
        </div>
      )}
      {result && !result.ok && <ErrorNotice error={result.error} />}
      <button type="submit" className="button button-primary" disabled={saving}>
        {saving ? 'Saving…' : 'Save'}
      </button>
    </form>
  )
}

function BudgetPanel() {
  const budget = useApi((signal) => api.getBudget(signal), [])
  if (!budget.data) return null
  return (
    <section style={{ marginTop: '2.5rem' }} aria-labelledby="budget-title">
      <h2 id="budget-title">Voice allowance this month</h2>
      {budget.data.map((b) => {
        const share = b.monthlyBudget > 0 ? Math.min(100, (b.used / b.monthlyBudget) * 100) : 100
        return (
          <div key={b.modelFamily} className="field">
            <span className="field-label">{familyLabel[b.modelFamily]}</span>
            <div className="mini-bar" role="img" aria-label={`${Math.round(share)}% used`}>
              <div style={{ width: `${share}%` }} />
            </div>
            <p className="hint">
              {b.used.toLocaleString('en')} of {b.monthlyBudget.toLocaleString('en')} characters used. Episodes pause
              before going over, so nothing is charged.
            </p>
          </div>
        )
      })}
    </section>
  )
}
