# ADR-002: Framework-only runtime

Status: accepted.

V1 uses the .NET runtime libraries for JSON, processes, configuration parsing, and persistence. This keeps the global CLI deployable without a runtime package graph. Test-only packages are permitted.

