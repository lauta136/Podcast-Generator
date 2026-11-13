import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { api } from '../api/client'
import { PlayerContext, type LoadedEpisode, type Player } from './usePlayer'

const SAVE_EVERY_MS = 5000

/**
 * Owns the single <audio> element, so playback survives navigation between pages.
 * Saves the resume position every few seconds while playing, on pause, at the end and when the tab is hidden.
 */
export function PlayerProvider({ children, initialRate }: { children: ReactNode; initialRate: number }) {
  const audio = useRef<HTMLAudioElement>(null)
  const [episode, setEpisode] = useState<LoadedEpisode | null>(null)
  const [playing, setPlaying] = useState(false)
  const [currentTime, setCurrentTime] = useState(0)
  const [duration, setDuration] = useState(0)
  const [rate, setRateState] = useState(initialRate)
  const [volume, setVolumeState] = useState(1)
  const pendingSeek = useRef<number | null>(null)
  const lastSaved = useRef<{ id: string; at: number } | null>(null)

  // Preferences arrive after the first render: adopt their speed (React's "adjust state on prop change" pattern).
  const [seenInitialRate, setSeenInitialRate] = useState(initialRate)
  if (initialRate !== seenInitialRate) {
    setSeenInitialRate(initialRate)
    setRateState(initialRate)
  }

  useEffect(() => {
    if (audio.current) audio.current.playbackRate = rate
  }, [rate])

  const save = useCallback((completed = false, keepalive = false) => {
    const el = audio.current
    const id = episode?.id
    if (!el || !id || !Number.isFinite(el.currentTime)) return
    const at = Math.round(el.currentTime * 10) / 10
    if (!completed && lastSaved.current?.id === id && lastSaved.current.at === at) return
    lastSaved.current = { id, at }
    if (keepalive) {
      // The page may be going away: a keepalive request outlives it.
      void fetch(`/api/episodes/${id}/progress`, {
        method: 'PUT',
        keepalive: true,
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ positionSec: at, completed }),
      }).catch(() => undefined)
    } else {
      api.saveProgress(id, at, completed).catch(() => undefined) // best effort; the next save retries
    }
  }, [episode?.id])

  useEffect(() => {
    if (!playing) return
    const timer = setInterval(() => save(), SAVE_EVERY_MS)
    return () => clearInterval(timer)
  }, [playing, save])

  useEffect(() => {
    const onHide = () => {
      if (document.visibilityState === 'hidden') save(false, true)
    }
    document.addEventListener('visibilitychange', onHide)
    window.addEventListener('pagehide', onHide)
    return () => {
      document.removeEventListener('visibilitychange', onHide)
      window.removeEventListener('pagehide', onHide)
    }
  }, [save])

  const load = useCallback((next: LoadedEpisode, startAt: number) => {
    const el = audio.current
    if (!el) return
    if (episode?.id === next.id) return
    if (episode) save()
    setEpisode(next)
    setCurrentTime(startAt)
    setDuration(0)
    pendingSeek.current = startAt
    el.src = api.audioUrl(next.id)
    el.load()
  }, [episode, save])

  const toggle = useCallback(() => {
    const el = audio.current
    if (!el || !episode) return
    if (el.paused) void el.play().catch(() => setPlaying(false))
    else el.pause()
  }, [episode])

  const seek = useCallback((seconds: number) => {
    const el = audio.current
    if (!el) return
    const max = Number.isFinite(el.duration) ? el.duration : seconds
    el.currentTime = Math.min(Math.max(0, seconds), max)
    setCurrentTime(el.currentTime)
  }, [])

  const skip = useCallback((delta: number) => seek((audio.current?.currentTime ?? 0) + delta), [seek])

  const setRate = useCallback((r: number) => setRateState(r), [])

  const setVolume = useCallback((v: number) => {
    setVolumeState(v)
    if (audio.current) audio.current.volume = v
  }, [])

  const value = useMemo<Player>(
    () => ({ episode, playing, currentTime, duration, rate, volume, load, toggle, seek, skip, setRate, setVolume }),
    [episode, playing, currentTime, duration, rate, volume, load, toggle, seek, skip, setRate, setVolume],
  )

  return (
    <PlayerContext.Provider value={value}>
      {children}
      <audio
        ref={audio}
        preload="metadata"
        onLoadedMetadata={(e) => {
          const el = e.currentTarget
          setDuration(el.duration)
          el.playbackRate = rate
          if (pendingSeek.current !== null) {
            // Resuming at the very end would look like "nothing happens"; start over instead.
            el.currentTime = pendingSeek.current < el.duration - 5 ? pendingSeek.current : 0
            setCurrentTime(el.currentTime)
            pendingSeek.current = null
          }
        }}
        onTimeUpdate={(e) => setCurrentTime(e.currentTarget.currentTime)}
        onPlay={() => setPlaying(true)}
        onPause={() => {
          setPlaying(false)
          save()
        }}
        onEnded={() => {
          setPlaying(false)
          save(true)
        }}
      />
    </PlayerContext.Provider>
  )
}
