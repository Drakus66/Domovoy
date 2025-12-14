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
RUN dotnet publish "./Domovoy.Connectivity.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Final stage
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Domovoy.Connectivity.dll"]
