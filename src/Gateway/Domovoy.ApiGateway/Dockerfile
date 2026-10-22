# См. статью по ссылке https://aka.ms/customizecontainer, чтобы узнать как настроить контейнер отладки и как Visual Studio использует этот Dockerfile для создания образов для ускорения отладки.

# Этот этап используется при запуске из VS в быстром режиме (по умолчанию для конфигурации отладки)
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081


# Этот этап используется для сборки проекта службы
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/Gateway/Domovoy.ApiGateway/Domovoy.ApiGateway.csproj", "src/Gateway/Domovoy.ApiGateway/"]
RUN dotnet restore "./src/Gateway/Domovoy.ApiGateway/Domovoy.ApiGateway.csproj"
COPY . .
WORKDIR "/src/src/Gateway/Domovoy.ApiGateway"
RUN dotnet build "./Domovoy.ApiGateway.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Этот этап используется для публикации проекта службы, который будет скопирован на последний этап
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
# Версия компонента (Эпик 3K). CI подставляет её из build/components.json; локальная сборка
# остаётся на 0.0.0 — это и есть признак «образ собран здесь, а не пришёл из канала».
# AssemblyVersion не принимает pre-release суффикс, поэтому числовая часть идёт в Version,
# а полная строка (1.4.0-dev.57+ab12cd3) — в InformationalVersion, её и читает код.
ARG VERSION=0.0.0
ARG INFORMATIONAL_VERSION=0.0.0
RUN dotnet publish "./Domovoy.ApiGateway.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false /p:Version=$VERSION /p:InformationalVersion=$INFORMATIONAL_VERSION

# Этот этап используется в рабочей среде или при запуске из VS в обычном режиме (по умолчанию, когда конфигурация отладки не используется)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Domovoy.ApiGateway.dll"]