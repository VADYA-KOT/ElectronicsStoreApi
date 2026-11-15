# Build the Angular frontend.
FROM node:26-alpine AS frontend-build
WORKDIR /src/FrontendAngular

COPY FrontendAngular/package.json FrontendAngular/package-lock.json ./
RUN npm ci

COPY FrontendAngular/ ./
RUN npm run build
RUN test -f dist/electronics-store-frontend/browser/index.html


# SDK image used to apply Entity Framework Core migrations.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS migrations
WORKDIR /src

COPY ["ElectronicsStoreApi/ElectronicsStoreApi.csproj", "ElectronicsStoreApi/"]
RUN dotnet restore "ElectronicsStoreApi/ElectronicsStoreApi.csproj"

COPY . .
RUN dotnet tool install --global dotnet-ef --version 10.0.14
ENV PATH="$PATH:/root/.dotnet/tools"

ENTRYPOINT ["dotnet", "ef", "database", "update", "--project", "/src/ElectronicsStoreApi/ElectronicsStoreApi.csproj", "--startup-project", "/src/ElectronicsStoreApi/ElectronicsStoreApi.csproj"]


# ASP.NET Core runtime image.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080


# Build the ASP.NET Core API.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["ElectronicsStoreApi/ElectronicsStoreApi.csproj", "ElectronicsStoreApi/"]
RUN dotnet restore "ElectronicsStoreApi/ElectronicsStoreApi.csproj"

COPY . .
WORKDIR /src/ElectronicsStoreApi
RUN dotnet publish "ElectronicsStoreApi.csproj" --configuration Release --output /app/publish /p:UseAppHost=false


# Final image: API and compiled Angular application.
FROM base AS final
WORKDIR /app

COPY --from=build /app/publish .
COPY --from=frontend-build /src/FrontendAngular/dist/electronics-store-frontend/browser ./wwwroot

ENTRYPOINT ["dotnet", "ElectronicsStoreApi.dll"]
