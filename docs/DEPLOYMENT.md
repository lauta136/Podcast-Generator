# Deployment

The app runs on one small server with Docker Compose: PostgreSQL, the API (with its worker) and Caddy, which gets the HTTPS certificate and serves the web app. This guide uses Oracle Cloud's Always Free ARM server, but any Linux machine with Docker works the same way from step 4.

```
Browser ──https──▶ Caddy (web) ──/api──▶ API + worker ──▶ PostgreSQL
                     │                      │
                  web app             audio volume
```

What lives where:

| Piece | Where |
|---|---|
| Compose file, Caddyfile, Dockerfiles, backup script | `deploy/` in this repository |
| Images | Built by GitHub Actions on every merge to `main`, published as `ghcr.io/lauta136/podcast-generator-api` and `-web` |
| Secrets | A `.env` file on the server, readable only by you. Never in git |
| Data | Docker volumes on the server: database, audio, HTTPS certificate |

## 1. Create the Oracle Cloud account

1. Sign up at [oracle.com/cloud/free](https://www.oracle.com/cloud/free/). The card is only verified. Always Free resources aren't charged.
2. **Choose the home region carefully.** It can't be changed, and free ARM servers are only available there. A region near you or your listeners is best. If creating the server later says "out of capacity", that region is full right now; trying again later usually works.
3. **Upgrade to Pay As You Go** (Billing → Upgrade and Manage Payment). Oracle reclaims *idle* Always Free servers on free accounts, and this app is idle most of the time. Upgraded accounts aren't reclaimed, and Always Free resources stay free.
4. **Set a budget alert right away** (Billing → Budgets → Create Budget): a monthly budget of 1 USD with an alert at 100%. If anything ever starts costing money, you get an email the same day.
5. From now on, only create resources labelled **Always Free-eligible**.

## 2. Create the server

On your Mac, make an SSH key if you don't have one (`ls ~/.ssh/id_ed25519.pub` shows it):

```bash
ssh-keygen -t ed25519 -C oracle-podcast
```

In the console: **Compute → Instances → Create instance**.

| Setting | Value |
|---|---|
| Image | Canonical Ubuntu 24.04 (the aarch64 build is picked automatically for ARM) |
| Shape | Ampere → `VM.Standard.A1.Flex`, with the Always Free allowance: currently 2 OCPUs and 12 GB of memory |
| Networking | Create a new virtual cloud network with a public subnet; **assign a public IPv4 address** |
| SSH keys | Paste the contents of `~/.ssh/id_ed25519.pub` |
| Boot volume | The default (about 50 GB) is plenty and is within the free 200 GB |

When it's running, copy its **public IP address** from the instance page.

## 3. Open the web ports

The server only accepts SSH until you open ports 80 and 443.

1. On the instance page, open the **subnet**, then its **security list** (usually "Default Security List").
2. **Add ingress rules**, each with source CIDR `0.0.0.0/0`:
   - TCP, destination port `80` (Caddy needs it to get the certificate, then redirects to HTTPS)
   - TCP, destination port `443`
   - UDP, destination port `443` (HTTP/3; optional)

Don't open 5432 or 8080: the database and the API are only reachable inside Docker.

## 4. Install Docker

```bash
ssh ubuntu@<server-ip>
```

On the server:

```bash
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker ubuntu
exit
```

Sign in again (`ssh ubuntu@<server-ip>`) so the group change applies, and check:

```bash
docker run --rm hello-world
```

Ubuntu installs security updates by itself (unattended-upgrades is on by default).

## 5. Choose the address

Caddy needs a hostname for the certificate.

- **No domain (free):** use [sslip.io](https://sslip.io), which turns any IP into a hostname. For IP `203.0.113.7` the address is `203-0-113-7.sslip.io`. Nothing to register.
- **Your own domain:** add an `A` record pointing a name such as `podcast.yourname.dev` at the server's IP, and use that name.

## 6. Get the files and write the secrets

```bash
git clone https://github.com/lauta136/Podcast-Generator.git podcast
cd podcast/deploy
cp .env.example .env
chmod 600 .env
```

Generate the random secrets on the server, so they never travel anywhere:

```bash
sed -i "s/^POSTGRES_PASSWORD=.*/POSTGRES_PASSWORD=$(openssl rand -hex 32)/" .env
sed -i "s/^JWT_SIGNING_KEY=.*/JWT_SIGNING_KEY=$(openssl rand -hex 32)/" .env
```

Then edit the rest with `nano .env`:

| Variable | What to put |
|---|---|
| `SITE_ADDRESS` | The address from step 5, e.g. `203-0-113-7.sslip.io` |
| `INVITE_CODE` | A code you'll share with people you invite, e.g. three random words |
| `LLM_API_KEY` | A **new** Gemini key for the server, in the no-billing Google AI Studio project |
| `TTS_API_KEY` | A **new** Google Cloud key restricted to the Text-to-Speech API (Credentials → the key → API restrictions) |

Separate keys mean you can revoke the server's without touching your laptop's.

## 7. Start it

The images are published by the **Images** workflow after a merge to `main`. **The first time only**, make both packages public so the server can download them without signing in: on GitHub, your profile → **Packages** → `podcast-generator-api` → **Package settings** → **Change visibility** → Public. Do the same for `podcast-generator-web`. They contain no secrets: those are only in `.env`.

```bash
docker compose pull
docker compose up -d
docker compose ps
```

`migrate` should show `Exited (0)`, the others `Up`. Open `https://<SITE_ADDRESS>`, choose **Create an account**, and enter the invite code. The first account you create is yours.

To build on the server instead of pulling (slower, but needs no GitHub packages): `docker compose build`, then `docker compose up -d`.

## 8. Backups

`deploy/backup.sh` writes a database dump and an audio archive to `deploy/backups/`. It keeps 14 dumps and 2 audio archives. Run it every night with cron:

```bash
chmod +x ~/podcast/deploy/backup.sh
(crontab -l 2>/dev/null; echo "15 3 * * * $HOME/podcast/deploy/backup.sh >> $HOME/backup.log 2>&1") | crontab -
```

These backups sit on the same disk, so they cover mistakes, not a lost server. Also:

- **Boot volume backups** (Storage → Block Volume → Boot Volumes → your volume → Boot Volume Backups): Always Free includes 5. A monthly one is a good habit.
- **A copy off the server** now and then, from your Mac:
  ```bash
  scp -r ubuntu@<server-ip>:podcast/deploy/backups ~/podcast-backups
  ```

### Restoring

On a fresh server, follow steps 4 to 6, copy the backup files into `deploy/backups/`, then:

```bash
cd ~/podcast/deploy
docker compose up -d --wait db
docker compose exec -T db pg_restore -U podcast -d podcast --no-owner < backups/db-<date>.dump
docker run --rm -v podcast_audio:/audio -v "$PWD/backups:/backups:ro" alpine tar -xzf /backups/audio-<date>.tar.gz -C /audio
docker compose up -d
```

## Updating

After merging a PR, wait for the **Images** workflow to finish on GitHub, then on the server:

```bash
cd ~/podcast && git pull
cd deploy && docker compose pull && docker compose up -d
```

`git pull` brings changes to the Compose file, the Caddyfile or the backup script. Migrations run by themselves before the new API starts. To go back to an earlier version, set `API_IMAGE` and `WEB_IMAGE` in `.env` to the image tagged with that commit's SHA, then run `docker compose up -d`.

## Everyday commands

All run from `~/podcast/deploy`:

| To | Run |
|---|---|
| See what's running | `docker compose ps` |
| Follow the API's logs | `docker compose logs -f api` |
| Restart everything | `docker compose restart` |
| Stop everything (data is kept) | `docker compose down` |
| Open a database shell | `docker compose exec db psql -U podcast` |
| Change a secret | Edit `.env`, then `docker compose up -d` |

## Troubleshooting

- **The site doesn't load, but `curl -I http://localhost` on the server answers.** The ports aren't open: check the security list rules from step 3. If they are there, Ubuntu's own firewall may be blocking them:
  ```bash
  sudo iptables -I INPUT 6 -p tcp -m multiport --dports 80,443 -j ACCEPT
  sudo netfilter-persistent save
  ```
- **The browser warns about the certificate, or Caddy's log mentions "challenge".** Port 80 must be reachable from the internet, and `SITE_ADDRESS` must resolve to this server. Check the log with `docker compose logs web`. If Let's Encrypt refuses, Caddy tries ZeroSSL by itself.
- **`docker compose up` says a variable is missing.** That's on purpose: `.env` lacks one of the required secrets.
- **`migrate` exited with an error.** Read it with `docker compose logs migrate`. The API doesn't start until migrations succeed, so the old data is safe.
- **Episodes fail.** Check the keys with `docker compose logs api`. Free-tier limits apply to all users together.
