import type { SvgIconComponent } from '@mui/icons-material';
import LightbulbRoundedIcon from '@mui/icons-material/LightbulbRounded';
import PaletteRoundedIcon from '@mui/icons-material/PaletteRounded';
import WbIncandescentRoundedIcon from '@mui/icons-material/WbIncandescentRounded';
import PowerSettingsNewRoundedIcon from '@mui/icons-material/PowerSettingsNewRounded';
import ThermostatRoundedIcon from '@mui/icons-material/ThermostatRounded';
import DeviceThermostatRoundedIcon from '@mui/icons-material/DeviceThermostatRounded';
import WaterDropRoundedIcon from '@mui/icons-material/WaterDropRounded';
import Co2RoundedIcon from '@mui/icons-material/Co2Rounded';
import DirectionsRunRoundedIcon from '@mui/icons-material/DirectionsRunRounded';
import SensorDoorRoundedIcon from '@mui/icons-material/SensorDoorRounded';
import LockRoundedIcon from '@mui/icons-material/LockRounded';
import WaterRoundedIcon from '@mui/icons-material/WaterRounded';
import BoltRoundedIcon from '@mui/icons-material/BoltRounded';
import ElectricMeterRoundedIcon from '@mui/icons-material/ElectricMeterRounded';
import BatteryFullRoundedIcon from '@mui/icons-material/BatteryFullRounded';
import LightModeRoundedIcon from '@mui/icons-material/LightModeRounded';
import NetworkCheckRoundedIcon from '@mui/icons-material/NetworkCheckRounded';
import SensorsRoundedIcon from '@mui/icons-material/SensorsRounded';
import DevicesOtherRoundedIcon from '@mui/icons-material/DevicesOtherRounded';
import type { Capability, CapabilityDevice } from '../../api/capabilityDevices';
import { effectiveArchetype } from '../../api/capabilityDevices';

/** Loose coercions — adapters report state as strings, numbers or booleans. */
export const asBool = (v: unknown) => v === true || v === 'ON' || v === 'true' || v === 1 || v === 'on';
export const asNum = (v: unknown) => {
  const n = typeof v === 'number' ? v : Number(v);
  return Number.isFinite(n) ? n : 0;
};

export type DeviceCategory = 'light' | 'switch' | 'climate' | 'sensor' | 'security' | 'energy' | 'other';

/** Accent per category — tuned to read on both the light and dark surfaces. */
export const CATEGORY_ACCENT: Record<DeviceCategory, string> = {
  light: '#FFB020',
  switch: '#5B7CFF',
  climate: '#FF6B6B',
  sensor: '#38BDF8',
  security: '#34D399',
  energy: '#2DD4BF',
  other: '#94A3B8',
};

interface CapMeta {
  label: string;
  Icon: SvgIconComponent;
}

/** Human label + icon for a well-known capability id. */
const CAPABILITY_META: Record<string, CapMeta> = {
  on_off: { label: 'Power', Icon: PowerSettingsNewRoundedIcon },
  brightness: { label: 'Brightness', Icon: LightbulbRoundedIcon },
  color: { label: 'Color', Icon: PaletteRoundedIcon },
  color_temp: { label: 'Color temperature', Icon: WbIncandescentRoundedIcon },
  temperature: { label: 'Temperature', Icon: DeviceThermostatRoundedIcon },
  temperature_setpoint: { label: 'Target temperature', Icon: ThermostatRoundedIcon },
  humidity: { label: 'Humidity', Icon: WaterDropRoundedIcon },
  co2: { label: 'CO₂', Icon: Co2RoundedIcon },
  occupancy: { label: 'Occupancy', Icon: DirectionsRunRoundedIcon },
  contact: { label: 'Contact', Icon: SensorDoorRoundedIcon },
  lock: { label: 'Lock', Icon: LockRoundedIcon },
  valve: { label: 'Valve', Icon: WaterRoundedIcon },
  power: { label: 'Power draw', Icon: BoltRoundedIcon },
  energy: { label: 'Energy', Icon: ElectricMeterRoundedIcon },
  battery: { label: 'Battery', Icon: BatteryFullRoundedIcon },
  illuminance: { label: 'Illuminance', Icon: LightModeRoundedIcon },
  link_quality: { label: 'Link quality', Icon: NetworkCheckRoundedIcon },
};

/** A readable label for a capability id (well-known or namespaced/custom). */
export const capabilityLabel = (id: string): string =>
  CAPABILITY_META[id]?.label ?? id.replace(/[_:]/g, ' ').replace(/\b\w/g, (c) => c.toUpperCase());

export const capabilityIcon = (id: string): SvgIconComponent =>
  CAPABILITY_META[id]?.Icon ?? SensorsRoundedIcon;

const has = (device: CapabilityDevice, id: string) => device.capabilities.some((c) => c.id === id);
const writable = (device: CapabilityDevice, id: string) =>
  device.capabilities.some((c) => c.id === id && c.writable);

/** Backend archetype (Epic 2D) → UI category. Falls back to the capability heuristic if unknown/absent. */
const ARCHETYPE_CATEGORY: Record<string, DeviceCategory> = {
  light: 'light', switch: 'switch', thermostat: 'climate', valve: 'climate',
  climate_sensor: 'sensor', motion: 'sensor', contact: 'sensor', sensor: 'sensor',
  lock: 'security', energy_meter: 'energy', control_block: 'other',
};

export function deviceCategory(device: CapabilityDevice): DeviceCategory {
  // Prefer the authoritative semantic archetype from the backend (Epic 2D).
  const mapped = ARCHETYPE_CATEGORY[effectiveArchetype(device)];
  if (mapped) return mapped;
  // Fallback: derive from the capability set (also used when archetype is unknown/absent).
  if (has(device, 'brightness') || has(device, 'color') || has(device, 'color_temp')) return 'light';
  if (has(device, 'lock')) return 'security';
  if (has(device, 'temperature_setpoint') || has(device, 'valve')) return 'climate';
  if (writable(device, 'on_off')) return 'switch';
  if (has(device, 'power') || has(device, 'energy')) return 'energy';
  if (device.capabilities.some((c) => !c.writable)) return 'sensor';
  return 'other';
}

/** The capability the tile should foreground (its quick-control / headline state). */
export function primaryCapability(device: CapabilityDevice): Capability | undefined {
  const order = [
    'on_off', 'brightness', 'lock', 'temperature_setpoint', 'temperature',
    'occupancy', 'contact', 'humidity', 'co2', 'power', 'illuminance', 'battery',
  ];
  for (const id of order) {
    const cap = device.capabilities.find((c) => c.id === id);
    if (cap) return cap;
  }
  return device.capabilities[0];
}

const fmtNum = (v: unknown, unit?: string | null) => {
  const n = asNum(v);
  const rounded = Math.round(n * 10) / 10;
  return `${rounded}${unit ? ` ${unit}` : ''}`;
};

export interface DeviceVisual {
  category: DeviceCategory;
  accent: string;
  Icon: SvgIconComponent;
  /** "Energized" tiles get an accent glow (a lamp that's on, a triggered sensor). */
  isActive: boolean;
  /** Headline state line, e.g. "On · 80%", "21.5 °C", "Open". */
  primary: string;
  /** Sub-line, e.g. model or adapter. */
  secondary: string;
}

export function describeDevice(device: CapabilityDevice): DeviceVisual {
  const category = deviceCategory(device);
  const accent = CATEGORY_ACCENT[category];
  const state = device.state ?? {};
  const Icon = category === 'sensor' || category === 'energy'
    ? capabilityIcon(primaryCapability(device)?.id ?? '')
    : capabilityIconForCategory(category);

  const on = 'on_off' in state ? asBool(state.on_off) : asNum(state.brightness) > 0;
  let isActive = false;
  let primary = '—';

  switch (category) {
    case 'light': {
      const bri = 'brightness' in state ? Math.round(asNum(state.brightness)) : undefined;
      isActive = on;
      primary = on ? `On${bri !== undefined ? ` · ${bri}%` : ''}` : 'Off';
      break;
    }
    case 'switch': {
      isActive = on;
      primary = on ? 'On' : 'Off';
      break;
    }
    case 'security': {
      const locked = asBool(state.lock);
      isActive = !locked; // unlocked = needs attention
      primary = locked ? 'Locked' : 'Unlocked';
      break;
    }
    case 'climate': {
      if ('temperature_setpoint' in state) primary = `${fmtNum(state.temperature_setpoint, '°C')} target`;
      else if ('temperature' in state) primary = fmtNum(state.temperature, '°C');
      else if ('valve' in state) primary = fmtNum(state.valve, '%');
      break;
    }
    case 'energy': {
      if ('power' in state) primary = fmtNum(state.power, 'W');
      else if ('energy' in state) primary = fmtNum(state.energy, 'kWh');
      break;
    }
    case 'sensor':
    default: {
      primary = sensorHeadline(device, state);
      isActive = asBool(state.occupancy) || asBool(state.contact);
      break;
    }
  }

  const secondary = device.model || device.adapterSource;
  return { category, accent, Icon, isActive, primary, secondary };
}

function sensorHeadline(device: CapabilityDevice, state: Record<string, unknown>): string {
  if ('temperature' in state) return fmtNum(state.temperature, '°C');
  if ('humidity' in state) return fmtNum(state.humidity, '%');
  if ('co2' in state) return fmtNum(state.co2, 'ppm');
  if ('illuminance' in state) return fmtNum(state.illuminance, 'lux');
  if ('occupancy' in state) return asBool(state.occupancy) ? 'Motion' : 'Clear';
  if ('contact' in state) return asBool(state.contact) ? 'Open' : 'Closed';
  if ('battery' in state) return fmtNum(state.battery, '%');
  const prim = primaryCapability(device);
  if (prim && prim.id in state) return String(state[prim.id]);
  return '—';
}

function capabilityIconForCategory(category: DeviceCategory): SvgIconComponent {
  switch (category) {
    case 'light': return LightbulbRoundedIcon;
    case 'switch': return PowerSettingsNewRoundedIcon;
    case 'security': return LockRoundedIcon;
    case 'climate': return ThermostatRoundedIcon;
    case 'energy': return BoltRoundedIcon;
    case 'sensor': return SensorsRoundedIcon;
    default: return DevicesOtherRoundedIcon;
  }
}

/** Human-readable value for one capability (used in the detail drawer rows). */
export function formatCapabilityValue(cap: Capability, value: unknown): string {
  if (value === undefined || value === null || value === '') return '—';
  if (cap.kind === 'Boolean') return asBool(value) ? 'Yes' : 'No';
  if (cap.kind === 'Number') return fmtNum(value, cap.unit);
  return String(value);
}
