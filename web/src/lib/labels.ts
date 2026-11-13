import type { DifficultyRating, EpisodeStatus, EpisodeStyle, VoiceFamily } from '../api/types'

export const statusLabel: Record<EpisodeStatus, string> = {
  Pending: 'Waiting to start',
  GeneratingOutline: 'Planning the episode',
  GeneratingSections: 'Writing',
  GeneratingAudio: 'Recording',
  Ready: 'Ready',
  Failed: 'Failed',
  PausedForBudget: 'Paused: monthly voice budget used up',
}

export const isGenerating = (s: EpisodeStatus) =>
  s === 'Pending' || s === 'GeneratingOutline' || s === 'GeneratingSections' || s === 'GeneratingAudio'

export const styleLabel: Record<EpisodeStyle, string> = {
  Conversational: 'Conversation',
  Storytelling: 'Story',
  Explainer: 'Explainer',
}

export const familyLabel: Record<VoiceFamily, string> = {
  WaveNet: 'Standard (WaveNet)',
  Chirp3HD: 'Premium (Chirp 3: HD)',
}

export const ratingLabel: Record<DifficultyRating, string> = {
  TooEasy: 'Too easy',
  JustRight: 'Just right',
  TooHard: 'Too hard',
}

/** 0.85 → "0.85×" */
export const formatRate = (rate: number) => `${rate.toFixed(2).replace(/0$/, '')}×`
