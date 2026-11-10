# Podcast Generator

AI-generated listening podcasts for language immersion. Pick a topic, a CEFR level and a length; the app writes a natural, level-appropriate episode in German, reads it aloud with Google Cloud TTS, and plays it with a synced transcript.

- Product behavior: [docs/BRIEF.md](docs/BRIEF.md)
- Technical decisions: [docs/DESIGN.md](docs/DESIGN.md)
- How the code is organized: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)

## Stack
ASP.NET Core (.NET 10) · EF Core + PostgreSQL · React + TypeScript + Vite · Gemini or Groq (LLM, free tier) · Google Cloud Text-to-Speech.

## Repository layout
```
src/PodcastGenerator.Api             Controllers, DTOs, middleware
src/PodcastGenerator.Application     Services, interfaces, prompts
src/PodcastGenerator.Domain          Entities, enums
src/PodcastGenerator.Infrastructure  EF Core, repositories, providers, storage
tests/                               Unit and integration tests (fake providers)
web/                                 React frontend
```

## Prerequisites
- .NET SDK 10 (pinned in `global.json`)
- Node.js 22+ (see `web/.nvmrc`)
- Docker (for PostgreSQL)

## Run locally
```bash
docker compose up -d
```
```bash
dotnet run --project src/PodcastGenerator.Api
```
```bash
cd web && npm install && npm run dev
```
PostgreSQL listens on `localhost:5433`. The API applies migrations on startup in Development. The frontend proxies `/api` to the API at `http://localhost:5273`. Health check: `GET /health`.

## Secrets
Secrets are never committed. Locally they go in `dotnet user-secrets` for the API project; elsewhere, environment variables (`Section__Key`). Keys never reach the frontend.

| Setting | Purpose |
|---|---|
| `Llm:ApiKey` | Gemini or Groq API key |
| `Tts:Google:ApiKey` | Google Cloud API key restricted to the Text-to-Speech API |

## LLM setup (free tier)
Episodes are written by any OpenAI-compatible chat API. Pick one; switching is config only.

**Gemini (default)**
1. Create a **new Google Cloud project with no billing account linked**, just for the Gemini key. If a project has billing linked, Google charges for all Gemini usage, even within the free limits, and your TTS project will have billing enabled.
2. In [Google AI Studio](https://aistudio.google.com/apikey), create an API key in that project.
3. Store it:
   ```bash
   dotnet user-secrets set "Llm:ApiKey" "<your key>" --project src/PodcastGenerator.Api
   ```
4. Models: the default is `gemini-3.5-flash`, with `gemini-3.5-flash-lite` as automatic fallback when the main model is overloaded or out of daily quota (`Llm:Model`, `Llm:FallbackModel`). Free-tier limits are per model and change without notice. To see which models your key can use:
   ```bash
   KEY=$(dotnet user-secrets list --project src/PodcastGenerator.Api | sed -n 's/^Llm:ApiKey = //p'); curl -s -H "x-goog-api-key: $KEY" "https://generativelanguage.googleapis.com/v1beta/models?pageSize=200" | python3 -c 'import sys,json; [print(m["name"]) for m in json.load(sys.stdin).get("models",[]) if "flash" in m["name"]]'
   ```

**Groq**
1. Create a key at [console.groq.com/keys](https://console.groq.com/keys).
2. Store it and point the app at Groq:
   ```bash
   dotnet user-secrets set "Llm:ApiKey" "<your key>" --project src/PodcastGenerator.Api
   ```
   ```bash
   dotnet user-secrets set "Llm:BaseUrl" "https://api.groq.com/openai/v1/" --project src/PodcastGenerator.Api
   ```
   ```bash
   dotnet user-secrets set "Llm:Model" "<a model from the Groq console>" --project src/PodcastGenerator.Api
   ```
3. Remove `Llm:ReasoningEffort` (set it to an empty string) if the chosen model rejects it.

A 10-minute episode uses about 5 LLM requests (1 outline + 2 sections + 2 proofreading passes); a 60-minute one about 19. Turning proofreading off (`Episodes:Proofread=false`) roughly halves that, at the cost of occasional grammar slips in the German. Free tiers may use prompts for training, so the app only ever sends the topic, level and generated text, never personal data.

Busy models (HTTP 503) are retried with backoff of up to a minute per attempt; timeouts are retried only once, because a timed-out request may still count against the quota. If the main model stays unavailable, the fallback model takes over. If both are out of quota, the episode fails with a clear message instead of waiting for hours. Use **Retry** later; finished sections are kept.

## Text-to-Speech setup (Google Cloud)
Text-to-Speech requires billing on its Google Cloud project, so usage **beyond the free tier costs money**. The app stops itself before that happens (see "Budget guard" below), but set up a billing alert as a second safety net.

1. In the [Google Cloud console](https://console.cloud.google.com/), create a project for TTS (this is the one with billing; keep the Gemini key in its separate no-billing project).
2. Link a billing account, then enable the **Cloud Text-to-Speech API** (APIs & Services → Library).
3. **Billing → Budgets & alerts → Create budget:** set an amount such as 1 USD with alerts at 50%, 90% and 100%.
4. **APIs & Services → Credentials → Create credentials → API key.** Edit the key: under **API restrictions**, choose **Restrict key** and select only **Cloud Text-to-Speech API**.
5. Store it:
   ```bash
   dotnet user-secrets set "Tts:Google:ApiKey" "<your key>" --project src/PodcastGenerator.Api
   ```

**Budget guard.** Before every request the app reserves the text's size (UTF-8 bytes, which is never less than what Google bills) against a monthly budget per voice family, and pauses the episode before the budget would be exceeded. Nothing is refunded, failed requests included, so the count only overestimates. Defaults are `Tts:Budgets:WaveNet:MonthlyChars = 900000` and `Tts:Budgets:Chirp3HD:MonthlyChars = 900000`, 90% of 1M characters, because Google's pages disagree about WaveNet's free tier (1M vs 4M). Check the [pricing page](https://cloud.google.com/text-to-speech/pricing) for your account before raising it. `GET /api/tts/budget` shows this month's usage. A 10-minute B1 episode is about 9,000 characters.

Audio files are stored under `src/PodcastGenerator.Api/audio-storage/` (`Storage:LocalPath`), which is git-ignored.

## Tests
```bash
dotnet test
```
```bash
cd web && npm test
```
CI runs both on every pull request with fake providers and never uses real API keys.
