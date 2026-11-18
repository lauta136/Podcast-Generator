#!/usr/bin/env bash
# Nightly backup: the database (a compressed pg_dump) and the audio files, into deploy/backups/.
# Keeps the last 14 database dumps and the last 2 audio archives (audio is large and changes slowly).
# Run from cron on the server, e.g.:  15 3 * * *  /home/ubuntu/podcast/deploy/backup.sh >> /home/ubuntu/backup.log 2>&1
# Restore: see docs/DEPLOYMENT.md.
set -euo pipefail

cd "$(dirname "$0")"
mkdir -p backups
stamp=$(date -u +%Y-%m-%dT%H%MZ)
project=${COMPOSE_PROJECT_NAME:-podcast} # the "name:" in docker-compose.yml

# Custom format (-Fc) is compressed and lets pg_restore restore selectively. Written to a temp name first,
# so a failed dump never replaces a good one.
docker compose exec -T db pg_dump -U podcast -d podcast -Fc > "backups/db-$stamp.dump.partial"
mv "backups/db-$stamp.dump.partial" "backups/db-$stamp.dump"

# The audio volume, read through a throwaway container so the API keeps running.
docker run --rm \
  -v "${project}_audio:/audio:ro" \
  -v "$PWD/backups:/backups" \
  alpine tar -czf "/backups/audio-$stamp.tar.gz.partial" -C /audio .
mv "backups/audio-$stamp.tar.gz.partial" "backups/audio-$stamp.tar.gz"

ls -1t backups/db-*.dump | tail -n +15 | xargs -r rm --
ls -1t backups/audio-*.tar.gz | tail -n +3 | xargs -r rm --

echo "$(date -u +%FT%TZ) backup ok: db-$stamp.dump, audio-$stamp.tar.gz"
