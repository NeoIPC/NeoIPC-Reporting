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

- `GET /validation-report` renders the Validation Report, which lists every record a NeoIPC
  validation rule flags, to HTML or PDF for the departments `departmentFilter` names (every department
  the DHIS2 session can see when it names none), applying the rules `rules` names (all when absent) and
  admitting test departments with `includeTestData`. It needs the F_NEOIPC_REPORT authority and applies
  the admin-uploaded validation-exception file when there is one. A rule id the report does not know is
  refused with `unknown-validation-rule`, and a request that accepts only JSON with
  `no-acceptable-output`, since the report has no data output.
- `GET /validation-report/rules?locale=` lists the rules the report applies, each with a one-sentence
  summary of what it checks, read from the report's own string resources in the requested language and
  in English where a summary is not translated; a locale the report does not serve is a 400.
  `GET /validation-report/locales` and `GET /validation-report/parameters` follow the other reports'.
  Which languages the report offers is governed by `RenderReadyLanguages`, as for the others.
- `--emit-schemas` writes `validation-report.json` beside the other two schemas, and the parameter
  generator carries an `integer[]` report parameter as `int[]`.

### Changed

- The image sets EB Garamond from Octavio Pardo's completed static OTFs, pinned to a commit of their
  repository, in place of Debian's `fonts-ebgaramond`, the unfinished original whose bold face lacks
  the subscript digits, ≥ and −: the bold Q₁, Q₂ and Q₃ headers of the Partner and Reference Reports'
  tables rendered as boxes.

## [0.3.0] - 2026-09-07

### Added

- An MIT licence, with the notice shipped inside the image.

### Changed

- The image bakes the reports at `reports-v0.1.0-alpha` and neoipcr at `v0.0.0.9001`, in place of the
  first alpha of each. What that carries is in those products' own changelogs; the two that change
  this service's behaviour are the Reference Report's department filter, which now reaches the render,
  and the antibiotic-utilization table, whose exported name the reports call and which no earlier
  neoipcr release carried.
- Quarto 1.10.18 and the TeX Live packages the reports' archival-PDF (PDF/A) output requires; a
  KOMA-Script "tagging not supported" warning surfaces as an error on the LaTeX log channel rather
  than letting a document assert a conformance it lost.
- Each report mode — live fetch, stored reference dataset, uploaded partner dataset — accepts only
  the parameters it can honour, and refuses the rest under its own problem code instead of ignoring
  them. The reference report takes a `departmentFilter` parameter in place of the removed
  `hospitalFilter`, and the service passes it to the report, which the pin below carries, so the
  filter reaches the rendered output rather than being ignored. A failed output negotiation is a coded `406`
  rather than a bodiless `415`, and a request whose dataset is stored or uploaded, but which accepts
  no rendered output, is refused before any stored dataset is looked up.
- The default `Reporting:Dhis2BaseUrl` is `http://dhis2-backend:8080`, the DHIS2 service's name in
  this repository's own compose file; a deployment that sets nothing follows it.
- R and the `r-cran-*` packages track the current CRAN release again; the stopgap `r-base-core` pin
  is gone.
- Dependencies moved to current releases, among them Roslyn 5.9.0 for the source generator,
  AngleSharp 1.8.0, Testcontainers 4.15, `Microsoft.AspNetCore.OpenApi` 10.0.11 and the
  `alpine/git` image that clones the report sources, 2.54.0.
- The image builds on the .NET SDK 10.0.400 and runs on ASP.NET Core 10.0.11. The generator's
  Roslyn can be no newer than the compiler in the SDK image, so the two move together.
- TinyTeX 2026.09, its packages installed from the TeX Live snapshot of 2026-08-31, the earliest
  day whose `texlive-scripts` revision matches the bundle's.

### Fixed

- A DHIS2 that did not answer within the session client's timeout crashed the request with a
  `500`; the session now fails authentication, so the request is refused with the `401` or `403`
  an invalid session gets, and the log names the likely cause.
- The assembly inside the image reported `1.0.0` whatever tag the image carried; it now reports the
  service's version, which the release workflow verifies against the tag.
- The documented local development stack crash-looped: its compose overlay selected the Development
  settings, which point the report sources at a checkout that does not exist inside the image.
- `--emit-schemas` wrote CRLF on Windows and LF elsewhere, so the schema snapshots diffed for drift
  changed with whichever platform wrote them last.

### Security

- `Microsoft.OpenApi` resolves to 2.7.5 or later, clearing GHSA-v5pm-xwqc-g5wc (stack overflow on
  a circular schema reference). `Microsoft.AspNetCore.OpenApi` 10.0.11 requires that floor itself,
  so the service no longer carries a direct reference to hold it.
- Testcontainers 4.15 brings SSH.NET 2026.0.0, clearing GHSA-q939-rpr3-3284 (arbitrary file write
  through a recursive SCP download) from the test project; the image never contained it.

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

[Unreleased]: https://github.com/NeoIPC/NeoIPC-Reporting/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/NeoIPC/NeoIPC-Reporting/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/NeoIPC/NeoIPC-Reporting/compare/v0.1.4...v0.2.0
