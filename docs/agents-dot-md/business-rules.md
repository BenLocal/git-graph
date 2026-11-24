# Business Rules

> Intentional product rules and domain semantics (stored in `docs/business-rules/`); check before changing product behavior or treating surprising logic as a bug.

This module records intentional product behavior—things that might look like bugs in isolation but are required by the product. Keeping them here prevents future contributors from "fixing" intended behavior.

Split with neighboring docs:
- Technical and architectural trade-offs belong in ADRs (`adr.md`).
- Multi-step workflows, roles, and state transitions belong in `flows.md`; link to rules here rather than redefining them.
- This module answers only: "how should this rule or condition behave in this product?".

## When to record

- A product rule is non-obvious from code or counter-intuitive.
- An investigation concludes "the code is right; this is the intended product behavior".
- The same product behavior question is asked a second time.

Do not record rules that are still under discussion; record them once settled.

## Files and entries

- Location: `docs/business-rules/<domain>.md`, grouped by domain. The first two lines must be `# Title` and `> One-line summary` for indexing.
- One subsection per rule:

```md
## Rule name

- Rule: <what the product requires>
- Source: <decision maker / issue or PR reference>, <YYYY-MM-DD>
- Code location: `<file or symbol>`
- Watch out for: <common ways this could be accidentally broken>
```

When a rule changes, update its subsection, source, and date in place (do not keep stale rules). Follow the redaction rules in `AGENTS.md` under Memory.

## When using

- If a change alters a recorded rule, confirm with the user first, then update both code and the rule doc together.
- If code and a recorded rule disagree, report the discrepancy and let the user decide which one is outdated.

## Business rules index (generated)

Rebuilt by `scripts/reindex.py` from `docs/business-rules/`; do not edit between the markers.

<!-- BUSINESS_RULES:START -->
- (no business rules yet; add them under docs/business-rules/ as described above)
<!-- BUSINESS_RULES:END -->
