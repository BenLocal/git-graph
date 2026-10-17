# Build, test, run and publish with the .NET SDK.

RID         ?= linux-x64
PUBLISH_DIR ?= artifacts/publish/$(RID)

.PHONY: restore build test run clean publish

restore: ## Restore NuGet packages
	$(DOTNET) restore $(SOLUTION)

build: ## Build the solution (CONFIG=Debug|Release)
	$(DOTNET) build $(SOLUTION) -c $(CONFIG)

test: ## Run the test suite
	$(DOTNET) test $(SOLUTION) -c $(CONFIG)

run: ## Run the app
	$(DOTNET) run --project $(APP) -c $(CONFIG)

clean: ## Remove build output and published artifacts
	$(DOTNET) clean $(SOLUTION) -c $(CONFIG)
	rm -rf artifacts

publish: ## Publish a self-contained app (RID=linux-x64|win-x64|osx-arm64)
	$(DOTNET) publish $(APP) -c Release -r $(RID) --self-contained -o $(PUBLISH_DIR)
