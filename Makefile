# Entry point only: the targets live in mk/*.mk, helper scripts in scripts/.
# Run `make` or `make help` to list them.

SOLUTION := GitGraph.slnx
APP      := src/GitGraph.App
CONFIG   ?= Debug
DOTNET   ?= dotnet
PYTHON   ?= python3

.DEFAULT_GOAL := help

include mk/*.mk

.PHONY: help
help: ## List the available targets
	@awk 'BEGIN {FS = ":.*## "} /^[a-zA-Z0-9_-]+:.*## / {printf "  \033[36m%-12s\033[0m %s\n", $$1, $$2}' $(MAKEFILE_LIST)
