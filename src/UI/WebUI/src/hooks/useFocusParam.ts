// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';

/**
 * Deep-link focus for attribution chips (Epic 2G tail): reads the given query param once (e.g.
 * `/automations?focus={id}`), clears it from the URL and returns the id so the page can scroll to and
 * highlight that entity's card.
 */
export function useFocusParam(param = 'focus'): string | null {
  const [searchParams, setSearchParams] = useSearchParams();
  const [focusId, setFocusId] = useState<string | null>(null);

  useEffect(() => {
    const id = searchParams.get(param);
    if (!id) return;
    setFocusId(id);
    setSearchParams((p) => { p.delete(param); return p; }, { replace: true });
  }, [searchParams, setSearchParams, param]);

  return focusId;
}

/** Callback ref that scrolls the focused card into view once it mounts. */
export function scrollIntoViewRef(focused: boolean) {
  return focused ? (el: HTMLElement | null) => el?.scrollIntoView({ block: 'center' }) : undefined;
}
