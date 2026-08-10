// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import i18n from 'i18next';
import { capabilityDevicesApi, CapabilityDevice } from '../api/capabilityDevices';
import { modeApi, HomeState } from '../api/mode';
import { Proposal, proposalsApi } from '../api/proposals';
import { zonesApi, Zone } from '../api/zones';
import { deviceLabel } from '../components/devices/deviceNaming';
import { createSharedResource } from './sharedResource';
import { useUIStore } from './uiStore';

/**
 * «Горячая четвёрка» главной страницы, каждая — один опрос на всё приложение
 * (см. [`sharedResource`](./sharedResource.ts) о том, почему это понадобилось).
 *
 * Интервалы оставлены прежними: смысл правки — убрать дублирование одинаковых запросов, а не
 * менять частоту, с которой дом обновляется на экране.
 */

/** Очередь предложений. Раньше её поллили четыре компонента ради одного и того же `.length`. */
export const pendingProposals = createSharedResource<Proposal[]>(
  () => proposalsApi.list('Proposed'),
  60_000,
);

/** Режим дома. Раньше — три независимых опроса, включая родителя и его же ребёнка. */
export const homeMode = createSharedResource<HomeState>(() => modeApi.getMode(), 60_000);

/**
 * Реестр устройств — общий для всех страниц. Полл остаётся страховкой; живое состояние приходит
 * по SignalR и вливается сюда через {@link applyDeviceState}, поэтому подписчик видит дом
 * одинаково независимо от того, на какой он странице (раньше главная была живой, а реестр отставал
 * до двадцати секунд — пользователь видел два состояния одного дома).
 */
export const capabilityDevices = createSharedResource<CapabilityDevice[]>(
  () => capabilityDevicesApi.getDevices().then(announceNewDevices),
  20_000,
  () => { knownDeviceIds = null; },
);

// Идентификаторы, известные по последней успешной загрузке; null до первой — иначе перезапуск
// объявлял бы новосельем весь дом сразу. Живёт здесь, а не на странице: приветствие относится к
// коллекции, а не к тому, кто на неё сейчас смотрит.
let knownDeviceIds: Set<string> | null = null;

function announceNewDevices(list: CapabilityDevice[]): CapabilityDevice[] {
  if (knownDeviceIds) {
    for (const d of list) {
      if (!knownDeviceIds.has(d.id)) {
        useUIStore.getState().showNotification('info', i18n.t('common:newResident', { name: deviceLabel(d) }));
      }
    }
  }
  knownDeviceIds = new Set(list.map((d) => d.id));
  return list;
}

/** Зоны — необязательные метаданные группировки, но их читает почти каждая страница устройств. */
export const zones = createSharedResource<Zone[]>(() => zonesApi.getZones(), 60_000);

/**
 * Словарь архетипов из контракта. Меняется только с релизом, поэтому опрос редкий; запасной список
 * в `api/capabilityDevices` работает, пока ответ не пришёл.
 */
export const deviceArchetypes = createSharedResource<string[]>(
  () => capabilityDevicesApi.getArchetypes(),
  30 * 60_000,
);

/** Влить пришедшее по SignalR состояние одного устройства в общий реестр. */
export function applyDeviceState(deviceId: string, state: Record<string, unknown>): void {
  capabilityDevices.set((prev) =>
    prev?.map((d) => (d.id === deviceId ? { ...d, state: { ...d.state, ...state }, isOnline: true } : d)));
}

/** Оптимистично отразить отправленную команду до того, как устройство отчитается. */
export function applyDeviceCommand(deviceId: string, set: Record<string, unknown>): void {
  capabilityDevices.set((prev) =>
    prev?.map((d) => (d.id === deviceId ? { ...d, state: { ...d.state, ...set } } : d)));
}

/** Локально применить правку полей устройства (зона, псевдоним, архетип) до подтверждения сервером. */
export function patchDevice(deviceId: string, patch: Partial<CapabilityDevice>): void {
  capabilityDevices.set((prev) => prev?.map((d) => (d.id === deviceId ? { ...d, ...patch } : d)));
}

/** Применить псевдонимы, назначенные диалогом переименования. */
export function patchAliases(updates: Record<string, string>): void {
  capabilityDevices.set((prev) => prev?.map((d) => (d.id in updates ? { ...d, alias: updates[d.id] } : d)));
}
