# Бандл топологии (Эпик 3K).
#
# Не сервис, а способ доставки файлов: боевой compose, .env.template, manifest.json и
# конфиги-ассеты. Образ от `scratch` — ровно один слой без базового образа и без рантайма,
# поэтому updater скачивает его слой напрямую через Registry v2 и распаковывает штатными
# GZipStream + System.Formats.Tar. Ни ORAS, ни `docker cp`, ни запуск контейнера не нужны.
#
# Собирается ТОЛЬКО из сгенерированного каталога dist/ (build/tools/gen-topology.mjs),
# в репозитории эти файлы не лежат — источник истины остаётся один, корневой docker-compose.yml.

FROM scratch
COPY dist/ /
