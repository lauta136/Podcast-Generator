import { describe, expect, it } from 'vitest'
import { formatTime } from './format'

describe('formatTime', () => {
  it('formats minutes and seconds', () => {
    expect(formatTime(75)).toBe('1:15')
  })
  it('includes hours for long episodes', () => {
    expect(formatTime(3725)).toBe('1:02:05')
  })
  it('clamps negatives to zero', () => {
    expect(formatTime(-3)).toBe('0:00')
  })
})
