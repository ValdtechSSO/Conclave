#!/bin/sh
set -eu

if [ "$#" -lt 3 ] || [ "$#" -gt 4 ]; then
  echo "usage: $0 <run-id> <scope> <prompt-file> [repository]" >&2
  exit 2
fi

run_id=$1
scope=$2
prompt_file=$3
repository=${4:-$PWD}
test_providers="codex,deepseek"
test_models="codex=gpt-5.6-terra,deepseek=deepseek-v4-flash"

exec conclave plan \
  --id "$run_id" \
  --directory "$repository" \
  --prompt-file "$prompt_file" \
  --scope "$scope" \
  --providers "$test_providers" \
  --models "$test_models" \
  --snapshot working-tree \
  --max-cost-usd 0.25 \
  --progress-format jsonl \
  --json
