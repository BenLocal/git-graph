# Coding guidelines (Karpathy's guardrails, expanded)

> Actionable rules for writing, changing and reviewing code; AGENTS.md keeps only the four headline rules.

These apply whenever code is written, changed or reviewed; trivial edits can relax them. They expand the workspace rules in AGENTS.md.

1. **Think before writing (don't guess, don't hide confusion)**
   - State the key assumptions before starting. If unsure, stop and ask instead of guessing silently.
   - When a request has several reasonable readings, lay out the options and let the user choose; don't pick one silently.
   - If there is a simpler approach, say so. Push back when it's warranted.
2. **Simplicity first (just enough)**
   - Write only the code the current problem needs. No unrequested features, abstractions, settings or defenses against impossible cases.
   - Don't abstract logic that is used in one place. If 200 lines could be 50, rewrite it.
3. **Surgical changes (touch only what must change)**
   - When editing existing code, don't "improve" neighbouring code, comments or formatting. Don't refactor what isn't broken. Match the existing style even if you'd prefer another.
   - Clean up only the orphans your own change created (unused usings, variables, methods). Point out pre-existing dead code but don't delete it unless asked.
   - Check: every changed line traces back to the user's request.
4. **Goal-driven (define success, loop until it's verified)**
   - Turn tasks into verifiable goals: "add validation" → "write a test for invalid input, then make it pass"; "fix the bug" → "write a test that reproduces it, then fix".
   - For multi-step work, give a short plan with a check for each step (step → check: what to verify).

## How changes land (follow the existing implementation)
- Before changing anything, find the closest similar code in the same area and extend it with the same layering and naming.
- Keep existing behavior and contracts unless the user asks to change them.
- Go through existing boundaries (`GitRepository`, `IAppHost`, `IDialogService`) instead of coupling around them.
- No large refactors, bulk renames or architecture migrations unless the user asks.
