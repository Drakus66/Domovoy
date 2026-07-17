<div align="center">

# 🏠 Domovoy

**A local, automation-first smart-home server for a private house and its grounds.**

**English** · [Русский](README.ru.md)

</div>

---

> **Domovoy** (Russian: *Домовой*) is the house spirit of Slavic folklore — an unseen guardian
> that lives in a home, watches over it, and keeps it in order. This project is that spirit made
> technological: not a remote control for your devices, but a caretaker that *runs the house*.
> You set the way the household should live — modes, setpoints, the limits of what's allowed —
> and Domovoy keeps things in order without demanding your attention.

## The idea

Most smart-home platforms are, at their core, **device controllers**: a nice UI with buttons and
sliders on top of a pile of integrations. You are the automation.

Domovoy inverts that. The goal is **automatic control by scenario with minimal user input**. You
tell the house *what "night" or "away" means*, what temperature and air quality you want, where the
boundaries are — and the system handles the rest: lighting by sensors and time of day, climate via
ventilation and heating, supplementary underfloor heat, outdoor lighting and irrigation, access
control. The closest modern analogy is a butler-AI like Jarvis: an unobtrusive keeper, not a
dashboard full of switches.

The product bet is a specific intersection that no existing system occupies:

> **The determinism of installer-grade systems + failure isolation + learning automation that you
> can replay against history, explain, and roll back — guaranteed to work locally, purpose-built
> for a private home with grounds.**

## Motivation

Existing platforms each give up something we refuse to give up:

- **Monolithic hubs** (Home Assistant, openHAB, Homey) are a single point of failure — one broken
  integration or a bad update can take the whole instance down, and there is no "critical layer"
  that keeps safety-essential behavior running regardless.
- **Cloud-first ecosystems** (SmartThings, Apple/Google/Alexa Home) break when the cloud changes and
  put your home on the critical path of someone else's servers.
- **Installer systems** (Loxone, KNX, Control4) are genuinely reliable and deterministic, but closed,
  expensive, and locked to a professional — no openness, no DIY, no learning.
- **"AI smart home"** as a marketing label usually means an LLM you can talk to — not a system that
  actually *learns your home from its own history* and can justify what it did.

Domovoy is built to keep the reliability of the professional systems, the openness and DIY spirit of
Home Assistant, and add proactive, **explainable** machine learning with a human in the loop.

## Core concepts

### 🧱 Capability-first, contract-first core
Devices are not modeled as a closed set of types (light / sensor / switch). They are described by an
open set of **capabilities** — `on_off`, `temperature_setpoint`, `lock`, `valve`, `presence`, and so
on. A single versioned event/command contract (CloudEvents-style envelopes) sits at the center, so
new device kinds, plugins, and ML features extend the system **without recompiling the core**. Zones
and grounds are first-class, not bolted on.

### 🔌 Failure isolation and a deterministic safety layer
An event bus (RabbitMQ) plus **out-of-process plugins** mean a crashing or updating integration can
never take the core down. Above it runs a layer of **deterministic rules** — anti-freeze,
CO₂ → ventilation, smoke → unlock and the like — that keep working even when the UI, ML, or cloud are
unavailable. These aren't wired into the code: they ship as **safety templates** you adopt and bind to
your own sensors, so the household decides what is watched and what happens. Monolithic systems
fundamentally can't isolate failures like this.

### 🧠 Layered, learning automation you can trust
Control is layered, not flat:

1. a **deterministic safety floor** (always-local rules, scheduler, sun events) that is never overridden;
2. **user setpoints and modes** on top of it;
3. **ML optimization** on top of that.

Machine learning (built on **ML.NET**, running locally) learns from telemetry and your own actions,
then **proposes deterministic rules and setpoints into an approval queue**. You approve the model
version and how much authority it has — never individual outputs one by one. New models roll out in
**shadow mode** first.

### 🔍 Replay and explainability
Every proposal can be **replayed against real history** ("how would this have behaved last week?")
before it goes live, and every action the system takes can be **explained** ("why did the light turn
on?" → the rule plus the triggering event). This is the trust layer that makes learning automation
safe to live with.

### 📴 Offline-first as a verifiable invariant
The whole system must run stably with **no external network**. Cloud integrations (robot vacuum,
mower, traffic-aware commute, and the like) are strictly **optional plugins** — never in the critical
path. Telemetry and the domain event log are stored locally and become the fuel for ML from day one.

### 🧩 Modular services and a plugin architecture
Services are independent: you can update or stop one and the rest keeps running. Integrations are
**plugins over the bus** with a manifest and a supervisor, and they are **resource-aware** — a heavy
plugin (GPU vision, for example) enables only if the host actually has the CPU/RAM/GPU for it,
otherwise the base functionality carries on. The target deployment is a homelab (a mini-PC growing
into a full machine or rack), not an industrial installation.

## What Domovoy deliberately is *not*

- **Not "a better Home Assistant" measured by integration count.** We lean on standards (Zigbee2MQTT,
  MQTT, Matter/Thread as it converges), a native DIY protocol, and a plugin SDK — not a race to
  clone thousands of integrations.
- **Not a rich-dashboard platform.** The thesis is "set your modes, the house runs itself" — that
  means *less* UI, not more.
- **Not a catch-up voice assistant.** Local voice, when it comes, is integrating a ready-made local
  stack as a plugin, not building one from scratch.
- **Not "AI" as a label.** Having a model isn't the point. The moat is explainability, replay, and
  safe execution — the model just proposes.

## Technology at a glance

- **Backend:** .NET 9 · C# · ASP.NET Core (Minimal API + background services)
- **Messaging:** RabbitMQ (AMQP + MQTT) as the event bus
- **Storage:** MongoDB — current state, time-series telemetry, and an append-only event log in one database
- **Machine learning:** ML.NET (local, no Python in the control path)
- **Realtime & UI:** SignalR · React + Vite + TypeScript + MUI
- **Hardware:** a native Domovoy protocol for DIY devices (Arduino / ESP8266 / ESP32), alongside Zigbee and ESPHome
- **Infrastructure:** Docker Compose · Prometheus for observability

## License

Domovoy is free software: you can redistribute it and/or modify it under the
terms of the **GNU Affero General Public License, version 3 or later
(AGPL-3.0-or-later)**, as published by the Free Software Foundation. The full
text is in [LICENSE](LICENSE).

```
Copyright (C) 2025-2026 Ilya Dryagin

This program is distributed in the hope that it will be useful, but WITHOUT
ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS
FOR A PARTICULAR PURPOSE. See the GNU Affero General Public License for more
details.
```

**Network use is distribution.** Because Domovoy is a networked server, the
AGPL's Section 13 applies: if you run a modified version and let others
interact with it over a network, you must offer those users the corresponding
source code of your modified version.

**Commercial licensing.** The AGPL is ideal for the open community, but its
copyleft obligations can be impractical for embedding Domovoy in a proprietary
product or offering it as a closed hosted service. For those cases a separate
commercial license can be arranged — contact the author.

---

<div align="center">
<sub>Domovoy — the house spirit, in software.</sub>
</div>
