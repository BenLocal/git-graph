# Build, test, run and publish with the .NET SDK.

# Default to the host's runtime identifier; pass RID=... to cross-publish.
ifeq ($(OS),Windows_NT)
  HOST_OS   := win
  HOST_ARCH := $(if $(filter ARM64,$(PROCESSOR_ARCHITECTURE)),arm64,x64)
else
  HOST_OS   := $(if $(filter Darwin,$(shell uname -s)),osx,linux)
  HOST_ARCH := $(if $(filter arm64 aarch64,$(shell uname -m)),arm64,x64)
endif

RID         ?= $(HOST_OS)-$(HOST_ARCH)
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

publish: ## Publish a self-contained app for this machine (RID=linux-x64|win-x64|osx-arm64 to override)
	@# Start empty, so files an older build left (a dropped dependency's dll) don't ship; a custom PUBLISH_DIR is left alone.
	@case "$(PUBLISH_DIR)" in artifacts/*) rm -rf "$(PUBLISH_DIR)" ;; esac
	$(DOTNET) publish $(APP) -c Release -r $(RID) --self-contained -o $(PUBLISH_DIR)

.PHONY: lint format

lint: ## Check formatting and analyzers, as CI does
	$(DOTNET) format $(SOLUTION) --verify-no-changes

format: ## Fix formatting
	$(DOTNET) format $(SOLUTION)
