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
#   * The TinyTeX / Quarto / EB Garamond / Noto licence `ADD --checksum` values are version-coupled
#     and stay inline at their ADD lines — bump the checksum together with the
#     matching version ARG here. The Noto Sans fonts are pinned by their commit alone.
#   * TINYTEX_TLNET_SNAPSHOT must track TINYTEX_VERSION: the archive day has to fall
#     inside the window tlmgr accepts for the bundle — see the tinytex-builder stage.
# ----------------------------------------------------------------------------
ARG DOTNET_ASPNET_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0-resolute@sha256:e12b240891f34144edd813a11e86649dca6120165adfb5ad0a29bbde6753a975
ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0-resolute@sha256:17d5b93701079599eb771cf72f2aaa45a5826d9802f2cfbae565fd2eecf72073
ARG ALPINE_GIT_IMAGE=alpine/git:v2.54.0@sha256:6f3b5029566da8e90b24945933dcd806be866b64b1e706f51828bf84faccf21b
ARG TINYTEX_VERSION=2026.09
ARG TINYTEX_TLNET_SNAPSHOT=2026/08/31
ARG QUARTO_VERSION=1.10.18
# EB Garamond from the EBGaramond12 repository (github.com/octaviopardo/EBGaramond12, SIL OFL
# 1.1), a commit of that repository: it has no releases.
ARG EBGARAMOND_COMMIT=106a4a6d377987459ae5e68673a4570f13b957fb
# Every Noto Sans family as static OTFs from the Noto project's font distribution
# (github.com/notofonts/notofonts.github.io), and the SIL OFL 1.1 text from the Noto Sans source
# repository (github.com/notofonts/latin-greek-cyrillic): commits of each, since the distribution
# has no releases of its own.
ARG NOTOSANS_COMMIT=1eb09fb315562c7e07d738691870e52c39b45b39
ARG NOTOSANS_LICENCE_COMMIT=023d7b73d5c1a5ed6489bd04a120244c1f2bff3f
# R (r-base-core) and the r-cran-* packages are deliberately NOT pinned in this inventory:
# r2u is a rolling current-CRAN mirror with no dated snapshots and no old-version retention,
# so a version pin can't be held — they track CRAN, and a deliberate validated rebuild is the
# reproducibility unit. R is installed in the r-runtime stage below.

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
ARG NEOIPCR_REPO=NeoIPC/neoipcr
ARG NEOIPCR_BRANCH=main
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
    # locally) carried one in. It is regenerated on every render and is never a
    # source input; QuartoReportProducer leaves it out of every render's workdir
    # as well, and this is the build-phase half.
    find /toolkit-output -type d -name .quarto -prune -exec rm -rf {} +

# ----------------------------------------------------------------------------
# Stage: r-runtime — shared parent for r-builder-base and the final image's
# font-runtime and base stages. Carries everything both paths need: R + the binary CRAN
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
        # glue is named even though tidyverse pulls it in, because it does so only TRANSITIVELY: glue is
        # not a tidyverse member package, it is a dependency of several of them. The reports call it
        # directly and depend on a specific capability of it — glue_safe(), which is what keeps a
        # translated string from being evaluated as R at render time — so leaving it to arrive by
        # accident of someone else's dependency graph is the wrong footing for a security property.
        r-cran-glue \
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
        r-cran-logger \
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
# TinyTeX v2026.09 (published 2026-09-01) carries texlive.infra r79982, which ships tlmgr, and texlive-scripts
# r80134. tlmgr bounds the repository it accepts on those two packages: one whose texlive-scripts is older
# than the local one is refused as "older than the local installation", and `install` stops when
# texlive.infra is newer there ("tlmgr itself needs to be updated" — on Linux texlive.infra is the one
# package tlmgr treats as critical). 2026-08-31 is the earliest archive day whose texlive-scripts is not
# older than the bundle's (08-30 still carries r80127), and its texlive.infra equals the bundle's; the
# earliest such day keeps the packages installed here closest to the bundle's own. When bumping the bundle,
# read `revision` under `name texlive-scripts` and `name texlive.infra` in its tlpkg/texlive.tlpdb and in
# the archive's tlpkg/texlive.tlpdb.xz for the days around the release, and pin the earliest day whose
# texlive-scripts is >= the bundle's while its texlive.infra is <= the bundle's.
ADD --chmod=644 --checksum=sha256:cf9a4d19742eeb6d54a3de91fb5df071360f23877cafa5b39893421b32ca6295 \
    https://github.com/rstudio/tinytex-releases/releases/download/v${TINYTEX_VERSION}/TinyTeX-1-linux-x86_64-v${TINYTEX_VERSION}.tar.xz \
    /tmp/TinyTeX.tar.xz
USER $APP_UID
# Pin tlmgr to the frozen tlnet snapshot chosen above instead of the rolling tlnet.yihui.org mirror. The
# rolling mirror eventually advances texlive.infra past the bundle's, from which point `tlmgr install`
# self-terminates demanding `tlmgr update --self`; the dated archive stays inside the window tlmgr accepts,
# so install just works — and being immutable it makes the installed package set reproducible build-to-build.
# gnupg (added above) lets tlmgr verify the snapshot's signature rather than warning "gpg unavailable".
# Archival PDF (PDF/A-4) support for the reports' `pdf-standard`, which Quarto emits as
# \DocumentMetadata{pdfstandard={a-4}, xmp=true} on LuaLaTeX. Note there is no `tagging=on`: Quarto adds
# that only for the standards that require tagged structure (a-2a, a-3a, ua-1, ua-2), and the reports
# deliberately declare a-4 alone because KOMA-Script cannot produce tagged structure — see the tripwire in
# ReportLogDrain.cs. The tagging-stack packages are still needed: \DocumentMetadata pulls them in whether or
# not tagging is active, and they are what a tagging-capable class would later require. latex-lab provides
# documentmetadata-support.ltx (without it: "No support files for \DocumentMetadata found"); pdfmanagement
# (renamed from the now-empty pdfmanagement-testphase stub) provides pdfmanagement-init.sty; tagpdf the
# low-level tag engine; luamml the math→MathML tagging (loaded only when tagging is active, so an a-4-only
# compile logs "luamml has been disabled"); colorprofiles the ICC output intent PDF/A needs. ALL must be
# present at build time — the runtime image's minimal Perl (perl-base, no File::Find) cannot run tlmgr, so
# there is deliberately no render-time auto-install fallback.
RUN tar xf /tmp/TinyTeX.tar.xz -C $HOME && \
    ~/.TinyTeX/bin/x86_64-linux/tlmgr option repository https://texlive.info/tlnet-archive/${TINYTEX_TLNET_SNAPSHOT}/tlnet && \
    ~/.TinyTeX/bin/x86_64-linux/tlmgr install \
        anyfontsize babel babel-english babel-estonian babel-german \
        babel-greek babel-italian babel-spanish babel-turkish caption \
        colorprofiles colortbl fontawesome5 footnotehyper koma-script latex-lab \
        luamml luatexbase microtype multirow parskip pdfcol pdfmanagement placeins \
        selnolig tagpdf tcolorbox tikzfill upquote wrapfig \
        xurl

# ----------------------------------------------------------------------------
# Stage: Quarto (only the linux-amd64 binaries we need).
# ----------------------------------------------------------------------------
FROM ${DOTNET_ASPNET_IMAGE} AS quarto-downloader
ARG QUARTO_VERSION
ADD --checksum=sha256:afad071b5bd22c02f2d300695743189d3650e0537a53073e654b630cff2b0c73 \
    https://github.com/quarto-dev/quarto-cli/releases/download/v${QUARTO_VERSION}/quarto-${QUARTO_VERSION}-linux-amd64.tar.gz \
    /tmp/quarto.tar.gz
# Drop only the tools we don't use. Keep tools/x86_64/deno — it is Quarto's runtime (the `quarto`
# launcher execs it for every command), so removing it breaks ALL rendering with exit 127. We render
# via LaTeX (not typst) and don't use Jupyter, so those two are safe to drop.
RUN mkdir -p /opt/quarto && \
    tar -zxf /tmp/quarto.tar.gz -C /opt/quarto --strip-components=1 && \
    rm -rf /opt/quarto/bin/tools/x86_64/typst* \
           /opt/quarto/share/jupyter

# ----------------------------------------------------------------------------
# Stage: EB Garamond (the four faces the Partner and Reference Reports set, and
# the licence that goes with them).
# ----------------------------------------------------------------------------
# Debian's fonts-ebgaramond is the unfinished original release, whose bold face lacks the subscript
# digits, ≥, and −, so a bold Q₁ table header renders as boxes with it; the completed static OTFs of
# the EBGaramond12 repository carry them in every face. The reports use the regular, italic, bold, and
# bold italic faces of the family "EB Garamond". The SIL OFL requires its text and the copyright
# notice to accompany every copy of the fonts; a published image is a copy, so the repository's
# OFL.txt ships beside them.
FROM scratch AS ebgaramond-downloader
ARG EBGARAMOND_COMMIT
ADD --chmod=644 --checksum=sha256:8d618a6bc6e1f4444e1990c39770a62ccbc29394e57afcc58acd530c16489275 \
    https://raw.githubusercontent.com/octaviopardo/EBGaramond12/${EBGARAMOND_COMMIT}/fonts/otf/EBGaramond-Regular.otf \
    /ebgaramond/EBGaramond-Regular.otf
ADD --chmod=644 --checksum=sha256:296feee09f6fa157f913cf3d75460b5e282b3c43dff4ddffbcc5c1a924f4fd14 \
    https://raw.githubusercontent.com/octaviopardo/EBGaramond12/${EBGARAMOND_COMMIT}/fonts/otf/EBGaramond-Italic.otf \
    /ebgaramond/EBGaramond-Italic.otf
ADD --chmod=644 --checksum=sha256:2832055876314e4932c46809e919a45fd726a53faa6bfae53be9628723bde90f \
    https://raw.githubusercontent.com/octaviopardo/EBGaramond12/${EBGARAMOND_COMMIT}/fonts/otf/EBGaramond-Bold.otf \
    /ebgaramond/EBGaramond-Bold.otf
ADD --chmod=644 --checksum=sha256:c010c63d6924e0dec2fedc1b0cb57e86ae9047bb0e9651ea603b0967d6e3072e \
    https://raw.githubusercontent.com/octaviopardo/EBGaramond12/${EBGARAMOND_COMMIT}/fonts/otf/EBGaramond-BoldItalic.otf \
    /ebgaramond/EBGaramond-BoldItalic.otf
ADD --chmod=644 --checksum=sha256:058611bd968817d532cedeab6acaa055e882d365a88523df00e7917ad7a0f704 \
    https://raw.githubusercontent.com/octaviopardo/EBGaramond12/${EBGARAMOND_COMMIT}/OFL.txt \
    /ebgaramond/OFL.txt

# ----------------------------------------------------------------------------
# Stage: every Noto Sans family as static OTFs in the Compact Font Format (CFF) flavour (the faces the
# figures of the Partner and Reference Reports set or fall back to, and the licence that goes with
# them).
# ----------------------------------------------------------------------------
# The reports draw their PDF figures with the Cairo device, which embeds a TrueType font's glyphs
# outside WinAnsi as a CID-keyed TrueType font without the CIDToGIDMap entry PDF/A-4 requires (ISO
# 32000-2, 9.7.4, Table 115), while it embeds a CFF-flavoured font as CFF, which the requirement
# does not cover. fonts-noto-core ships the Noto families as TrueType only, so these OTFs replace
# its Noto Sans families for fontconfig (see the font-runtime stage): Noto Sans in its four faces, and every
# other Noto Sans family in its regular face and, where it has one, its bold face; they carry the
# scripts and symbols Noto Sans lacks. The UI variants and the test font are left out. The stage
# fetches the tree at the distribution's commit without file contents, selects those files by
# pathspec, and downloads only their contents, about 17 MB; the commit pins them as a checksum pins
# a download. Each font carries its copyright notice and SIL OFL licence description in its
# metadata, and the licence text, the same for every Noto family, ships beside them, as for EB
# Garamond.
FROM ${ALPINE_GIT_IMAGE} AS notosans-downloader
ARG NOTOSANS_COMMIT
ARG NOTOSANS_LICENCE_COMMIT
WORKDIR /distribution
RUN <<'EOF'
set -eu
git init -q .
git remote add origin https://github.com/notofonts/notofonts.github.io.git
git fetch -q --depth 1 --filter=blob:none origin "$NOTOSANS_COMMIT"
git read-tree FETCH_HEAD
set -- \
    ':(glob)fonts/NotoSans*/unhinted/otf/NotoSans*-Regular.otf' \
    ':(glob)fonts/NotoSans*/unhinted/otf/NotoSans*-Bold.otf' \
    'fonts/NotoSans/unhinted/otf/NotoSans-Italic.otf' \
    'fonts/NotoSans/unhinted/otf/NotoSans-BoldItalic.otf' \
    ':(exclude,glob)fonts/NotoSansTest/**' \
    ':(exclude,glob)fonts/*UI/**' \
    ':(exclude,glob)fonts/*/unhinted/otf/*UI-*.otf'
git fetch -q origin $(git ls-files -s -- "$@" | cut -d' ' -f2)
git checkout-index -- $(git ls-files -- "$@")
mkdir /notosans
find fonts -name '*.otf' -exec cp {} /notosans/ \;
chmod 644 /notosans/*.otf
EOF
ADD --chmod=644 --checksum=sha256:cee9892f9f0cc8fe882c9e9537ee6a89621d86ee7ceaf70b02e2b2b1c25c061a \
    https://raw.githubusercontent.com/notofonts/latin-greek-cyrillic/${NOTOSANS_LICENCE_COMMIT}/OFL.txt \
    /notosans/OFL.txt

# ----------------------------------------------------------------------------
# Stage: font-runtime (security updates + locales + fonts + their fontconfig rules).
# Inherits R + the binary CRAN packages from r-runtime. It stands apart from base so that the
# pull-request CI builds it, and with it the checks below that keep the figures' fonts CFF, without
# the toolkit, neoipcr, Quarto, and TinyTeX that base adds and no pull-request job builds.
# ----------------------------------------------------------------------------
FROM r-runtime AS font-runtime
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
        fonts-noto-core \
        locales && \
    locale-gen de_DE.UTF-8 el_GR.UTF-8 en_GB.UTF-8 es_ES.UTF-8 et_EE.UTF-8 it_IT.UTF-8 tr_TR.UTF-8  # keep in sync with LocaleResolver.DefaultTerritories — an ungenerated locale crashes lualatex/tlmgr

COPY --from=ebgaramond-downloader /ebgaramond/ /usr/share/fonts/opentype/ebgaramond/
COPY --from=notosans-downloader /notosans/ /usr/share/fonts/opentype/notosans/
# Noto Sans falls back to Noto fonts in their CFF builds. fontconfig would otherwise choose between
# the two builds of a family by its own order, and fall back from Noto Sans to DejaVu Sans, also
# TrueType. So it leaves out every TrueType Noto Sans family, and DejaVu, which fontconfig's own
# packages bring and nothing here sets, each named by family and font format. A rule naming their
# files would not hold: fontconfig strips the working directory from the front of a path glob, so
# the glob stops matching for a process run from a parent directory of the fonts, the root among
# them. Noto Sans falls back first to Noto Sans Math, Noto Sans Symbols, and Noto Sans Symbols 2,
# bound as strongly as the family a figure names, so that ≥ always comes from Math; then to every
# other Noto Sans family, bound weakly and placed ahead of the families the distribution's own rules
# add for sans-serif. Each alias inserts its families directly after Noto Sans, so the strongly
# bound one comes last in the file to come first in the list. fontconfig ranks a font that supports
# the requested language between the two bindings, and Pango requests each run of text in its own
# language, derived from the run's script when the render's locale does not cover it, so a script is
# drawn from its own family: Nepali's danda from Noto Sans Devanagari, although Noto Sans Bengali has
# one too. Math's Arabic letters would outrank Noto Sans Arabic, so they leave its character set.
# Under C.UTF-8, which names no language, the order of the weak families decides instead. EB
# Garamond, also CFF, supplies the few characters no installed Noto font carries. Both lists are
# generated from the installed fonts, so they follow NOTOSANS_COMMIT and the distribution's packages,
# and the build, which runs from the root, fails if a Noto Sans family is still reachable in another
# build or ≥ comes from another font.
# Not covered: the scripts that only other Noto families carry (Noto Serif Tibetan, Ahom, Tangut,
# Dogra, Yezidi, and Nyiakeng Puachue Hmong; Noto Music; Noto Traditional Nushu; Noto Looped Thai)
# still come from fonts-noto-core's TrueType builds and would break PDF/A-4, and Chinese, Japanese,
# and Korean have no font, so a figure draws them as boxes. A language whose script needs one of
# these families adds the family's OTF to the fetch above when the language is added. The generic
# monospace and serif families resolve to the TrueType Noto Mono and Noto Serif, which no figure
# sets.
RUN <<'EOF'
set -eu
fonts=/usr/share/fonts/opentype/notosans
families=$(fc-scan --format '%{family[0]}\n' "$fonts" | LC_ALL=C sort -u)
rejected=$(fc-scan --format '%{family[0]}\n' /usr/share/fonts/truetype/noto /usr/share/fonts/truetype/dejavu \
    | grep -E '^(Noto Sans|DejaVu )' | LC_ALL=C sort -u)
case "$families$rejected" in
    *'<'* | *'&'*) echo "a font family name would break the generated XML" >&2; exit 1 ;;
esac
{
    cat <<'XML'
<?xml version="1.0"?>
<!DOCTYPE fontconfig SYSTEM "urn:fontconfig:fonts.dtd">
<fontconfig>
  <selectfont>
    <rejectfont>
XML
    printf '%s\n' "$rejected" \
        | sed 's|.*|      <pattern><patelt name="family"><string>&</string></patelt><patelt name="fontformat"><string>TrueType</string></patelt></pattern>|'
    cat <<'XML'
    </rejectfont>
  </selectfont>
  <match target="scan">
    <test name="family"><string>Noto Sans Math</string></test>
    <edit name="charset" mode="assign">
      <minus>
        <name>charset</name>
        <charset><range><int>0x0600</int><int>0x06FF</int></range></charset>
      </minus>
    </edit>
  </match>
  <alias>
    <family>Noto Sans</family>
    <accept>
XML
    printf '%s\n' "$families" \
        | grep -vxF -e 'Noto Sans' -e 'Noto Sans Math' -e 'Noto Sans Symbols' -e 'Noto Sans Symbols 2' \
        | sed 's|.*|      <family>&</family>|'
    cat <<'XML'
    </accept>
  </alias>
  <alias binding="same">
    <family>Noto Sans</family>
    <accept>
      <family>Noto Sans Math</family>
      <family>Noto Sans Symbols</family>
      <family>Noto Sans Symbols 2</family>
    </accept>
  </alias>
</fontconfig>
XML
} > /etc/fonts/conf.d/70-neoipc-noto-sans-otf.conf
fc-cache -f /usr/share/fonts/opentype/ebgaramond "$fonts"
if fc-list -f '%{family[0]}|%{file}\n' | grep '^Noto Sans' | grep -v "|$fonts/"; then
    echo "a Noto Sans family is reachable outside $fonts" >&2; exit 1
fi
if fc-list -f '%{family[0]}\n' | grep '^DejaVu'; then
    echo "DejaVu is reachable" >&2; exit 1
fi
if [ "$(fc-match -f '%{file}' 'Noto Sans:lang=en:charset=2265')" != "$fonts/NotoSansMath-Regular.otf" ]; then
    echo "≥ no longer comes from Noto Sans Math" >&2; exit 1
fi
EOF

# ----------------------------------------------------------------------------
# Stage: runtime base (Quarto + TinyTeX + toolkit + neoipcr).
# Inherits R, the locales, and the fonts from font-runtime; adds only what's
# unique to the final image.
# ----------------------------------------------------------------------------
FROM font-runtime AS base
ARG DEBIAN_FRONTEND=noninteractive

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
# sweeps fontconfig, which the font-rendering path needs, so we remove only
# these targets and leave their orphaned deps in place. The python3
# interpreter therefore stays — patched by the apt upgrade above, and not
# reachable from any runtime path.
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
    ln -s /opt/quarto/bin/tools/x86_64/pandoc /usr/local/bin/pandoc
COPY --from=tinytex-builder --chown=$APP_UID /home/app/.TinyTeX /home/app/.TinyTeX
ENV PATH="/home/app/.TinyTeX/bin/x86_64-linux:$PATH"

# Lock down /toolkit (read-only at runtime). The render path only reads from
# here — it copies the rendered report's directory into its workdir and
# symlinks the shared resources — so nothing should be writing to /toolkit.
RUN chmod -R a-w /toolkit

# Pre-create the reporting file-storage roots owned by the app user so a named
# volume mounted here (compose persistence for reference datasets + the
# validation-exception file) is seeded with app ownership. Docker copies an
# image directory's ownership into a fresh empty volume on first mount; without
# these dirs existing in the image the volume mountpoint is created root-owned
# and the non-root app (USER $APP_UID) gets EACCES on every upload. Paths match
# ReportingOptions ReferenceDataDir / ValidationExceptionsDir.
RUN mkdir -p /home/app/NeoIPC/ReferenceData /home/app/NeoIPC/ValidationExceptions && \
    chown -R $APP_UID /home/app/NeoIPC

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
# Directory.Build.props carries <Version>, and MSBuild applies it to every project below it. Copied
# with the project files rather than arriving with `COPY . .` below, so restore and publish evaluate
# the same properties — and so a version change invalidates this layer, which is correct: the
# published assembly embeds it.
COPY ["Directory.Build.props", "./"]
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
# MIT requires the copyright and permission notice to accompany every copy of the
# software; a published image is a copy, so ship the licence inside it.
COPY LICENSE /app/LICENSE
ENTRYPOINT ["dotnet", "NeoIPC.Reporting.dll"]
