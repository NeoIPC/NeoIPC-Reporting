# syntax=docker/dockerfile:1.7

# Three reports-source modes selected via build-arg REPORTS_SOURCE:
#   github-branch   - default. Clone ${REPORTS_REPO} at branch ${REPORTS_BRANCH}
#                     with submodules. The repo + branch overrides exist so a
#                     dev image can pull from a fork or a feature branch
#                     before the work merges to upstream main.
#   github-tag      - clone ${REPORTS_REPO} --branch ${REPORTS_TAG}.
#   workspace       - COPY from the named build context "surveillance-toolkit"
#                     (passed in via --build-context). Lets workspace builds
#                     keep the main context = this repo's root, with the
#                     sibling Surveillance-Toolkit checkout supplied separately.
ARG REPORTS_SOURCE=github-branch
ARG REPORTS_REPO=https://github.com/NeoIPC/Surveillance-Toolkit.git
ARG REPORTS_BRANCH=main
ARG REPORTS_TAG=

# Three neoipcr-source modes selected via build-arg NEOIPCR_SOURCE:
#   github-branch   - default. pak-install ${NEOIPCR_REPO}@${NEOIPCR_BRANCH}.
#                     The repo + branch overrides mirror REPORTS_* — same
#                     rationale (fork / feature-branch builds).
#   github-tag      - pak-install ${NEOIPCR_REPO}@${NEOIPCR_TAG}.
#   workspace       - COPY from the named build context "neoipcr" to /neoipcr
#                     and install devtools (matches editable workspace mode).
ARG NEOIPCR_SOURCE=github-branch
ARG NEOIPCR_REPO=Brar/neoipcr
ARG NEOIPCR_BRANCH=TowardsReferenceReport
ARG NEOIPCR_TAG=

# ----------------------------------------------------------------------------
# Stage: ingest the Surveillance-Toolkit (full repo, including po4a submodule).
# ----------------------------------------------------------------------------
# The toolkit's localization pipeline depends on tools/po4a/ (a submodule).
# Docker's `ADD <git-url>` does NOT recurse submodules, so use git clone.

FROM alpine/git:v2.52.0 AS toolkit-source-github-branch
ARG REPORTS_REPO
ARG REPORTS_BRANCH
RUN git clone --recurse-submodules --depth 1 --branch "${REPORTS_BRANCH}" \
    "${REPORTS_REPO}" /toolkit && \
    rm -rf /toolkit/.git /toolkit/tools/po4a/.git

FROM alpine/git:v2.52.0 AS toolkit-source-github-tag
ARG REPORTS_REPO
ARG REPORTS_TAG
RUN test -n "${REPORTS_TAG}" || (echo "REPORTS_TAG build-arg required when REPORTS_SOURCE=github-tag" >&2; exit 1) && \
    git clone --recurse-submodules --depth 1 --branch "${REPORTS_TAG}" \
    "${REPORTS_REPO}" /toolkit && \
    rm -rf /toolkit/.git /toolkit/tools/po4a/.git

# Workspace mode pulls the toolkit from a named build context that is
# supplied separately at build time:
#   --build-context surveillance-toolkit=<path-to-Surveillance-Toolkit>
# This keeps the main build context the same as in github-* modes (the
# NeoIPC-Reporting submodule root), so the build stage's "src/..." COPY
# paths don't have to know which mode is active. Requires
# `git submodule update --init tools/po4a` in the source checkout.
FROM alpine/git:v2.52.0 AS toolkit-source-workspace
COPY --from=surveillance-toolkit . /toolkit
RUN rm -rf /toolkit/.git /toolkit/tools/po4a/.git

# Selector: pick whichever of the three matches REPORTS_SOURCE.
FROM toolkit-source-${REPORTS_SOURCE} AS toolkit-source

# ----------------------------------------------------------------------------
# Stage: run the toolkit's own localization pipeline.
# ----------------------------------------------------------------------------
# Invoke-Localization.ps1 is the toolkit's authoritative entry point: validates
# string layers, updates po4a YAML keys, runs po4a per config, regenerates the
# glossary. Running the wrapper directly keeps us aligned with whatever the
# pipeline shape is upstream (no reimplementation drift). Base is the official
# PowerShell-on-Debian image so we don't pay the cost of installing pwsh on
# top of aspnet:10.0; this stage is build-only and discarded.

FROM mcr.microsoft.com/powershell:7.4-debian-12 AS reports-builder
ARG DEBIAN_FRONTEND=noninteractive

# Dependencies the pipeline invokes: Perl + the modules po4a actually loads
# for the [type: yaml] / [type: text] -o markdown / [type: asciidoc] /
# [type: xml] configs the toolkit ships, plus gettext (msgmerge / msgcat),
# python3 + ruamel.yaml + polib for the glossary script. libxml2-utils for
# xmlcatalog (po4a's xml module needs it for documentation.po4a.cfg).
#
# Cache mounts on /var/cache/apt + /var/lib/apt let the apt index and the
# downloaded debs survive across builds even when the layer re-runs. The
# `rm -f .../docker-clean` removes the upstream image's policy that strips
# downloaded packages on every install.
RUN --mount=type=cache,target=/var/cache/apt,sharing=locked \
    --mount=type=cache,target=/var/lib/apt,sharing=locked \
    rm -f /etc/apt/apt.conf.d/docker-clean && \
    apt-get -qq update && \
    apt-get -qq install --no-install-recommends \
        perl \
        liblocale-gettext-perl libtext-wrapi18n-perl libunicode-linebreak-perl \
        libyaml-tiny-perl libsyntax-keyword-try-perl libpod-parser-perl \
        libxml2-utils \
        gettext \
        python3 python3-ruamel.yaml python3-polib

# powershell-yaml is the only external PSGallery module the toolkit's
# scripts import (Test-StringResourceLayers.ps1, Update-Po4aYamlKeys.ps1,
# Convert-InfectiousAgentList.ps1). The rest of the toolkit's pwsh modules
# live in scripts/modules/ and ship with the source.
RUN pwsh -NoProfile -Command "Set-PSRepository -Name PSGallery -InstallationPolicy Trusted; Install-Module -Name powershell-yaml -Force -Scope AllUsers"

COPY --from=toolkit-source /toolkit /toolkit
WORKDIR /toolkit
RUN pwsh -NoProfile -File ./scripts/Invoke-Localization.ps1 -Update -NonInteractive

# Strip step. Keep what the runtime actually needs:
#   /toolkit-output/glossary*.yaml         (matches ../../glossary*.yaml reach)
#   /toolkit-output/reports/<top-level>    (common.yaml, common.<lang>.yaml)
#   /toolkit-output/reports/{common,filters,logos}/  (shared sibling dirs)
#   /toolkit-output/reports/<Report>/      (one dir per report — both the
#                                           Quarto QMD set and the
#                                           Generate-*.R scripts ship here)
# Drop everything else (PO sources, scripts/, tools/po4a/, doc/, metadata/,
# tests, sample-data, READMEs, .git*). The "if [ ... ]; then ... fi" form
# returns 0 when the condition is false, so the for loops complete with
# exit 0 even when some entries don't match.
RUN set -eu; \
    mkdir -p /toolkit-output/reports; \
    for f in glossary*.yaml; do \
        if [ -e "$f" ]; then cp "$f" /toolkit-output/; fi; \
    done; \
    for f in reports/common*.yaml; do \
        if [ -e "$f" ]; then cp "$f" /toolkit-output/reports/; fi; \
    done; \
    for dir in reports/common reports/filters reports/logos; do \
        if [ -d "$dir" ]; then cp -r "$dir" /toolkit-output/reports/; fi; \
    done; \
    for dir in reports/*/; do \
        if [ -f "${dir}$(basename "$dir").qmd" ]; then cp -r "$dir" /toolkit-output/reports/; fi; \
    done

# ----------------------------------------------------------------------------
# Stage: r-runtime — shared parent for r-builder-base and the final-image
# base stage. Carries everything both paths need: R + the binary CRAN
# packages our reports import, pre-installed so the runtime image performs no
# install.packages() at render time (and so needs neither a compiler nor an
# apt bridge there).
# ----------------------------------------------------------------------------
# Why the shared parent: previously r-builder-base (build path) and base
# (runtime path) each apt-installed r-base-core independently from
# aspnet:10.0. Daily upstream pushes invalidated each separately; the work
# was duplicated. With r-runtime as the common ancestor, the heavy install
# is cached once and both paths inherit it.
#
# Why r2u: the previous approach pak-installed every CRAN package from
# source, taking 3-4 minutes for tidyverse + gt + ggplot2 + transitive deps.
# r2u (https://eddelbuettel.github.io/r2u/) mirrors all of CRAN as Ubuntu
# binary debs, so apt-get install r-cran-* completes in seconds with no
# compilation.

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS r-runtime
ARG DEBIAN_FRONTEND=noninteractive

# CRAN apt key — needed for r-base-core (newer than Ubuntu's r-base-core).
ADD --chown=1 --chmod=644 --checksum=sha256:ffa734347fd95fd7393f575448ba5e52cf3137067db77224d4857863351e4e8f \
    https://cloud.r-project.org/bin/linux/ubuntu/marutter_pubkey.asc \
    /etc/apt/trusted.gpg.d/cran_ubuntu_key.asc

# r2u apt key — needed for the r-cran-* binary packages.
ADD --chown=1 --chmod=644 --checksum=sha256:39062e2ecd9140c8b07f9d78d019224bfbf1ef4c8a519c070805dda4a8f7835c \
    https://eddelbuettel.github.io/r2u/assets/dirk_eddelbuettel_key.asc \
    /etc/apt/trusted.gpg.d/cranapt_key.asc

# Apt pinning so r-cran-* prefer r2u's binaries over Ubuntu's older
# r-base- packages even when both repositories carry the same name.
COPY <<"EOF" /etc/apt/preferences.d/99cranapt
Package: *
Pin: release o=CRAN-Apt Project
Pin: release l=CRAN-Apt Packages
Pin-Priority: 700
EOF

RUN --mount=type=cache,target=/var/cache/apt,sharing=locked \
    --mount=type=cache,target=/var/lib/apt,sharing=locked \
    rm -f /etc/apt/apt.conf.d/docker-clean && \
    apt-get -qq update && \
    apt-get -qq install --no-install-recommends \
        software-properties-common \
        dirmngr \
        ca-certificates && \
    add-apt-repository -y "deb https://cloud.r-project.org/bin/linux/ubuntu $(lsb_release -cs)-cran40/" && \
    add-apt-repository -y "deb [arch=amd64] https://r2u.stat.illinois.edu/ubuntu $(lsb_release -cs) main" && \
    apt-get -qq update && \
    apt-get -qq install --no-install-recommends \
        r-base-core \
        # CRAN packages our reports need at runtime (binary via r2u):
        r-cran-pak \
        r-cran-argparse \
        r-cran-rmarkdown \
        r-cran-knitr \
        r-cran-tidyverse \
        r-cran-smplot2 \
        r-cran-scales \
        r-cran-patchwork \
        r-cran-gt \
        # neoipcr's DESCRIPTION imports — pre-install as binaries so pak's
        # neoipcr resolution (in the github-* variant stages) finds them
        # ready and only has to compile neoipcr itself.
        r-cran-askpass \
        r-cran-httr2 \
        r-cran-ids \
        r-cran-jsonlite \
        r-cran-lubridate \
        r-cran-uuid \
        r-cran-tidyselect

# ----------------------------------------------------------------------------
# Stage: install dev tools needed to source-build neoipcr from GitHub.
# Inherits R + binary CRAN deps from r-runtime; adds only the compilers /
# libraries that R CMD INSTALL needs.
# ----------------------------------------------------------------------------

FROM r-runtime AS r-builder-base
ARG DEBIAN_FRONTEND=noninteractive
RUN --mount=type=cache,target=/var/cache/apt,sharing=locked \
    --mount=type=cache,target=/var/lib/apt,sharing=locked \
    rm -f /etc/apt/apt.conf.d/docker-clean && \
    apt-get -qq update && \
    apt-get -qq install --no-install-recommends \
        build-essential \
        gfortran \
        libblas-dev \
        liblapack-dev

# Variant: pak-install neoipcr from a branch. Spec travels as a positional
# argument so commandArgs(TRUE) picks it up — avoids brittle shell-escaping
# inside the Rscript -e expression. Cache mount on /root/.cache/R preserves
# pak's downloaded source archives across rebuilds.
FROM r-builder-base AS r-builder-github-branch
ARG NEOIPCR_REPO
ARG NEOIPCR_BRANCH
RUN --mount=type=cache,target=/root/.cache/R \
    Rscript -e 'pak::pkg_install(commandArgs(TRUE), ask = FALSE);' "${NEOIPCR_REPO}@${NEOIPCR_BRANCH}" > /dev/null

# Variant: pak-install neoipcr from a tag.
FROM r-builder-base AS r-builder-github-tag
ARG NEOIPCR_REPO
ARG NEOIPCR_TAG
RUN --mount=type=cache,target=/root/.cache/R \
    test -n "${NEOIPCR_TAG}" || (echo "NEOIPCR_TAG build-arg required when NEOIPCR_SOURCE=github-tag" >&2; exit 1) && \
    Rscript -e 'pak::pkg_install(commandArgs(TRUE), ask = FALSE);' "${NEOIPCR_REPO}@${NEOIPCR_TAG}" > /dev/null

# Variant: install devtools (for load_all) but NOT neoipcr itself; the
# editable neoipcr is COPYed into the final image at /neoipcr and read
# at render time via NEOIPCR_DEV_PATH. r-cran-devtools comes from r2u
# as a binary — no source compile.
FROM r-builder-base AS r-builder-workspace
ARG DEBIAN_FRONTEND=noninteractive
RUN --mount=type=cache,target=/var/cache/apt,sharing=locked \
    --mount=type=cache,target=/var/lib/apt,sharing=locked \
    rm -f /etc/apt/apt.conf.d/docker-clean && \
    apt-get -qq update && \
    apt-get -qq install --no-install-recommends r-cran-devtools

# Selector + library cleanup (drops doc/help/html/examples/demo, strips .so).
FROM r-builder-${NEOIPCR_SOURCE} AS r-builder
RUN find /usr/local/lib/R/site-library -type d \
        \( -name doc -o -name help -o -name html -o -name examples -o -name demo \) \
        -exec rm -rf {} + 2>/dev/null; \
    find /usr/local/lib/R/site-library -name "*.so" -exec strip --strip-debug {} + 2>/dev/null; \
    true

# ----------------------------------------------------------------------------
# Stage: TinyTeX + the babel set the toolkit's reports use.
# ----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS tinytex-builder
ARG DEBIAN_FRONTEND=noninteractive
RUN --mount=type=cache,target=/var/cache/apt,sharing=locked \
    --mount=type=cache,target=/var/lib/apt,sharing=locked \
    rm -f /etc/apt/apt.conf.d/docker-clean && \
    apt-get -qq update && \
    apt-get -qq install --no-install-recommends perl wget fontconfig xz-utils
ADD --chmod=644 --checksum=sha256:ef6c552b2959744f449213fb124e58954a235e7d41ad191e48080869728ae451 \
    https://github.com/rstudio/tinytex-releases/releases/download/v2026.05/TinyTeX-1-linux-x86_64-v2026.05.tar.xz \
    /tmp/TinyTeX.tar.xz
USER $APP_UID
RUN tar xf /tmp/TinyTeX.tar.xz -C $HOME && \
    ~/.TinyTeX/bin/x86_64-linux/tlmgr install \
        babel babel-english babel-estonian babel-german babel-greek \
        babel-italian babel-spanish babel-turkish \
        koma-script anyfontsize multirow placeins tcolorbox fontawesome5 \
        colortbl wrapfig tikzfill pdfcol caption

# ----------------------------------------------------------------------------
# Stage: Quarto (only the linux-amd64 binaries we need).
# ----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS quarto-downloader
ADD --checksum=sha256:bdf689b5589789a1f21d89c3b83d78ed02a97914dd702e617294f2cc1ea7387d \
    https://github.com/quarto-dev/quarto-cli/releases/download/v1.8.27/quarto-1.8.27-linux-amd64.tar.gz \
    /tmp/quarto.tar.gz
ADD --checksum=sha256:066e20cdb882994160e18524a552b97e03648eb9aa0c7cdf5680a6493be65ab2 \
    https://registry.npmjs.org/@esbuild/linux-x64/-/linux-x64-0.27.3.tgz \
    /tmp/esbuild.tgz
RUN mkdir -p /opt/quarto && \
    tar -zxf /tmp/quarto.tar.gz -C /opt/quarto --strip-components=1 && \
    tar -zxf /tmp/esbuild.tgz -C /tmp && \
    cp /tmp/package/bin/esbuild /opt/quarto/bin/tools/x86_64/esbuild && \
    rm -rf /opt/quarto/bin/tools/x86_64/deno* \
           /opt/quarto/bin/tools/x86_64/typst* \
           /opt/quarto/share/jupyter

# ----------------------------------------------------------------------------
# Stage: runtime base (R + locales + fonts + Quarto + TinyTeX + toolkit).
# Inherits R + the binary CRAN packages from r-runtime; adds only what's
# unique to the final image (fonts, locales, Quarto, TinyTeX, toolkit).
# ----------------------------------------------------------------------------
FROM r-runtime AS base
ARG DEBIAN_FRONTEND=noninteractive
RUN --mount=type=cache,target=/var/cache/apt,sharing=locked \
    --mount=type=cache,target=/var/lib/apt,sharing=locked \
    rm -f /etc/apt/apt.conf.d/docker-clean && \
    apt-get -qq update && \
    # Pull Ubuntu (noble) security updates into the runtime image for packages
    # inherited from r-runtime / the aspnet base — gnutls28, libgcrypt20,
    # xz-utils, expat, cairo, python3, etc. — so it ships the latest
    # noble-patched versions rather than the base image's snapshot.
    apt-get -qq upgrade -y && \
    apt-get -qq install --no-install-recommends \
        fonts-ebgaramond \
        fonts-ebgaramond-extra \
        fonts-noto-core \
        fonts-noto-extra \
        locales && \
    locale-gen de_DE.UTF-8 el_GR.UTF-8 en_GB.UTF-8 es_ES.UTF-8 et_EE.UTF-8 it_IT.UTF-8 tr_TR.UTF-8

# Attack-surface reduction: drop build-/desktop-only packages that have no role
# at render time, so they neither ship in nor get scanned against the image.
#   xdg-utils, packagekit, policykit-1   - desktop/D-Bus tooling, no daemon here
#   elfutils                              - eu-* dev tools (libelf1/libdw1 kept)
#   software-properties-common           - build-only (add-apt-repository); its
#                                           removal orphans the python3 stack
#   python3-cryptography, python3-jwt     - Python libs the .NET service never calls
# autoremove --purge then sweeps the now-orphaned transitive deps (the python3
# interpreter included, if nothing kept depends on it). autoremove removes only
# auto-installed orphans, so explicitly-installed runtime deps (ca-certificates,
# r-base-core, the r-cran-* set) are untouched.
RUN --mount=type=cache,target=/var/cache/apt,sharing=locked \
    --mount=type=cache,target=/var/lib/apt,sharing=locked \
    apt-get -qq purge -y \
        xdg-utils packagekit policykit-1 elfutils \
        software-properties-common python3-cryptography python3-jwt && \
    apt-get -qq autoremove -y --purge

# Runtime layout convention (matches the per-render layout in
# QuartoReportGenerator.cs — see ReportingOptions.ReportsSourceDir):
#   /toolkit/                 (toolkit root — read-only, owned by root)
#     glossary*.yaml
#     reports/
#       common*.yaml
#       common/, filters/, logos/    (shared sibling dirs)
#       <Report>/                    (one dir per report)
COPY --from=reports-builder /toolkit-output /toolkit

# Bring neoipcr (and any extra packages r-builder added on top of r-runtime,
# e.g. devtools in workspace mode) into the final image. Most of the
# site-library is already populated from r-runtime; this COPY overlays the
# variant-specific additions.
COPY --from=r-builder /usr/local/lib/R/site-library /usr/local/lib/R/site-library
COPY --from=quarto-downloader /opt/quarto /opt/quarto
RUN ln -s /opt/quarto/bin/quarto /usr/local/bin/quarto && \
    ln -s /opt/quarto/bin/tools/x86_64/pandoc /usr/local/bin/pandoc && \
    printf '<fontconfig>\n  <selectfont>\n      <rejectfont>\n          <glob>/usr/share/fonts/woff/*</glob>\n      </rejectfont>\n  </selectfont>\n</fontconfig>\n' > /etc/fonts/conf.d/70-no-woff.conf
COPY --from=tinytex-builder --chown=$APP_UID /home/app/.TinyTeX /home/app/.TinyTeX
ENV PATH="/home/app/.TinyTeX/bin/x86_64-linux:$PATH"

# Lock down /toolkit (read-only at runtime). The render path only ever creates
# symlinks pointing into here; nothing should be writing to /toolkit. A
# defence-in-depth complement to the per-render symlink-forest model.
RUN chmod -R a-w /toolkit

USER $APP_UID
WORKDIR /app
EXPOSE 8080

# ----------------------------------------------------------------------------
# Stage: workspace neoipcr placement + runtime BuildMode wiring.
# ----------------------------------------------------------------------------
# Per-mode stages so the no-op variants don't carry a stray /neoipcr dir,
# and so each variant bakes in the matching Reporting:BuildMode env.

FROM base AS base-neoipcr-workspace
USER root
COPY --from=neoipcr --chown=0:0 . /neoipcr
RUN chmod -R a-w /neoipcr
USER $APP_UID
ENV Reporting__BuildMode=Workspace

FROM base AS base-neoipcr-github-branch
ENV Reporting__BuildMode=GithubBranch

FROM base AS base-neoipcr-github-tag
ENV Reporting__BuildMode=GithubTag

FROM base-neoipcr-${NEOIPCR_SOURCE} AS final-base

# ----------------------------------------------------------------------------
# Stage: build the .NET project.
# ----------------------------------------------------------------------------
# The source generator (NeoIPC.Reporting.Generators) reads committed
# .qmd-schema.json snapshots under src/NeoIPC.Reporting/Schemas/ — no
# Surveillance-Toolkit access is needed at compile time. The reports-builder
# stage is still consumed by the final image for runtime rendering.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/NeoIPC.Reporting/NeoIPC.Reporting.csproj", "src/NeoIPC.Reporting/"]
COPY ["src/NeoIPC.Reporting.Generators/NeoIPC.Reporting.Generators.csproj", "src/NeoIPC.Reporting.Generators/"]
RUN dotnet restore "./src/NeoIPC.Reporting/NeoIPC.Reporting.csproj"
COPY . .
WORKDIR "/src/src/NeoIPC.Reporting"

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./NeoIPC.Reporting.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    /p:UseAppHost=false

# ----------------------------------------------------------------------------
# Stage: final image.
# ----------------------------------------------------------------------------
FROM final-base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "NeoIPC.Reporting.dll"]
