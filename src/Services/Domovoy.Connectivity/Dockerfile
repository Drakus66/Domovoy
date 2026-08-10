# Dockerfile for Domovoy.Connectivity

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
# Central package versions live here — must be present before restore so versions stay pinned.
COPY ["Directory.Packages.props", "./"]
COPY ["src/Services/Domovoy.Connectivity/Domovoy.Connectivity.csproj", "src/Services/Domovoy.Connectivity/"]
COPY ["src/Common/Domovoy.Common/Domovoy.Common.csproj", "src/Common/Domovoy.Common/"]
COPY ["src/Common/Domovoy.MessageBus/Domovoy.MessageBus.csproj", "src/Common/Domovoy.MessageBus/"]
RUN dotnet restore "./src/Services/Domovoy.Connectivity/Domovoy.Connectivity.csproj"
COPY . .
WORKDIR "/src/src/Services/Domovoy.Connectivity"
RUN dotnet build "./Domovoy.Connectivity.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Publish stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
# Версия компонента (Эпик 3K) — см. комментарий в src/Gateway/Domovoy.ApiGateway/Dockerfile.
ARG VERSION=0.0.0
ARG INFORMATIONAL_VERSION=0.0.0
RUN dotnet publish "./Domovoy.Connectivity.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false /p:Version=$VERSION /p:InformationalVersion=$INFORMATIONAL_VERSION

# Final stage
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Domovoy.Connectivity.dll"]
