# Coding guidelines (Karpathy's guardrails, expanded)

> Actionable rules for writing, changing and reviewing code; AGENTS.md keeps only the four headline rules.

These apply whenever code is written, changed or reviewed; trivial edits can relax them. They expand the workspace rules in AGENTS.md.

1. **Think before writing (don't guess, don't hide confusion)**
   - State the key assumptions before starting. If unsure, stop and ask instead of guessing silently.
   - When a request has several reasonable readings, lay out the options and let the user choose; don't pick one silently.
   - Before asking the user, separate verifiable facts from choices: run commands or inspect code yourself for behavior, timings, outputs and formats, and ask the user only about product choices and personal preferences.
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

## Prove it works before reporting done
- Verify with real artifacts: run the feature, read actual values, inspect the diff. File timestamps, subagent self-reports, "it compiles", or cached screenshots are indirect signals, not proof.
- Match the verification to the change: CLI changes run real commands; UI changes exercise the changed flow in the running app (or snapshots where visual layout is the point); parsers or migrations replay saved real inputs; performance changes compare before/after profiles; storage changes read back written values.
- Verify only in local environments or user-designated environments; never reproduce, drive, stress-test, or inspect data on production.
- When verification fails, suspect the observation method first (wrong process, stale cache, old logs) before suspecting the system under test.
- Prefer scriptable checks that leave reproducible output over manual assertions.
- When reporting completion, state the evidence level reached for key claims (explicitly mark anything below level 4 as "untested"):
  1. Inferred only;
  2. Pointed to specific `file:line`;
  3. Reasoned through failure cases and confirmed unreachable;
  4. Verified by running a script or test against real code;
  5. Reproduced and verified in the running application.

## Only commit changes backed by evidence when fixing bugs
- "Might help" defensive code is an assumption, not a fix—do not commit it. Commit only the minimal change proven necessary by evidence.
- If evidence refutes a hypothesis, revert changes added for that hypothesis rather than keeping them "just in case".
- When a cheap local test path exists, write the failing reproduction test first, then fix it so the test turns green.
- If two fixes based on the same premise fail the same check, question the premise: state it in one sentence, run an observation where the outcome differs if the premise is false, and verify before attempting a third fix.

## Test behavior, not implementation
- Validate each test with two hypothetical mutations: breaking the behavior (wrong return value, missing or duplicate side effect) must fail the test; changing internal implementation while keeping observable behavior identical must keep it passing.
- Derive expected values from requirements rather than by running the code under test; assert only what the contract promises.

## Make scripts and commands safe to rerun
- Before writing state-changing scripts, commands, or loops, answer three questions: What happens if run twice in a row? What happens if it crashes mid-way and is rerun? Does it converge to the same end state?
- Derive each step from current state: read current state, compare with target, apply only the diff. Ensure locks, temp files, and partial records are recognizable as stale (for example, write to a temp path then atomically rename).

## Pin behavior before refactoring
- Before restructuring, pin existing behavior with characterization tests, snapshots, or before/after output comparisons; fill coverage gaps first. Type checks and lint alone do not pin behavior.
- Keep behavior identical at each step and compare before/after outputs on real artifacts.
- After renaming, search specifically for old names in strings, docs, comments, configs, and scripts.

## Handling review feedback
Filter review comments (from humans or other models) before acting, and state a disposition for each: fix, reject with reason, or ask the user to decide.
- For "what if null is passed here" hypotheses, trace callers to confirm it can actually happen; reject if upstream validation or types already prevent it.
- "Another style would be nicer" is not an issue unless it identifies a concrete failure mode in the current code.
- Before adding an abstraction or helper, ask whether a second variant will actually appear; if not, keep it inline.
- Keep patterns consistent with the rest of the repository when a reviewer only saw the diff.
- Conversely, address items raised independently by multiple reviewers, backed by a concrete execution path, or involving correctness/safety—do not reject them just because the fix is involved.

> "Prove it works before reporting done" and the following sections draw on [pstack](https://github.com/cursor/plugins/tree/main/pstack) (`prove-it-works`, `blast-radius`, `attack-the-premise`, `test-behavior-not-implementation`, `make-operations-idempotent`, bug-fix & refactoring playbooks, `interrogate`; MIT).

## How changes land (follow the existing implementation)
- Before changing anything, find the closest similar code in the same area and extend it with the same layering and naming.
- Keep existing behavior and contracts unless the user asks to change them.
- Go through existing boundaries (`GitRepository`, `IAppHost`, `IDialogService`) instead of coupling around them.
- No large refactors, bulk renames or architecture migrations unless the user asks.
