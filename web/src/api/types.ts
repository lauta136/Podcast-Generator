// Mirrors the API's DTOs (src/PodcastGenerator.Api/Dtos). Enums travel as strings.

export const LEVELS = ['A1', 'A2', 'B1', 'B2', 'C1', 'C2'] as const
export type CefrLevel = (typeof LEVELS)[number]

export const STYLES = ['Conversational', 'Storytelling', 'Explainer'] as const
export type EpisodeStyle = (typeof STYLES)[number]

export type EpisodeStatus =
  | 'Pending'
  | 'GeneratingOutline'
  | 'GeneratingSections'
  | 'GeneratingAudio'
  | 'Ready'
  | 'Failed'
  | 'PausedForBudget'

export type VoiceFamily = 'WaveNet' | 'Chirp3HD'
export type DifficultyRating = 'TooEasy' | 'JustRight' | 'TooHard'

export interface User {
  id: string
  email: string | null
  displayName: string
}

export interface Preferences {
  nativeLanguage: string
  targetLanguage: string
  defaultLevel: CefrLevel
  defaultDurationMin: number
  interests: string[]
  voiceName: string | null
  voiceModelFamily: VoiceFamily
  playbackSpeed: number
  speakingRate: number | null
  updatedAt: string
}

export type PreferencesUpdate = Omit<Preferences, 'updatedAt'>

export interface Voice {
  name: string
  languageCode: string
  gender: string
  modelFamily: VoiceFamily
}

export interface Episode {
  id: string
  title: string | null
  description: string | null
  topic: string
  languageCode: string
  level: CefrLevel
  style: EpisodeStyle
  durationMin: number
  targetWords: number
  speakingRate: number
  status: EpisodeStatus
  lastError: string | null
  difficultyRating: DifficultyRating | null
  audioDurationSec: number | null
  createdAt: string
}

export interface EpisodeStatusInfo {
  episodeId: string
  status: EpisodeStatus
  title: string | null
  lastError: string | null
  sectionsTotal: number
  sectionsCompleted: number
  audioTotal: number
  audioCompleted: number
}

export interface Estimate {
  level: CefrLevel
  durationMin: number
  targetWords: number
  speakingRate: number
  voiceModelFamily: VoiceFamily
  estimatedChars: number
  budgetRemaining: number
  monthlyBudget: number
  fitsBudget: boolean
}

export interface CreateEpisodeRequest {
  topic?: string
  surpriseMe: boolean
  level?: CefrLevel
  durationMin?: number
  style: EpisodeStyle
  speakingRate?: number
}

export interface Progress {
  positionSec: number
  completed: boolean
  updatedAt: string | null
}

export interface EpisodeListItem {
  id: string
  title: string | null
  topic: string
  level: CefrLevel
  style: EpisodeStyle
  durationMin: number
  speakingRate: number
  status: EpisodeStatus
  audioDurationSec: number | null
  difficultyRating: DifficultyRating | null
  createdAt: string
  progress: Progress | null
}

export interface EpisodePage {
  items: EpisodeListItem[]
  total: number
  page: number
  pageSize: number
}

export interface TranscriptSegment {
  id: string
  paragraph: number
  text: string
  startSec: number | null
  endSec: number | null
}

export interface TranscriptSection {
  id: string
  order: number
  title: string
  segments: TranscriptSegment[]
}

export interface Transcript {
  episodeId: string
  title: string | null
  sections: TranscriptSection[]
}

export interface Budget {
  modelFamily: VoiceFamily
  used: number
  monthlyBudget: number
  remaining: number
}
