# Изменения стека мониторинга (Prometheus-only)

**Дата**: April 29, 2026  
**Причина**: Оптимизация для домашнего использования (100-500 устройств)

---

## Что было изменено

### Удаленные сервисы

| Сервис | Порт | Причина удаления |
|--------|------|------------------|
| Grafana | 3000 | Избыточность - WebUI покрывает все нужды |
| Loki | 3100 | Не нужен при использовании docker logs |
| Promtail | 9080 | Сборщик логов для Loki |

### Оставшиеся сервисы

| Сервис | Порт | Назначение |
|--------|------|------------|
| Prometheus | 9090 | Метрики и health monitoring |
| MongoDB Exporter | 9216 | Метрики базы данных |

---

## Преимущества новой конфигурации

### Экономия ресурсов

| Ресурс | До | После | Экономия |
|--------|-----|-------|----------|
| RAM | ~600MB-1.5GB | ~200-400MB | **400MB-1.1GB** |
| CPU | Высокая нагрузка | Умеренная | **~60%** |
| Порты | 14 открытых | 11 открытых | **-3 порта** |
| Volumes | 5 | 3 | **-2 volume** |

### Упрощение

- Меньше компонентов для обслуживания
- Единая точка входа (WebUI :80)
- Просмотр логов через `docker-compose logs`

---

## Как просматривать логи теперь

### Вариант 1: Docker CLI (рекомендуется)

```bash
# Все сервисы
docker-compose logs -f

# Конкретный сервис
docker-compose logs -f unified-device-service

# Последние 100 строк
docker-compose logs --tail=100 api-gateway
```

### Вариант 2: WebUI (планируется)

В будущих версиях WebUI можно добавить endpoint для отображения логов:
```
GET /api/system/logs?service=unified-device-service&lines=100
```

---

## Конфигурация Prometheus

### Параметры хранения

```yaml
--storage.tsdb.retention.time=7d    # Хранение 7 дней (было 15)
--storage.tsdb.retention.size=2GB   # Лимит 2GB
scrape_interval: 30s                 # Было 15s - меньше нагрузка
```

### Мониторинг сервисов

Prometheus собирает метрики с:
- api-gateway:8080
- db-gateway:8080
- unified-device-service:8080
- connectivity-service:8080
- rabbitmq:15692
- mongodb-exporter:9216
- localhost:9090 (self)

### Endpoints метрик

Каждый .NET сервис автоматически экспортирует метрики на `/metrics` благодаря `prometheus-net.AspNetCore`.

---

## Использование Prometheus

### Доступ

```
http://localhost:9090        # Prometheus UI (опционально)
http://localhost:9090/api/v1/query?query=up  # API для WebUI
```

### Полезные запросы (PromQL)

```promql
# Проверка доступности сервисов
up

# Количество устройств (если экспортируется кастомная метрика)
domovoy_devices_total

# MQTT соединения
rabbitmq_connections

# Использование памяти процессами
dotnet_total_memory_bytes

# HTTP запросы
http_requests_total
```

### Интеграция с WebUI

WebUI может запрашивать данные у Prometheus через backend proxy:

```csharp
// ApiGateway endpoint
app.MapGet("/api/metrics", async () => {
    var response = await httpClient.GetAsync(
        "http://prometheus:9090/api/v1/query?query=up");
    return await response.Content.ReadAsStringAsync();
});
```

---

## Просмотр метрик

### Вариант 1: Prometheus UI (для отладки)

Откройте `http://localhost:9090` и используйте Expression browser.

### Вариант 2: Встроенный в WebUI (рекомендуется)

Создайте простой компонент в React:

```tsx
const SystemMetrics = () => {
  const { data } = useQuery('/api/metrics');
  
  return (
    <div>
      <h3>Статус системы</h3>
      {data?.data?.result?.map(r => (
        <div key={r.metric.job}>
          {r.metric.job}: {r.value[1] === '1' ? '✅' : '❌'}
        </div>
      ))}
    </div>
  );
};
```

---

## Ротация и очистка

### Автоматическая ротация

Prometheus автоматически удаляет старые данные:
- По времени: старше 7 дней
- По размеру: превышение 2GB

### Ручная очистка (если нужно)

```bash
# Остановить и очистить данные Prometheus
docker-compose stop prometheus
docker volume rm domovoy_prometheus_data
docker-compose up -d prometheus
```

### Docker логи ротация

Настройте `/etc/docker/daemon.json`:

```json
{
  "log-driver": "json-file",
  "log-opts": {
    "max-size": "10m",
    "max-file": "3"
  }
}
```

---

## Файлы изменены

| Файл | Изменение |
|------|-----------|
| `docker-compose.yml` | Убраны Grafana, Loki, Promtail; добавлены retention limits для Prometheus |
| `prometheus.yaml` | Убраны несуществующие сервисы; актуальные target'ы |
| `Directory.Packages.props` | Убран `Serilog.Sinks.Grafana.Loki` |
| `SerilogBootstrap.cs` | Убран Loki sink, улучшен output template |
| `loki-config.yaml` | ❌ Удален |
| `promtail-config.yaml` | ❌ Удален |
| `grafana/` | ❌ Удалена директория |

---

## Проверка после изменений

```bash
# Пересобрать и запустить
docker-compose down
docker-compose up -d --build

# Проверить статус
docker-compose ps

# Проверить метрики
curl http://localhost:9090/api/v1/query?query=up

# Проверить логи
docker-compose logs -f prometheus
```

---

## Будущие улучшения

- [ ] Добавить `/api/system/metrics` endpoint в ApiGateway
- [ ] Создать React компонент для отображения статуса сервисов
- [ ] Добавить alerts в Prometheus (опционально для дома)
- [ ] Рассмотреть AlertManager для критических уведомлений (email/Telegram)

---

**Итог**: Мониторинг стал проще, быстрее, дешевле по ресурсам, при этом сохраняет всю необходимую функциональность для домашнего умного дома.
