# Hikers' Diary — agent working rules

> **Canonical source of truth for how agents work in this repo.** Harness-agnostic:
> Claude Code reads it via the one-line `CLAUDE.md` (`@AGENTS.md`); other harnesses read
> this file directly. Edit working rules HERE, never in `CLAUDE.md`.

`Hikers' Diary` is a Big Walk mod (BepInEx 6, IL2CPP). It keeps the world and the players
as the players left them: objects, vehicles, lights, time of day, player poses and items, and the
bodies of players who logged out. A later revision lets players change saves from text chat.

**Orientation — read before working:** `README.md` (user-facing surface), `docs/DESIGN.md`
(scope, revisions, spikes), the current `M*-PLAN.md`, `CONTEXT.md` (glossary, authoritative
for meaning), `docs/adr/` (decisions), `docs/KNOWN-HAZARDS.md` (invariants we must not
break again), `docs/{DOCS-STRATEGY,TRACEABILITY,RELEASE-RUNBOOK,CI}.md`.

## Requirement traceability (binding)

This project uses [`traceable-reqs`](https://github.com/BigscreenVR/traceable-reqs)
(`traceable-reqs.toml` = the authoritative `REQ-*` registry). The full contract is
`docs/TRACEABILITY.md`. The rules you must follow:

1. **Tag evidence in the same change.** When you write a function/test/doc-section that
   satisfies a requirement stage, add its tag in that same commit:
   `// [impl->REQ-FOO]` · `// [unit->REQ-FOO]` · `<!-- [doc->REQ-FOO] -->`
   Stages: `doc` / `impl` / `unit` / `int`. Tag *on or immediately above* the real
   evidence — never at file tops to satisfy coverage.
2. **Run `traceable-reqs check` before declaring work done.** Exit-1 means missing/invalid
   evidence — fix it, don't ship it.
3. **New requirement → add it to `traceable-reqs.toml` first** (with a `REQ-*` id), then
   satisfy it. No untracked work; no untagged evidence.
4. **KNOWN-HAZARDS are `REQ-HAZARD-*` requirements** — each needs a test before it's
   "covered." Treat the hazard list as a conformance checklist you must satisfy, not advice.
5. **Activate, don't pre-fail.** Requirements you aren't yet working stay
   `required_stages = []`. Activate (set real stages) only when starting the milestone that
   delivers them.

## Writing standard: ASD-STE100 (binding)

Write all text for a person or an agent in ASD-STE100 (Simplified Technical English). It applies to these texts:

- reports and gate records
- pull requests, issues, requests, and comments
- changelogs and release notes
- commit message bodies
- documentation and requirement text
- messages to other agents

Code, identifiers, commands, and quoted text keep their exact form. The full rule set, the word list, and the audit command are in `docs/WRITING-STE100.md`. Read it before you write a changelog, a pull request, or a report.

## Other conventions

- Use the same code style as the code near it. Use only C#. The plugin project disables
  `Nullable`, because the Il2CppInterop proxy assemblies conflict with the nullable
  attributes that the compiler makes.
- **Game symbols are facts, not guesses.** Class, method, and field names come from the
  decompiled game (`BepInEx/interop/*.dll` signatures from `ilspycmd`, bodies from Cpp2IL).
  If a symbol is missing, or the game build is not a Known Build, the mod installs no hooks
  and writes one log line. It never runs with part of its hooks.
- **The host is the authority.** Only the host writes the save. Clients never write the
  shared world state.
- Keep the game-independent code (save format, history, match rules) in
  `HikersDiary.Core`. This code must have no BepInEx, Unity, or game references, so that
  unit tests can run it.
- Keep a seam for a later local control API (pseudo-players, see `docs/DESIGN.md`). Do not
  build that API in revisions (a) to (c).
- Obey every `docs/KNOWN-HAZARDS.md` invariant — each is a `REQ-HAZARD-*` with a test.
- Docs are for two audiences (persons and AI dev-agents), as `docs/DOCS-STRATEGY.md`
  specifies. The docs-site build is a gate.
- **The local build must have the game.** The plugin references proxy assemblies in the
  Big Walk installation (`GameDir` in `Directory.Build.props`, override in `Directory.Build.props.user`).
- **This project has no hosted CI.** It uses no GitHub Actions and no runners.
  `pwsh scripts/gates.ps1` is the whole CI. Run it by hand before you declare that the work
  is complete, and before every release (`docs/CI.md`).
- **Measure, do not guess.** A claim about game behavior must have a log line, a decompiled
  body, or an in-game test.
- Commit messages end with the co-author trailer of the agent that wrote them:
  `Co-authored by: <your live agent name>` (for example `Co-authored by: gears`). Keep the
  harness trailers too.

## Plans and context hygiene

- **JIT plans.** Plan the next immediate body of work just-in-time (a short `*-PLAN.md`),
  not the whole project up front. A plan names scope, open design questions, tasks, and the
  gate (build + `traceable-reqs check` green). `docs/DESIGN.md` is the standing design;
  plans are per-milestone.
- If you finish a significant body of work without need for user intervention, or if your
  context gets too high, you can clear your own context and keep moving:
  1. Create a JIT plan for the next immediate body of work, if it isn't already planned.
  2. If you are a live agent: commune immediate next steps and a broad project-status and
     end-goal summary, then clear.
