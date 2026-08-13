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

if rg -n '^(paths|entrypoints|handlers|classes|tests|entities_read|entities_written|routes):' src/Modules --glob 'module.contract.yml'; then
  echo "module contract contains generated structural fields" >&2
  exit 1
fi

found_module=false
for module in src/Modules/*; do
  test -d "$module" || continue
  found_module=true
  test -f "$module/AGENTS.md"
  test -f "$module/module.contract.yml"
  expected_id=$(basename "$module" | tr '[:upper:]' '[:lower:]')
  actual_id=$(sed -n 's/^id:[[:space:]]*//p' "$module/module.contract.yml" | head -n 1)
  if test "$actual_id" != "$expected_id"; then
    echo "module id '$actual_id' does not match directory '$expected_id'" >&2
    exit 1
  fi
done
test "$found_module" = true

test -f src/Modules/Planning/Features/CreatePlan/PlanOrchestrator.cs
test -f src/Modules/Planning/Features/ShowRun/ShowRunService.cs
test -f src/Modules/Planning/Features/DiagnoseEnvironment/DoctorService.cs
test -f src/Modules/Planning/Features/PruneRuns/PruneRunsService.cs
test -f domain/contexts/planning.md
test -f architecture/decisions/ADR-001-compact-modular-architecture.md

if rg -n 'ProjectReference.*(Infrastructure|Hosts)' src/Modules/Planning/Conclave.Planning.csproj; then
  echo "Conclave.Planning must not reference infrastructure or hosts" >&2
  exit 1
fi

if rg -n 'ProjectReference.*Hosts' src/Modules; then
  echo "modules must not reference hosts" >&2
  exit 1
fi

project_count=$(find src -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*' | wc -l | tr -d '[:space:]')
if test "$project_count" != 3; then
  echo "expected the three justified product assemblies, found $project_count" >&2
  exit 1
fi

echo "architecture validation passed"
