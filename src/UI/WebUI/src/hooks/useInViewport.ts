// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useEffect, useRef, useState, type RefObject } from 'react';

/**
 * Latch-once viewport visibility (roadmap dashboard fill): lets a tile defer expensive data (its
 * sparkline series) until it actually scrolls near the viewport. Once seen it stays "in view" so the
 * loaded data isn't torn down on scroll. Falls back to immediately-visible where IntersectionObserver
 * is unavailable (jsdom / SSR).
 */
export function useInViewport<T extends Element>(rootMargin = '200px'): [RefObject<T>, boolean] {
  const ref = useRef<T>(null);
  const [inView, setInView] = useState(false);

  useEffect(() => {
    if (inView) return;
    const el = ref.current;
    if (!el) return;
    if (typeof IntersectionObserver === 'undefined') { setInView(true); return; }

    const observer = new IntersectionObserver((entries) => {
      if (entries.some((e) => e.isIntersecting)) {
        setInView(true);
        observer.disconnect();
      }
    }, { rootMargin });
    observer.observe(el);
    return () => observer.disconnect();
  }, [inView, rootMargin]);

  return [ref, inView];
}
