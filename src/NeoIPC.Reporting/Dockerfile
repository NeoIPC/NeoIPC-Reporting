FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS r-builder
ADD --chown=1 --chmod=644 --checksum=sha256:ffa734347fd95fd7393f575448ba5e52cf3137067db77224d4857863351e4e8f https://cloud.r-project.org/bin/linux/ubuntu/marutter_pubkey.asc /etc/apt/trusted.gpg.d/cran_ubuntu_key.asc
RUN apt-get -qq update && \
    apt-get -qq install --no-install-recommends \
        software-properties-common \
        dirmngr && \
    add-apt-repository -y "deb https://cloud.r-project.org/bin/linux/ubuntu $(lsb_release -cs)-cran40/" && \
    apt-get -qq update && \
    apt-get -qq install --no-install-recommends \
        r-base-core \
        build-essential \
        gfortran \
        libblas-dev \
        liblapack-dev && \
    rm -rf /var/lib/apt/lists/* && \
    apt-get -qq clean
RUN Rscript -e 'install.packages("pak", repos = sprintf("https://r-lib.github.io/p/pak/stable/%s/%s/%s", .Platform$pkgType, R.Version()$os, R.Version()$arch));pak::pkg_install(c("argparse","rmarkdown","knitr","tidyverse","smplot2","scales","patchwork","gt","Brar/neoipcr@TowardsReferenceReport"), ask = FALSE);' > /dev/null && \
    find /usr/local/lib/R/site-library -type d \( -name doc -o -name help -o -name html -o -name examples -o -name demo \) -exec rm -rf {} + 2>/dev/null; \
    find /usr/local/lib/R/site-library -name "*.so" -exec strip --strip-debug {} + 2>/dev/null; \
    true

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS tinytex-builder
RUN apt-get -qq update && \
    apt-get -qq install --no-install-recommends perl wget fontconfig && \
    rm -rf /var/lib/apt/lists/*
ADD --chmod=644 --checksum=sha256:c990efa116f65db5c501b2a7fa5cf94a8fb70ca5b925f6c8e667e4a1b2b4d2b6 https://github.com/rstudio/tinytex-releases/releases/download/v2026.03.02/TinyTeX-1-v2026.03.02.tar.gz /tmp/TinyTeX.tar.gz
USER $APP_UID
RUN tar xf /tmp/TinyTeX.tar.gz -C $HOME && \
    ~/.TinyTeX/bin/x86_64-linux/tlmgr install babel babel-english babel-estonian babel-german babel-greek babel-italian babel-spanish babel-turkish koma-script anyfontsize multirow placeins tcolorbox fontawesome5 colortbl wrapfig tikzfill pdfcol caption

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS quarto-downloader
ADD --checksum=sha256:bdf689b5589789a1f21d89c3b83d78ed02a97914dd702e617294f2cc1ea7387d https://github.com/quarto-dev/quarto-cli/releases/download/v1.8.27/quarto-1.8.27-linux-amd64.tar.gz /tmp/quarto.tar.gz
ADD --checksum=sha256:066e20cdb882994160e18524a552b97e03648eb9aa0c7cdf5680a6493be65ab2 https://registry.npmjs.org/@esbuild/linux-x64/-/linux-x64-0.27.3.tgz /tmp/esbuild.tgz
RUN mkdir -p /opt/quarto && \
    tar -zxf /tmp/quarto.tar.gz -C /opt/quarto --strip-components=1 && \
    tar -zxf /tmp/esbuild.tgz -C /tmp && \
    cp /tmp/package/bin/esbuild /opt/quarto/bin/tools/x86_64/esbuild && \
    rm -rf /opt/quarto/bin/tools/x86_64/deno* \
           /opt/quarto/bin/tools/x86_64/typst* \
           /opt/quarto/share/jupyter

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
ADD --chown=1 --chmod=644 --checksum=sha256:ffa734347fd95fd7393f575448ba5e52cf3137067db77224d4857863351e4e8f https://cloud.r-project.org/bin/linux/ubuntu/marutter_pubkey.asc /etc/apt/trusted.gpg.d/cran_ubuntu_key.asc
ADD https://github.com/NeoIPC/Surveillance-Toolkit.git#main:reports /reports
RUN apt-get -qq update && \
    apt-get -qq install --no-install-recommends \
        software-properties-common \
        dirmngr && \
    add-apt-repository -y "deb https://cloud.r-project.org/bin/linux/ubuntu $(lsb_release -cs)-cran40/" && \
    apt-get -qq update && \
    apt-get -qq install --no-install-recommends \
        r-base-core \
        fonts-ebgaramond \
        fonts-ebgaramond-extra \
        fonts-noto-core \
        locales && \
    locale-gen de_DE.UTF-8 el_GR.UTF-8 en_GB.UTF-8 es_ES.UTF-8 et_EE.UTF-8 it_IT.UTF-8 tr_TR.UTF-8 && \
    apt-get -qq purge --auto-remove software-properties-common dirmngr && \
    rm -rf /var/lib/apt/lists/* && \
    apt-get -qq clean
COPY --from=r-builder /usr/local/lib/R/site-library /usr/local/lib/R/site-library
COPY --from=quarto-downloader /opt/quarto /opt/quarto
RUN ln -s /opt/quarto/bin/quarto /usr/local/bin/quarto && \
    ln -s /opt/quarto/bin/tools/x86_64/pandoc /usr/local/bin/pandoc && \
    printf '<fontconfig>\n  <selectfont>\n      <rejectfont>\n          <glob>/usr/share/fonts/woff/*</glob>\n      </rejectfont>\n  </selectfont>\n</fontconfig>\n' > /etc/fonts/conf.d/70-no-woff.conf
COPY --from=tinytex-builder --chown=$APP_UID /home/app/.TinyTeX /home/app/.TinyTeX
ENV PATH="/home/app/.TinyTeX/bin/x86_64-linux:$PATH"
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/NeoIPC.Reporting/NeoIPC.Reporting.csproj", "src/NeoIPC.Reporting/"]
RUN dotnet restore "./src/NeoIPC.Reporting/NeoIPC.Reporting.csproj"
COPY . .
WORKDIR "/src/src/NeoIPC.Reporting"

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./NeoIPC.Reporting.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "NeoIPC.Reporting.dll"]
