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

Getting keys and enabling Google Cloud TTS (including a billing alert) is documented in the slices that first need them.

## Tests
```bash
dotnet test
```
```bash
cd web && npm test
```
CI runs both on every pull request with fake providers and never uses real API keys.
