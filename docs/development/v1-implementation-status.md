# Conclave V1 implementation status

All six V1 slices from the implementation plan are represented in the executable pipeline.

| Slice | Implemented outcome |
|---|---|
| Walking skeleton | CLI → retained snapshot → isolated workspace → provider → JSON → validated plan → deterministic Markdown → cleanup/integrity assertion |
| Evidence and targets | Claim typing, file/symbol resolution at `SnapshotSha`, linkage/completeness checks, safe future `create` targets |
| Multi-provider proposals | Per-stage models, concurrent independent calls, randomized opaque aliases, quorum, usage aggregation |
| Cross-review | Workspace reset, ephemeral inputs, foreign proposals only, shuffled presentation, anonymous validated reviews |
| Synthesis | Configurable participant fallback, shuffled inputs, final schema/evidence/completeness gate, disagreement preservation |
| Reliability and operability | Retries, classified failures, timeouts/process-tree kill, budgets, `doctor`, `show`, `prune`, retention, stable exit codes and shared-Git/original-state guards |

The CLI commands implemented in V1 are `plan`, `show`, `doctor`, and `prune`. V2 review and V3 MCP remain deliberately outside V1 as required by the source plan.

Validation commands:

```bash
dotnet build Conclave.sln --configuration Release --no-restore
dotnet test Conclave.sln --no-build --no-restore
./tools/scripts/validate-architecture.sh
```
