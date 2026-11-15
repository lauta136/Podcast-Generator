import type { SeasonDetail } from '../api/types'
import { isGenerating } from './labels'

export type PartState =
  | { kind: 'episode' } // made: listen, or see its progress
  | { kind: 'next' } // can be made now
  | { kind: 'waiting'; reason: string } // the part before it isn't ready
  | { kind: 'later' }

/** What each planned part can do right now. Episodes are made in order, each from the ones before. */
export function partStates(season: SeasonDetail): PartState[] {
  const made = season.episodes.length
  const latest = season.episodes.at(-1)
  return season.parts.map((part) => {
    if (part.episode) return { kind: 'episode' }
    if (part.order !== made + 1) return { kind: 'later' }
    if (latest && latest.status !== 'Ready') {
      return {
        kind: 'waiting',
        reason: isGenerating(latest.status)
          ? `Available when part ${latest.seasonOrder} is ready.`
          : `Part ${latest.seasonOrder} needs to finish first.`,
      }
    }
    return { kind: 'next' }
  })
}

export type NextPart =
  | { kind: 'ready'; id: string; title: string; order: number }
  | { kind: 'not-ready'; title: string; order: number }

/** What follows part `order` when it ends: the next part if it's ready, or a note that it isn't. Null after the last. */
export function findNextPart(season: SeasonDetail, order: number): NextPart | null {
  const part = season.parts.find((p) => p.order === order + 1)
  if (!part) return null
  return part.episode?.status === 'Ready'
    ? { kind: 'ready', id: part.episode.id, title: part.episode.title ?? part.title, order: part.order }
    : { kind: 'not-ready', title: part.title, order: part.order }
}

/** Poll while the plan or an episode is being made. */
export const seasonIsBusy = (season: SeasonDetail) =>
  !season.planned || season.episodes.some((e) => isGenerating(e.status))
