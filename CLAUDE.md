# Claude Code — NeoIPC-Reporting Instructions

This file documents the NeoIPC-Reporting repository (.NET reporting backend — Docker container, Quarto/R report rendering). If this repository is checked out as a submodule of the `neoipc-workspace`, the workspace-level `CLAUDE.md` adds additional workspace-specific guardrails (file boundary, cross-repo change order) on top of the guardrails below.

---

## Guardrails

These guardrails are **universal** — mirrored in every NeoIPC repository's instruction files. If you add or change a guardrail here, add `<!-- SYNC: propagate to all repos -->` next to it so the change gets propagated when the workspace is next used.

- **Never** put personal names or other identifying information in source code (comments, strings, commit messages, etc.), except in copyright statements and file-header attribution lines (e.g. `Author:`, `@author`, `Copyright (c)` fields).
- **Never** read, write, or access files under `secrets/`, `data/local/`, or `.env`. This includes listing, globbing, searching, or interacting with these paths in any way — not just reading file contents. If the user provides a path under these directories, use it as-is without exploring the directory.
- **Never** push directly to `main` or `master` on this repository.
- **Never** make HTTP calls to the DHIS2 API or attempt to read JSON files returned from the DHIS2 API. These files contain sensitive surveillance data and are not needed for code-level tasks.
- **Never** put absolute local paths into files that get checked in. Use relative paths or generic placeholders. Local checkout paths are developer-specific and meaningless to others.
- Treat infection definitions in the Surveillance-Toolkit repository as normative. When a conflict exists between code and definitions, **fix the code**, not the definitions.
- **Never** introduce non-permissive dependencies (fonts, libraries, templates). All fonts must be SIL OFL or equivalent.
- **Always** keep `CLAUDE.md` and `.github/copilot-instructions.md` in sync within this repository. When you modify one, apply the same change to the other.
- **Always** push back when evidence contradicts the user's suggestion or implied assumption. Do not defer to the user's position when authoritative sources (AMA Manual of Style, protocol definitions, language specifications, etc.) say otherwise. Present the evidence clearly and let the user decide.
- **Always** consider both personal data protection (GDPR) and organizational/reputational concerns when making decisions about data shared between partners, published in reports, or exposed through APIs. Small cell counts in shared reports can expose which departments had specific rare pathogens or resistance patterns.
- **Never** use deprecated or outdated APIs. Before introducing a function from a third-party package or a base library, verify it is current. When a replacement exists, use the replacement. When unsure, check the package's `NEWS.md` / release notes rather than assuming.
- **Always** read the upstream source directly when you need a definitive answer about a third-party system's behaviour (DHIS2 in particular, but also R / tidyverse packages, Quarto, Pandoc, .NET runtime, etc.). Docs, release notes, and changelogs are known to be unreliable for some of these projects — the source is the ultimate authority, the written reference is a convenience shortcut. When working via the neoipc-workspace, see its `CLAUDE.md` → Reference checkouts for the `refs/` submodules that support this workflow.
