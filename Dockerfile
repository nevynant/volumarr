# Volumarr (Sonarr fork) container build. No official Dockerfile ships in
# this repo -- Sonarr's real images are built by a separate linuxserver.io
# pipeline this fork doesn't have access to -- so this is a from-scratch
# multi-stage build: frontend (webpack) + backend (dotnet build) into a slim
# ASP.NET runtime image.

FROM node:22 AS frontend-build
WORKDIR /src
COPY package.json yarn.lock ./
RUN yarn install --frozen-lockfile
COPY tsconfig.json ./
COPY frontend ./frontend
RUN yarn build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /src
COPY src ./src
COPY Logo ./Logo
# Building the whole solution (not `dotnet publish` on a single project) --
# confirmed live that the app loads its platform-specific assembly
# (Sonarr.Mono.dll on Linux) dynamically via reflection at runtime, not a
# compile-time reference, so publishing just NzbDrone.Console alone silently
# omits it. A solution-wide build centralizes every project's output into one
# folder (see Directory.Build.props' BaseIntermediateOutputPath), which is
# also how this was run and verified locally throughout development.
#
# Debug, not Release -- Sonarr's own pre-existing source hits a Release-only
# error under .NET 10 (SYSLIB0006, an obsolete Thread API) suppressed for
# Debug via Directory.Build.props. Analyzers disabled separately -- confirmed
# live that analyzer-driven CA1724 errors (also pre-existing Sonarr code)
# fire during a from-scratch container build regardless of configuration.
RUN dotnet build src/Sonarr.sln \
    -c Debug -p:RunAnalyzersDuringBuild=false -p:EnableNETAnalyzers=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=backend-build /src/_output/net10.0 ./
# Debug builds look for the frontend under "../UI" relative to the working
# directory (ConfigFileProvider.UiFolder), not "./UI" -- only Release uses
# the sibling path. WORKDIR is /app, so UI needs to sit at the container
# root, one level up, to match.
COPY --from=frontend-build /src/_output/UI /UI
EXPOSE 8989
# AssemblyName is OS-conditional (see NzbDrone.Console/Sonarr.Console.csproj):
# "Sonarr.Console.dll" on Windows builds, "Sonarr.dll" here on Linux --
# confirmed by inspecting the actual built image, not assumed from local
# Windows testing, which gave the wrong answer for this target platform.
ENTRYPOINT ["dotnet", "Sonarr.dll", "-nobrowser", "--data=/config"]
