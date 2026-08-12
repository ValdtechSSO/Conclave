# ADR-001: Project boundaries with vertical feature slices

Status: accepted.

Conclave keeps the project boundaries required by its V1 plan. Application orchestration is organized by use case under `Features/Plan`; CLI dispatch is organized under `Features`. Shared abstractions are promoted only when multiple slices consume them.

