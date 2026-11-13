import { createContext, useContext } from 'react'

export interface LoadedEpisode {
  id: string
  title: string
}

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
}

export const SPEEDS = [0.75, 1, 1.25, 1.5, 1.75, 2] as const

export const PlayerContext = createContext<Player | null>(null)

export function usePlayer(): Player {
  const player = useContext(PlayerContext)
  if (!player) throw new Error('usePlayer must be used inside <PlayerProvider>')
  return player
}
