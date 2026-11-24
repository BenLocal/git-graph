# Flows

> End-to-end feature flows (roles, steps, state transitions) and recurring team workflows (stored in `docs/flows/`); check before changing multi-step logic, state transitions, or taking over a recurring workflow.

`docs/flows/` holds two kinds of flows, one file per flow:
- **Feature / business flows**: how an operation runs end-to-end with code pointers at each step. Link to `business-rules.md` for rule definitions and `adr.md` for technical trade-offs.
- **Recurring workflows**: repeatable multi-step tasks so agents follow consistent steps and output formats.

## When to write one

- A flow spans multiple components or layers and is hard to understand from a single file.
- State transitions and rollback/cancellation paths are non-obvious.
- A recurring maintenance or verification workflow happens more than once.
- You trace a complex flow end-to-end for the first time and want to save the next session from redoing it.

## Files and format

- Location: `docs/flows/<flow-name>.md`. The first two lines must be `# Title` and `> One-line summary` for indexing.
- Include `## Steps` (linking to code entry points), plus `## State transitions`, `## Edge cases`, and `## Regression checks` as needed. Keep recorded flows updated in the same change when code modifies the flow, and update the regression table in `testing.md`.

## Flows index (generated)

Rebuilt by `scripts/reindex.py` from `docs/flows/`; do not edit between the markers.

<!-- FLOWS:START -->
- (no business flows yet; add them under docs/flows/ as described above)
<!-- FLOWS:END -->
