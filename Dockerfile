# SDK-based image used only to apply Entity Framework Core migrations.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS migrations
WORKDIR /src

COPY ["ElectronicsStoreApi/ElectronicsStoreApi.csproj", "ElectronicsStoreApi/"]
RUN dotnet restore "ElectronicsStoreApi/ElectronicsStoreApi.csproj"

COPY . .
RUN dotnet tool install --global dotnet-ef --version 10.0.14
ENV PATH="$PATH:/root/.dotnet/tools"

ENTRYPOINT ["dotnet", "ef", "database", "update", "--project", "/src/ElectronicsStoreApi/ElectronicsStoreApi.csproj", "--startup-project", "/src/ElectronicsStoreApi/ElectronicsStoreApi.csproj"]


# Runtime base image.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080


# Build application.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["ElectronicsStoreApi/ElectronicsStoreApi.csproj", "ElectronicsStoreApi/"]
RUN dotnet restore "ElectronicsStoreApi/ElectronicsStoreApi.csproj"

COPY . .
WORKDIR "/src/ElectronicsStoreApi"
RUN dotnet publish "ElectronicsStoreApi.csproj" -c Release -o /app/publish /p:UseAppHost=false


# Final runtime image.
FROM base AS final
WORKDIR /app

COPY --from=build /app/publish .
COPY --from=build /src/Frontend /Frontend

ENTRYPOINT ["dotnet", "ElectronicsStoreApi.dll"]