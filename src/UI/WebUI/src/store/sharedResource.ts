// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useCallback, useSyncExternalStore } from 'react';

/**
 * Один опрос на ресурс, сколько бы компонентов его ни читало.
 *
 * Зачем. На простаивающей главной шесть независимых `setInterval` давали около двадцати запросов в
 * минуту при живом SignalR-канале: предложения поллили ЧЕТЫРЕ компонента одновременно (навигация, её
 * же вложенный очаг, дайджест и рельса) — всем нужен был один и тот же `list.length`; режим дома —
 * три; активность — три. Каждый компонент честно писал свой `useEffect` с таймером, и каждый был по
 * отдельности прав.
 *
 * Как. Ресурс объявляется один раз и раздаёт хук. Пока на него подписан хотя бы один компонент,
 * работает ровно один таймер; последний отписавшийся его гасит. Данные лежат в модуле, поэтому
 * подключившийся позже компонент показывает последнее известное значение сразу, а не после своего
 * первого запроса — исчезает расхождение «главная живая, реестр отстаёт».
 *
 * Почему не zustand. Здесь нужен не глобальный стор состояния приложения, а кэш с подсчётом
 * подписчиков и владением таймером; `useSyncExternalStore` даёт ровно это без лишнего слоя. Стор
 * uiStore/themeStore остаются тем, чем были, — состоянием интерфейса.
 */

/** Что видит компонент: последнее значение, признак первой загрузки и ручное обновление. */
export interface SharedResource<T> {
  data: T | undefined;
  /** true, пока не завершилась ПЕРВАЯ загрузка (последующие фоновые обновления не мигают). */
  loading: boolean;
  error: unknown;
  /** Немедленно перечитать и разослать всем подписчикам — после действия пользователя. */
  refresh: () => Promise<void>;
}

interface ResourceState<T> {
  data: T | undefined;
  loading: boolean;
  error: unknown;
}

export interface SharedResourceHandle<T> {
  /** Хук для компонента: подписывается, поддерживает опрос, отдаёт значение. */
  use: (enabled?: boolean) => SharedResource<T>;
  /** Перечитать вне React (например, после успешной команды из обработчика). */
  refresh: () => Promise<void>;
  /** Заменить значение локально, без запроса — для оптимистичных обновлений и push-событий. */
  set: (updater: (prev: T | undefined) => T | undefined) => void;
  /** Текущее значение без подписки. */
  peek: () => T | undefined;
}

/**
 * Сброс всех объявленных ресурсов. Нужен тестам: значение живёт в модуле, а модуль переживает
 * отдельный тест — без сброса соседний тест видел бы данные предыдущего.
 */
const resetters: Array<() => void> = [];

export function resetSharedResources(): void {
  resetters.forEach((reset) => reset());
}

export function createSharedResource<T>(
  fetcher: () => Promise<T>,
  intervalMs: number,
  onReset?: () => void,
): SharedResourceHandle<T> {
  let state: ResourceState<T> = { data: undefined, loading: true, error: undefined };
  const listeners = new Set<() => void>();
  let timer: ReturnType<typeof setInterval> | null = null;
  let inFlight: Promise<void> | null = null;

  const emit = () => listeners.forEach((l) => l());

  const setState = (next: Partial<ResourceState<T>>) => {
    state = { ...state, ...next };
    emit();
  };

  const load = (): Promise<void> => {
    // Совпавшие по времени запросы разных компонентов схлопываются в один — иначе первый кадр после
    // монтирования четырёх потребителей дал бы четыре одинаковых запроса.
    if (inFlight) return inFlight;

    inFlight = fetcher()
      .then((data) => { setState({ data, loading: false, error: undefined }); })
      .catch((error) => {
        // Прошлое значение сохраняем: сбой шлюза не должен опустошать интерфейс, который уже что-то
        // показывает (offline-first и здесь тоже).
        setState({ loading: false, error });
      })
      .finally(() => { inFlight = null; });

    return inFlight;
  };

  const subscribe = (listener: () => void) => {
    listeners.add(listener);
    if (listeners.size === 1) {
      load();
      timer = setInterval(load, intervalMs);
    }
    return () => {
      listeners.delete(listener);
      if (listeners.size === 0 && timer !== null) {
        clearInterval(timer);
        timer = null;
      }
    };
  };

  const getSnapshot = () => state;

  resetters.push(() => {
    state = { data: undefined, loading: true, error: undefined };
    inFlight = null;
    onReset?.();
    emit();
  });

  return {
    use(enabled = true) {
      // Функция подписки ОБЯЗАНА быть стабильной: useSyncExternalStore переподписывается всякий раз,
      // когда меняется её идентичность. С инлайновой стрелкой это происходило на каждом рендере —
      // подписчик уходил в ноль и возвращался, а значит гасился и заново заводился таймер И
      // выполнялась внеочередная загрузка. Оптимистичное изменение тут же затиралось ответом
      // сервера, а «один опрос на ресурс» превращался в «опрос на каждый рендер».
      const subscribeToResource = useCallback(
        (listener: () => void) => (enabled ? subscribe(listener) : () => undefined),
        [enabled],
      );

      const snapshot = useSyncExternalStore(subscribeToResource, getSnapshot, getSnapshot);
      return { ...snapshot, refresh: load };
    },
    refresh: load,
    set(updater) {
      setState({ data: updater(state.data), loading: false });
    },
    peek: () => state.data,
  };
}
