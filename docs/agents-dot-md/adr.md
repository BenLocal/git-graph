# Architecture Decision Records (ADR)

> Records of technical and architectural trade-offs (stored in `docs/adr/`); check before overturning or bypassing an existing design, and add one after making a hard-to-reverse trade-off.

ADRs record *why* a decision was made so future contributors (and agents) do not have to re-derive it or "fix" an intentional design as a bug. `architecture.md` describes what the system looks like today; ADRs describe why it looks that way. Explain each decision in its ADR and link to it from `architecture.md`. Product rules belong in `business-rules.md`, and multi-step processes belong in `flows.md`; ADRs cover technical and architectural trade-offs only.

## When to write one

Write an ADR only when all three hold:

1. **Hard to reverse**: changing direction later carries significant cost (data migration, interface contracts, cross-component rework).
2. **Surprising without context**: a newcomer reading the code would ask "why not use the more common approach?".
3. **Real trade-off**: viable alternatives existed, and this choice was made for concrete reasons.

## Files and numbering

- Location: `docs/adr/NNNN-<kebab-case-title>.md` (e.g., `docs/adr/0001-cli-git-over-libgit2.md`). Create `docs/adr/` when the first ADR is added.
- Numbering: highest existing number in `docs/adr/` plus one, zero-padded to 4 digits; numbers are never reused, even for deprecated ADRs.
- The first two lines must be `# NNNN Title` and `> Status: ...`, which the index generator reads.

## Format

```md
# 0001 Example decision title

> Status: Accepted (2026-11-24)

<1-3 sentences: context, what was decided, and why.>
```

Add optional sections only when genuinely useful:
- **Alternatives considered**: when a rejected option is likely to be proposed again.
- **Consequences**: non-obvious downstream effects or maintenance obligations.
- **Signals to revisit**: conditions under which the decision should be reconsidered.

## Status and superseding

Allowed statuses: `Proposed` / `Accepted` / `Deprecated` / `Superseded by NNNN`. To overturn a decision, write a new ADR explaining what it supersedes and why; update only the status line of the old ADR to `Superseded by NNNN` without altering its body.

If a task conflicts with an accepted ADR, stop and point out the conflict to the user before proceeding.

## ADR index (generated)

Rebuilt by `scripts/reindex.py` from `docs/adr/`; do not edit between the markers.

<!-- ADR:START -->
- (no ADRs yet; add them under docs/adr/ as described above)
<!-- ADR:END -->
