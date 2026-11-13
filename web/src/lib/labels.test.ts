import { describe, expect, it } from 'vitest'
import { formatRate, isGenerating } from './labels'

describe('formatRate', () => {
  it.each([
    [0.85, '0.85×'],
    [0.8, '0.8×'],
    [1, '1.0×'],
    [1.25, '1.25×'],
  ])('%s → %s', (rate, expected) => expect(formatRate(rate)).toBe(expected))
})

describe('isGenerating', () => {
  it('is true only while the pipeline runs', () => {
    expect(isGenerating('GeneratingAudio')).toBe(true)
    expect(isGenerating('Pending')).toBe(true)
    expect(isGenerating('Ready')).toBe(false)
    expect(isGenerating('Failed')).toBe(false)
    expect(isGenerating('PausedForBudget')).toBe(false)
  })
})
