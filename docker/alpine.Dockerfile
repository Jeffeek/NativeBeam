# syntax=docker/dockerfile:1.7
# Alpine variant. Smaller image, but you must publish for `linux-musl-x64`
# (or `linux-musl-arm64`) because Alpine ships musl libc, not glibc.

# ----------------------------------------------------------------------------
# Stage 1 - build & AOT-publish for musl.
# ----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build

# clang + LLD + zlib + the basic compiler toolchain are required by ILCompiler.
RUN apk add --no-cache clang lld build-base zlib-dev

WORKDIR /src
COPY . .

RUN dotnet restore samples/NativeBeam.Demo/NativeBeam.Demo.csproj \
 && dotnet publish samples/NativeBeam.Demo/NativeBeam.Demo.csproj \
        -c Release \
        -r linux-musl-x64 \
        /p:PublishAot=true \
        -o /app/publish

# ----------------------------------------------------------------------------
# Stage 2 - runtime. Plain Alpine is fine because the AOT binary is fully
# self-contained; we only need Chromium and its shared libraries.
# ----------------------------------------------------------------------------
FROM alpine:3.20 AS runtime

# Alpine packages Chromium directly. ttf-freefont covers the basic
# Liberation-equivalent fonts; font-noto-emoji is optional.
RUN apk add --no-cache \
        chromium \
        ca-certificates \
        ttf-freefont \
        font-noto-emoji \
        nss \
        freetype \
        harfbuzz

RUN adduser -D -s /sbin/nologin app
USER app
WORKDIR /home/app

ENV CHROME_PATH=/usr/bin/chromium-browser

COPY --from=build --chown=app:app /app/publish/NativeBeam.Demo /home/app/NativeBeam.Demo

ENTRYPOINT ["/home/app/NativeBeam.Demo"]
