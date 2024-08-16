# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# These ARGs allow for swapping out the base used to make the final image when debugging from VS
ARG LAUNCHING_FROM_VS
# This sets the base image for final, but only if LAUNCHING_FROM_VS has been defined
ARG FINAL_BASE_IMAGE=${LAUNCHING_FROM_VS:+aotdebug}

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/runtime:9.0-preview-bookworm-slim AS base
USER app
WORKDIR /app


# This stage is used to build the service project
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0-preview-bookworm-slim-amd64 AS build
ARG TARGETARCH
# Install clang/zlib1g-dev dependencies for publishing to native
RUN dpkg --add-architecture arm64 \
    && apt-get update \
    && apt-get install -y --no-install-recommends \
    clang zlib1g-dev zlib1g-dev:arm64 gcc-aarch64-linux-gnu llvm
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["UdpToHttpGateway/UdpToHttpGateway.csproj", "UdpToHttpGateway/"]
RUN dotnet restore "./UdpToHttpGateway/UdpToHttpGateway.csproj" -a $TARGETARCH
COPY . .
WORKDIR "/src/UdpToHttpGateway"
RUN dotnet build "./UdpToHttpGateway.csproj" -c $BUILD_CONFIGURATION -a $TARGETARCH -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./UdpToHttpGateway.csproj" -a $TARGETARCH -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=true

# This stage is used as the base for the final stage when launching from VS to support debugging in regular mode (Default when not using the Debug configuration)
FROM base as aotdebug
USER root
# Install GDB to support native debugging
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
    gdb
USER app

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM ${FINAL_BASE_IMAGE:-mcr.microsoft.com/dotnet/runtime-deps:9.0-preview-bookworm-slim} AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["./UdpToHttpGateway"]