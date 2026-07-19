// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

/**
 * Simplified static hearth mark (house + flame, no face — the favicon geometry) for spots below
 * ~28px where HearthAvatar's face would blur into noise. Replaces the old glowing ember dots;
 * colors follow the active theme the same way the dots did (house = primary, flame = secondary).
 */
export default function HearthMark({ size = 18 }: { size?: number }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 48 48"
      fill="none"
      aria-hidden="true"
      style={{ flexShrink: 0, display: 'block' }}
    >
      <path
        d="M24 6 L41 20 V41 a1 1 0 0 1-1 1 H8 a1 1 0 0 1-1-1 V20 Z"
        fill="none"
        stroke="var(--mui-palette-primary-main)"
        strokeWidth={4.2}
        strokeLinejoin="round"
      />
      <path
        d="M25.5 10 C28.2 16 31.5 20 31.5 27 a7.5 7.5 0 0 1-15 0 C16.5 22.5 18 20 19.5 16.5 C20 18.5 20.5 19.2 21 20.2 C22.3 16.8 24.4 14 25.5 10 Z"
        fill="var(--mui-palette-secondary-main)"
      />
    </svg>
  );
}
