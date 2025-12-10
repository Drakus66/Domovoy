# Dockerfile for Domovoy.DiscoveryService

# Base image for runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/Services/Domovoy.DiscoveryService/Domovoy.DiscoveryService.csproj", "src/Services/Domovoy.DiscoveryService/"]
COPY ["src/Common/Domovoy.Common/Domovoy.Common.csproj", "src/Common/Domovoy.Common/"]
COPY ["src/Common/Domovoy.MessageBus/Domovoy.MessageBus.csproj", "src/Common/Domovoy.MessageBus/"]
RUN dotnet restore "./src/Services/Domovoy.DiscoveryService/Domovoy.DiscoveryService.csproj"
COPY . .
WORKDIR "/src/src/Services/Domovoy.DiscoveryService"
RUN dotnet build "./Domovoy.DiscoveryService.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Publish stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Domovoy.DiscoveryService.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Final stage
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Domovoy.DiscoveryService.dll"]
