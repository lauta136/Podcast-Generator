import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'
import { Link } from 'react-router'
import { api } from '../api/client'
import type { VocabularyItem } from '../api/types'

type State =
  | { kind: 'hidden' }
  | { kind: 'offer'; segmentId: string; text: string; x: number; y: number }
  | { kind: 'saving'; x: number; y: number }
  | { kind: 'saved'; item: VocabularyItem; x: number; y: number }
  | { kind: 'error'; message: string; x: number; y: number }

const MAX_CHARS = 80

/** The selected text, if it lies inside one transcript sentence; otherwise null. */
function selectedWord(): { segmentId: string; text: string; rect: DOMRect } | null {
  const selection = window.getSelection()
  if (!selection || selection.isCollapsed || selection.rangeCount === 0) return null
  const sentenceOf = (node: Node | null) =>
    (node instanceof Element ? node : node?.parentElement)?.closest<HTMLElement>('.transcript-sentence') ?? null
  const start = sentenceOf(selection.anchorNode)
  if (!start || start !== sentenceOf(selection.focusNode) || !start.dataset.segment) return null
  const text = selection.toString().trim()
  if (text.length === 0 || text.length > MAX_CHARS) return null
  return { segmentId: start.dataset.segment, text, rect: selection.getRangeAt(0).getBoundingClientRect() }
}

/**
 * Select a word or a short phrase in the read-along and a "Save word" button appears above it. Saving asks the
 * server for the dictionary form and the meaning in that sentence, and shows them briefly.
 */
export function SaveWordPopover() {
  const [state, setState] = useState<State>({ kind: 'hidden' })
  const box = useRef<HTMLDivElement>(null)

  // Centred over the word, but never past the window's edges (words near the margin, long meanings).
  useLayoutEffect(() => {
    const el = box.current
    if (!el) return
    el.style.marginLeft = '0px'
    const rect = el.getBoundingClientRect()
    const edge = 8
    const shift = rect.left < edge ? edge - rect.left : rect.right > window.innerWidth - edge ? window.innerWidth - edge - rect.right : 0
    el.style.marginLeft = `${shift}px`
  }, [state])

  const update = useCallback(() => {
    const word = selectedWord()
    setState((current) => {
      if (current.kind === 'saving' || current.kind === 'saved' || current.kind === 'error') return current
      if (!word) return { kind: 'hidden' }
      return { kind: 'offer', segmentId: word.segmentId, text: word.text, x: word.rect.left + word.rect.width / 2, y: word.rect.top }
    })
  }, [])

  useEffect(() => {
    // After the mouse or finger lifts, so the button doesn't chase a selection being dragged.
    const later = () => setTimeout(update, 0)
    document.addEventListener('mouseup', later)
    document.addEventListener('keyup', later)
    document.addEventListener('touchend', later)
    const hide = () => setState((s) => (s.kind === 'offer' ? { kind: 'hidden' } : s))
    window.addEventListener('scroll', hide, true)
    return () => {
      document.removeEventListener('mouseup', later)
      document.removeEventListener('keyup', later)
      document.removeEventListener('touchend', later)
      window.removeEventListener('scroll', hide, true)
    }
  }, [update])

  // The result stays a few seconds, then gets out of the way.
  useEffect(() => {
    if (state.kind !== 'saved' && state.kind !== 'error') return
    const timer = setTimeout(() => setState({ kind: 'hidden' }), state.kind === 'saved' ? 5000 : 6000)
    return () => clearTimeout(timer)
  }, [state])

  async function save() {
    if (state.kind !== 'offer') return
    const { segmentId, text, x, y } = state
    setState({ kind: 'saving', x, y })
    try {
      const item = await api.saveWord(segmentId, text)
      window.getSelection()?.removeAllRanges()
      setState({ kind: 'saved', item, x, y })
    } catch (e) {
      setState({ kind: 'error', message: e instanceof Error ? e.message : String(e), x, y })
    }
  }

  if (state.kind === 'hidden') return null
  const style = { left: `${state.x}px`, top: `${state.y}px` }
  return (
    <div ref={box} className="save-word" style={style} role={state.kind === 'offer' ? undefined : 'status'} aria-live="polite">
      {state.kind === 'offer' && (
        // mousedown would clear the selection before the click lands
        <button type="button" className="button button-primary" onMouseDown={(e) => e.preventDefault()} onClick={() => void save()}>
          Save “{state.text.length > 24 ? `${state.text.slice(0, 24)}…` : state.text}”
        </button>
      )}
      {state.kind === 'saving' && <span className="save-word-note">Saving…</span>}
      {state.kind === 'saved' && (
        <span className="save-word-note">
          <strong>{state.item.baseForm}</strong>: {state.item.translation}{' '}
          <Link to="/vocabulary">Words</Link>
        </span>
      )}
      {state.kind === 'error' && <span className="save-word-note save-word-error">{state.message}</span>}
    </div>
  )
}
