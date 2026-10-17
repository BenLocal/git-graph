# Demo repositories and window snapshots for checking the UI.

DEMO_DIR ?= .demo
THEME    ?= Dark
SHOT     ?= $(DEMO_DIR)/snapshot.png
SELECT   ?= 3
COMPARE  ?=

.PHONY: demo demo-run snapshot

demo: ## Create demo repositories and state (DEMO_DIR=.demo, THEME=Dark|Light|System)
	$(PYTHON) scripts/make_demo.py --out $(DEMO_DIR) --theme $(THEME)

demo-run: demo ## Run the app against the demo state
	GITGRAPH_STATE=$(abspath $(DEMO_DIR))/state.json $(DOTNET) run --project $(APP) -c $(CONFIG)

snapshot: demo ## Render the demo window to a PNG (SHOT=path, SELECT=row, COMPARE=row)
	GITGRAPH_STATE=$(abspath $(DEMO_DIR))/state.json \
	GITGRAPH_SNAPSHOT=$(abspath $(SHOT)) \
	GITGRAPH_SNAPSHOT_SELECT=$(SELECT) \
	GITGRAPH_SNAPSHOT_COMPARE=$(COMPARE) \
	$(DOTNET) run --project $(APP) -c $(CONFIG)
	@echo "snapshot: $(SHOT)"
