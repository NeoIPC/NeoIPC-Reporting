# syntax=docker/dockerfile:1.7

# ----------------------------------------------------------------------------
# Pinned upstream versions — central inventory. Bump deliberately and validate a
# full build; never ride a rolling tag.
#   * Base images are pinned by digest (the readable tag stays for humans; the
#     digest is what BuildKit resolves). Refresh one with:
#       docker buildx imagetools inspect <image>:<tag> --format '{{.Manifest.Digest}}'
#     A digest pin freezes the base image, so .NET / OS-layer security patches
#     arrive only on a deliberate digest bump — the `base` stage's `apt upgrade`
#     still pulls resolute security updates for the apt-managed libraries.
#   * Version ARGs hold the bare number (no leading `v`); the `v` is prepended at
#     the URL where the upstream tag carries one (TinyTeX, Quarto).
#   * The TinyTeX / Quarto / esbuild `ADD --checksum` values are version-coupled
#     and stay inline at their ADD lines — bump the checksum together with the
#     matching version ARG here.
#   * TINYTEX_TLNET_SNAPSHOT must track TINYTEX_VERSION (its tlmgr revision has to
#     match the bundle's — see the tinytex-builder stage).
# ----------------------------------------------------------------------------
ARG DOTNET_ASPNET_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0-resolute@sha256:0aa8645b7e7c83630bdb5ca096a91e2e4a356e9236ec1c01bb471a7410bb18a9
ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0-resolute@sha256:196f61c6262aa890f41dd654ff52d3b995187124a37778fb990ed0c31c26da44
ARG ALPINE_GIT_IMAGE=alpine/git:v2.52.0@sha256:4a0e72d49596a1f5d3701aeedafdadc5c0da4062be4657c7bdc4017387f591cc
ARG TINYTEX_VERSION=2026.05
ARG TINYTEX_TLNET_SNAPSHOT=2026/05/02
ARG QUARTO_VERSION=1.9.38
ARG ESBUILD_VERSION=0.27.3
# r-base-core anchor. Currently the transient r2u stopgap value (held at 4.6.0
# while r2u catches up to CRAN's 4.6.1 — see the r-runtime stage); becomes the
# deliberate R anchor once that skew heals. The r-cran-* packages are NOT pinned:
# r2u is a rolling current-CRAN mirror with no dated snapshots and no old-version
# retention, so a version pin can't be held — they track CRAN, and a deliberate
# validated rebuild is the reproducibility unit.
ARG RBASECORE_VERSION=4.6.0

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

FROM ${ALPINE_GIT_IMAGE} AS toolkit-source-github-branch
ARG REPORTS_REPO
ARG REPORTS_BRANCH
RUN git clone --recurse-submodules --depth 1 --branch "${REPORTS_BRANCH}" \
    "${REPORTS_REPO}" /toolkit && \
    rm -rf /toolkit/.git /toolkit/tools/po4a/.git

FROM ${ALPINE_GIT_IMAGE} AS toolkit-source-github-tag
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
FROM ${ALPINE_GIT_IMAGE} AS toolkit-source-workspace
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
# pipeline shape is upstream (no reimplementation drift). The wrapper imports the NeoIPC-Tools module, which
# requires PowerShell >= 7.6, so the base is the .NET SDK image — the source Microsoft now points to for
# PowerShell in Docker (the standalone powershell image is deprecated and caps at 7.5). sdk:10.0-resolute
# ships pwsh 7.6. Build-only stage, discarded.

FROM ${DOTNET_SDK_IMAGE} AS reports-builder
ARG DEBIAN_FRONTEND=noninteractive

# Force a valid UTF-8 locale for Perl / po4a (which otherwise warn and fall back to the "C" locale when the
# base image's locale isn't generated). C.UTF-8 is built into glibc (no locale-gen needed) and is UTF-8-aware.
ENV LC_ALL=C.UTF-8 LANG=C.UTF-8

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
    done; \
    # Drop Quarto's per-project scratch/cache dir if a dirty source tree (e.g. a
    # workspace-mode COPY of a working checkout where someone rendered a report
    # locally) carried one in. It is regenerated on every render and /toolkit is
    # made read-only in the final image, so a baked .quarto would symlink Quarto's
    # write targets (project-cache/deno-kv-file) to read-only storage and every
    # render would fail to open its project cache. The per-render symlink-forest
    # (QuartoReportProducer) excludes it too; this is the build-phase half.
    find /toolkit-output -type d -name .quarto -prune -exec rm -rf {} +

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

FROM ${DOTNET_ASPNET_IMAGE} AS r-runtime
ARG DEBIAN_FRONTEND=noninteractive
ARG RBASECORE_VERSION

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
    # r2u is a single university-hosted mirror that intermittently drops a .deb mid-download from CI runners
    # ("OpenSSL ... unexpected eof while reading"). Retry fetches and disable HTTP pipelining so a dropped
    # connection re-fetches the one file instead of failing the whole apt batch.
    printf 'Acquire::Retries "5";\nAcquire::http::Pipeline-Depth "0";\n' > /etc/apt/apt.conf.d/80-retries && \
    # Transient stopgap: boost r-base-core to the RBASECORE_VERSION anchor. CRAN's r-base-core 4.6.1 declares
    # `Breaks: r-cran-rlang (< 1.2.0-5~)` while r2u still serves rlang 1.2.0-1 — a CRAN-vs-r2u release lag that
    # makes the whole r-cran-* set unresolvable; 4.6.0 lacks that Breaks, so the install resolves. This is a
    # version-glob pin (priority 1001 only boosts a *present* 4.6.0): once r2u advances past it the glob matches
    # nothing and apt falls back to the current r-base-core — the build never breaks, it just holds R at 4.6.0
    # until removed. Drop this pin once r2u serves r-cran-rlang >= 1.2.0-5. (printf, not a COPY heredoc, so it
    # can consume the RBASECORE_VERSION ARG.)
    printf 'Package: r-base-core\nPin: version %s-*\nPin-Priority: 1001\n' "${RBASECORE_VERSION}" > /etc/apt/preferences.d/98-rbasecore-stopgap && \
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

# Variant: workspace mode installs NOTHING in the builder. neoipcr itself is not
# packaged here — the editable copy is COPYed into the final image at /neoipcr and
# read at render time via NEOIPCR_DEV_PATH (load_neoipcr -> devtools::load_all).
# devtools is installed in the FINAL workspace stage (base-neoipcr-workspace), not
# here: an apt `r-cran-devtools` (r2u binary) lands in /usr/lib/R/site-library,
# which the final image inherits in place but does NOT copy from this builder
# stage (it copies only /usr/local/lib/R/site-library), so a build-stage install
# would never reach the image. Installing it directly in the final stage avoids
# that — and uses the same proven r2u binary path as the rest of the R stack,
# rather than pak (which could source-compile devtools' heavy system deps on a
# brand-new Ubuntu when PPM lacks resolute binaries and the -dev headers are absent).
FROM r-builder-base AS r-builder-workspace

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
FROM ${DOTNET_ASPNET_IMAGE} AS tinytex-builder
ARG DEBIAN_FRONTEND=noninteractive
ARG TINYTEX_VERSION
ARG TINYTEX_TLNET_SNAPSHOT
RUN --mount=type=cache,target=/var/cache/apt,sharing=locked \
    --mount=type=cache,target=/var/lib/apt,sharing=locked \
    rm -f /etc/apt/apt.conf.d/docker-clean && \
    apt-get -qq update && \
    apt-get -qq install --no-install-recommends perl wget fontconfig xz-utils gnupg
# TinyTeX v2026.05 (published 2026-05-01) ships TeX Live 2026's tlmgr (texlive-scripts r78861). The install
# below pins tlmgr to the frozen tlnet snapshot whose tlmgr revision MATCHES this bundle's — the 2026-05-02
# archive (the 05-01 archive carries r78852, one revision too old, which tlmgr rejects as "older than local").
# When bumping this bundle, find the matching snapshot date the same way: tlmgr reports "older than local
# installation" if the pin is too old and "tlmgr itself needs to be updated" if it is too new.
ADD --chmod=644 --checksum=sha256:ef6c552b2959744f449213fb124e58954a235e7d41ad191e48080869728ae451 \
    https://github.com/rstudio/tinytex-releases/releases/download/v${TINYTEX_VERSION}/TinyTeX-1-linux-x86_64-v${TINYTEX_VERSION}.tar.xz \
    /tmp/TinyTeX.tar.xz
USER $APP_UID
# Pin tlmgr to the frozen tlnet snapshot dated to this bundle instead of the rolling tlnet.yihui.org mirror.
# The rolling mirror advances its tlmgr revision past the bundle's, so `tlmgr install` self-terminates
# demanding `tlmgr update --self`; the dated archive snapshot matches the bundle's tlmgr, so install just
# works — and being immutable it makes the installed package set reproducible build-to-build. gnupg (added
# above) lets tlmgr verify the snapshot's signature rather than warning "gpg unavailable".
RUN tar xf /tmp/TinyTeX.tar.xz -C $HOME && \
    ~/.TinyTeX/bin/x86_64-linux/tlmgr option repository https://texlive.info/tlnet-archive/${TINYTEX_TLNET_SNAPSHOT}/tlnet && \
    ~/.TinyTeX/bin/x86_64-linux/tlmgr install \
        anyfontsize babel babel-english babel-estonian babel-german \
        babel-greek babel-italian babel-spanish babel-turkish caption \
        colortbl fontawesome5 footnotehyper koma-script luatexbase \
        microtype multirow parskip pdfcol placeins \
        selnolig tcolorbox tikzfill upquote wrapfig \
        xurl

# ----------------------------------------------------------------------------
# Stage: Quarto (only the linux-amd64 binaries we need).
# ----------------------------------------------------------------------------
FROM ${DOTNET_ASPNET_IMAGE} AS quarto-downloader
ARG QUARTO_VERSION
ARG ESBUILD_VERSION
ADD --checksum=sha256:ea8c897368791ad9f200010c087ea3111b2e556b12a960487dd4e216902aa102 \
    https://github.com/quarto-dev/quarto-cli/releases/download/v${QUARTO_VERSION}/quarto-${QUARTO_VERSION}-linux-amd64.tar.gz \
    /tmp/quarto.tar.gz
ADD --checksum=sha256:066e20cdb882994160e18524a552b97e03648eb9aa0c7cdf5680a6493be65ab2 \
    https://registry.npmjs.org/@esbuild/linux-x64/-/linux-x64-${ESBUILD_VERSION}.tgz \
    /tmp/esbuild.tgz
# Replace Quarto's bundled esbuild with the pinned build, then drop only the tools we don't use.
# Keep tools/x86_64/deno — it is Quarto's runtime (the `quarto` launcher execs it for every command),
# so removing it breaks ALL rendering with exit 127. We render via LaTeX (not typst) and don't use
# Jupyter, so those two are safe to drop.
RUN mkdir -p /opt/quarto && \
    tar -zxf /tmp/quarto.tar.gz -C /opt/quarto --strip-components=1 && \
    tar -zxf /tmp/esbuild.tgz -C /tmp && \
    cp /tmp/package/bin/esbuild /opt/quarto/bin/tools/x86_64/esbuild && \
    rm -rf /opt/quarto/bin/tools/x86_64/typst* \
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
    # Pull Ubuntu (resolute) security updates into the runtime image for packages
    # inherited from r-runtime / the aspnet base — gnutls28, libgcrypt20,
    # xz-utils, expat, cairo, python3, etc. — so it ships the latest
    # resolute-patched versions rather than the base image's snapshot.
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
#   packagekit, policykit-1              - desktop/D-Bus tooling, no daemon here
#   elfutils                              - eu-* dev tools (libelf1/libdw1 kept)
#   software-properties-common           - build-only (add-apt-repository)
#   python3-cryptography, python3-jwt     - Python libs the .NET service never calls
# Do NOT add xdg-utils here: r-base-core hard-Depends on it (for xdg-open behind
# browseURL), so `apt purge xdg-utils` cascades out the entire R stack
# (r-base-core + every r-cran-* package), leaving an image with R's site-library
# but no R runtime — every report render then fails with "Failed to spawn
# 'Rscript'". It is desktop tooling we never invoke, but apt will not keep R
# installed without it, so it stays.
# Named purge only — NO `autoremove`. A blanket `autoremove --purge` here also
# sweeps fontconfig (whose /etc/fonts/conf.d the next step writes into, and
# which the font-rendering path needs), so we remove only these targets and
# leave their orphaned deps in place. The python3 interpreter therefore stays —
# patched by the apt upgrade above, and not reachable from any runtime path.
RUN --mount=type=cache,target=/var/cache/apt,sharing=locked \
    --mount=type=cache,target=/var/lib/apt,sharing=locked \
    apt-get -qq purge -y \
        packagekit policykit-1 elfutils \
        software-properties-common python3-cryptography python3-jwt

# Runtime layout convention (matches the per-render layout in
# QuartoReportProducer.cs — see ReportingOptions.ReportsSourceDir):
#   /toolkit/                 (toolkit root — read-only, owned by root)
#     glossary*.yaml
#     reports/
#       common*.yaml
#       common/, filters/, logos/    (shared sibling dirs)
#       <Report>/                    (one dir per report)
COPY --from=reports-builder /toolkit-output /toolkit

# Bring whatever r-builder added on top of r-runtime into the final image: in the
# github-branch / github-tag modes that is neoipcr (pak-installed into the builder's
# site-library); in workspace mode r-builder adds nothing — neoipcr is the editable
# /neoipcr copy and devtools is installed in base-neoipcr-workspace below. Most of the
# site-library is already populated from r-runtime; this COPY overlays those additions.
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
ARG DEBIAN_FRONTEND=noninteractive
USER root
# devtools is needed only in workspace mode (load_neoipcr -> devtools::load_all of
# the editable /neoipcr). Installed here, in the final image, as an r2u binary so it
# lands in /usr/lib/R/site-library (on R's .libPaths) directly — the r2u apt repo is
# already configured from the r-runtime stage. Doing it here (not in r-builder-workspace)
# is deliberate: apt installs to /usr/lib, which the final image does NOT copy from the
# builder, so a build-stage install would silently vanish.
RUN --mount=type=cache,target=/var/cache/apt,sharing=locked \
    --mount=type=cache,target=/var/lib/apt,sharing=locked \
    rm -f /etc/apt/apt.conf.d/docker-clean && \
    apt-get -qq update && \
    apt-get -qq install --no-install-recommends r-cran-devtools
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

FROM ${DOTNET_SDK_IMAGE} AS build
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
