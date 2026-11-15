import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { api } from '../api/client'
import { findNextPart } from '../lib/seasons'
import { PlayerContext, UP_NEXT_SECONDS, type LoadedEpisode, type Player, type UpNext } from './usePlayer'

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
  const playWhenLoaded = useRef(false)
  const lastSaved = useRef<{ id: string; at: number } | null>(null)
  const [upNext, setUpNext] = useState<UpNext | null>(null)
  // The current episode for callbacks. Reading it from a ref keeps load/save stable: if they changed with every
  // episode, a page still showing the previous episode would re-run its "load my episode" effect and switch back.
  const episodeRef = useRef<LoadedEpisode | null>(null)
  const [lastAdvance, setLastAdvance] = useState<{ from: string; to: string } | null>(null)

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
    const id = episodeRef.current?.id
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
  }, [])

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

  const switchTo = useCallback((next: LoadedEpisode, startAt: number, autoplay: boolean) => {
    const el = audio.current
    if (!el) return
    if (episodeRef.current?.id === next.id) return
    if (episodeRef.current) save()
    playWhenLoaded.current = autoplay
    setUpNext(null)
    episodeRef.current = next
    setEpisode(next)
    setCurrentTime(startAt)
    setDuration(0)
    pendingSeek.current = startAt
    el.src = api.audioUrl(next.id)
    el.load()
  }, [save])

  const load = useCallback((next: LoadedEpisode, startAt: number) => switchTo(next, startAt, false), [switchTo])

  /** A season episode ended: look up the next part. */
  const offerNextPart = useCallback(async (ended: LoadedEpisode) => {
    if (!ended.seasonId || !ended.seasonOrder) return
    const seasonId = ended.seasonId
    try {
      const next = findNextPart(await api.getSeason(seasonId), ended.seasonOrder)
      if (!next) return
      setUpNext(
        next.kind === 'ready'
          ? { kind: 'countdown', from: ended.id, seasonId, id: next.id, title: next.title, order: next.order, secondsLeft: UP_NEXT_SECONDS }
          : { kind: 'not-ready', from: ended.id, seasonId, title: next.title, order: next.order },
      )
    } catch {
      // offline or signed out: no offer, nothing else breaks
    }
  }, [])

  const playUpNext = useCallback(async () => {
    if (upNext?.kind !== 'countdown') return
    const { id, title, order, seasonId, from } = upNext
    setUpNext(null)
    // Continue where the listener left that part, unless they finished it.
    const progress = await api.getProgress(id).catch(() => null)
    const startAt = progress && !progress.completed ? progress.positionSec : 0
    switchTo({ id, title, seasonId, seasonOrder: order }, startAt, true)
    setLastAdvance({ from, to: id })
  }, [upNext, switchTo])

  const cancelUpNext = useCallback(() => setUpNext(null), [])

  // The countdown: one tick per second, then the next part starts.
  useEffect(() => {
    if (upNext?.kind !== 'countdown') return
    const timer = setTimeout(() => {
      if (upNext.secondsLeft <= 1) void playUpNext()
      else setUpNext((u) => (u?.kind === 'countdown' ? { ...u, secondsLeft: u.secondsLeft - 1 } : u))
    }, 1000)
    return () => clearTimeout(timer)
  }, [upNext, playUpNext])

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
    () => ({
      episode,
      playing,
      currentTime,
      duration,
      rate,
      volume,
      load,
      toggle,
      seek,
      skip,
      setRate,
      setVolume,
      upNext,
      playUpNext: () => void playUpNext(),
      cancelUpNext,
      lastAdvance,
    }),
    [episode, playing, currentTime, duration, rate, volume, load, toggle, seek, skip, setRate, setVolume, upNext, playUpNext, cancelUpNext, lastAdvance],
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
          if (playWhenLoaded.current) {
            playWhenLoaded.current = false
            void el.play().catch(() => setPlaying(false)) // the listener already started audio here, so this is allowed
          }
        }}
        onTimeUpdate={(e) => setCurrentTime(e.currentTarget.currentTime)}
        onPlay={() => {
          setPlaying(true)
          setUpNext(null) // replaying (or anything else) cancels the offer
        }}
        onPause={() => {
          setPlaying(false)
          save()
        }}
        onEnded={() => {
          setPlaying(false)
          save(true)
          if (episodeRef.current) void offerNextPart(episodeRef.current)
        }}
      />
    </PlayerContext.Provider>
  )
}
