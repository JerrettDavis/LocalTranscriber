# syntax=docker/dockerfile:1.7

# ============================================================================
# LocalTranscriber Dockerfile
# Supports: CPU-only (default) and CUDA GPU acceleration
# ============================================================================
# Build args:
#   VARIANT: "cpu" (default) or "cuda" for GPU support
#   DOTNET_VERSION: .NET SDK version (default: 10.0)
# ============================================================================

ARG DOTNET_VERSION=10.0
ARG VARIANT=cpu

# ----------------------------------------------------------------------------
# Base: .NET SDK for building
# ----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build-base
WORKDIR /src

# Install nbgv for versioning
RUN dotnet tool install -g nbgv
ENV PATH="$PATH:/root/.dotnet/tools"

# Copy solution and project files for layer caching
COPY *.sln ./
COPY Directory.Build.props ./
COPY version.json ./
COPY global.json ./
COPY src/*/*.csproj ./src/

# Restructure project files
RUN for file in src/*.csproj; do \
        dir=$(basename "$file" .csproj); \
        mkdir -p "src/$dir"; \
        mv "$file" "src/$dir/"; \
    done

# Restore dependencies
RUN dotnet restore

# Copy everything
COPY . .

# ----------------------------------------------------------------------------
# Build stage
# ----------------------------------------------------------------------------
FROM build-base AS build
ARG CONFIGURATION=Release
RUN dotnet build -c $CONFIGURATION --no-restore

# ----------------------------------------------------------------------------
# Publish CLI
# ----------------------------------------------------------------------------
FROM build AS publish-cli
ARG CONFIGURATION=Release
WORKDIR /src/src/LocalTranscriber.Cli
RUN dotnet publish -c $CONFIGURATION -o /app/cli --no-build

# ----------------------------------------------------------------------------
# Publish Web
# ----------------------------------------------------------------------------
FROM build AS publish-web
ARG CONFIGURATION=Release
WORKDIR /src/src/LocalTranscriber.Web
RUN dotnet publish -c $CONFIGURATION -o /app/web --no-build

# ----------------------------------------------------------------------------
# Runtime: CPU variant
# ----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime-cpu

# Install audio dependencies for NAudio on Linux
RUN apt-get update && apt-get install -y --no-install-recommends \
    libasound2 \
    libpulse0 \
    ffmpeg \
    && rm -rf /var/lib/apt/lists/*

# ----------------------------------------------------------------------------
# Runtime: CUDA variant (GPU acceleration)
# ----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime-cuda-base

# Install CUDA runtime + audio dependencies
RUN apt-get update && apt-get install -y --no-install-recommends \
    libasound2 \
    libpulse0 \
    ffmpeg \
    wget \
    gnupg \
    && rm -rf /var/lib/apt/lists/*

# Add NVIDIA CUDA repo and install runtime (adjust version as needed)
RUN wget -qO - https://developer.download.nvidia.com/compute/cuda/repos/ubuntu2404/x86_64/3bf863cc.pub | gpg --dearmor -o /usr/share/keyrings/cuda-archive-keyring.gpg \
    && echo "deb [signed-by=/usr/share/keyrings/cuda-archive-keyring.gpg] https://developer.download.nvidia.com/compute/cuda/repos/ubuntu2404/x86_64/ /" > /etc/apt/sources.list.d/cuda.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends cuda-cudart-12-4 libcublas-12-4 \
    && rm -rf /var/lib/apt/lists/*

ENV LD_LIBRARY_PATH=/usr/local/cuda/lib64:$LD_LIBRARY_PATH

# ----------------------------------------------------------------------------
# Final: CLI
# ----------------------------------------------------------------------------
FROM runtime-${VARIANT} AS cli
WORKDIR /app
COPY --from=publish-cli /app/cli .

# Create models directory
RUN mkdir -p /app/models /app/output

VOLUME ["/app/models", "/app/output"]
ENTRYPOINT ["dotnet", "LocalTranscriber.Cli.dll"]

# ----------------------------------------------------------------------------
# Final: Web
# ----------------------------------------------------------------------------
FROM runtime-${VARIANT} AS web
WORKDIR /app
COPY --from=publish-web /app/web .

# Create models directory
RUN mkdir -p /app/models /app/output

VOLUME ["/app/models", "/app/output"]
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "LocalTranscriber.Web.dll"]

# ----------------------------------------------------------------------------
# Default target
# ----------------------------------------------------------------------------
FROM web AS final
