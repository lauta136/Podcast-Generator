# Design Decisions — AI Language Learning Podcast App

Read `docs/BRIEF.md` first (product requirements). This file fixes the technical decisions. If they conflict, this file wins on technical choices and the brief wins on product behavior.

## 0. Amendments (agreed 2026-11-08)
These override the sections below where they differ.
- **.NET 10 LTS** instead of .NET 8 (.NET 8 support ends 2026-11-10). SDK pinned in `global.json`.
- **CEFR level** is selected in preferences and can be overridden per episode. **Difficulty ratings** (*too easy / just right / too hard*) are in the MVP: `Episode.DifficultyRating`, `PUT /api/episodes/{id}/rating`. Recent ratings at the same level are added to the outline prompt to fine-tune within that level. They never change the level.
- **Episode length** is a parameter (5–60 min, max in `Episodes:MaxDurationMin`). 10 min is only the acceptance test.
- **Length is measured, not trusted:** count words per generated section. If the episode is short, generate one more section from the outline.
- **Audio:** per-paragraph MP3 chunks (cached, resumable) concatenated into one MP3 per episode when all chunks are done. Duration comes from parsing MP3 frame headers (no ffmpeg). Sentence timings inside a paragraph are split proportionally by character count.
- **TTS budget:** reserve estimated characters atomically before each request, count characters as sent (failures included), default budgets at ~90% of the free tier.
- **One worker loop** with a lease. The lease still protects restarts and expired locks.
- **Repositories** are query-specific, not generic. Validation uses DataAnnotations (no FluentValidation dependency).
- **Native language default is English** (configurable), replacing Spanish in §1.
- **Seasons come after the MVP** (see the brief). For the MVP, `Episode` has a nullable `SeasonId` and `SeasonOrder`, and a `Season` table (`UserId`, title, topic, level, arc JSON) exists but has no API or UI yet. Episode prompts will later take the season arc and summaries of earlier episodes, so each `Episode` stores a short `Summary` generated with its outline.
- **TTS auth is an API key** restricted to the Text-to-Speech API (`Tts:Google:ApiKey`), over REST, instead of a service account + SDK: no new dependencies. Agreed 2026-11-09.
- **TTS budgets default to 900,000 characters per family** (90% of 1M), because Google's pages disagree about WaveNet's free tier (1M vs 4M). Usage is counted in UTF-8 bytes, which is never less than what Google bills.
- **Audio is one stitched MP3 per episode**, joined from per-paragraph chunks. Proofreading runs on every section before the text is final (see ARCHITECTURE).
- `TranscriptSegment` has a nullable `Translation` column, ready for on-demand translation after the MVP.
- **Test runners:** xUnit (backend), Vitest (frontend). Frontend lint uses oxlint, which ships with the current Vite template.

## 1. Scope
- Build **Phase 1 (MVP) only**: preferences, episode configuration, outline + section-based script generation, TTS, audio player, transcript, library.
- Do not build vocabulary, quizzes, multi-speaker, seasons, offline mode or extra languages. Keep the model ready for them, don't implement them.
- German is the only target language for now; native language is configurable (default English).
- Real functionality only. No mock data in real code paths. Fakes live in test projects.

## 2. Stack (fixed, intentionally boring)
- **Backend:** ASP.NET Core Web API (.NET 10, see §0), C#, async/await, built-in DI.
- **Data:** PostgreSQL + EF Core (code-first migrations), Npgsql. LINQ for queries.
- **Architecture:** classic layered: `Api` (controllers, DTOs, middleware) → `Application` (services, interfaces) → `Domain` (entities, enums) → `Infrastructure` (EF Core, repositories, external providers, storage). Repository + service pattern, thin controllers, DTOs for all input/output (never expose entities).
- **Validation/errors:** DataAnnotations, global exception handling returning `ProblemDetails`.
- **Frontend:** React + TypeScript + Vite, React Router, plain `fetch` wrapped in a small typed API client, hooks for state. CSS Modules or plain CSS. No extra state libraries, UI kits or Tailwind.
- **Background work:** built-in `BackgroundService` + a persistent `GenerationJob` table in PostgreSQL. No Hangfire.
- **Local infra:** `docker-compose.yml` with PostgreSQL only.
- **Auth:** MVP uses one seeded local user, but every owned entity has a `UserId`. JWT auth (register/login) comes right after the MVP works, as its own PR.

## 3. Providers (behind interfaces)
```csharp
public interface ILlmProvider {
    Task<T> GenerateJsonAsync<T>(LlmRequest request, CancellationToken ct);
    Task<string> GenerateTextAsync(LlmRequest request, CancellationToken ct);
}
public interface ITtsProvider {
    Task<TtsResult> SynthesizeAsync(TtsRequest request, CancellationToken ct); // mp3 bytes + duration
    Task<IReadOnlyList<VoiceInfo>> GetVoicesAsync(string languageCode, CancellationToken ct);
}
public interface IAudioStorage {
    Task<string> SaveAsync(string key, Stream audio, string contentType, CancellationToken ct);
    Task<Stream> OpenReadAsync(string key, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}
```
Provider choice is configuration (`Llm:*`, `Tts:*`, `Storage:*`), resolved through DI.

**LLM (free tier):** one HTTP chat-completions client with configurable `BaseUrl`, `ApiKey`, `Model`. Default for development: **Gemini Flash** via its OpenAI-compatible endpoint (`https://generativelanguage.googleapis.com/v1beta/openai/`). **Groq** (`https://api.groq.com/openai/v1`) works by changing config only. Model names live only in config.
- Free tiers are rate limited: rate limiter plus 429 handling (honor `Retry-After`, exponential backoff with jitter). Limits in config per provider. After max retries mark the step failed (resumable), never crash the job.
- Free tiers may train on prompts: never send personal data.

**TTS:** `GoogleCloudTtsProvider`, MP3 output, German voices. Default **WaveNet** (4M free chars/month); optional premium **Chirp 3: HD** (1M free chars/month), selectable via user preference. Voice catalog comes from the provider and records each voice's model family.
- Billing is enabled on the Google project, so going over the free limit **charges money**. Track characters per model family per month in `TtsUsage`. At the configured budget (`Tts:Budgets:WaveNet:MonthlyChars`, `Tts:Budgets:Chirp3HD:MonthlyChars`) **pause the job** with a clear UI message. Never continue past the free limit automatically.
- Batch by paragraph (~800–1,000 chars per request, check the current per-request limit), keep a mapping to transcript segments, compute `startSec/endSec` cumulatively.
- Cache key = hash(text + voice + model + provider). Never regenerate completed audio.

**Storage:** `LocalFileAudioStorage` (configurable folder) behind `IAudioStorage`, served by an endpoint with HTTP Range support. Cloud storage later.

## 4. Generation pipeline
Every step is **idempotent and persisted**: a restart or failure never redoes completed work or re-spends quota.
1. `POST /api/episodes` validates, creates `Episode(Pending)` + `GenerationJob`, returns **202**. Never generate inside the request.
2. The `BackgroundService` picks up runnable jobs using a lease (`lockedUntil`) and recovers expired leases on startup.
3. **Outline:** one LLM call returns JSON (title, description, sections with title, summary, target words, vocabulary/grammar focus). Persist sections.
4. **Sections:** one LLM call per section. Context = episode metadata + full outline + short rolling summary of previous sections + last few sentences of the previous one. Never resend all previous text. Split output into sentences/paragraphs, persist as `TranscriptSegment`s, mark the section done immediately.
5. **Audio:** synthesize per paragraph, persist file + duration + timings per segment, then stitch.
6. `Episode = Ready` only when all sections and audio segments are complete.
7. Retry endpoint re-queues only failed/missing items.
- Statuses: Pending → GeneratingOutline → GeneratingSections → GeneratingAudio → Ready | Failed (plus Paused for budget). Sections and audio segments have their own status, `attempts` and `lastError`.
- Length: `targetWords = durationMin × wordsPerMinute(level)`, wpm in config (≈110–130 for A1–B1, ≈140–150 for B2+). Sections of ~600–1,200 words. Never pad with repetition.
- Prompts: versioned template files (`Application/Prompts/`), CEFR-specific constraints, natural spoken podcast-host style, no markdown or stage directions.
- Validate every LLM JSON response; on invalid JSON retry once with a repair prompt, then fail the step.

## 5. Data model
`User`, `UserPreferences`, `Season` (post-MVP, see §0), `Episode`, `EpisodeSection`, `TranscriptSegment`, `AudioSegment` (voice, model family, provider, duration, storage key), `ListeningProgress`, `GenerationJob`, `TtsUsage`. Indexes for library search/filter (title, topic, language, level, status).

## 6. API (MVP)
- `GET/PUT /api/preferences`
- `POST /api/episodes`, `GET /api/episodes` (search, filters, paging), `GET /api/episodes/{id}`, `DELETE /api/episodes/{id}` (also deletes audio)
- `GET /api/episodes/{id}/status`, `POST /api/episodes/{id}/retry`
- `GET /api/episodes/{id}/transcript` (sections + segments with timings)
- `GET /api/episodes/{id}/audio` (one stitched file, Range supported)
- `GET/PUT /api/episodes/{id}/progress`, `PUT /api/episodes/{id}/rating`
- `GET /api/voices?language=de`

## 7. Frontend (MVP screens)
Home, Create Episode (progress polling every 2–3 s), My Episodes (search, filter, progress, resume), Listening page (persistent mini-player, ±15 s, speeds 0.75–2×, seek, volume, resume position saved every ~5 s and on pause, transcript with click-to-seek and current-segment highlight, section navigation, difficulty rating), Profile/Preferences. Every screen has loading, empty and error states. Mobile-first, keyboard accessible. Show estimated characters/budget impact on the create form.

## 8. Git, CI and PRs
- `main` is protected. One branch per slice: `feat/…`, `fix/…`, `chore/…`, `docs/…`. Conventional Commits.
- One PR per slice using `.github/pull_request_template.md`. Keep PRs small (< ~400 lines when possible). The owner merges.
- GitHub Actions CI on every PR: backend (restore, build with warnings as errors, format check, test with a PostgreSQL service) and frontend (npm ci, lint, typecheck, test, build). Dependabot weekly for nuget, npm, github-actions.
- **CI never uses real API keys.** Tests use fake providers.

## 9. Hard rules
1. Never commit secrets. `dotnet user-secrets` locally, env vars elsewhere, keep `.env.example`. Keys never reach the frontend.
2. Never exceed free TTS limits automatically.
3. Never generate an episode inside an HTTP request.
4. Never redo completed LLM/TTS work.
5. Never commit generated audio, `bin/`, `obj/`, `node_modules/` or `.env`.

## 10. Ask first
New NuGet/npm dependencies beyond the stack above, breaking schema changes, changing a provider decision in §3, anything that could cost money.

## 11. Tests
Unit: length estimation, prompt building, JSON validation/repair, rate limiter, character budget, job state transitions, MP3 duration parsing. Integration (PostgreSQL + fake providers): create episode → Ready; failure mid-way → retry redoes only the failed items. Manual end-to-end checklist with real providers on a 10-minute episode.

## 12. Definition of done
A user can set preferences, create a 10-minute German B1 episode, watch progress, listen with all controls, read the transcript, close the browser, come back through the library and resume. Killing the API mid-generation and restarting finishes the episode without redoing finished sections or audio.
