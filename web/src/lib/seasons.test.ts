import { describe, expect, it } from 'vitest'
import type { EpisodeListItem, SeasonDetail } from '../api/types'
import { findNextPart, partStates, seasonIsBusy } from './seasons'

const episode = (order: number, status: EpisodeListItem['status']): EpisodeListItem => ({
  id: `e${order}`,
  title: `Teil ${order}`,
  topic: `Teil ${order}`,
  level: 'B1',
  style: 'Storytelling',
  durationMin: 10,
  speakingRate: 0.85,
  status,
  audioDurationSec: 600,
  difficultyRating: null,
  createdAt: '2026-11-15T00:00:00Z',
  progress: null,
  seasonId: 's',
  seasonOrder: order,
  seasonTitle: 'Serie',
})

const season = (episodes: EpisodeListItem[], planned = true): SeasonDetail => ({
  id: 's',
  title: 'Serie',
  description: null,
  topic: 'Hanse',
  level: 'B1',
  style: 'Storytelling',
  episodeCount: 3,
  durationMin: 10,
  speakingRate: null,
  planned,
  parts: planned
    ? [1, 2, 3].map((order) => ({ order, title: `Teil ${order}`, summary: '', episode: episodes.find((e) => e.seasonOrder === order) ?? null }))
    : [],
  episodes,
  createdAt: '2026-11-15T00:00:00Z',
})

describe('partStates', () => {
  it('offers the next part only once the latest one is ready', () => {
    expect(partStates(season([episode(1, 'Ready')])).map((s) => s.kind)).toEqual(['episode', 'next', 'later'])
    expect(partStates(season([episode(1, 'GeneratingAudio')])).map((s) => s.kind)).toEqual(['episode', 'waiting', 'later'])
    expect(partStates(season([episode(1, 'Ready'), episode(2, 'Ready'), episode(3, 'Ready')])).map((s) => s.kind)).toEqual([
      'episode',
      'episode',
      'episode',
    ])
  })

  it('says why a part is waiting', () => {
    expect(partStates(season([episode(1, 'Failed')]))[1]).toEqual({ kind: 'waiting', reason: 'Part 1 needs to finish first.' })
  })
})

describe('seasonIsBusy', () => {
  it('is busy while planning or generating', () => {
    expect(seasonIsBusy(season([episode(1, 'GeneratingOutline')], false))).toBe(true)
    expect(seasonIsBusy(season([episode(1, 'GeneratingSections')]))).toBe(true)
    expect(seasonIsBusy(season([episode(1, 'Ready')]))).toBe(false)
    expect(seasonIsBusy(season([episode(1, 'Failed')]))).toBe(false)
  })
})

describe('findNextPart', () => {
  it('offers the next part when it is ready', () => {
    expect(findNextPart(season([episode(1, 'Ready'), episode(2, 'Ready')]), 1)).toEqual({ kind: 'ready', id: 'e2', title: 'Teil 2', order: 2 })
  })

  it('says the next part is not ready while it is missing, being made or failed', () => {
    expect(findNextPart(season([episode(1, 'Ready')]), 1)).toEqual({ kind: 'not-ready', title: 'Teil 2', order: 2 })
    expect(findNextPart(season([episode(1, 'Ready'), episode(2, 'GeneratingAudio')]), 1)?.kind).toBe('not-ready')
    expect(findNextPart(season([episode(1, 'Ready'), episode(2, 'Failed')]), 1)?.kind).toBe('not-ready')
  })

  it('offers nothing after the last part', () => {
    expect(findNextPart(season([episode(1, 'Ready'), episode(2, 'Ready'), episode(3, 'Ready')]), 3)).toBeNull()
  })
})
