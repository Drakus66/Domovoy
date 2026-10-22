// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { ReactNode, useRef, useState, KeyboardEvent, MouseEvent } from 'react';
import { Box, IconButton } from '@mui/material';
import type { SxProps, Theme } from '@mui/material/styles';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';

interface FlipCardProps {
  front: ReactNode;
  back: ReactNode;
  /** aria-label of the flip trigger while the front is showing. */
  flipLabel: string;
  /** aria-label while the back is showing (defaults to flipLabel). */
  backLabel?: string;
  /** Mount the back only after the first flip (for backs that fetch on mount). */
  lazyBack?: boolean;
  /** Position of the trigger button; default top-right. */
  triggerSx?: SxProps<Theme>;
  sx?: SxProps<Theme>;
  onFlipChange?: (flipped: boolean) => void;
}

// Half of the rotation: the moment a side geometrically starts/stops facing the viewer, so the
// visibility handoff below lines up with what the eye sees.
const HALF_TURN = '0.25s';

/**
 * 3D flip container: the front carries the glance (a chart, a headline number), the back the
 * detail. **The front alone defines the card's footprint** — the back overlays it absolutely, so
 * flipping never resizes the card and never shifts the surrounding grid (flip several cards in a
 * row and nothing moves). A back taller than the face scrolls inside; give the back's root
 * `minHeight: '100%'` (not `height`) so the overflow actually reaches the scroll container. The
 * front always stays in the DOM. The trigger is a sibling of the rotor (it must not rotate, and
 * must not nest inside a CardActionArea side). Honors prefers-reduced-motion with an instant swap.
 */
export default function FlipCard({
  front, back, flipLabel, backLabel, lazyBack = false, triggerSx, sx, onFlipChange,
}: FlipCardProps) {
  const [flipped, setFlipped] = useState(false);
  const [backMounted, setBackMounted] = useState(!lazyBack);
  const triggerRef = useRef<HTMLButtonElement>(null);

  const setFlip = (next: boolean) => {
    setFlipped(next);
    if (next) setBackMounted(true);
    onFlipChange?.(next);
  };

  const onTriggerClick = (e: MouseEvent) => {
    e.stopPropagation();
    setFlip(!flipped);
  };

  const onKeyDown = (e: KeyboardEvent) => {
    if (e.key === 'Escape' && flipped) {
      e.stopPropagation();
      setFlip(false);
      triggerRef.current?.focus();
    }
  };

  // Inferred literal type on purpose: SxProps is a union that cannot be spread.
  const sideSx = (active: boolean) => ({
    minWidth: 0,
    backfaceVisibility: 'hidden',
    WebkitBackfaceVisibility: 'hidden',
    // The inactive side leaves the tab order and the a11y tree but stays in the DOM. The delay
    // matches the half-turn — exactly when the side stops (starts) facing the viewer.
    visibility: active ? 'visible' : 'hidden',
    transition: `visibility 0s linear ${HALF_TURN}`,
    '@media (prefers-reduced-motion: reduce)': { transition: 'none' },
  } as const);

  return (
    <Box onKeyDown={onKeyDown} sx={{ position: 'relative', height: '100%', perspective: '1200px', ...sx }}>
      <Box
        sx={{
          position: 'relative',
          height: '100%',
          transformStyle: 'preserve-3d',
          transition: 'transform .5s cubic-bezier(.2,.7,.3,1)',
          transform: flipped ? 'rotateY(180deg)' : 'none',
          '@media (prefers-reduced-motion: reduce)': { transition: 'none' },
        }}
      >
        {/* In flow: the face sizes the rotor, so mounting or showing the back never moves layout. */}
        <Box sx={{ ...sideSx(!flipped), height: '100%' }}>{front}</Box>
        {/* Overlay: exactly the face's box; taller back content scrolls inside the card. */}
        <Box
          sx={{
            ...sideSx(flipped),
            position: 'absolute',
            inset: 0,
            transform: 'rotateY(180deg)',
            overflowY: 'auto',
          }}
        >
          {backMounted ? back : null}
        </Box>
      </Box>
      <IconButton
        ref={triggerRef}
        size="small"
        aria-pressed={flipped}
        aria-label={flipped ? (backLabel ?? flipLabel) : flipLabel}
        onClick={onTriggerClick}
        onMouseDown={(e) => e.stopPropagation()}
        onTouchStart={(e) => e.stopPropagation()}
        sx={{
          position: 'absolute', top: 4, right: 4, zIndex: 2,
          color: 'text.secondary', opacity: 0.8,
          '&:hover': { opacity: 1 },
          '& svg': { fontSize: 18 },
          ...triggerSx,
        }}
      >
        {flipped ? <CloseRoundedIcon /> : <InfoOutlinedIcon />}
      </IconButton>
    </Box>
  );
}
