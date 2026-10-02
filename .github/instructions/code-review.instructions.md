---
applyTo: "**"
---

# `NeoIPC-Reporting` — Code Review Instructions

Read these before generating review comments on this repository. They cover (1) review-process discipline and (2) domain and API facts without which a correct construct looks wrong.

## Review-Process Discipline

- **One comment per finding.** Do NOT post multiple comments for the same finding — neither at the same `(file, line)` nor at different occurrences of the same pattern. If the same construct appears at multiple lines, raise it ONCE and list the additional lines in that single comment.
- **Continue the conversation on existing threads.** If a finding has already been raised on this pull request in an earlier review, do NOT create a new comment for it. Reply on the existing thread instead — even if the line number has shifted or the surrounding diff has changed.
- **Respect resolved threads.** If a previously raised finding was marked resolved (because it was fixed in a commit, accepted as a false positive with reasoning, or explicitly deferred to a later pull request), do NOT raise the same finding again in subsequent reviews of the same pull request. The maintainer's resolution is authoritative.
- **Trust maintainer rebuttals.** When a maintainer replies to a finding with a reasoned rebuttal, accept the rebuttal and do not re-raise the same finding in any later review of the same pull request.
- **Before raising a finding, check the file's full context**: a line read in isolation looks wrong where the surrounding lines, the base image's documentation, or the upstream specification show why the construct is correct.

## Project Context

This is a **.NET 10 ASP.NET Core minimal API** service that renders Surveillance-Toolkit Quarto reports as PDF / HTML / JSON, gated by DHIS2 session authentication. It is consumed by the `neoipc-app` (DHIS2 App Platform) front end over HTTP. The runtime container bundles R, TinyTeX, Quarto, and the reports themselves. A Roslyn source generator, `ParameterRecordGenerator` in `src/NeoIPC.Reporting.Generators`, emits the parameter records from the reports' schema snapshots (`src/NeoIPC.Reporting/Schemas/*.qmd-schema.json`), which mirror each Quarto document's `params:` block.

## Library and API Conventions

### Docker Base Image `mcr.microsoft.com/dotnet/aspnet:10.0`

- `APP_UID` is defined as an `ENV` by the upstream Microsoft image (currently `1654`) and is visible to every stage that `FROM`s it, including non-final builder stages. The image creates a Linux user `app` via `useradd --create-home`, so `/home/app` is registered in `/etc/passwd` as that user's home directory.
- Because `app`'s home is registered in `/etc/passwd`, Docker's `USER $APP_UID` (or `USER 1654`) directive causes the engine to set `HOME=/home/app` automatically for subsequent `RUN` / `CMD` commands. `tar … -C $HOME` and `~/.something` expansions work as expected without an explicit `ENV HOME=…` line. Do not flag `USER $APP_UID` in a builder stage as "HOME may be empty or root's home".

### Docker Compose Merge Behaviour (Compose Specification)

- Per the [Compose Specification merge rules](https://github.com/compose-spec/compose-spec/blob/main/13-merge.md), `ports` is merged as the **union of unique entries** across base + override files, not replaced. Identical entries are deduplicated, but repeating a base-file mapping in an override is still bad practice — only list the additional mappings the override actually adds.

### Quarto and R Interoperability

- The Reference Report directory and file are named `Reference-Report` (hyphenated). Any glob, regex, or hard-coded filename in C# that references the report MUST use the hyphenated form consistently — globs and matching regexes must stay in lockstep when a rename happens.
