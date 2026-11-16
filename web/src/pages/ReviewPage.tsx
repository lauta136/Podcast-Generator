import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { api } from '../api/client'
import type { ReviewResult, VocabularyItem } from '../api/types'
import { ErrorNotice, Loading } from '../components/Feedback'
import { useApi } from '../lib/useApi'
import { highlight } from '../lib/vocabulary'
import './vocabulary.css'

/** Flashcards: the word in its sentence, then its meaning. Space shows the answer; 1 = Again, 2 = Got it. */
export function ReviewPage() {
  const due = useApi((signal) => api.dueWords(signal), [])
  return (
    <div className="page page-narrow">
      <p className="hint">
        <Link to="/vocabulary">Words</Link>
      </p>
      <h1>Review</h1>
      {due.error && <ErrorNotice error={due.error} onRetry={due.reload} />}
      {due.loading && !due.data && <Loading />}
      {due.data && <Session key={due.data.map((c) => c.id).join()} cards={due.data} onMore={due.reload} />}
    </div>
  )
}

function Session({ cards, onMore }: { cards: VocabularyItem[]; onMore: () => void }) {
  const [index, setIndex] = useState(0)
  const [revealed, setRevealed] = useState(false)
  const [again, setAgain] = useState(0)
  const [error, setError] = useState<Error>()
  const [busy, setBusy] = useState(false)
  const card = cards[index]

  async function answer(result: ReviewResult) {
    if (!card || busy) return
    setBusy(true)
    try {
      await api.reviewWord(card.id, result)
      if (result === 'Again') setAgain((n) => n + 1)
      setError(undefined)
      setRevealed(false)
      setIndex((i) => i + 1)
    } catch (e) {
      setError(e as Error)
    } finally {
      setBusy(false)
    }
  }

  // Keyboard: Space or Enter shows the answer, then 1 = Again, 2 = Got it.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.target instanceof HTMLInputElement || e.target instanceof HTMLButtonElement || !card) return
      if (!revealed && (e.key === ' ' || e.key === 'Enter')) {
        e.preventDefault()
        setRevealed(true)
      } else if (revealed && e.key === '1') void answer('Again')
      else if (revealed && e.key === '2') void answer('GotIt')
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  })

  if (cards.length === 0) {
    return (
      <div className="empty">
        <h2>Nothing to review</h2>
        <p>Every saved word is scheduled for later. New words you save are ready to review right away.</p>
        <Link className="button" to="/vocabulary">
          See your words
        </Link>
      </div>
    )
  }

  if (!card) {
    return (
      <div className="notice notice-ok" role="status">
        <p>
          Done: {cards.length} {cards.length === 1 ? 'word' : 'words'} reviewed
          {again > 0 ? `, ${again} coming back in 10 minutes` : ''}.
        </p>
        <div className="row">
          <button type="button" className="button" onClick={onMore}>
            Check for more
          </button>
          <Link className="button button-quiet" to="/vocabulary">
            See your words
          </Link>
        </div>
      </div>
    )
  }

  const [before, match, after] = highlight(card.context, card.text)
  return (
    <>
      <p className="hint" aria-live="polite">
        Card {index + 1} of {cards.length}
      </p>
      <article className="card" aria-label="Flashcard">
        <p className="card-word" lang="de">
          {card.baseForm}
        </p>
        <p className="card-context" lang="de">
          {before}
          <mark>{match}</mark>
          {after}
        </p>
        {revealed ? (
          <p className="card-meaning">{card.translation}</p>
        ) : (
          <button type="button" className="button" onClick={() => setRevealed(true)}>
            Show meaning
          </button>
        )}
      </article>
      {revealed && (
        <div className="card-answers">
          <button type="button" className="button" disabled={busy} onClick={() => void answer('Again')}>
            Again <span className="key-hint">(1)</span>
          </button>
          <button type="button" className="button button-primary" disabled={busy} onClick={() => void answer('GotIt')}>
            Got it <span className="key-hint">(2)</span>
          </button>
        </div>
      )}
      {error && <ErrorNotice error={error} />}
    </>
  )
}
