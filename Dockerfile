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
COPY *.sln ./
COPY Core/ Core/
COPY Data/ Data/
COPY Applications/PGAN.Poracle.Web.Api/ Applications/PGAN.Poracle.Web.Api/
COPY Applications/PGAN.Poracle.Web.App/PGAN.Poracle.Web.App.csproj Applications/PGAN.Poracle.Web.App/
RUN dotnet restore
RUN dotnet publish Applications/PGAN.Poracle.Web.Api/PGAN.Poracle.Web.Api.csproj -c Release -o /app/publish

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime
WORKDIR /app
COPY --from=dotnet-build /app/publish .
COPY --from=angular-build /app/angular/dist/client-app/browser wwwroot/
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "PGAN.Poracle.Web.Api.dll"]
