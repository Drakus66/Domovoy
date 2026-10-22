// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { create } from 'zustand';

/**
 * Подтверждение опасного действия — диалогом интерфейса, а не `window.confirm`.
 *
 * `window.confirm` вызывался в семнадцати местах, при том что нормальный MUI-диалог в проекте уже
 * был написан (в реестре устройств). Это не косметика: системный confirm игнорирует тему — в тёмном
 * интерфейсе он выглядит как окно чужого приложения, — не локализуется дальше своего текста,
 * блокирует поток выполнения и в киоске Android может быть подавлен браузером, то есть «удалить»
 * иногда просто не срабатывало.
 *
 * Диалог смонтирован один раз в `App` (как `NotificationContainer`), поэтому вызов — обычная
 * функция, а не хук: её видно из любого обработчика, и вставлять JSX в каждую страницу не нужно.
 *
 * ```ts
 * if (!await confirmAction({ message: t('confirm.delete', { name: item.name }) })) return;
 * ```
 */
export interface ConfirmRequest {
  /** Текст вопроса. Заголовок и подписи кнопок необязательны — есть общие значения. */
  message: string;
  title?: string;
  confirmLabel?: string;
  cancelLabel?: string;
  /** По умолчанию действие считается необратимым и кнопка подтверждения красная. */
  destructive?: boolean;
}

interface ConfirmStore {
  request: ConfirmRequest | null;
  resolve: ((ok: boolean) => void) | null;
  ask: (request: ConfirmRequest) => Promise<boolean>;
  settle: (ok: boolean) => void;
}

export const useConfirmStore = create<ConfirmStore>((set, get) => ({
  request: null,
  resolve: null,

  ask: (request) =>
    new Promise<boolean>((resolve) => {
      // Второй вопрос поверх первого — сценарий не из этого приложения; предыдущий закрываем
      // отказом, чтобы висящее обещание не осталось неразрешённым навсегда.
      get().resolve?.(false);
      set({ request, resolve });
    }),

  settle: (ok) => {
    const { resolve } = get();
    set({ request: null, resolve: null });
    resolve?.(ok);
  },
}));

/** Спросить подтверждение из любого обработчика. Возвращает выбор пользователя. */
export const confirmAction = (request: ConfirmRequest): Promise<boolean> =>
  useConfirmStore.getState().ask(request);
