# NeoIPC.Reporting

ASP.NET Core minimal-API container that renders the NeoIPC Surveillance
Toolkit's Quarto reports (PDF / HTML) and exposes the underlying datasets
as JSON. Authenticates via DHIS2 session cookies; gates admin endpoints
with claims-based authorization derived from the user's DHIS2 authorities.

## Repository layout

- `src/NeoIPC.Reporting/` — the service (and its `Dockerfile`).
- `src/NeoIPC.Reporting.Generators/` — Roslyn incremental source generator
  that emits `<Report>RenderParameters` records and `Schema` data from the
  toolkit's master QMD `params:` blocks.
- `tests/NeoIPC.Reporting.Tests/` — NUnit test project. `Category=Unit`
  and `Category=Generator` run on every PR; `Category=Integration` spins
  up the built image via Testcontainers and runs only on
  `workflow_dispatch` (see `.github/workflows/build-and-test.yml`).
- `compose.yml` — minimal stack: Postgres + DHIS2 + this service + Traefik.

### Running tests locally

```bash
# Unit + generator tests (no Docker needed)
dotnet test --filter "Category!=Integration"

# Integration tests (require a built image; uses NEOIPC_REPORTING_IMAGE_TAG
# env var, defaults to neoipc-reporting:smoke-test)
dotnet test --filter "Category=Integration"
```

## Workspace IDE launch (F5)

Double-clicking `NeoIPC-Reporting.sln` and pressing F5 in Visual Studio
runs the **Workspace** profile out of the box: the .NET service starts
locally and points at `repos/Surveillance-Toolkit/reports` and
`repos/neoipcr` from the parent workspace checkout. Edits to the toolkit
or to neoipcr show up on the next render — no rebuild needed.

This works only when the project is opened *inside* the workspace
checkout (i.e. via the workspace's nested `repos/NeoIPC-Reporting/`).
The workspace-relative paths in `appsettings.Development.json` are
resolved at startup against the host's `ContentRoot`.

For DHIS2 authentication and live data, run a DHIS2 stack on the side
(typically the workspace's own `repos/neoipc-dhis2/compose.yml`) and
override `Reporting:Dhis2BaseUrl` via env var or
`appsettings.Development.local.json`.

## Build modes (Docker)

Three reports-source modes via `--build-arg REPORTS_SOURCE`:

| Mode | What it does | Build-args (defaults shown) |
|------|-------------|------------------------------|
| `github-branch` (default) | Clone `${REPORTS_REPO}` at branch `${REPORTS_BRANCH}` (with submodules) | `REPORTS_REPO=https://github.com/NeoIPC/Surveillance-Toolkit.git`, `REPORTS_BRANCH=main` |
| `github-tag`              | Clone a tagged release            | `REPORTS_REPO=…`, `REPORTS_TAG=vX.Y.Z` (required) |
| `workspace`               | COPY from the named build context `surveillance-toolkit` (passed via `--build-context`) | — |

Three neoipcr-source modes via `--build-arg NEOIPCR_SOURCE`:

| Mode | What it does | Build-args (defaults shown) |
|------|-------------|------------------------------|
| `github-branch` (default) | `pak::pkg_install("${NEOIPCR_REPO}@${NEOIPCR_BRANCH}")` | `NEOIPCR_REPO=Brar/neoipcr`, `NEOIPCR_BRANCH=TowardsReferenceReport` |
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
# Standalone build against the current dev branches on a fork.
cd repos/NeoIPC-Reporting
docker build -f src/NeoIPC.Reporting/Dockerfile \
  --build-arg REPORTS_REPO=https://github.com/Brar/Surveillance-Toolkit.git \
  --build-arg REPORTS_BRANCH=PartnerReport \
  --build-arg NEOIPCR_REPO=Brar/neoipcr \
  --build-arg NEOIPCR_BRANCH=PartnerReport \
  -t neoipc-reporting:dev .

# Tagged toolkit + tagged neoipcr (upstream).
docker build -f src/NeoIPC.Reporting/Dockerfile \
  --build-arg REPORTS_SOURCE=github-tag --build-arg REPORTS_TAG=v1.2.0 \
  --build-arg NEOIPCR_SOURCE=github-tag --build-arg NEOIPCR_TAG=v0.1.0 \
  -t neoipc-reporting:v1.2.0 .

# Workspace build (sibling checkouts supplied via named build contexts).
cd repos/NeoIPC-Reporting
docker build -f src/NeoIPC.Reporting/Dockerfile \
  --build-context surveillance-toolkit=../Surveillance-Toolkit \
  --build-context neoipcr=../neoipcr \
  --build-arg REPORTS_SOURCE=workspace --build-arg NEOIPCR_SOURCE=workspace \
  -t neoipc-reporting:workspace .
```

The matching `Reporting:BuildMode` runtime env is baked into the image
per `NEOIPCR_SOURCE` mode — `github-branch` and `github-tag` images
ignore `NEOIPCR_DEV_PATH`; `workspace` images export it as `/neoipcr`.

## Local stack via compose

```bash
cd repos/NeoIPC-Reporting
cp .env.sample .env   # if present in your checkout
docker compose up --build
```

Brings up Postgres + DHIS2 + this service + Traefik on `localhost:${TRAEFIK_PORT:-8080}`.

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
