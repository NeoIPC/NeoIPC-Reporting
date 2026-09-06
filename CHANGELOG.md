# Changelog

Notable changes to the NeoIPC reporting service and the container image it ships in.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the version lives in
[Directory.Build.props](Directory.Build.props). The release workflow reads the section matching the
released version out of this file and publishes it as the GitHub Release body. A pull-request job
fails while the version declared there is not described here, and on a tag push the section is
extracted before the image is built, so a version this file does not describe fails its release
with nothing published.

Report content is not authored here: the Quarto sources and the R package are pinned by
`pinned-sources.yml` and baked into the image at build time, so a change to a report appears in that
product's own changelog, and here only as the pin that carries it.

## [Unreleased]

### Added

- An MIT licence, with the notice shipped inside the image.

### Changed

- Quarto 1.10.18 and the TeX Live packages the reports' archival-PDF (PDF/A) output requires; a
  KOMA-Script "tagging not supported" warning surfaces as an error on the LaTeX log channel rather
  than letting a document assert a conformance it lost.
- Each report mode — live fetch, stored reference dataset, uploaded partner dataset — accepts only
  the parameters it can honour, and refuses the rest under its own problem code instead of ignoring
  them. The reference report takes a `departmentFilter` parameter in place of the removed
  `hospitalFilter`, and the service passes it to the report; it takes effect once the pinned report
  sources carry it, which `reports-v0.0.1-alpha` does not: with that pin the JSON output ignores the
  filter, and a rendered output that carries it fails. A failed output negotiation is a coded `406`
  rather than a bodiless `415`.
- The default `Reporting:Dhis2BaseUrl` is `http://dhis2-backend:8080`, the DHIS2 service's name in
  this repository's own compose file; a deployment that sets nothing follows it.
- R and the `r-cran-*` packages track the current CRAN release again; the stopgap `r-base-core` pin
  is gone.
- Dependencies moved to current releases, among them Roslyn 5.6.0 for the source generator,
  Testcontainers 4.13 and `Microsoft.AspNetCore.OpenApi` 10.0.10.

### Fixed

- The assembly inside the image reported `1.0.0` whatever tag the image carried; it now reports the
  service's version, which the release workflow verifies against the tag.
- The documented local development stack crash-looped: its compose overlay selected the Development
  settings, which point the report sources at a checkout that does not exist inside the image.
- `--emit-schemas` wrote CRLF on Windows and LF elsewhere, so the schema snapshots diffed for drift
  changed with whichever platform wrote them last.

### Security

- `Microsoft.OpenApi` raised to 2.7.5, clearing GHSA-v5pm-xwqc-g5wc (stack overflow on a circular
  schema reference).

## [0.2.0] - 2026-07-06

### Added

- Render-engine failures are recovered to per-source log channels at their true severity: LaTeX
  errors from Quarto's error block, R errors from knitr's output, so a failed render can be filtered
  by engine and severity instead of read out of the render's progress output.
- Stable error codes on `problem+json` responses.
- Release verification in CI: the unit and generator tests run before the image is built, a version
  regression within a release line is rejected, a moved tag is rejected, and a GitHub Release
  documents each tagged image.

### Changed

- Immutable upstream pins for the report sources and the R package — `reports-v0.0.1-alpha` and
  neoipcr `v0.0.0.9000` in this release — so a published image records exactly which versions of
  each it was built from and a rebuild bakes the same report and package sources. The release
  workflow verifies the pinned versions exist and that the pinned R package is the one the pinned
  reports were tested against.
- Report languages are gated to a render-ready allowlist; a byte-identical reference-data upload is
  rejected with `409`; unit codes are required only for online partner reports; the
  locale-independent JSON output is served without a locale; `q=0` in content negotiation is
  honoured throughout; render working directories are retained by a `RenderWorkdirRetention`
  setting; storage lives on an application-owned volume.

### Fixed

- A cancelled render terminates the whole `quarto` → `Rscript`/`lualatex`/`pandoc` process tree
  instead of orphaning it, so repeated timeouts no longer accumulate detached processes.
- `confidenceIntervals=all|rate|none` was rejected with `400`, because the parameter was bound
  case-sensitively as an enum whose names are capitalized; it now accepts the lowercase tokens the
  app sends.

[Unreleased]: https://github.com/NeoIPC/NeoIPC-Reporting/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/NeoIPC/NeoIPC-Reporting/compare/v0.1.4...v0.2.0
