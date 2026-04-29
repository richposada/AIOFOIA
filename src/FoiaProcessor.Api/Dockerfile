# syntax=docker/dockerfile:1.6
# Multi-stage build: SPA → API publish → runtime image with bundled wwwroot.

# ---------- Stage 1: Build the React SPA ----------
FROM node:20-alpine AS spa-build
WORKDIR /spa
COPY src/foia-processor-ui/package.json src/foia-processor-ui/package-lock.json* ./
RUN npm ci
COPY src/foia-processor-ui/ ./
RUN npm run build

# ---------- Stage 2: Publish the .NET API ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS api-build
WORKDIR /src
COPY foia-agent.sln ./
COPY src/FoiaProcessor.Api/FoiaProcessor.Api.csproj    src/FoiaProcessor.Api/
COPY src/FoiaProcessor.Agents/FoiaProcessor.Agents.csproj   src/FoiaProcessor.Agents/
COPY src/FoiaProcessor.McpTools/FoiaProcessor.McpTools.csproj src/FoiaProcessor.McpTools/
COPY src/FoiaProcessor.Data/FoiaProcessor.Data.csproj   src/FoiaProcessor.Data/
RUN dotnet restore src/FoiaProcessor.Api/FoiaProcessor.Api.csproj
COPY src/FoiaProcessor.Api/    src/FoiaProcessor.Api/
COPY src/FoiaProcessor.Agents/ src/FoiaProcessor.Agents/
COPY src/FoiaProcessor.McpTools/ src/FoiaProcessor.McpTools/
COPY src/FoiaProcessor.Data/   src/FoiaProcessor.Data/
RUN dotnet publish src/FoiaProcessor.Api/FoiaProcessor.Api.csproj \
        -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---------- Stage 3: Runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=api-build /app/publish ./
COPY --from=spa-build /spa/dist    ./wwwroot/
ENTRYPOINT ["dotnet", "FoiaProcessor.Api.dll"]
