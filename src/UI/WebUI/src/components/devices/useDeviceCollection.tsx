// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import i18n from 'i18next';
import { HubConnection } from '@microsoft/signalr';
import {
  capabilityDevicesApi, CapabilityDevice, isUnassignedZone,
} from '../../api/capabilityDevices';
import { Zone } from '../../api/zones';
import { buildDeviceHubConnection, startDeviceHub } from '../../api/deviceHub';
import {
  applyDeviceCommand, applyDeviceState, capabilityDevices, patchAliases, patchDevice, zones as zonesResource,
} from '../../store/liveData';
import type { CommandFn } from './CapabilityControls';
import { deviceLabel, proposeZoneName } from './deviceNaming';
import { useDeviceRename } from './useDeviceRename';

/**
 * Одна коллекция устройств на всё приложение: список, зоны, живое состояние по SignalR и действия
 * над устройством (команда, зона, псевдоним, тип).
 *
 * Зачем. Главная страница и реестр устройств несли около 120 строк построчной копипасты — свой fetch,
 * свой `zoneName`, свои четыре обработчика, свой хаб — и, что важнее, каждая держала СВОЙ список.
 * Главная жила по SignalR, реестр — на 20-секундном поллинге, и один и тот же дом выглядел на двух
 * страницах по-разному. Теперь список общий (см. `store/liveData`), а хаб один на всех подписчиков.
 */

// Один хаб на приложение: подписчики считаются, последний уходящий останавливает соединение.
// Раньше каждая страница поднимала своё соединение и получала весь поток состояний независимо.
let hub: HubConnection | null = null;
let hubUsers = 0;
let hubCancelled = false;

function acquireHub(): () => void {
  hubUsers += 1;
  if (hubUsers === 1) {
    hubCancelled = false;
    const conn = buildDeviceHubConnection();

    conn.on('DeviceStateUpdated', (deviceId: string, state: Record<string, unknown>) => {
      applyDeviceState(deviceId, state);
    });
    // Новое устройство в доме — перечитываем список: описания capability по хабу не приходят.
    conn.on('DeviceDiscovered', () => { capabilityDevices.refresh(); });
    conn.onclose(() => { if (!hubCancelled) startDeviceHub(conn, () => hubCancelled); });

    startDeviceHub(conn, () => hubCancelled);
    hub = conn;
  }

  return () => {
    hubUsers -= 1;
    if (hubUsers === 0) {
      hubCancelled = true;
      hub?.stop();
      hub = null;
    }
  };
}

export interface DeviceCollection {
  devices: CapabilityDevice[];
  zones: Zone[];
  loading: boolean;
  /** Последняя ошибка действия/загрузки; `null` очищает её. */
  error: string | null;
  setError: (value: string | null) => void;
  refresh: () => Promise<void>;
  /** Имя зоны устройства с локализованными запасными вариантами. */
  zoneName: (zoneId?: string | null) => string;
  handleCommand: CommandFn;
  handleAssignZone: (deviceId: string, zoneId: string | null) => void;
  handleSetAlias: (deviceId: string, alias: string | null) => void;
  handleSetArchetype: (deviceId: string, archetype: string | null) => void;
  /** Диалог переименования при смене зоны (Эпик 3G) — вставить в дерево страницы. */
  renameDialog: React.ReactNode;
}

export function useDeviceCollection(): DeviceCollection {
  const { t } = useTranslation('devices');
  const [error, setError] = useState<string | null>(null);

  const devicesState = capabilityDevices.use();
  const zonesState = zonesResource.use();
  const devices = devicesState.data ?? [];
  const zones = zonesState.data ?? [];

  useEffect(() => acquireHub(), []);

  useEffect(() => {
    // i18n.t, а не хук t: зависимость от t пересоздавала бы эффекты вокруг соединения при смене языка.
    if (devicesState.error) setError(i18n.t('devices:errors.loadDevices'));
  }, [devicesState.error]);

  const unassignedLabel = t('unassigned');
  const zoneName = useCallback(
    (zoneId?: string | null): string => {
      if (isUnassignedZone(zoneId)) return unassignedLabel;
      return zones.find((z) => z.id === zoneId)?.name ?? unassignedLabel;
    },
    [zones, unassignedLabel],
  );

  const handleCommand = useCallback<CommandFn>((deviceId, set) => {
    applyDeviceCommand(deviceId, set); // оптимистично: контрол отражает намерение сразу
    capabilityDevicesApi.sendCommand(deviceId, set)
      .catch(() => setError(i18n.t('devices:errors.command')));
  }, []);

  const { dialog: renameDialog, requestRename } = useDeviceRename(patchAliases);

  const handleAssignZone = useCallback((deviceId: string, zoneId: string | null) => {
    const device = devices.find((d) => d.id === deviceId);
    patchDevice(deviceId, { zoneId: zoneId ?? '' });
    capabilityDevicesApi.assignZone(deviceId, zoneId)
      .catch(() => setError(i18n.t('devices:errors.assignZone')));

    if (device) {
      const newZoneName = zoneId ? (zones.find((z) => z.id === zoneId)?.name ?? null) : null;
      const current = deviceLabel(device);
      requestRename([{ device, current, proposed: proposeZoneName(current, newZoneName, zones.map((z) => z.name)) }]);
    }
  }, [devices, zones, requestRename]);

  const handleSetAlias = useCallback((deviceId: string, alias: string | null) => {
    patchDevice(deviceId, { alias });
    capabilityDevicesApi.setAlias(deviceId, alias)
      .catch(() => setError(i18n.t('devices:errors.setAlias')));
  }, []);

  const handleSetArchetype = useCallback((deviceId: string, archetype: string | null) => {
    patchDevice(deviceId, { archetype });
    capabilityDevicesApi.setArchetype(deviceId, archetype)
      .catch(() => setError(i18n.t('devices:errors.setArchetype')));
  }, []);

  return {
    devices,
    zones,
    loading: devicesState.loading,
    error,
    setError,
    refresh: capabilityDevices.refresh,
    zoneName,
    handleCommand,
    handleAssignZone,
    handleSetAlias,
    handleSetArchetype,
    renameDialog,
  };
}
