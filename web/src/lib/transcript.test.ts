import { describe, expect, it } from 'vitest'
import type { TranscriptSection } from '../api/types'
import { activeSegmentIndex, timeline } from './transcript'

const sections: TranscriptSection[] = [
  {
    id: 's1',
    order: 0,
    title: 'Anfang',
    segments: [
      { id: 'a', paragraph: 0, text: 'Hallo.', startSec: 0, endSec: 2 },
      { id: 'b', paragraph: 0, text: 'Willkommen.', startSec: 2, endSec: 5 },
    ],
  },
  {
    id: 's2',
    order: 1,
    title: 'Ende',
    segments: [
      { id: 'c', paragraph: 0, text: 'Tschüss.', startSec: 5, endSec: 7 },
      { id: 'd', paragraph: 1, text: 'Ohne Audio.', startSec: null, endSec: null },
    ],
  },
]

describe('timeline', () => {
  it('flattens sections, keeps the section index and drops untimed sentences', () => {
    const t = timeline(sections)
    expect(t.map((s) => s.id)).toEqual(['a', 'b', 'c'])
    expect(t.map((s) => s.sectionIndex)).toEqual([0, 0, 1])
  })
})

describe('activeSegmentIndex', () => {
  const t = timeline(sections)
  it.each([
    [-1, -1],
    [0, 0],
    [1.9, 0],
    [2, 1],
    [4.99, 1],
    [5, 2],
    [100, 2],
  ])('at %ss is %i', (time, expected) => {
    expect(activeSegmentIndex(t, time)).toBe(expected)
  })

  it('is -1 for an empty transcript', () => {
    expect(activeSegmentIndex([], 3)).toBe(-1)
  })
})
