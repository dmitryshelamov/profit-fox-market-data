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

The owner, on soyo-02: Docker Engine with Compose from the official Docker repository, as on soyo-01, `soyo` in the `docker`
group, and `jq` for `deploy.sh`:

```bash
ssh -t 192.168.8.213 'curl -fsSL https://get.docker.com | sudo sh && sudo usermod -aG docker soyo && sudo apt-get install -y jq'
```

The owner, on soyo-03: a runner of this repository next to the one of profit-fox, in a folder of its own. Settings → Actions →
Runners → New self-hosted runner (Linux) of this repository gives the download commands and a token; run them in
`~/actions-runner-market-data` instead of `~/actions-runner`, then:

```bash
./config.sh --url https://github.com/dmitryshelamov/profit-fox-market-data --token <token> --labels soyo-03 --unattended
```

```bash
sudo ./svc.sh install soyo && sudo ./svc.sh start
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

On soyo-02, the script and the password of the database, generated on the server and never printed:

```bash
ssh 192.168.8.213 'mkdir -p ~/profit-fox-market-data && curl -fsSL https://raw.githubusercontent.com/dmitryshelamov/profit-fox-market-data/main/deploy/deploy.sh -o ~/profit-fox-market-data/deploy.sh && chmod 700 ~/profit-fox-market-data/deploy.sh'
```

```bash
ssh 192.168.8.213 'umask 077 && test ! -e ~/profit-fox-market-data/.env.prod && printf "MARKETDATA_DB_PASSWORD=%s\n" "$(openssl rand -hex 32)" > ~/profit-fox-market-data/.env.prod'
```

The deploy key in `authorized_keys` of soyo-02, allowed only from soyo-03 and only as `deploy.sh`:

```bash
ssh soyo@192.168.8.142 'cat ~/.ssh/profit-fox-market-data-deploy.pub' | sed 's|^|from="192.168.8.142",command="/home/soyo/profit-fox-market-data/deploy.sh",no-pty,no-port-forwarding,no-agent-forwarding,no-X11-forwarding,no-user-rc |' | ssh 192.168.8.213 'cat >> ~/.ssh/authorized_keys'
```

The owner, the token of T-Invest, typed into the server and never shown:

```bash
ssh -t 192.168.8.213 'umask 077 && read -rsp "T-Invest token: " token && echo && printf "MARKETDATA_TINVEST_TOKEN=%s\n" "$token" >> ~/profit-fox-market-data/.env.prod'
```
