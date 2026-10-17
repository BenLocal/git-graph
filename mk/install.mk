# Install or update the app for the current user, and remove it again.

PREFIX   ?= $(HOME)/.local
SHORTCUT ?= 1

.PHONY: install uninstall

install: publish ## Install or update the app under PREFIX (default ~/.local); SHORTCUT=0 skips the desktop shortcut
	$(PYTHON) scripts/install.py install --from $(PUBLISH_DIR) --prefix $(PREFIX) $(if $(filter 0,$(SHORTCUT)),--no-shortcut)

uninstall: ## Remove the installed app from PREFIX
	$(PYTHON) scripts/install.py uninstall --prefix $(PREFIX)
