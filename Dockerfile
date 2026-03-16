# Stage 1: Build Angular SPA
FROM node:22-alpine AS angular-build
WORKDIR /app/angular
COPY Applications/PGAN.Poracle.Web.App/ClientApp/package*.json ./
RUN npm ci
COPY Applications/PGAN.Poracle.Web.App/ClientApp/ ./
RUN npx ng build --configuration production

# Stage 2: Build .NET API
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS dotnet-build
WORKDIR /src
COPY PGAN.Poracle.Web.slnx ./
COPY Core/PGAN.Poracle.Web.Core.Abstractions/PGAN.Poracle.Web.Core.Abstractions.csproj Core/PGAN.Poracle.Web.Core.Abstractions/
COPY Core/PGAN.Poracle.Web.Core.Mappings/PGAN.Poracle.Web.Core.Mappings.csproj Core/PGAN.Poracle.Web.Core.Mappings/
COPY Core/PGAN.Poracle.Web.Core.Models/PGAN.Poracle.Web.Core.Models.csproj Core/PGAN.Poracle.Web.Core.Models/
COPY Core/PGAN.Poracle.Web.Core.Repositories/PGAN.Poracle.Web.Core.Repositories.csproj Core/PGAN.Poracle.Web.Core.Repositories/
COPY Core/PGAN.Poracle.Web.Core.Services/PGAN.Poracle.Web.Core.Services.csproj Core/PGAN.Poracle.Web.Core.Services/
COPY Core/PGAN.Poracle.Web.Core.UnitsOfWork/PGAN.Poracle.Web.Core.UnitsOfWork.csproj Core/PGAN.Poracle.Web.Core.UnitsOfWork/
COPY Data/PGAN.Poracle.Web.Data/PGAN.Poracle.Web.Data.csproj Data/PGAN.Poracle.Web.Data/
COPY Data/PGAN.Poracle.Web.Data.Scanner/PGAN.Poracle.Web.Data.Scanner.csproj Data/PGAN.Poracle.Web.Data.Scanner/
COPY Applications/PGAN.Poracle.Web.Api/PGAN.Poracle.Web.Api.csproj Applications/PGAN.Poracle.Web.Api/
COPY Applications/PGAN.Poracle.Web.App/PGAN.Poracle.Web.App.csproj Applications/PGAN.Poracle.Web.App/
RUN dotnet restore
COPY . .
RUN dotnet publish Applications/PGAN.Poracle.Web.Api/PGAN.Poracle.Web.Api.csproj -c Release -o /app/publish

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime
WORKDIR /app
RUN groupadd --system --gid 1000 appgroup && \
    useradd --system --uid 1000 --gid appgroup --no-create-home appuser
COPY --from=dotnet-build /app/publish .
COPY --from=angular-build /app/angular/dist/ClientApp/browser wwwroot/

RUN mkdir -p /app/data && chown appuser:appgroup /app/data

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DATA_DIR=/app/data

USER appuser
ENTRYPOINT ["dotnet", "PGAN.Poracle.Web.Api.dll"]
