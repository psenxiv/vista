# Vista — First Tester Release (Phase 3.e.4)

Date: 2026-09-22
Status: Approved

Publish the current build, which saves nothing, as an unofficial experimental plugin for a
closed beta. Testers add a custom plugin repository and install Vista from Dalamud's installer.
Comes after `2026-09-22-look-at-and-follow-target-design.md` (3.e.3) and before saving (3.f).

Out of scope: listing in the official Dalamud repositories, a public announcement, a testers'
README, an in-plugin notice that nothing is saved, and a changelog.

## Audience

- Closed beta: only the people the user sends the repository URL to know it exists. Nothing in
  the plugin or the README mentions it.

## Repository

- A custom plugin repository file, `repo.json`, at the root of the public `psenxiv/vista` repo,
  served from its raw URL on `main`. It lists one plugin, Vista, with:
  - `Author`, `Name`, `InternalName`, `AssemblyVersion`, `DalamudApiLevel`, `Punchline`,
    `Description`, `RepoUrl`, `IconUrl`, `ApplicableVersion`, `LastUpdate`;
  - `DownloadLinkInstall`, `DownloadLinkUpdate` and `DownloadLinkTesting`, all pointing at the
    release's `latest.zip`.
- Testers paste the raw URL into `/xlsettings` → Experimental → Custom Plugin Repositories, save,
  and install Vista from `/xlplugins`. Updates arrive like any other plugin's.

## Releases

- Each release is a GitHub Release on `psenxiv/vista`, tagged `vX.Y.Z`, with DalamudPackager's
  `latest.zip` attached.
- **Versioning:** the first tester release is `0.2.0`; each later phase bumps the minor version.
  The version lives in one place, the plugin project, and the tag must match it.
- **API level:** the manifest's `DalamudApiLevel` is 15, matching the release channel's Dalamud
  15.0.3.5.

## Automation

A GitHub Actions workflow runs on a pushed `v*` tag:

1. Check out, set up .NET 10.
2. Fetch Dalamud's release reference assemblies into the path `Dalamud.NET.Sdk` reads on Linux,
   from goatcorp's `dalamud-distrib` `latest.zip`.
3. Run the tests; stop on a failure.
4. Build Release and package with DalamudPackager.
5. Refuse when the tag doesn't match the project's version.
6. Create the GitHub Release with `latest.zip`.
7. Update `repo.json` (version, `LastUpdate`, download links) and commit it to `main`.

Releasing is then: bump the version, commit, tag, push the tag.

## Manifest and what testers see

- `Name`: "Vista". `Author`: "psenxiv".
- `Punchline`: "Camera tracks for cinematic shots."
- `Description`: "Create smooth, cinematic camera paths in FFXIV, organise them into scenes, and play
  them back live."
- `Tags`: camera, cinematic, screenshots, video, events.
- `Changelog` for 0.2.0: "First closed beta."
- `AcceptsFeedback`: false. Testers report to the user directly.
- `RepoUrl`: `https://github.com/psenxiv/vista`. `IconUrl`: the icon's raw URL.
- The `/vista` command's help text: "/vista opens the editor".

## Icon

- A simple icon made for it: a camera glyph on a plain background, 512 × 512 PNG, in the repo
  (for example `images/icon.png`), referenced from the manifest and `repo.json`.

## Licence

- MIT, in a `LICENSE` file at the repo root, under the pseudonymous author name "psenxiv".

## Testing

- The workflow's own run on a test tag (a prerelease tag such as `v0.2.0-rc.1`, published as a
  GitHub prerelease and never written to `repo.json`) proves the pipeline end to end.
- Then the user tags `v0.2.0`; a separate `CHECKLIST-3e4.md` covers adding the repository in
  game, installing, launching, and updating to a later version.
