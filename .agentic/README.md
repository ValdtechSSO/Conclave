# Agent engineering plane

This directory holds deterministic engineering policy and reusable workflow
guidance. Generated indexes and runtime evidence are intentionally ignored by
Git and must never be treated as manually maintained semantic truth.

Architecture conformance has three separate inputs:

- portable rules: `tools/architecture/rules.json`;
- project policy: `policies/architecture/project-policy.json`;
- explicit waivers: `policies/architecture/waivers.json`.

Their contracts live under `contracts/schemas/`. Run the supplied implementation
with `./tools/scripts/validate-architecture.sh`; do not replace portable rule
semantics with project-specific shell checks.
