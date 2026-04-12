# Stage 1: Build Angular SPA
FROM node:25-alpine AS angular-build
WORKDIR /app/angular
COPY Applications/Pgan.PoracleWebNet.App/ClientApp/package*.json ./
RUN npm ci
COPY Applications/Pgan.PoracleWebNet.App/ClientApp/ ./
RUN npx ng build --configuration production

# Stage 2: Build .NET API
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet-build
WORKDIR /src
COPY Pgan.PoracleWebNet.slnx ./
COPY Core/Pgan.PoracleWebNet.Core.Abstractions/Pgan.PoracleWebNet.Core.Abstractions.csproj Core/Pgan.PoracleWebNet.Core.Abstractions/
COPY Core/Pgan.PoracleWebNet.Core.Mappings/Pgan.PoracleWebNet.Core.Mappings.csproj Core/Pgan.PoracleWebNet.Core.Mappings/
COPY Core/Pgan.PoracleWebNet.Core.Models/Pgan.PoracleWebNet.Core.Models.csproj Core/Pgan.PoracleWebNet.Core.Models/
COPY Core/Pgan.PoracleWebNet.Core.Repositories/Pgan.PoracleWebNet.Core.Repositories.csproj Core/Pgan.PoracleWebNet.Core.Repositories/
COPY Core/Pgan.PoracleWebNet.Core.Services/Pgan.PoracleWebNet.Core.Services.csproj Core/Pgan.PoracleWebNet.Core.Services/
COPY Data/Pgan.PoracleWebNet.Data/Pgan.PoracleWebNet.Data.csproj Data/Pgan.PoracleWebNet.Data/
COPY Data/Pgan.PoracleWebNet.Data.Scanner/Pgan.PoracleWebNet.Data.Scanner.csproj Data/Pgan.PoracleWebNet.Data.Scanner/
COPY Applications/Pgan.PoracleWebNet.Api/Pgan.PoracleWebNet.Api.csproj Applications/Pgan.PoracleWebNet.Api/
COPY Applications/Pgan.PoracleWebNet.App/Pgan.PoracleWebNet.App.csproj Applications/Pgan.PoracleWebNet.App/
COPY Tests/Pgan.PoracleWebNet.Tests/Pgan.PoracleWebNet.Tests.csproj Tests/Pgan.PoracleWebNet.Tests/
RUN dotnet restore
COPY . .
RUN dotnet publish Applications/Pgan.PoracleWebNet.Api/Pgan.PoracleWebNet.Api.csproj -c Release -o /app/publish

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN useradd --system --no-create-home appuser
COPY --from=dotnet-build /app/publish .
COPY --from=angular-build /app/angular/dist/ClientApp/browser wwwroot/

RUN mkdir -p /app/data && chown -R appuser /app/data

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
# ASPNETCORE_URLS takes precedence over Server:Port in appsettings.
# To override: docker run -e ASPNETCORE_URLS=http://+:9090 -p 9090:9090 ...
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DATA_DIR=/app/data

USER appuser
ENTRYPOINT ["dotnet", "Pgan.PoracleWebNet.Api.dll"]
