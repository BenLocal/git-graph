# Releases: pushing a v* tag makes the Release workflow build the packages and publish them on GitHub.

.PHONY: tag

tag: lint test ## Check, then create the tag vX.Y.Z locally (VERSION=X.Y.Z); pushing it publishes a release
	@test -n "$(VERSION)" || { echo "usage: make tag VERSION=1.2.3"; exit 1; }
	@git diff --quiet && git diff --cached --quiet || { echo "commit your changes first"; exit 1; }
	git tag -a v$(VERSION) -m "Git Graph $(VERSION)"
	@echo "Created v$(VERSION). Push it to release:  git push origin v$(VERSION)"

.PHONY: deb

deb: publish ## Build a .deb from the linux-x64 build (VERSION=X.Y.Z, default 0.0.0-dev)
	$(PYTHON) scripts/build_deb.py --from $(PUBLISH_DIR) --version $(or $(VERSION),0.0.0-dev) --out artifacts
