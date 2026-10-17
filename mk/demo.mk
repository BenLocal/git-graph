# Demo repositories and window snapshots for checking the UI.

DEMO_DIR ?= .demo
THEME    ?= Dark
SHOT     ?= $(DEMO_DIR)/snapshot.png
SELECT   ?= 3
COMPARE  ?=
TAB      ?=
PANEL    ?=
SIDEBAR  ?=
FULLDIFF ?=

.PHONY: demo demo-run snapshot

demo: ## Create demo repositories and state (DEMO_DIR=.demo, THEME=Dark|Light|System)
	$(PYTHON) scripts/make_demo.py --out $(DEMO_DIR) --theme $(THEME)

demo-run: demo ## Run the app against the demo state
	GITGRAPH_STATE=$(abspath $(DEMO_DIR))/state.json $(DOTNET) run --project $(APP) -c $(CONFIG)

snapshot: demo ## Render the demo window to a PNG (SHOT=path, SELECT=row, COMPARE=row, TAB=0|1, PANEL=max|hidden, SIDEBAR=0, FULLDIFF=1|expanded)
	GITGRAPH_STATE=$(abspath $(DEMO_DIR))/state.json \
	GITGRAPH_SNAPSHOT=$(abspath $(SHOT)) \
	GITGRAPH_SNAPSHOT_SELECT=$(SELECT) \
	GITGRAPH_SNAPSHOT_COMPARE=$(COMPARE) \
	GITGRAPH_SNAPSHOT_TAB=$(TAB) \
	GITGRAPH_SNAPSHOT_PANEL=$(PANEL) \
	GITGRAPH_SNAPSHOT_SIDEBAR=$(SIDEBAR) \
	GITGRAPH_SNAPSHOT_FULLDIFF=$(FULLDIFF) \
	$(DOTNET) run --project $(APP) -c $(CONFIG)
	@echo "snapshot: $(SHOT)"
