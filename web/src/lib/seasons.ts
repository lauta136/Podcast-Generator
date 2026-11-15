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

/** Poll while the plan or an episode is being made. */
export const seasonIsBusy = (season: SeasonDetail) =>
  !season.planned || season.episodes.some((e) => isGenerating(e.status))
