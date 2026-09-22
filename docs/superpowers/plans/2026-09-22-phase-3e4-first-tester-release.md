# First Tester Release (Phase 3.e.4) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Vista releasable as a closed-beta experimental plugin: its manifest, icon, licence, custom repository file, and a tag-driven GitHub Actions release.

**Architecture:** The plugin project holds the one version number. DalamudPackager builds `latest.zip` and a completed manifest from `Vista.json` on a Release build. A workflow triggered by a `v*` tag builds against Dalamud's release reference assemblies, tests, checks the tag against the version, publishes a GitHub Release, and, for a non-prerelease tag, rewrites `repo.json` on `main`.

**Tech Stack:** GitHub Actions (windows-latest), .NET 10, Dalamud.NET.Sdk 15.0.0 with DalamudPackager, PowerShell.

**Spec:** `docs/superpowers/specs/2026-09-22-first-tester-release-design.md`

## Global Constraints

- Text is exact from the spec:
  - Punchline: "Camera tracks for cinematic shots."
  - Description: "Create smooth, cinematic camera paths in FFXIV, organise them into scenes, and play them back live."
  - Tags: camera, cinematic, screenshots, video, events.
  - Changelog for 0.2.0: "First closed beta."
  - `AcceptsFeedback`: false.
  - Command help: "/vista opens the editor".
- Author "psenxiv"; repo `https://github.com/psenxiv/vista`; raw base `https://raw.githubusercontent.com/psenxiv/vista/main/`.
- `DalamudApiLevel` 15; Dalamud reference assemblies from `https://goatcorp.github.io/dalamud-distrib/latest.zip` (release channel, currently 15.0.3.5).
- Version `0.2.0` in `src/Vista.Plugin/Vista.Plugin.csproj` only. A tag `vX.Y.Z` must match it; a prerelease tag `vX.Y.Z-suffix` must match on `X.Y.Z` and never touches `repo.json`.
- Commits: one line, conventional prefix, no body, no trailer. Pushing tags is the user's call; nothing here pushes.

## Order

One task, done by the controller in a worktree (`.claude/worktrees/3e4-release`) while 3.e.5 runs on `main`, then merged. It touches `Plugin.cs` only on the command help line.

---

### Task 1: Release files and workflow

**Files:**
- Create: `LICENSE`, `repo.json`, `images/icon.svg`, `images/icon.png`, `.github/workflows/release.yml`
- Modify: `src/Vista.Plugin/Vista.json`, `src/Vista.Plugin/Vista.Plugin.csproj`, `src/Vista.Plugin/Plugin.cs` (help text)

- [ ] **Step 1: Licence.** MIT, "Copyright (c) 2026 psenxiv".
- [ ] **Step 2: Version.** `<Version>0.2.0</Version>` in the plugin project.
- [ ] **Step 3: Manifest.** `Vista.json`: Name, Author, Punchline, Description, Tags, `ApplicableVersion` "any", `AcceptsFeedback` false, `RepoUrl`, `IconUrl` (raw `images/icon.png`), `Changelog` "First closed beta.".
- [ ] **Step 4: Help text.** `HelpMessage = "/vista opens the editor"`.
- [ ] **Step 5: Icon.** `images/icon.svg`: a white camera glyph on a dark rounded square, 512 × 512; `images/icon.png` rendered from it with `rsvg-convert -w 512 -h 512`.
- [ ] **Step 6: `repo.json`.** One entry mirroring the manifest, with `InternalName` "Vista", `AssemblyVersion` "0.2.0", `TestingAssemblyVersion` "0.2.0", `DalamudApiLevel` 15, `IsTestingExclusive` false, `LastUpdate` (Unix seconds), and `DownloadLinkInstall`/`DownloadLinkUpdate`/`DownloadLinkTesting` all `https://github.com/psenxiv/vista/releases/download/v0.2.0/latest.zip`.
- [ ] **Step 7: Workflow.** `.github/workflows/release.yml`, on `push: tags: ['v*']`, `permissions: contents: write`, one job on `windows-latest`:
  1. Checkout with full history; set up .NET 10.
  2. Download and expand the release distrib into `$env:AppData\XIVLauncher\addon\Hooks\dev`, and set `DALAMUD_HOME` to it for later steps.
  3. Read `<Version>` from the project; strip the tag's `v` and any `-suffix`; fail when they differ. Mark prerelease when the tag has a suffix.
  4. `dotnet test tests/Vista.Tests/Vista.Tests.csproj -c Release`.
  5. `dotnet build src/Vista.Plugin/Vista.Plugin.csproj -c Release`; locate `latest.zip` under `src/Vista.Plugin/bin/Release`.
  6. Create the GitHub Release for the tag with the zip attached (`gh release create`, `--prerelease` when a prerelease), titled "Vista <tag>", notes from the manifest's Changelog.
  7. Not a prerelease: rewrite `repo.json` (versions, `LastUpdate`, the three download links, plus Punchline, Description, Tags and Changelog from the manifest), commit as `github-actions[bot]` with `chore(release) publish <tag>`, and push to `main`.
- [ ] **Step 8: Check locally.** `dotnet build src/Vista.Plugin/Vista.Plugin.csproj -c Release` with `DALAMUD_HOME` set as `build.sh` sets it: `latest.zip` and the built manifest appear, and the built manifest carries `AssemblyVersion` 0.2.0, `DalamudApiLevel` 15 and the new text. Validate `repo.json` with `python3 -m json.tool`, and the workflow YAML parses.
- [ ] **Step 9: Commit and merge.**

```bash
git add LICENSE repo.json images .github src/Vista.Plugin/Vista.json src/Vista.Plugin/Vista.Plugin.csproj src/Vista.Plugin/Plugin.cs
git commit -m "chore(release) add the manifest, icon, licence, repository file and release workflow"
```

Then cherry-pick onto `main` once 3.e.5 has landed.

### After the task

`CHECKLIST-3e4.md`: pushing `v0.2.0-rc.1` (the user's call) runs the workflow and makes a prerelease without touching `repo.json`; pushing `v0.2.0` publishes and updates `repo.json`; in game, adding the raw `repo.json` URL under Experimental lists Vista with its icon and text, installs it, and a later version updates it.
