#!/bin/sh
set -eu

root=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
cd "$root"

for name in Services Managers Helpers Utils Common; do
  if find src -type d -name "$name" -print -quit | grep -q .; then
    echo "forbidden catch-all directory found: $name" >&2
    exit 1
  fi
done

if rg -n '^(paths|entrypoints|handlers|classes|tests|entities_read|entities_written|routes):' src --glob 'module.contract.yml'; then
  echo "module contract contains generated structural fields" >&2
  exit 1
fi

test -f src/Conclave.Orchestration/Features/Plan/PlanOrchestrator.cs
test -f src/Conclave.Orchestration/module.contract.yml
test -f domain/contexts/planning.md
test -f architecture/decisions/ADR-001-project-and-slice-architecture.md

if rg -n 'ProjectReference.*Conclave\.(Infrastructure|Repository|Providers|Validation|Orchestration|Cli)' src/Conclave.Core; then
  echo "Conclave.Core must not reference outward projects" >&2
  exit 1
fi

echo "architecture validation passed"

