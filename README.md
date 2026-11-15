# profit-fox-market-data

## Production

Production runs on soyo-02 (`192.168.8.213`) in the local network only. It is the `profit-fox-market-data` compose project
of `docker-compose.prod.yml`:

| Service | What it does |
|---|---|
| `db` | TimescaleDB on the volume `profit-fox-market-data-db`; no port leaves the server |
| `market-data` | the service; migrates the database on start; REST on `http://192.168.8.213:5236`, gRPC on `192.168.8.213:5237`, both without authorization |

The deploy is the pipeline of profit-fox (its README, "Production"). Nothing is built on soyo-02 or anywhere in the LAN: from
Russian networks some nuget.org addresses fail at TLS. A GitHub-hosted runner builds the image, soyo-02 pulls it from the
public GHCR package `ghcr.io/dmitryshelamov/profit-fox-market-data`, and a self-hosted runner of this repository on soyo-03
(`192.168.8.142`, label `soyo-03`) only starts the deploy on soyo-02 over SSH.

### Versions

A version is a number `N`: tag `vN` on a commit of `main`, and the image `:N` built from that commit (label
`org.opencontainers.image.revision`). A rebuild of a commit keeps its number. Branches `release/x.y.z` are only names of pull
requests.

### Deploy and roll back

1. A merge into `main` runs CI, then `.github/workflows/deploy.yml`. Once CI passes, the `build` job tags the commit
   `v<max+1>` and pushes the image `:N`.
2. The `image` job checks that `:N` is in GHCR, and the `deploy` job on soyo-03 waits for the owner's approval in the
   `production` environment: Actions → the Deploy run → Review deployments → Approve.
3. The job runs `deploy N` on soyo-02 over SSH. `~/profit-fox-market-data/deploy.sh` checks on GitHub that tag `vN` is on a
   commit of `main`, downloads `docker-compose.prod.yml` of that commit to `~/profit-fox-market-data/releases/N/`, pulls the
   image and checks it is built from that commit. Then it starts the database, replaces the service and waits up to 120 s for
   `/health`. The running version is in `~/profit-fox-market-data/current`.

To roll back, or to deploy a version again: Actions → Deploy → Run workflow on `main` with the version number, then approve.
Such a run builds nothing; the image `:N` must be in GHCR. A rollback keeps the data, but the database keeps the migrations of
the newer version: roll back only to a version that works with them.

On the server, `docker compose -p profit-fox-market-data ps` lists the services and
`docker compose -p profit-fox-market-data logs market-data` shows the logs of the service. `/health` answers `Healthy` while
the database answers, and 503 otherwise.

### Settings without a restart

`~/profit-fox-market-data/config/market-data.json` on soyo-02, if it exists, overrides any setting of `appsettings.json` and of the
environment of `docker-compose.prod.yml`, and the service reads it again on each change. The folder is mounted into the container
read-only as `/app/config`. When a change takes effect:

| Settings | Take effect |
|---|---|
| `BybitApi` (`RequestsPerSecond`, `LimitPauseSeconds`, `BanPauseMinutes`), `TInvestApi:MarketDataRequestsPerMinute` | at once: the next request, the next pause |
| the sections of the loadings and streams (`CandleSync`, `OpenInterestSync`, `FundingPaymentSync`, `LongShortRatioSync`, `BuyVolumeSync`, `InstrumentSync`, `CandleStream`, `LiquidationStream`, `TickerCollection`, `FullHistory`) | from the next round of each |
| `Kestrel`, `ConnectionStrings`, `Metrics`, `Serilog` | only after a restart |

```bash
ssh 192.168.8.213 'nano ~/profit-fox-market-data/config/market-data.json'
```

```json
{ "BybitApi": { "LimitPauseSeconds": 5, "RequestsPerSecond": 8 } }
```

A file that is not valid JSON, or no file, leaves the service on the settings of its image, at start and on a change alike; the log
says so. A value that breaks a section, such as `"RequestsPerSecond": 0`, is logged once, and the section keeps its last valid
settings until it is fixed; the same value in the file at a start stops the service, as a broken image would. The folder is made
once as `soyo` (README, "Setting up the machines"), or Docker makes it owned by root.

### Secrets

The secrets live only in `~/profit-fox-market-data/.env.prod` on soyo-02 (mode `600`), never in git, the image or GitHub:

```text
MARKETDATA_DB_PASSWORD=<64 hex characters>
MARKETDATA_TINVEST_TOKEN=<a read-only token of T-Invest>
```

The token is one of this service only: T-Invest counts its limits per token, so a token shared with profit-fox would share
them too.

### Metrics

The service pushes the metrics of its calls to Bybit and T-Invest over OTLP to the Prometheus of profit-fox on soyo-03
(`Metrics__OtlpEndpoint` in `docker-compose.prod.yml`), under the job `profit-fox-market-data`. Their names are those of
profit-fox, so its dashboard "Bybit REST" counts this service in the load on the IP.

The dashboard "Market data" is not provisioned: it lives in the Grafana of soyo-03 (`http://192.168.8.142:3000`) and is edited
there. `deploy/grafana/market-data.json` is its copy, to import it into a new Grafana: Dashboards → New → Import → Upload
dashboard JSON file. After a change in Grafana worth keeping, export it (Share → Export → Save to file) over that file.

### A new deploy.sh

The deploy key can run only `~/profit-fox-market-data/deploy.sh`, so a change to `deploy/deploy.sh` reaches the server by
hand, after its PR is merged and before its version is deployed. A change of the command raises `DEPLOY_PROTOCOL` in the
script and in `deploy.yml`, and the old copy refuses the new workflow until it is replaced:

```bash
ssh 192.168.8.213 'curl -fsSL https://raw.githubusercontent.com/dmitryshelamov/profit-fox-market-data/main/deploy/deploy.sh -o ~/profit-fox-market-data/deploy.sh && chmod 700 ~/profit-fox-market-data/deploy.sh'
```

### Setting up the machines

Done once. Steps with `sudo` or in the GitHub settings are the owner's.

The owner, on soyo-02: `curl` and `jq` for `deploy.sh`, Docker Engine with Compose from the official Docker repository and `soyo`
in the `docker` group, with the log rotation and `live-restore` of soyo-01:

```bash
ssh -t 192.168.8.213 'sudo apt-get update -q && sudo apt-get install -y -q curl ca-certificates jq && curl -fsSL https://get.docker.com | sudo sh && sudo usermod -aG docker soyo && echo "{\"log-driver\":\"json-file\",\"log-opts\":{\"max-size\":\"10m\",\"max-file\":\"3\"},\"live-restore\":true}" | sudo tee /etc/docker/daemon.json && sudo systemctl restart docker'
```

On soyo-03: a runner of this repository, `soyo-03-profit-fox-market-data` with the label `soyo-03`, next to the one of
profit-fox (`~/actions-runner-profit-fox`) in `~/actions-runner-profit-fox-market-data`: the runner of the version of the other,
downloaded from `github.com/actions/runner/releases`, and registered with a token of this repository, which `gh` gives:

```bash
gh api -X POST repos/dmitryshelamov/profit-fox-market-data/actions/runners/registration-token --jq .token | ssh 192.168.8.142 'read -r token; cd ~/actions-runner-profit-fox-market-data && ./config.sh --url https://github.com/dmitryshelamov/profit-fox-market-data --token "$token" --name soyo-03-profit-fox-market-data --labels soyo-03 --work _work --unattended'
```

It runs as the user service `actions-runner-profit-fox-market-data.service` of `soyo` (`~/.config/systemd/user`, `ExecStart`
its `run.sh`, `Restart=always`), which linger keeps running without a login, so it needs no `sudo`:

```bash
ssh 192.168.8.142 'systemctl --user status actions-runner-profit-fox-market-data.service'
```

The owner, in GitHub:

- Settings → Environments → New environment `production`: required reviewer — the owner; deployment branches — selected
  branches, `main`.
- Settings → Actions → General → Approval for running fork pull request workflows from contributors: "Require approval for
  all external contributors".
- Settings → Rules → Rulesets → New tag ruleset for `v*`: restrict updates and deletions, block force pushes. Creation stays
  allowed: the `build` job creates the tags.
- After the first build: the package `profit-fox-market-data` → Package settings → Change visibility → Public. Until then
  soyo-02 cannot pull the image and the deploy fails at the pull, before anything changes.

The agent, with the owner's approval. On soyo-03, the deploy key and the host key of soyo-02:

```bash
ssh soyo@192.168.8.142 'ssh-keygen -t ed25519 -N "" -C profit-fox-market-data-deploy -f ~/.ssh/profit-fox-market-data-deploy && ssh-keyscan -t ed25519 192.168.8.213 >> ~/.ssh/known_hosts'
```

On soyo-02, the script and the password of the database, generated on the server and never printed (before the first merge into
`main`, copy `deploy/deploy.sh` with `scp` instead):

```bash
ssh 192.168.8.213 'mkdir -p ~/profit-fox-market-data && curl -fsSL https://raw.githubusercontent.com/dmitryshelamov/profit-fox-market-data/main/deploy/deploy.sh -o ~/profit-fox-market-data/deploy.sh && chmod 700 ~/profit-fox-market-data/deploy.sh'
```

```bash
ssh 192.168.8.213 'umask 077 && test ! -e ~/profit-fox-market-data/.env.prod && printf "MARKETDATA_DB_PASSWORD=%s\n" "$(openssl rand -hex 32)" > ~/profit-fox-market-data/.env.prod'
```

The folder of the settings file, before the first deploy that mounts it:

```bash
ssh 192.168.8.213 'mkdir -p ~/profit-fox-market-data/config'
```

The deploy key in `authorized_keys` of soyo-02, allowed only from soyo-03 and only as `deploy.sh`:

```bash
ssh soyo@192.168.8.142 'cat ~/.ssh/profit-fox-market-data-deploy.pub' | sed 's|^|from="192.168.8.142",command="/home/soyo/profit-fox-market-data/deploy.sh",no-pty,no-port-forwarding,no-agent-forwarding,no-X11-forwarding,no-user-rc |' | ssh 192.168.8.213 'cat >> ~/.ssh/authorized_keys'
```

The owner, the token of T-Invest, typed into the server and never shown:

```bash
ssh -t 192.168.8.213 'umask 077 && read -rsp "T-Invest token: " token && echo && printf "MARKETDATA_TINVEST_TOKEN=%s\n" "$token" >> ~/profit-fox-market-data/.env.prod'
```
