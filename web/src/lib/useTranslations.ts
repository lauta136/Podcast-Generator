import { useCallback, useState } from 'react'
import { api } from '../api/client'

export type TranslationState =
  | { status: 'loading' }
  | { status: 'done'; text: string }
  | { status: 'error'; message: string }

/**
 * Translations the listener opened, by sentence id. Asking for one sentence translates its whole paragraph
 * (one server request), so the other sentences of that paragraph open instantly afterwards.
 */
export function useTranslations(episodeId: string) {
  const [known, setKnown] = useState<Record<string, string>>({})
  const [open, setOpen] = useState<Record<string, TranslationState>>({})

  const toggle = useCallback(
    (segmentId: string) => {
      if (open[segmentId] && open[segmentId].status !== 'error') {
        setOpen(({ [segmentId]: _closed, ...rest }) => rest)
        return
      }
      if (known[segmentId] !== undefined) {
        setOpen((o) => ({ ...o, [segmentId]: { status: 'done', text: known[segmentId] } }))
        return
      }

      setOpen((o) => ({ ...o, [segmentId]: { status: 'loading' } }))
      api
        .translate(episodeId, segmentId)
        .then((result) => {
          const byId = Object.fromEntries(result.sentences.map((s) => [s.id, s.translation]))
          setKnown((k) => ({ ...k, ...byId }))
          setOpen((o) => (o[segmentId] ? { ...o, [segmentId]: { status: 'done', text: byId[segmentId] ?? '' } } : o))
        })
        .catch((e: unknown) => {
          const message = e instanceof Error ? e.message : String(e)
          setOpen((o) => (o[segmentId] ? { ...o, [segmentId]: { status: 'error', message } } : o))
        })
    },
    [episodeId, known, open],
  )

  const closeAll = useCallback(() => setOpen({}), [])

  return { open, toggle, closeAll }
}
