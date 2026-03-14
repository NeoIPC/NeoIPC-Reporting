# Claude Code — NeoIPC-Reporting Instructions

This file documents the NeoIPC-Reporting repository (.NET reporting backend — Docker container, Quarto/R report rendering). If this repository is checked out as a submodule of the `neoipc-workspace`, the workspace-level `CLAUDE.md` adds additional workspace-specific guardrails (file boundary, cross-repo change order) on top of the guardrails below.

---

## Guardrails

These guardrails are **universal** — mirrored in every NeoIPC repository's instruction files. If you add or change a guardrail here, add `<!-- SYNC: propagate to all repos -->` next to it so the change gets propagated when the workspace is next used.

- **Never** put personal names or other identifying information in source code (comments, strings, commit messages, etc.).
- **Never** read, write, or access files under `secrets/`, `data/local/`, or `.env`.
- **Never** push directly to `main` or `master` on this repository.
- **Never** make HTTP calls to the DHIS2 API or attempt to read JSON files returned from the DHIS2 API. These files contain sensitive surveillance data and are not needed for code-level tasks.
- **Never** put absolute local paths into files that get checked in. Use relative paths or generic placeholders. Local checkout paths are developer-specific and meaningless to others.
- Treat infection definitions in the Surveillance-Toolkit repository as normative. When a conflict exists between code and definitions, **fix the code**, not the definitions.
- **Never** introduce non-permissive dependencies (fonts, libraries, templates). All fonts must be SIL OFL or equivalent.
- **Always** keep `CLAUDE.md` and `.github/copilot-instructions.md` in sync within this repository. When you modify one, apply the same change to the other.
