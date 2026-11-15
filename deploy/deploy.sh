#!/usr/bin/env bash
# Deploys version N of profit-fox-market-data on soyo-02; deploying an earlier N rolls back. A copy of deploy/deploy.sh of
# profit-fox for the one image of this service.
# The deploy key in ~/.ssh/authorized_keys runs only this script (a forced command), so the request comes in
# SSH_ORIGINAL_COMMAND as "deploy <N> <protocol>". The script trusts nothing else the caller sends: it takes the commit
# of tag vN from GitHub, accepts it only from main and runs only an image built from that commit.
# Installed by hand as ~/profit-fox-market-data/deploy.sh (README, "Production"); it needs curl, jq and docker.

# Raised when the command changes; deploy.yml passes the same number, so an outdated copy on the server refuses.
DEPLOY_PROTOCOL=1
REPOSITORY=dmitryshelamov/profit-fox-market-data
ROOT="$HOME/profit-fox-market-data"

parse_command() {
  local pattern='^deploy ([1-9][0-9]*) ([1-9][0-9]*)$'
  if ! [[ "$1" =~ $pattern ]]; then
    echo "deploy: expected 'deploy <version> <protocol>'" >&2
    return 1
  fi
  if [[ "${BASH_REMATCH[2]}" != "$DEPLOY_PROTOCOL" ]]; then
    echo "deploy: the workflow speaks protocol ${BASH_REMATCH[2]}, this script $DEPLOY_PROTOCOL: copy deploy/deploy.sh of that commit to the server" >&2
    return 1
  fi
  echo "${BASH_REMATCH[1]}"
}

fail() {
  echo "deploy: $*" >&2
  exit 1
}

github() {
  curl --fail --silent --show-error --max-time 30 -H 'Accept: application/vnd.github+json' \
    "https://api.github.com/repos/$REPOSITORY/$1"
}

# The commit of tag vN; a missing tag, or one that names anything but a commit, is refused.
tag_commit() {
  local ref
  ref="$(github "git/ref/tags/v$1")" || fail "no tag v$1"
  jq --exit-status --raw-output 'select(.object.type == "commit") | .object.sha' <<<"$ref" ||
    fail "tag v$1 does not name a commit"
}

# main contains the commit when main is the commit itself or comes after it.
in_main() {
  local status
  status="$(github "compare/main...$1" | jq --raw-output '.status')" || return 1
  [[ "$status" == "behind" || "$status" == "identical" ]]
}

# /health answers once the migrations are done and while the database answers.
wait_for_health() {
  local deadline=$((SECONDS + 120))
  until curl --fail --silent --output /dev/null --max-time 5 http://127.0.0.1:5236/health; do
    ((SECONDS < deadline)) || return 1
    sleep 2
  done
}

main() {
  set -euo pipefail

  local version commit release image revision
  version="$(parse_command "${SSH_ORIGINAL_COMMAND:-}")" || exit 1

  # Nothing on the server changes until the version is known to be a commit of main.
  commit="$(tag_commit "$version")"
  in_main "$commit" || fail "the commit $commit of tag v$version is not in main"
  echo "deploy: version $version is commit $commit"

  release="$ROOT/releases/$version"
  mkdir -p "$release"
  curl --fail --silent --show-error --max-time 30 --output "$release/docker-compose.prod.yml" \
    "https://raw.githubusercontent.com/$REPOSITORY/$commit/docker-compose.prod.yml"

  compose() {
    MARKETDATA_VERSION="$version" docker compose --file "$release/docker-compose.prod.yml" \
      --env-file "$ROOT/.env.prod" "$@"
  }

  compose pull --quiet
  # An image pushed under an existing number from another commit does not run.
  image="ghcr.io/$REPOSITORY:$version"
  revision="$(docker image inspect --format '{{ index .Config.Labels "org.opencontainers.image.revision" }}' "$image")"
  [[ "$revision" == "$commit" ]] || fail "$image is built from '$revision', not from $commit"

  compose up --detach --wait db
  compose up --detach --no-deps market-data
  wait_for_health || fail "version $version did not answer /health in 120 s"

  echo "$version" >"$ROOT/current"
  # GHCR keeps every version, so an image no container uses can be pulled again.
  docker image prune --all --force --filter "until=168h"
  echo "deploy: version $version is running"
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
  main
fi
