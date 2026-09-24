.PHONY: help build test format lint verify soak mutate regression-scene checks package bump testing release

help: ## List the targets
	@grep -E '^[a-z-]+:.*## ' $(MAKEFILE_LIST) | awk -F':.*## ' '{printf "  make %-16s %s\n", $$1, $$2}'

build: ## Debug build for loading as a dev plugin
	@scripts/build.sh

test: ## Run the Core tests
	@scripts/test.sh

format: ## Format the code with CSharpier
	@scripts/format.sh

lint: ## Build the plugin and tests with analyzer warnings as errors
	@scripts/lint.sh

verify: ## Format, lint, test and check coverage: run before every commit
	@scripts/verify.sh

soak: ## Run the property tests many times, stopping at a failure; RUNS=<n> for other than 20
	@scripts/soak.sh $(RUNS)

mutate: ## Mutation-test Core with Stryker; SINCE=<commit> for changes since it
	@scripts/mutate.sh $(SINCE)

regression-scene: ## Rewrite the camera regression scene file from its cases
	@scripts/regression-scene.sh

checks: ## Serve the in-game checklist page; PORT=<n> for other than 3000
	@scripts/checks.sh $(PORT)

package: ## Release build and latest.zip, as CI makes it
	@scripts/package.sh

bump: ## Set the version and commit it: make bump VERSION=0.6.0.1
	@scripts/bump.sh $(VERSION)

testing: ## Ship the current version to opted-in testers (test-vX.Y.Z.N)
	@scripts/release.sh test

release: ## Ship the current version to everyone (prod-vX.Y.Z.N)
	@scripts/release.sh prod
