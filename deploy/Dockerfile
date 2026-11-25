# syntax=docker/dockerfile:1

# ---- 1. Frontend (React/Vite) ----------------------------------------------
FROM node:24-alpine AS web
WORKDIR /web
COPY src/web/package.json src/web/package-lock.json ./
RUN npm ci
COPY src/web/ ./
RUN npm run build

# ---- 2. Backend (ASP.NET Core) ---------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS api
WORKDIR /src
COPY src/CarTracker.Api/CarTracker.Api.csproj src/CarTracker.Api/
RUN dotnet restore src/CarTracker.Api/CarTracker.Api.csproj
COPY src/CarTracker.Api/ src/CarTracker.Api/
RUN dotnet publish src/CarTracker.Api/CarTracker.Api.csproj -c Release -o /out --no-restore /p:UseAppHost=false

# ---- 3. Runtime --------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:11.0
# Set by the release workflow (git tag without "v"); shown by /api/health.
ARG VERSION=dev
WORKDIR /app
COPY --from=api /out ./
COPY --from=web /web/dist ./wwwroot
RUN mkdir -p /data && chown app:app /data
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Storage__DataPath=/data \
    AppVersion=$VERSION
VOLUME /data
EXPOSE 8080
# Runs as the image's non-root "app" user (uid 1654) → /data on the host must be writable by 1654.
USER app
# No curl in the image; bash's /dev/tcp is enough. Healthy only after migrations ran and Kestrel listens.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080 && printf "GET /api/health HTTP/1.0\r\n\r\n" >&3 && head -1 <&3 | grep -q " 200 "'
ENTRYPOINT ["dotnet", "CarTracker.Api.dll"]
