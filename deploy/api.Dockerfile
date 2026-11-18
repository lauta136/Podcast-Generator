# The API with its background worker. Built from the repository root:
#   docker build -f deploy/api.Dockerfile .
# The SDK stage runs on the build machine's own architecture and cross-compiles for the target (arm64 on Oracle).

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# Project files first, so a code-only change reuses the restored packages.
COPY global.json Directory.Build.props .editorconfig ./
COPY src/PodcastGenerator.Domain/PodcastGenerator.Domain.csproj src/PodcastGenerator.Domain/
COPY src/PodcastGenerator.Application/PodcastGenerator.Application.csproj src/PodcastGenerator.Application/
COPY src/PodcastGenerator.Infrastructure/PodcastGenerator.Infrastructure.csproj src/PodcastGenerator.Infrastructure/
COPY src/PodcastGenerator.Api/PodcastGenerator.Api.csproj src/PodcastGenerator.Api/
RUN dotnet restore src/PodcastGenerator.Api/PodcastGenerator.Api.csproj -a $TARGETARCH

COPY src/ src/
RUN dotnet publish src/PodcastGenerator.Api/PodcastGenerator.Api.csproj -c Release -a $TARGETARCH --no-restore -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
# Audio lives on a volume at /data/audio. Creating the folder here, owned by the non-root user the image runs as,
# makes a new Docker volume start with the same owner, so the API can write to it.
RUN mkdir -p /data/audio && chown $APP_UID /data/audio
COPY --from=build /out .
USER $APP_UID
ENV ASPNETCORE_ENVIRONMENT=Production \
    Storage__LocalPath=/data/audio \
    Proxy__TrustForwardedHeaders=true
EXPOSE 8080
ENTRYPOINT ["dotnet", "PodcastGenerator.Api.dll"]
