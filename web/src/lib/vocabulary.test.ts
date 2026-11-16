import { describe, expect, it } from 'vitest'
import { dueLabel, highlight } from './vocabulary'

describe('highlight', () => {
  it('finds the word regardless of case and keeps the sentence as written', () => {
    expect(highlight('Die Kaufleute handelten mit Hering.', 'kaufleute')).toEqual(['Die ', 'Kaufleute', ' handelten mit Hering.'])
  })

  it('handles umlauts', () => {
    expect(highlight('Über die Straße.', 'über')).toEqual(['', 'Über', ' die Straße.'])
  })

  it('leaves the sentence alone when the word is missing', () => {
    expect(highlight('Hallo.', 'Welt')).toEqual(['Hallo.', '', ''])
  })
})

describe('dueLabel', () => {
  const now = Date.parse('2026-11-16T12:00:00Z')

  it.each([
    ['2026-11-16T11:00:00Z', 'Due now'],
    ['2026-11-16T12:10:00Z', 'Due in 10 min'],
    ['2026-11-16T15:00:00Z', 'Due in 3 h'],
    ['2026-11-17T12:00:00Z', 'Due tomorrow'],
    ['2026-11-23T12:00:00Z', 'Due in 7 days'],
  ])('%s → %s', (dueAt, expected) => expect(dueLabel(dueAt, now)).toBe(expected))
})
