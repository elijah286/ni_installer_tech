# Syntax: docker/dockerfile:1
# The CLI image is intentionally headless. It can resolve and validate plans but
# cannot install host drivers, firmware, or alter NI licensing/activation state.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY NISetup.Cli/NISetup.Cli.csproj NISetup.Cli/
RUN dotnet restore NISetup.Cli/NISetup.Cli.csproj
COPY NISetup.Cli/ NISetup.Cli/
RUN dotnet publish NISetup.Cli/NISetup.Cli.csproj --configuration Release --output /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
RUN useradd --create-home --uid 10001 nisetup
COPY --from=build /app/publish/ ./
USER nisetup
ENTRYPOINT ["./ni-setup"]
