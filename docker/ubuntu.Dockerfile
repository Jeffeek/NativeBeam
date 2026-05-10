# syntax=docker/dockerfile:1.7
# Multi-stage build that produces a Native AOT binary of NativeBeam and runs
# it on a minimal Ubuntu runtime alongside system Chromium.

# ----------------------------------------------------------------------------
# Stage 1 - build & AOT-publish.
# `runtime-deps` images don't have the AOT toolchain; the SDK image does, but
# we need clang + zlib1g-dev for ILCompiler to link the native binary.
# ----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

RUN apt-get update \
 && apt-get install -y --no-install-recommends clang zlib1g-dev \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /src
COPY . .

RUN dotnet restore samples/NativeBeam.Demo/NativeBeam.Demo.csproj \
 && dotnet publish samples/NativeBeam.Demo/NativeBeam.Demo.csproj \
        -c Release \
        -r linux-x64 \
        /p:PublishAot=true \
        -o /app/publish

# ----------------------------------------------------------------------------
# Stage 2 - runtime. `runtime-deps` is the smallest .NET base; for AOT we do
# not need the managed runtime layer at all, only its native dependencies.
# ----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS runtime

# Chromium + the system libraries it needs to render headless. fonts-liberation
# covers the common metric-compatible substitutions; add fonts-noto-cjk /
# fonts-noto-color-emoji as your content requires.
RUN apt-get update \
 && apt-get install -y --no-install-recommends \
        chromium-browser \
        ca-certificates \
        fonts-liberation \
        fonts-noto-color-emoji \
        libnss3 \
        libxss1 \
        libgbm1 \
        libasound2 \
 && rm -rf /var/lib/apt/lists/*

# Run as non-root. Chromium refuses --no-sandbox by default in some distros;
# providing a real user removes the need for it.
RUN useradd --create-home --shell /usr/sbin/nologin app
USER app
WORKDIR /home/app

ENV CHROME_PATH=/usr/bin/chromium-browser

COPY --from=build --chown=app:app /app/publish/NativeBeam.Demo /home/app/NativeBeam.Demo

ENTRYPOINT ["/home/app/NativeBeam.Demo"]
