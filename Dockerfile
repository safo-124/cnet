# MepCatalog: one image with the ASP.NET Core API serving the built React app.
#   docker build -t mepcatalog .
#   docker run -p 8080:8080 -v mepcatalog-data:/data mepcatalog

# 1. Build the React app
FROM node:24-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

# 2. Publish the API (project files first, so the restore layer is cached between builds)
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS api
WORKDIR /src
COPY src/MepCatalog.Core/MepCatalog.Core.csproj src/MepCatalog.Core/
COPY src/MepCatalog.Data/MepCatalog.Data.csproj src/MepCatalog.Data/
COPY src/MepCatalog.Ifc/MepCatalog.Ifc.csproj src/MepCatalog.Ifc/
COPY src/MepCatalog.Reporting/MepCatalog.Reporting.csproj src/MepCatalog.Reporting/
COPY src/MepCatalog.Ai/MepCatalog.Ai.csproj src/MepCatalog.Ai/
COPY src/MepCatalog.Api/MepCatalog.Api.csproj src/MepCatalog.Api/
RUN dotnet restore src/MepCatalog.Api/MepCatalog.Api.csproj
COPY src/ src/
RUN dotnet publish src/MepCatalog.Api/MepCatalog.Api.csproj -c Release -o /app --no-restore

# 3. Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /web/dist ./wwwroot
COPY data/sample-products.csv data/sample-building.ifc ./samples/
COPY data/datasheets/nordic-air-ka-series.pdf ./samples/datasheets/

# The SQLite database lives on a volume so it survives redeploys. Run as the image's non-root "app" user.
RUN mkdir -p /data && chown app /data
USER app

ENV ASPNETCORE_URLS=http://+:8080 \
    ConnectionStrings__Catalog="Data Source=/data/mepcatalog.db" \
    Samples__Path=/app/samples \
    Demo__Enabled=true

EXPOSE 8080
VOLUME /data
ENTRYPOINT ["dotnet", "MepCatalog.Api.dll"]
