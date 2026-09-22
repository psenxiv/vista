.PHONY: help build test package bump release rc

help: ## List the targets
	@grep -E '^[a-z]+:.*## ' $(MAKEFILE_LIST) | awk -F':.*## ' '{printf "  make %-8s %s\n", $$1, $$2}'

build: ## Debug build for loading as a dev plugin
	@scripts/build.sh

test: ## Run the Core tests
	@scripts/test.sh

package: ## Release build and latest.zip, as CI makes it
	@scripts/package.sh

bump: ## Set the version and commit it: make bump VERSION=0.3.0
	@scripts/bump.sh $(VERSION)

release: ## Tag and push the current version, publishing it
	@scripts/release.sh

rc: ## Tag and push a prerelease: make rc N=1
	@scripts/release.sh rc $(N)
