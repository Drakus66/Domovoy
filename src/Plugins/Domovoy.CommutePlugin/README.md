# Domovoy.CommutePlugin — reference plugin (Epic 2M)

The first **real out-of-process Domovoy plugin**, and the **canonical example** to copy when building your
own. It shows the whole Epic 1C contract end-to-end: a manifest, resource-aware gating, an isolated process
that connects to the bus itself, announces a virtual device, receives writable-capability commands and
publishes sensor state.

> **What it does.** Set where you're going (and optionally by when); it computes a traffic-aware ETA and the
> latest time to leave, publishing them as sensors — so they land on the dashboard, in history and in rules
> for free. There is no "mode": a deadline means "arrive by", no deadline means "depart now".

---

## Anatomy of a Domovoy plugin

A plugin has two halves, and **both are language-agnostic**:

1. **A supervised process.** The `PluginSupervisor` discovers a folder by its `plugin.json`, gates it on host
   resources, and launches `command`+`args` as an isolated OS process (start/stop/restart/crash-isolation are
   free). It does **not** link your code — any executable works (a compiled Go/Rust binary, `node`, `python`,
   `dotnet <dll>`, …). Note the process runs inside the supervisor's container, so its runtime must be present
   there (compiled binaries are the easiest; .NET is already available).
2. **A bus client.** The plugin connects to RabbitMQ itself and speaks the capability contract as **JSON over
   AMQP** (see below). The `Domovoy.Contracts` / `Domovoy.MessageBus` .NET assemblies used here are a
   convenience for .NET plugins — a Go/Python/JS plugin just reproduces the same JSON shapes.

## Project layout (copy this shape)

```
Domovoy.CommutePlugin/
├─ plugin.json                     # the manifest (source of truth; copied next to the DLL on build/publish)
├─ Program.cs                      # Generic Host: config, DI, register the worker
├─ CommuteOptions.cs               # strongly-typed config bound from COMMUTE__* env
├─ CommuteCapabilities.cs          # the device's capability vocabulary + descriptor
├─ CommuteMath.cs                  # pure, unit-tested decision maths
├─ Model/                          # GeoPoint, RouteEstimate — plain data
├─ Traffic/                        # ITrafficProvider + Simulated / TomTom implementations (replaceable port)
└─ Services/
   ├─ SettingsClient.cs            # reads the site location + geocoder from the public API gateway
   └─ CommutePlanner.cs            # BackgroundService: announce device, consume commands, publish state
```

## The manifest (`plugin.json`)

The supervisor reads this to gate and launch the plugin. Fields:

| Field | Meaning |
|---|---|
| `id` | Stable id (also the deploy folder name). Safe chars only: `[A-Za-z0-9._-]`. |
| `name`, `version`, `description` | Shown in the `/plugins` UI. |
| `kind` | `process` (the only supported kind today). |
| `command`, `args` | What to launch, resolved from the plugin folder. Here `dotnet Domovoy.CommutePlugin.dll`. |
| `providedCapabilities` | Capability ids the plugin exposes — documentation/discovery. |
| `subscriptions`, `commands` | Bus message types the plugin listens for. |
| `permissions` | Requested permissions (recorded now, enforced with auth in Phase 3). |
| `resources` | `{ cpuCores, memoryMb, gpu, internet }` — the plugin runs only where the host satisfies these. |
| `autoStart`, `enabled` | Auto-start when satisfiable / operator master switch. |

## The bus contract (JSON over AMQP)

All exchanges are AMQP **topic** exchanges. A plugin uses exactly three:

| Action | Exchange | Routing key | Envelope `type` |
|---|---|---|---|
| Announce a device | `domovoy.discovery` | `device.discovered` | `domovoy.device.discovered.v1` |
| Publish state | `domovoy.state` | `device.state.updated` | `domovoy.device.state.v1` |
| Receive a command | `domovoy.commands` | `device.command` | `domovoy.device.command.v1` |

Every message is a CloudEvents-aligned envelope:

```jsonc
{
  "id": "…", "specversion": "1.0",
  "type": "domovoy.device.state.v1",
  "source": "commute:planner", "subject": "<deviceId>", "time": "…",
  "data": { /* payload */ }
}
```

Payloads:
- **DeviceDiscoveredV1** — `{ "device": { id, name, zoneId, identity{ adapterSource, hardwareId }, capabilities[], manufacturer, model } }`
- **DeviceStateReportV1** — `{ "deviceId": "<guid>", "state": { "<capabilityId>": <value>, … } }`
- **DeviceCommandV1** — `{ "deviceId": "<guid>", "set": { "<writableCapabilityId>": <value>, … } }`

A **device id** is derived deterministically from `adapterSource|hardwareId` (a name-based v5 GUID), so it is
stable across restarts. Commands for **all** devices arrive on the `device.command` queue — filter by your
`deviceId` and ignore the rest.

### This plugin's devices — inputs + a sensor

The plugin shows the general plugin shape — **controllable inputs + a sensor output**, all usable in
automations. It announces three devices:

| Device | Capability | Dir | Meaning |
|---|---|---|---|
| **Маршрут · Старт** | `commute:origin` | write · `editor:geo` | Start location; empty = the site location (Epic 2K). |
| **Маршрут · Назначение** | `commute:destination` | write · `editor:geo` | Destination (`lat,lon` / `lat,lon\|Label`, or a place name). |
| | `commute:deadline` | write · `editor:time` | Optional arrive-by `HH:mm`; empty = depart now. |
| **Маршрут · Прогноз** | `commute:eta_minutes` | read | Traffic-aware travel time. |
| | `commute:distance_km` | read | Road distance. |
| | `commute:traffic_level` | read | `light` / `moderate` / `heavy`. |
| | `commute:leave_by` | read | Latest departure time to make the deadline. |
| | `commute:arrival_eta` | read | Projected arrival clock time. |
| | `commute:minutes_to_arrival` | read | Minutes to arrival if leaving now (S3 pre-warm hook). |
| | `commute:status` | read | One-line human status. |

Set a goal by writing the input devices from the WebUI (the location capabilities use the `editor:"geo"`
attribute so the dashboard renders a **map picker**, the deadline uses `editor:"time"`) or from a rule (the
standard 1D actuation path). The recompute cadence is adaptive — rare when idle, tighter as the moment to
leave nears.

> **Reusable UI hint.** `CapabilityAttributeKeys.Editor` (`"geo"` / `"time"`) is a platform-wide way for any
> plugin to ask the dashboard for a richer writable control than the raw kind implies — the value stays a
> plain string on the wire.

## Configuration

The supervisor does **not** inject env into plugins; env is declared on the `plugin-supervisor` compose
service and inherited by the child process. Keys (see `CommuteOptions`):

| Env | Default | Meaning |
|---|---|---|
| `RABBITMQ__HOSTNAME` / `__PORT` / `__USERNAME` / `__PASSWORD` | `rabbitmq` / `5672` / `user` / `user` | Bus connection. |
| `COMMUTE__SETTINGSBASEURL` | `http://api-gateway:8080` | Where to read the site location / geocoder. |

The traffic backend and its API key are **not** env: they are plugin **settings** (`CommuteSettings`,
keys `provider` / `tomTomApiKey`), edited from the plugins panel and applied live over the settings
channel. `COMMUTE__PROVIDER` / `COMMUTE__TOMTOMAPIKEY` no longer exist — changing a provider must not
mean editing compose and restarting the stack.

The **traffic backend is a replaceable port** (`ITrafficProvider`): the default `SimulatedTrafficProvider`
needs no key or network; `TomTomTrafficProvider` plugs in when a key is set. Swapping in HERE or a paid
Yandex key is a new class, not a rewrite.

## Build, package & deploy

```powershell
# from the repo root — framework-dependent publish; the manifest is copied automatically.
dotnet publish src/Plugins/Domovoy.CommutePlugin -c Release -o artifacts/commute-pkg

# Package for UI upload:
Compress-Archive -Path artifacts/commute-pkg/* -DestinationPath artifacts/commute-pkg.zip -Force
```

Install it **without touching containers** via the WebUI: **Plugins → Install plugin**, upload the `.zip`.
The supervisor extracts it into the plugins root, registers and auto-starts it. Uninstall from the same page.

Dev shortcut: publish straight into the deploy folder and let the watcher pick it up —
`dotnet publish src/Plugins/Domovoy.CommutePlugin -c Release -o plugins/commute-planner`.

## Tests

Pure logic (traffic simulator, geo maths, deadline/interval) is covered by
`tests/Domovoy.CommutePlugin.Tests` (`InternalsVisibleTo` exposes the internals). `dotnet test` there.

---

## Writing your own plugin — checklist

1. Create `src/Plugins/Domovoy.<YourPlugin>/` (add it to a `Plugins` solution folder).
2. Write a `plugin.json` (copy this one), set `id`, `command`/`args` and honest `resources`.
3. Connect to RabbitMQ (any AMQP client / any language).
4. On start: publish **DeviceDiscoveredV1** with your capabilities; subscribe to `device.command`.
5. In your loop: publish **DeviceStateReportV1**; on a command for your `deviceId`, act and recompute.
6. Read your own config from env/args; don't rely on the supervisor to pass it.
7. Publish the framework-dependent output, zip it, upload via **Plugins → Install plugin**.

Not .NET? The contract is just JSON over AMQP — reproduce the envelopes above in Go/Python/JS. Ship a
compiled binary (Go/Rust) or bundle the runtime, since the process launches inside the supervisor's image.
