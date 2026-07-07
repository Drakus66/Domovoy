import { describe, it, expect } from 'vitest';
import {
  deviceCategory, describeDevice, capabilityIcon, capabilityLabel, formatCapabilityValue,
} from './deviceVisuals';
import type { CapabilityDevice, Capability } from '../../api/capabilityDevices';

// The system Sun sensor (roadmap Epic 2L) — a virtual device with no hardware.
const sunDevice: CapabilityDevice = {
  id: 'sun',
  name: 'Sun',
  adapterSource: 'System',
  model: 'system/sun',
  zoneId: '',
  autoArchetype: 'sun',
  isOnline: true,
  lastUpdated: '2026-07-06T12:00:00Z',
  capabilities: [
    { id: 'sun_elevation', kind: 'Number', writable: false, unit: '°' },
    { id: 'sun_azimuth', kind: 'Number', writable: false, unit: '°' },
    { id: 'is_dark', kind: 'Boolean', writable: false },
    { id: 'sunrise', kind: 'Text', writable: false },
  ],
  state: { sun_elevation: 12.3, sun_azimuth: 176.5, is_dark: false, sunrise: '06:12' },
};

describe('deviceVisuals — system Sun sensor', () => {
  it('maps the sun archetype to the sensor category', () => {
    expect(deviceCategory(sunDevice)).toBe('sensor');
  });

  it('headlines the sun elevation with a degree unit', () => {
    expect(describeDevice(sunDevice).primary).toBe('12.3 °');
  });

  it('has a dedicated icon and translated label for sun capabilities', () => {
    expect(capabilityIcon('sun_elevation')).toBeDefined();
    expect(capabilityLabel('sun_elevation')).toBe('Высота солнца'); // ru default in tests
  });

  it('formats the text sunrise value verbatim and the boolean is_dark as yes/no', () => {
    const sunrise = sunDevice.capabilities.find((c) => c.id === 'sunrise') as Capability;
    const isDark = sunDevice.capabilities.find((c) => c.id === 'is_dark') as Capability;
    expect(formatCapabilityValue(sunrise, '06:12')).toBe('06:12');
    expect(formatCapabilityValue(isDark, false)).toBe('Нет'); // ru value.no
  });
});

// The system Time and Calendar sensors (roadmap Epic 2L).
const timeDevice: CapabilityDevice = {
  id: 'time', name: 'Time', adapterSource: 'System', model: 'system/clock', zoneId: '',
  autoArchetype: 'clock', isOnline: true, lastUpdated: '2026-07-06T09:30:00Z',
  capabilities: [
    { id: 'time_of_day', kind: 'Number', writable: false, unit: 'min' },
    { id: 'clock', kind: 'Text', writable: false },
  ],
  state: { time_of_day: 570, clock: '09:30' },
};

const calendarDevice: CapabilityDevice = {
  id: 'calendar', name: 'Calendar', adapterSource: 'System', model: 'system/calendar', zoneId: '',
  autoArchetype: 'calendar', isOnline: true, lastUpdated: '2026-07-06T09:30:00Z',
  capabilities: [
    { id: 'day_of_week', kind: 'Enum', writable: false },
    { id: 'is_weekend', kind: 'Boolean', writable: false },
    { id: 'is_holiday', kind: 'Boolean', writable: false },
  ],
  state: { day_of_week: 'Monday', is_weekend: false, is_holiday: false },
};

describe('deviceVisuals — system Time & Calendar sensors', () => {
  it('maps clock and calendar archetypes to the sensor category', () => {
    expect(deviceCategory(timeDevice)).toBe('sensor');
    expect(deviceCategory(calendarDevice)).toBe('sensor');
  });

  it('headlines the clock and the day of week', () => {
    expect(describeDevice(timeDevice).primary).toBe('09:30');
    expect(describeDevice(calendarDevice).primary).toBe('Monday');
  });

  it('translates the new capability labels (ru default)', () => {
    expect(capabilityLabel('clock')).toBe('Часы');
    expect(capabilityLabel('is_weekend')).toBe('Выходной');
  });
});
