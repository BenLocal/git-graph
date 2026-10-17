# Agent instruction docs (AGENTS.md and docs/), maintained with the agents-dot-md skill.

AGENTS_SKILL ?= $(HOME)/.claude/skills/agents-dot-md

.PHONY: reindex

reindex: ## Rebuild the generated indexes in AGENTS.md and docs/
	$(PYTHON) $(AGENTS_SKILL)/scripts/reindex.py .
