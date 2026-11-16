import { useState } from 'react'
import { Link } from 'react-router'
import { api } from '../api/client'
import type { VocabularyItem } from '../api/types'
import { ErrorNotice, Loading } from '../components/Feedback'
import { useApi, useDebounced } from '../lib/useApi'
import { dueLabel, highlight } from '../lib/vocabulary'
import './library.css'
import './vocabulary.css'

export function VocabularyPage() {
  const [search, setSearch] = useState('')
  const debounced = useDebounced(search.trim(), 300)
  const words = useApi((signal) => api.listVocabulary({ search: debounced, pageSize: 100 }, signal), [debounced])
  const [error, setError] = useState<Error>()

  async function remove(item: VocabularyItem) {
    try {
      await api.deleteWord(item.id)
      setError(undefined)
      words.reload()
    } catch (e) {
      setError(e as Error)
    }
  }

  const due = words.data?.due ?? 0
  return (
    <div className="page">
      <div className="library-head">
        <h1>Words</h1>
        {due > 0 ? (
          <Link className="button button-primary" to="/vocabulary/review">
            Review {due} {due === 1 ? 'word' : 'words'}
          </Link>
        ) : (
          words.data && words.data.total > 0 && <span className="hint">Nothing to review right now.</span>
        )}
      </div>
      <p className="lede">Words you saved while listening, each with the sentence it came from.</p>

      {words.data && (words.data.total > 0 || search !== '') && (
        <div className="filters filters-single" role="search">
          <label className="visually-hidden" htmlFor="word-search">
            Search your words
          </label>
          <input
            id="word-search"
            className="input"
            type="search"
            placeholder="Search in German or in your language"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>
      )}

      {error && <ErrorNotice error={error} />}
      {words.error && <ErrorNotice error={words.error} onRetry={words.reload} />}
      {words.loading && !words.data && <Loading />}

      {words.data && words.data.total === 0 && search === '' && (
        <div className="empty">
          <h2>No words yet</h2>
          <p>
            While you read along in an episode, select a word or a short phrase and choose <strong>Save</strong>. It
            appears here with its meaning and the sentence, ready to review.
          </p>
          <Link className="button" to="/library">
            Open the library
          </Link>
        </div>
      )}
      {words.data && words.data.total === 0 && search !== '' && <p className="hint">No saved word matches “{search}”.</p>}

      {words.data && words.data.items.length > 0 && (
        <ul className="words">
          {words.data.items.map((item) => {
            const [before, match, after] = highlight(item.context, item.text)
            return (
              <li key={item.id} className="word">
                <div className="word-main">
                  <p className="word-base" lang="de">
                    {item.baseForm}
                  </p>
                  <p className="word-meaning">{item.translation}</p>
                  <p className="word-context" lang="de">
                    {before}
                    <mark>{match}</mark>
                    {after}
                  </p>
                  <p className="hint">
                    {dueLabel(item.dueAt)}
                    {item.episodeId && item.startSec !== null && (
                      <>
                        {'. '}
                        <Link to={`/episodes/${item.episodeId}?t=${Math.ceil(item.startSec * 10) / 10}`}>
                          Hear it in {item.episodeTitle ?? 'the episode'}
                        </Link>
                      </>
                    )}
                  </p>
                </div>
                <button type="button" className="button button-quiet button-danger" onClick={() => void remove(item)}>
                  Delete<span className="visually-hidden"> {item.baseForm}</span>
                </button>
              </li>
            )
          })}
        </ul>
      )}
    </div>
  )
}
