import { describe, expect, it } from 'vitest'
import { toApiError } from './client'

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/problem+json' } })

describe('toApiError', () => {
  it('maps validation errors to camelCase form fields', async () => {
    const error = await toApiError(
      json(400, { title: 'One or more validation errors occurred.', errors: { SpeakingRate: ['Must be between 0.6 and 1.25.'] } }),
    )
    expect(error.status).toBe(400)
    expect(error.fieldErrors).toEqual({ speakingRate: ['Must be between 0.6 and 1.25.'] })
    expect(error.message).toBe('Must be between 0.6 and 1.25.')
  })

  it('prefers the detail for conflicts', async () => {
    const error = await toApiError(json(409, { title: 'Conflict', detail: 'Not enough free WaveNet budget left this month.' }))
    expect(error.message).toBe('Not enough free WaveNet budget left this month.')
  })

  it('explains server errors without a body', async () => {
    const error = await toApiError(new Response('<html>Bad gateway</html>', { status: 502 }))
    expect(error.message).toMatch(/server ran into a problem/)
  })
})
