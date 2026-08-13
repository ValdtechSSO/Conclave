# Feature workflow

1. Locate the owning module and cohesive feature area, then read its `AGENTS.md`
   and contract.
2. State affected invariants and risk.
3. Implement within `Features/{FeatureArea}`. Keep related operations together;
   promote a concept only when current consumers and a clearer ownership boundary
   justify it.
4. Run targeted tests, then full build/tests and architecture checks.
5. Record negative evidence and unresolved risk as well as passing checks.
