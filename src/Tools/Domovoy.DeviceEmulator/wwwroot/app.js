// Domovoy Device Emulator — minimal UI. Renders virtual devices, lets you set capability values
// (simulating the physical world / overriding actuators) and shows live MQTT activity.

const devicesEl = document.getElementById("devices");
const logEl = document.getElementById("log");
const statusEl = document.getElementById("status");

let devices = {}; // id -> { name, model, capabilities:[], state:{} }

function post(id, capability, value) {
  fetch(`/api/devices/${encodeURIComponent(id)}/set`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ capability, value })
  });
}

function postRegistration(id, registered) {
  fetch(`/api/devices/${encodeURIComponent(id)}/${registered ? "register" : "unregister"}`, { method: "POST" });
}

function fmt(v) {
  if (typeof v === "boolean") return v ? "ON" : "OFF";
  if (typeof v === "number") return Number.isInteger(v) ? v : v.toFixed(1);
  return v ?? "—";
}

function renderDevice(d) {
  const card = document.createElement("div");
  card.className = "card" + (d.registered ? "" : " unregistered");
  card.id = `dev-${d.id}`;

  const head = document.createElement("div");
  head.className = "head";

  const info = document.createElement("div");
  info.className = "info";
  const h = document.createElement("h2");
  h.textContent = d.name;
  const model = document.createElement("div");
  model.className = "model";
  model.textContent = `${d.model ?? ""} · ${d.id}`;
  info.append(h, model);

  const reg = document.createElement("button");
  reg.id = `reg-${d.id}`;
  const setReg = (on) => {
    reg.className = "reg " + (on ? "on" : "off");
    reg.textContent = on ? "● registered" : "○ register";
    reg.title = on ? "Unregister (remove from server)" : "Register (announce to server)";
  };
  setReg(!!d.registered);
  reg.onclick = () => postRegistration(d.id, !reg.classList.contains("on"));

  head.append(info, reg);
  card.append(head);

  for (const cap of d.capabilities) {
    const row = document.createElement("div");
    row.className = "cap";

    const id = document.createElement("span");
    id.className = "id";
    id.textContent = cap.id;

    const tag = document.createElement("span");
    tag.className = "tag";
    tag.textContent = cap.writable ? "actuator" : "sensor";

    const val = document.createElement("span");
    val.className = "val";
    val.id = `val-${d.id}-${cap.id}`;
    val.textContent = fmt(d.state[cap.id]) + (cap.unit ? ` ${cap.unit}` : "");

    row.append(id, tag);

    if (cap.kind === "Boolean") {
      const btn = document.createElement("button");
      btn.className = "toggle";
      const setBtn = (on) => { btn.classList.toggle("on", on); btn.textContent = on ? "ON" : "OFF"; };
      setBtn(!!d.state[cap.id]);
      btn.onclick = () => { const next = !btn.classList.contains("on"); setBtn(next); post(d.id, cap.id, next); };
      btn.id = `ctl-${d.id}-${cap.id}`;
      row.append(btn);
    } else if (cap.kind === "Number") {
      const range = document.createElement("input");
      range.type = "range";
      range.min = cap.min ?? 0;
      range.max = cap.max ?? 100;
      range.step = "0.5";
      range.value = d.state[cap.id] ?? range.min;
      range.id = `ctl-${d.id}-${cap.id}`;
      range.oninput = () => { val.textContent = fmt(parseFloat(range.value)) + (cap.unit ? ` ${cap.unit}` : ""); };
      range.onchange = () => post(d.id, cap.id, parseFloat(range.value));
      row.append(range);
    }

    row.append(val);
    card.append(row);
  }

  return card;
}

function renderAll(list) {
  devices = {};
  devicesEl.innerHTML = "";
  for (const d of list) {
    devices[d.id] = d;
    devicesEl.append(renderDevice(d));
  }
}

function applyState(id, state) {
  const d = devices[id];
  if (!d) return;
  d.state = state;
  for (const cap of d.capabilities) {
    const v = state[cap.id];
    const valEl = document.getElementById(`val-${id}-${cap.id}`);
    if (valEl) valEl.textContent = fmt(v) + (cap.unit ? ` ${cap.unit}` : "");
    const ctl = document.getElementById(`ctl-${id}-${cap.id}`);
    if (!ctl) continue;
    if (cap.kind === "Boolean") { ctl.classList.toggle("on", !!v); ctl.textContent = v ? "ON" : "OFF"; }
    else if (cap.kind === "Number" && document.activeElement !== ctl) { ctl.value = v; }
  }
}

function applyRegistration(id, registered) {
  const d = devices[id];
  if (d) d.registered = registered;
  const card = document.getElementById(`dev-${id}`);
  if (card) card.classList.toggle("unregistered", !registered);
  const reg = document.getElementById(`reg-${id}`);
  if (reg) {
    reg.className = "reg " + (registered ? "on" : "off");
    reg.textContent = registered ? "● registered" : "○ register";
    reg.title = registered ? "Unregister (remove from server)" : "Register (announce to server)";
  }
}

function appendLog(d) {
  const line = document.createElement("div");
  line.className = "line";
  line.innerHTML = `<span class="ts">${d.ts}</span> <span class="${d.source}">[${d.source}]</span> ${d.text ?? ""}`;
  logEl.prepend(line);
  while (logEl.childElementCount > 200) logEl.lastChild.remove();
}

function connect() {
  const ws = new WebSocket(`ws://${location.host}/ws`);
  ws.onopen = () => { statusEl.textContent = "● live"; statusEl.className = "ok"; };
  ws.onclose = () => { statusEl.textContent = "disconnected — retrying…"; statusEl.className = ""; setTimeout(connect, 1500); };
  ws.onmessage = (ev) => {
    const msg = JSON.parse(ev.data);
    if (msg.kind === "snapshot") renderAll(msg.data);
    else if (msg.kind === "state") applyState(msg.deviceId, msg.data);
    else if (msg.kind === "registration") applyRegistration(msg.deviceId, msg.data.registered);
    else if (msg.kind === "log") appendLog(msg.data);
  };
}

connect();
