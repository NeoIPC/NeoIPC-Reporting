# NeoIPC-Reporting

[![build-and-test](https://github.com/NeoIPC/NeoIPC-Reporting/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/NeoIPC/NeoIPC-Reporting/actions/workflows/build-and-test.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

The service that turns NeoIPC surveillance data into a report someone can read. It is an
ASP.NET Core minimal-API application, shipped as a container, that renders the
[Surveillance-Toolkit](https://github.com/NeoIPC/Surveillance-Toolkit)'s Quarto reports to
PDF or HTML on request — and exposes the datasets behind them as JSON.

[NeoIPC](https://neoipc.org) works to reduce the transmission of resistant bacteria in
neonatal intensive care across Europe and globally; its
[surveillance system](https://neoipc.org/surveillance/) collects healthcare-associated
infection and antimicrobial-use data from neonatal departments. This service is what gives
a department its results back.

## What it does

- **Renders reports on demand** — currently the Partner Report a department receives and the
  network-wide Reference Report. A render runs Quarto over the toolkit's report sources,
  with the [neoipcr](https://github.com/NeoIPC/neoipcr) R package pulling and computing the
  data behind them.
- **Serves the underlying datasets as JSON**, for callers that want the numbers rather than
  the document.
- **Authenticates against DHIS2** by forwarding the caller's session cookie, and gates
  administrative endpoints with claims derived from that user's DHIS2 authorities.
- **Bakes its own inputs.** A released image records the exact report sources and neoipcr
  version it shipped, so a report can be reproduced later rather than merely re-rendered.

## Repository layout

- `src/NeoIPC.Reporting/` — the service (and its `Dockerfile`).
- `src/NeoIPC.Reporting.Generators/` — Roslyn incremental source generator
  that emits `<Report>RenderParameters` records and `Schema` data from the
  toolkit's master QMD `params:` blocks.
- `tests/NeoIPC.Reporting.Tests/` — NUnit test project. `Category=Unit`
  and `Category=Generator` run on every PR; `Category=Container` builds
  the image and exercises it in isolation via Testcontainers, on
  `workflow_dispatch` only; `Category=Integration` runs against a live,
  seeded DHIS2 + reporting stack (configured through the `NEOIPC_*`
  environment variables read by `ExternalDhis2Fixture`) and self-skips
  when the stack is unreachable (see `.github/workflows/build-and-test.yml`).
- `compose.yml` — minimal stack: Postgres + DHIS2 + this service + Traefik.
- `pinned-sources.yml` — the report-sources and neoipcr tags a released image bakes.

### Running tests locally

```bash
# Unit + generator tests (no Docker needed)
dotnet test --filter "Category!=Integration&Category!=Container"

# Container smoke tests (build the image in isolation; uses
# NEOIPC_REPORTING_IMAGE_TAG, defaults to neoipc-reporting:smoke-test)
dotnet test --filter "Category=Container"

# Integration tests against a live, seeded stack (self-skip when unreachable;
# configured via the NEOIPC_* env vars read by ExternalDhis2Fixture)
dotnet test --filter "Category=Integration"
```

## A simple local development stack

```bash
cp .env.sample .env   # then edit: the sample's passwords are placeholders
docker compose up --build
```

Brings up Postgres + DHIS2 + this service + Traefik on `localhost:${TRAEFIK_PORT:-8080}`.
Every variable in the sample without a documented default is required — compose
substitutes an unset one with an empty string, so a missing value surfaces late
and unhelpfully (`postgis/postgis:` with no tag) rather than as a clear error.

Compose picks up [`compose.override.yml`](compose.override.yml) automatically, so
this is a **development** stack rather than a production-shaped one: it builds the
service in `Debug`, runs it with `ASPNETCORE_ENVIRONMENT=Development`, and exposes
the service and the Traefik dashboard on extra ports. Pass
`-f compose.yml` alone to bring up the base configuration without that overlay.

## Developing against local report sources

Opening `NeoIPC-Reporting.sln` and pressing F5 runs the **Workspace** launch profile: the
service starts locally and reads the report sources and the R package from **sibling
checkouts** rather than from the image. Edits to either show up on the next render, with no
rebuild.

That profile expects this layout, which is what `appsettings.Development.json` resolves its
`../../../` paths against:

```
<some directory>/
├── NeoIPC-Reporting/      # this repository
├── Surveillance-Toolkit/  # report sources
└── neoipcr/               # the R package
```

Change `Reporting:ReportsSourceDir` / `Reporting:NeoIpcrDevPath` if you keep them elsewhere.
For DHIS2 authentication and live data, run a DHIS2 instance alongside it and point
`Reporting:Dhis2BaseUrl` at it through the `Reporting__Dhis2BaseUrl` environment variable —
the service loads `appsettings.json` and `appsettings.Development.json` only, so a
`.local.json` overlay would be read by nothing.

## Build modes (Docker)

**Released images are built from pinned tags.** The `release` workflow builds in `github-tag` mode
from the tags recorded in [`pinned-sources.yml`](pinned-sources.yml) (a Surveillance-Toolkit
`reports-v*` tag and a neoipcr tag), so every published `ghcr.io/neoipc/neoipc-reporting:<ver>` bakes
immutable, reproducible sources and records them as `net.neoipc.*` image labels (inspect with
`docker inspect`). To change what an image bakes, edit `pinned-sources.yml` and cut a new release. The
modes below are the underlying Dockerfile options used for that and for local/dev builds.

Three reports-source modes via `--build-arg REPORTS_SOURCE`:

| Mode | What it does | Build-args (defaults shown) |
|------|-------------|------------------------------|
| `github-branch` (default) | Clone `${REPORTS_REPO}` at branch `${REPORTS_BRANCH}` (with submodules) | `REPORTS_REPO=https://github.com/NeoIPC/Surveillance-Toolkit.git`, `REPORTS_BRANCH=main` |
| `github-tag`              | Clone a tagged release            | `REPORTS_REPO=…`, `REPORTS_TAG=vX.Y.Z` (required) |
| `workspace`               | COPY from the named build context `surveillance-toolkit` (passed via `--build-context`) | — |

Three neoipcr-source modes via `--build-arg NEOIPCR_SOURCE`:

| Mode | What it does | Build-args (defaults shown) |
|------|-------------|------------------------------|
| `github-branch` (default) | `pak::pkg_install("${NEOIPCR_REPO}@${NEOIPCR_BRANCH}")` | `NEOIPCR_REPO=NeoIPC/neoipcr`, `NEOIPCR_BRANCH=main` |
| `github-tag`              | `pak::pkg_install("${NEOIPCR_REPO}@${NEOIPCR_TAG}")` | `NEOIPCR_REPO=…`, `NEOIPCR_TAG=vX.Y.Z` (required) |
| `workspace`               | COPY from the named build context `neoipcr` to `/neoipcr`, install `devtools` | — |

The two axes are independent: pick any source mode for each, and any
`*_REPO` / `*_BRANCH` / `*_TAG` value on the toolkit side is unrelated
to the neoipcr side. Build a fork-of-toolkit + upstream-branch-neoipcr
image (or vice versa, or both forks on different feature branches) by
setting only the args you care about. The repo + branch overrides exist
so a dev image can pull from a fork or a feature branch before the work
merges to upstream main; once merged, drop the override and the
defaults become correct again.

The main build context stays the same across all modes — this repo's
root. Workspace mode adds named build contexts (BuildKit `--build-context`)
for the sibling checkouts, instead of widening the main context.

### Examples

```bash
# Standalone build against a fork's feature branches (dev iteration).
docker build -f src/NeoIPC.Reporting/Dockerfile \
  --build-arg REPORTS_REPO=https://github.com/<you>/Surveillance-Toolkit.git \
  --build-arg REPORTS_BRANCH=<your-branch> \
  --build-arg NEOIPCR_REPO=<you>/neoipcr \
  --build-arg NEOIPCR_BRANCH=<your-branch> \
  -t neoipc-reporting:dev .

# Pinned tags (the release path): a Surveillance-Toolkit reports-v* tag + a neoipcr tag.
# These are the values pinned-sources.yml records; the release workflow passes them for you.
docker build -f src/NeoIPC.Reporting/Dockerfile \
  --build-arg REPORTS_SOURCE=github-tag --build-arg REPORTS_TAG=reports-v0.0.1-alpha \
  --build-arg NEOIPCR_SOURCE=github-tag --build-arg NEOIPCR_TAG=v0.0.0.9000 \
  -t neoipc-reporting:v0.2.0 .

# Local build from sibling checkouts (supplied via named build contexts).
docker build -f src/NeoIPC.Reporting/Dockerfile \
  --build-context surveillance-toolkit=../Surveillance-Toolkit \
  --build-context neoipcr=../neoipcr \
  --build-arg REPORTS_SOURCE=workspace --build-arg NEOIPCR_SOURCE=workspace \
  -t neoipc-reporting:workspace .
```

The matching `Reporting:BuildMode` runtime env is baked into the image
per `NEOIPCR_SOURCE` mode — `github-branch` and `github-tag` images
ignore `NEOIPCR_DEV_PATH`; `workspace` images export it as `/neoipcr`.

## Deployment expectations

- **Network policy.** The `Reporting:Dhis2BaseUrl` value drives both the
  in-process auth call to `/api/me` and the R-subprocess data fetch via
  neoipcr. Egress from the reporting container must be restricted to
  the in-cluster DHIS2 service. In Compose this is the default-bridge
  network behaviour; on Kubernetes apply a `NetworkPolicy` that allows
  only that destination.
- **Read-only resource trees.** The image's `/toolkit/` and (in
  workspace mode) `/neoipcr/` trees are `chmod -R a-w` at build time.
  The render path only ever creates symlinks pointing into them, so
  the immutability is the load-bearing defense; runtime
  `read_only: true` on the whole container is not used because the
  Quarto + R subprocesses write to a number of cache paths under
  `/home/app/.*` that would each need explicit tmpfs mounts. The
  Compose service mounts a tmpfs at `/tmp` so per-render workdir
  state doesn't accumulate in the container's layered filesystem.
- **`Dhis2BaseUrl` is part of the trusted computing base.** An attacker
  who can flip it would harvest forwarded JSESSIONIDs. Treat it as
  deployment configuration with the same trust level as the image
  itself; do not surface it as a runtime knob to untrusted callers.
- **Storage volumes.** The Compose stack mounts named volumes for
  admin-uploaded reference datasets and validation-exception files.
  These survive container restarts but live with the host operator's
  backup discipline.

## Part of the NeoIPC surveillance system

| Repository | Role |
|------------|------|
| [Surveillance-Toolkit](https://github.com/NeoIPC/Surveillance-Toolkit) | The protocol, the case definitions, the DHIS2 metadata and the report sources |
| [neoipcr](https://github.com/NeoIPC/neoipcr) | R package that reads NeoIPC data out of DHIS2 and computes the surveillance indicators |
| **NeoIPC-Reporting** | *(this repository)* Renders those reports on demand and serves them over HTTP |
| [neoipc-app](https://github.com/NeoIPC/neoipc-app) | DHIS2 application through which people request reports and administer reference data |

The report sources and the R package are **not authored here** — they are ingested from the
two repositories above at image-build time, and Quarto itself is a pinned binary added to
the image. Report content and R analysis code therefore belong upstream, not in this
repository.

## Contributing

Issues and pull requests are welcome. The service is pre-alpha and moving quickly, so it is
worth raising an issue before a larger change.

Note the split above before opening a pull request: a change to what a report *says* or how
a number is *computed* belongs in Surveillance-Toolkit or neoipcr, and reaches this service
through a version bump in [`pinned-sources.yml`](pinned-sources.yml). What belongs here is
the service around them — the API surface, authentication and authorization, the render
pipeline, logging and packaging.

Translations of the NeoIPC protocol, report text and surveillance vocabulary are managed on
[Weblate](https://hosted.weblate.org/projects/neoipc/); contributions in any language are
welcome and need no git knowledge.

## Licensing

MIT — see [LICENSE](LICENSE).

The report sources and the R package this service renders are separate products with their
own licensing; see [Surveillance-Toolkit](https://github.com/NeoIPC/Surveillance-Toolkit),
whose infectious-agent and antibiotics lists carry stricter, upstream-dictated terms.

## Funding

The NeoIPC project has received funding from the European Union's Horizon 2020 research and
innovation programme under grant agreement No 965328.
