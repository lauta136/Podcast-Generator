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

| Setting | Purpose | Needed from |
|---|---|---|
| `Llm:ApiKey` | Gemini or Groq API key | LLM pipeline slice |
| `Tts:CredentialsPath` | Path to the Google Cloud service-account JSON | TTS slice |

Enabling Google Cloud TTS (including a billing alert) is documented with the TTS slice.

## LLM setup (free tier)
Episodes are written by any OpenAI-compatible chat API. Pick one; switching is config only.

**Gemini (default)**
1. Create a **new Google Cloud project with no billing account linked**, just for the Gemini key. If a project has billing linked, Google charges for all Gemini usage, even within the free limits, and your TTS project will have billing enabled.
2. In [Google AI Studio](https://aistudio.google.com/apikey), create an API key in that project.
3. Store it:
   ```bash
   dotnet user-secrets set "Llm:ApiKey" "<your key>" --project src/PodcastGenerator.Api
   ```
4. Check the model and its daily limit in AI Studio. The default is `gemini-3.8-flash`. Free-tier daily limits are low and change without notice; `gemini-3.8-flash-lite` has a higher daily limit if you hit it. Override with `Llm:Model`.

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

A 10-minute episode uses about 3 LLM requests (1 outline + 2 sections); a 60-minute one about 10. Free tiers may use prompts for training, so the app only ever sends the topic, level and generated text, never personal data.

When the provider's quota runs out, the episode fails with a clear message instead of waiting for hours. Use **Retry** later; finished sections are kept.

## Tests
```bash
dotnet test
```
```bash
cd web && npm test
```
CI runs both on every pull request with fake providers and never uses real API keys.
