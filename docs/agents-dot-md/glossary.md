# Glossary Conventions

> Domain terms and their exact names in code (stored in `GLOSSARY.md` at the repository root); check when encountering ambiguous terms or naming new concepts.

The glossary keeps humans, product specs, and code aligned on one vocabulary. It answers "what does this term mean and what is it called in code?". Rule logic belongs in `business-rules.md`; step-by-step processes belong in `flows.md`.

## File location

- Single domain context: one `GLOSSARY.md` at the repository root (create it when the first domain term is settled; shared with the `domain-modeling` skill).
- Multiple domain contexts: `GLOSSARY-MAP.md` at the repository root listing each context and its `GLOSSARY.md`.

## Format

```md
# <Context name>

<1-2 sentences describing what this context covers.>

## Terms

**Worktree** (`WorktreeNode`):
A linked or main working tree of a git repository sharing `.git` common dir.
_Do not call_: checkout folder, repo clone
```

- Pick one canonical term per concept and list synonyms under `_Do not call_`.
- Keep definitions to 1-2 sentences stating what it is (not how it is implemented), with the code identifier in parentheses.
- Include only project-specific domain concepts, not generic technical terms.

## When using

- Point out terminology mismatches immediately and confirm the preferred term before proceeding.
- Add newly settled domain terms right away.
- When existing code naming conflicts with the glossary, list the mismatch for the user instead of renaming silently.
