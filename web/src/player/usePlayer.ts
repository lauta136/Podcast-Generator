import { createContext, useContext } from 'react'

export interface LoadedEpisode {
  id: string
  title: string
  /** For a season episode: lets the player offer the next part when this one ends. */
  seasonId?: string | null
  seasonOrder?: number | null
}

/** Shown when a season episode ends. */
export type UpNext =
  | { kind: 'countdown'; from: string; seasonId: string; id: string; title: string; order: number; secondsLeft: number }
  | { kind: 'not-ready'; from: string; seasonId: string; title: string; order: number }

export interface Player {
  episode: LoadedEpisode | null
  playing: boolean
  currentTime: number
  duration: number
  rate: number
  volume: number
  /** Loads an episode and seeks to `startAt` (the saved position). Doesn't start playback. */
  load: (episode: LoadedEpisode, startAt: number) => void
  toggle: () => void
  seek: (seconds: number) => void
  skip: (delta: number) => void
  setRate: (rate: number) => void
  setVolume: (volume: number) => void
  upNext: UpNext | null
  /** Starts the next part right away instead of waiting for the countdown. */
  playUpNext: () => void
  cancelUpNext: () => void
  /** The last automatic switch to a next part, so the page showing the old one can follow. */
  lastAdvance: { from: string; to: string } | null
}

export const SPEEDS = [0.75, 1, 1.25, 1.5, 1.75, 2] as const

/** Seconds before the next part of a season starts by itself. */
export const UP_NEXT_SECONDS = 8

export const PlayerContext = createContext<Player | null>(null)

export function usePlayer(): Player {
  const player = useContext(PlayerContext)
  if (!player) throw new Error('usePlayer must be used inside <PlayerProvider>')
  return player
}
