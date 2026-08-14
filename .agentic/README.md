# Agent engineering plane

This directory contains the context and governance owned by Conclave. Generated
indexes and runtime evidence are intentionally ignored by Git and must never be
treated as manually maintained semantic truth.

Architecture conformance combines:

- portable rules, schemas, documentation, and validation code supplied by the
  pinned `agentic-architecture-kit` distribution;
- Conclave's declared architecture in
  `policies/architecture/project-policy.json`;
- explicit exceptions in `policies/architecture/waivers.json`;
- semantic review acknowledgements and review authority declarations in
  `policies/architecture/{reviews,authorities}.json`.

`toolchain.json` pins the portable catalog to version `0.4.1`. Conclave does not
vendor a mutable copy of that catalog or its schemas. Run
`./tools/scripts/validate-architecture.sh` before declaring work complete. When
a rule reports a finding, use the same pinned `aak explain <RULE_ID>` command to
load its normative reference and repository-specific state.

The remaining policies, workflows, templates, repository contract, and evidence
schema are specific to Conclave and evolve with the project.
