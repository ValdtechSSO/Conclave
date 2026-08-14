# Architecture governance

Conclave consumes the published Agentic Architecture Kit instead of maintaining
a repository-local fork of its portable validator, schemas, rule catalog, and
normative documentation.

## Ownership split

The pinned kit version supplies the rules that apply across projects. Conclave
owns only the declarations and decisions that depend on this repository:

- `.agentic/toolchain.json` pins the distribution and catalog version.
- `.agentic/policies/architecture/project-policy.json` declares the observed and
  intended module, host, project, feature, and dependency boundaries.
- `.agentic/policies/architecture/waivers.json` records narrowly scoped,
  digest-bound exceptions.
- `.agentic/policies/architecture/reviews.json` records digest-bound semantic
  reviews.
- `.agentic/policies/architecture/authorities.json` declares who may approve
  governed changes and which GitHub protections must enforce that authority.
- `.github/CODEOWNERS` maps the declared protected scopes to that authority.

The module contract at `src/Modules/Planning/module.contract.yml` remains local
because it describes Conclave's functional vocabulary, ownership, risk, and
invariants. The evidence schema also remains local because it is a Conclave
product contract, not an architecture-kit contract.

## Agent workflow

Before the first write, load the complete preventive decision core:

```bash
uvx --from agentic-architecture-kit==0.4.1 aak core
```

Use the local wrapper for conformance checks:

```bash
./tools/scripts/validate-architecture.sh
```

When a finding reports a rule ID, retrieve the rule's normative reference,
current state, scope, and applicable waiver or review:

```bash
uvx --from agentic-architecture-kit==0.4.1 aak explain DEP001
```

Before declaring a task complete, run the architecture check again together
with the repository's build and test commands. An unresolved normative
reference is a failure, not permission to infer the rule from memory.

`REVIEW_REQUIRED` is resolved only by an accountable authority. Conclave uses
the kit's `solo-maintainer` mode because it has exactly one human maintainer.
This preserves pull requests, required checks, and no direct pushes without
pretending that approving one's own pull request is independent review.

First commit the candidate state and open its pull request. From that clean,
reachable candidate revision, create the review template:

```bash
./tools/scripts/validate-architecture.sh \
  --write-review-template /tmp/conclave-architecture-reviews.json
```

The maintainer then posts a durable GitHub issue or discussion comment containing
the rule, scope, digest, fingerprint, reviewed SHA, and decision. Copy only the
accepted records into `reviews.json` and use
`github-maintainer-attestation:<github-comment-url>` as approval evidence. The
revision must be reachable and the digest and fingerprint must remain unchanged.
The agent may prepare the text but must not post or invent the maintainer's
attestation. A commit, ADR, or agent-authored declaration is not a substitute.

## Updating the kit

Upgrade the version in `.agentic/toolchain.json`, the validation wrapper,
GitHub workflow, and agent commands as one reviewed change. Then run strict
validation. Rule digests deliberately invalidate stale waivers and reviews when
the semantics they authorized have changed.

Do not copy portable package assets back into this repository. Use
`aak export-offline` only when a deliberately vendored, air-gapped distribution
is required and record that exception as an architecture decision.

GitHub must protect `main` with pull requests, required status checks, and no
direct pushes, as declared in `authorities.json`. It must not require an
impossible independent CODEOWNER approval from the sole maintainer.
