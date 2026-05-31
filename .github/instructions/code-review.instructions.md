---
applyTo: "**"
---

# `NeoIPC-Reporting` — code review instructions

Read these before generating review comments on this repository. The points below cover (a) review-process discipline that has been a recurring problem in past reviews, and (b) domain / API facts that have caused false-positive findings.

## Review-process discipline

- **One comment per finding.** Do NOT post multiple comments for the same finding — neither at the same `(file, line)` nor at different occurrences of the same pattern. If the same construct appears at multiple lines, raise it ONCE and list the additional lines in that single comment.
- **Continue the conversation on existing threads.** If a finding has already been raised on this PR in an earlier review, do NOT create a new comment for it. Reply on the existing thread instead — even if the line number has shifted or the surrounding diff has changed.
- **Respect resolved threads.** If a previously-raised finding was marked resolved (either because it was fixed in a commit, accepted as a false positive with reasoning, or explicitly deferred to a later PR), do NOT raise the same finding again in subsequent reviews of the same PR. The maintainer's resolution is authoritative.
- **Trust maintainer rebuttals.** When a maintainer replies to a finding with a reasoned rebuttal, accept the rebuttal and do not re-raise the same finding in any later review of the same PR.
- **Before raising a finding, check the file's full context.** Many false positives have come from looking at a single line in isolation when surrounding lines, the base-image documentation, or upstream specification would have shown why the construct is correct.

## Project context

This is a **.NET 10 ASP.NET Core minimal API** service that renders Surveillance-Toolkit Quarto reports as PDF / HTML / JSON, gated by DHIS2 session authentication. It is consumed by the `neoipc-app` (DHIS2 App Platform) frontend over HTTP. The runtime container bundles R, TinyTeX, Quarto, and the reports themselves; later PRs add a Roslyn source generator that emits parameter records from QMD `params:` blocks.

## Library / API conventions

### Docker base image `mcr.microsoft.com/dotnet/aspnet:10.0`

- `APP_UID` is defined as an `ENV` by the upstream Microsoft image (currently `1654`) and is visible to every stage that `FROM`s it, including non-final builder stages. The image creates a Linux user `app` via `useradd --create-home`, so `/home/app` is registered in `/etc/passwd` as that user's home directory.
- Because `app`'s home is registered in `/etc/passwd`, Docker's `USER $APP_UID` (or `USER 1654`) directive causes the engine to set `HOME=/home/app` automatically for subsequent `RUN` / `CMD` commands. `tar … -C $HOME` and `~/.something` expansions work as expected without an explicit `ENV HOME=…` line. Do not flag `USER $APP_UID` in a builder stage as "HOME may be empty or root's home".

### Docker Compose merge behavior (Compose Specification)

- Per the [Compose Specification merge rules](https://github.com/compose-spec/compose-spec/blob/main/13-merge.md), `ports` is merged as the **union of unique entries** across base + override files, not replaced. Identical entries are deduplicated, but repeating a base-file mapping in an override is still bad practice — only list the additional mappings the override actually adds.

### Quarto / R interop

- The Reference Report directory and file are named `Reference-Report` (hyphenated). Any glob, regex, or hard-coded filename in C# that references the report MUST use the hyphenated form consistently — globs and matching regexes must stay in lockstep when a rename happens.
