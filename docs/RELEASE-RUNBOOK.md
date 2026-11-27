# Release runbook

> The generic shape of how a release ships. Fill every `<PLACEHOLDER>` for this project. The
> spine is fixed (changelog · bump · regenerate docs · tag · GitHub Release · docs publish);
> **signing / publish specifics are project-filled** — some projects delegate signing to an
> upstream binary, some self-sign, some do neither.

<!-- [<doc>->REQ-EXAMPLE-REL] -->

## One-time setup

`<Project-specific. e.g. release-signing key ceremony + where keys live; any repo secrets /
tokens the publish step needs. Keep release keys OUT of CI — sign locally if you sign at all.>`

## Per release

1. **Bump** the version (`<where this project's version lives>`) if needed; **regenerate
   generated docs** (`<your docs/reference gen command>`); land everything green.
2. **Write the user-facing changelog** — add a `## [<version>] - <date>` section at the top of
   `CHANGELOG.md` with **Added / Changed / Fixed** subsections. This section becomes the GitHub
   Release **body** verbatim, so it is the changelog every user reads. Rules:
   - **User-facing UX only.** What a person using the product notices or does differently. Name
     the actual commands / flags / surfaces they touch.
   - **No internal lingo** — no requirement ids, internal module names, commit hashes, or
     milestone / hazard codes. A reader who has never seen the source must understand every line.
   - **Flag breaking changes** prominently under Changed.
   - The release **fails loudly** if the tagged version has no `## [<version>]` section — the
     changelog is not optional.
3. **Tag**: `git tag <vX.Y.Z> && git push origin <vX.Y.Z>`. This triggers:
   - the **release workflow** — build artifacts, extract this version's `CHANGELOG.md` section
     into the release body, and create the **GitHub Release on this same repo** with the built
     artifacts + checksums;
   - the **docs-publish workflow** — build `docs-site/` and publish to GitHub Pages (drift-gated;
     see `docs/DOCS-STRATEGY.md`).
4. **Sign + publish** (project-specific):
   `<Fill the publish step. Examples: "delegate signing to <upstream binary> which verifies
   artifacts against the checksums before signing, then flips the draft public"; OR "self-sign
   with <tool> and attach the signatures"; OR "no signing — the workflow's draft release is the
   final artifact, just publish it.">`

## Notes

- **Three independent numbers — never conflate them:** the **semver** (`<vX.Y.Z>`), any
  **release counter** you keep (`<if applicable — a monotonic publish counter>`), and the
  **wire-protocol version** (`<if applicable — only if this project speaks a versioned
  protocol>`). Each moves on its own schedule.
- `<Any trust / verification model for updates: what a client trusts on first fetch vs. on
  subsequent updates; how a leaked signing key is revoked without a rebuild.>`
