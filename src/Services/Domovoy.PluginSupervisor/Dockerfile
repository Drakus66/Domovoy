FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# Central package versions live here — must be present before restore so versions stay pinned.
COPY ["Directory.Packages.props", "./"]
COPY ["src/Services/Domovoy.PluginSupervisor/Domovoy.PluginSupervisor.csproj", "src/Services/Domovoy.PluginSupervisor/"]
RUN dotnet restore "./src/Services/Domovoy.PluginSupervisor/Domovoy.PluginSupervisor.csproj"
COPY . .
WORKDIR "/src/src/Services/Domovoy.PluginSupervisor"
RUN dotnet build "./Domovoy.PluginSupervisor.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
# Версия компонента (Эпик 3K) — см. комментарий в src/Gateway/Domovoy.ApiGateway/Dockerfile.
ARG VERSION=0.0.0
ARG INFORMATIONAL_VERSION=0.0.0
RUN dotnet publish "./Domovoy.PluginSupervisor.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false /p:Version=$VERSION /p:InformationalVersion=$INFORMATIONAL_VERSION

# Первопартийные плагины едут ВНУТРИ образа супервизора (Эпик 3K). Плагин — это папка на томе
# хоста, а не compose-сервис, поэтому иначе канал обновлений доставить его не может: DLL пришлось
# бы копировать руками. Супервизор раскладывает их в plugins-root на старте (BuiltInPluginSeeder),
# уважая удаление владельцем. Имя папки = id плагина из его plugin.json.
RUN dotnet publish "/src/src/Plugins/Domovoy.CommutePlugin/Domovoy.CommutePlugin.csproj" \
    -c $BUILD_CONFIGURATION -o /app/builtin-plugins/commute-planner \
    /p:UseAppHost=false /p:Version=$VERSION /p:InformationalVersion=$INFORMATIONAL_VERSION

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=publish /app/builtin-plugins ./builtin-plugins
ENTRYPOINT ["dotnet", "Domovoy.PluginSupervisor.dll"]
