# Служба поставки и обновлений (Эпик 3K).
#
# Отличие от остальных сервисов: в образ доустанавливается docker CLI с compose-плагином.
# Обновление применяется тем же `docker compose`, которым владелец поднимает стек руками, —
# так пересоздание контейнеров остаётся заботой compose (Config/HostConfig/сети/healthcheck),
# а не воспроизводится вручную через Docker API, где любое пропущенное поле тихо ломает дом.

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

# docker-ce-cli + docker-compose-plugin из официального репозитория Docker.
# Демон не ставим: контейнер работает с сокетом хоста, свой Docker внутри не нужен.
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl gnupg \
    && install -m 0755 -d /etc/apt/keyrings \
    && curl -fsSL https://download.docker.com/linux/debian/gpg -o /etc/apt/keyrings/docker.asc \
    && chmod a+r /etc/apt/keyrings/docker.asc \
    && echo "deb [arch=amd64 signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/debian $(. /etc/os-release && echo "$VERSION_CODENAME") stable" \
       > /etc/apt/sources.list.d/docker.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends docker-ce-cli docker-compose-plugin \
    && apt-get purge -y gnupg \
    && apt-get autoremove -y \
    && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/Services/Domovoy.Updater/Domovoy.Updater.csproj", "src/Services/Domovoy.Updater/"]
RUN dotnet restore "./src/Services/Domovoy.Updater/Domovoy.Updater.csproj"
COPY . .
WORKDIR "/src/src/Services/Domovoy.Updater"
RUN dotnet build "./Domovoy.Updater.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
# Версия компонента (Эпик 3K) — см. комментарий в src/Gateway/Domovoy.ApiGateway/Dockerfile.
ARG VERSION=0.0.0
ARG INFORMATIONAL_VERSION=0.0.0
RUN dotnet publish "./Domovoy.Updater.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false /p:Version=$VERSION /p:InformationalVersion=$INFORMATIONAL_VERSION

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
# Работает от root: нужен доступ к docker.sock и запись в каталог установки на хосте.
# Наружу портов не публикует и доступен только внутри domovoy-network.
ENTRYPOINT ["dotnet", "Domovoy.Updater.dll"]
