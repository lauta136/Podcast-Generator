# Product Brief — AI Language Learning Podcast App

Technical decisions live in `docs/DESIGN.md`. This file defines product behavior.

## Problem
Learners at intermediate levels (A2–B2) run out of listening material that is interesting, at the right difficulty and about topics they care about. Native podcasts are too fast or too hard; textbook audio is dull.

## Product
A web app that generates podcast episodes in the target language, on any topic, at the learner's CEFR level and chosen length, with a synced transcript and a good player. First target: German, for a B1 learner whose native language is English.

## Users
One learner in the MVP (seeded local user). Registration and login come right after the MVP.

## MVP features

### Preferences (Profile page)
- Native language (default English). Target language: German only for now.
- Default CEFR level (A1–C2, default B1).
- Default episode length (minutes).
- Interests (free-text list), used by "Surprise me".
- Voice (from the provider's German voice catalog) and voice quality: Standard (WaveNet) or Premium (Chirp 3: HD).
- Speaking speed: how fast the voice speaks in new episodes. Each level has a default (slower for lower levels, e.g. 0.85× at B1); the learner can pick a fixed speed instead. Level and speed are separate on purpose, so a learner can move up one step at a time: first faster speech at the same level, then harder language at a comfortable speed.
- Default playback speed (the player's live speed control, on top of the speaking speed).

### Create Episode
- Topic: free text, or "Surprise me" (picked from interests).
- Level (prefilled from preferences, can be overridden per episode).
- Speaking speed (prefilled from preferences or the level default, can be overridden per episode). Length stays in minutes: slower speech means fewer words for the same duration.
- Length: 5–60 minutes (max is configurable).
- Optional style: conversational monologue, storytelling, news-style explainer.
- Shows an estimate before submitting: word count, characters to synthesize, and the share of this month's free TTS budget it uses. Submission is blocked if it would exceed the remaining budget.
- After submitting, shows generation progress (outline → sections n/N → audio n/N) and moves to the Listening page when ready. Failed episodes show the error and a Retry button. Paused-for-budget episodes say so plainly.

### My Episodes (library)
- List with title, topic, level, length, status, listening progress.
- Search by title/topic, filter by level and status, paging.
- Resume from the saved position. Delete an episode (also deletes its audio).

### Listening page
- Player: play/pause, ±15 s, seek bar, speed 0.75–2×, volume.
- Persistent mini-player while navigating the app.
- Resume position saved every ~5 s and on pause.
- Transcript under the player: the current sentence is highlighted and auto-scrolled; clicking a sentence seeks to it. Section list for jumping between sections.
- Difficulty rating: *Too easy / Just right / Too hard*. Ratings fine-tune future episodes **within the chosen level** (for example "an easier B1"). They never change the level automatically.

## Content quality
- Natural spoken style of a friendly podcast host. Plain text only: no markdown, headings, emojis or stage directions, since everything is read aloud.
- Vocabulary and grammar follow CEFR level constraints. Each section has a focus (a vocabulary field or grammar point) that appears naturally, without a lesson tone.
- The episode reaches its target length with new content. Never pad with repetition.
- Every section is proofread for grammar and spelling before it is read aloud, because learners pick up the mistakes they hear.
- No personal data in prompts.

## Out of scope for the MVP (keep the model ready)
Vocabulary lists and flashcards, quizzes, per-sentence translation (planned next: tap a sentence to see it in your native language), multiple speakers or dialogues, offline mode, other target languages.

## Planned after the MVP: Seasons
Episodes can be grouped into **seasons** on a big topic, for example "The Spanish Empire".
- Create a season from a topic, level and number of episodes. The app plans an arc first (one line per episode, in order), then generates episodes from it.
- Each episode knows what came before: its prompt gets the season arc plus short summaries of earlier episodes, so it builds on them instead of repeating them, and can briefly recap at the start.
- Existing standalone episodes can be moved into a season, and episodes can be reordered.
- Library shows seasons as folders with overall progress ("3 of 8 listened"). Playing a season continues to the next episode.
- Budget estimate on the create form covers the whole season, and episodes are generated one at a time so a season can stop at the TTS budget and resume next month.

For the MVP, episodes have a nullable season reference and an order number so seasons can be added without a breaking schema change.

## Success
A learner can set preferences, create a 10-minute German B1 episode, watch it generate, listen with all controls while following the transcript, close the browser, come back through the library and resume where they left off.
