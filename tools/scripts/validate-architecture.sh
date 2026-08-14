#!/bin/sh
set -eu

root=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)

if ! command -v uvx >/dev/null 2>&1; then
  echo "Architecture validation requires uvx. Install uv from https://docs.astral.sh/uv/." >&2
  exit 127
fi

exec uvx --from agentic-architecture-kit==0.4.1 \
  aak validate --root "$root" "$@"
