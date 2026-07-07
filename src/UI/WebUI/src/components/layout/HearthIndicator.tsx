// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useEffect, useState } from 'react';
import { Box, Tooltip } from '@mui/material';
import { keyframes } from '@mui/system';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { metricsApi } from '../../api/metrics';

const REFRESH_INTERVAL_MS = 60_000;

// The hearth "breathes" slowly while everything is fine — the one persistent
// sign of the house spirit in the chrome. Anything wrong and it stops moving.
const breathe = keyframes`
  0%, 100% { opacity: 0.45; transform: scale(0.85); }
  50% { opacity: 1; transform: scale(1); }
`;

type HearthState = 'burning' | 'smoldering' | 'cold';

/**
 * Hearth status ember next to the brand: a quiet presence indicator instead of a
 * technical health dot. Breathing = all services up; still amber = degraded;
 * grey = metrics unreachable. Links to /status; honors prefers-reduced-motion.
 */
export default function HearthIndicator() {
  const { t } = useTranslation('common');
  const [state, setState] = useState<HearthState>('burning');
  const [counts, setCounts] = useState({ up: 0, total: 0 });

  useEffect(() => {
    let cancelled = false;
    const poll = async () => {
      try {
        const services = await metricsApi.getServicesStatus();
        if (cancelled) return;
        const up = services.filter((s) => s.isUp).length;
        setCounts({ up, total: services.length });
        if (services.length === 0) setState('cold');
        else if (up === services.length) setState('burning');
        else setState('smoldering');
      } catch {
        if (cancelled) return;
        setCounts({ up: 0, total: 0 });
        setState('cold');
      }
    };
    poll();
    const interval = setInterval(poll, REFRESH_INTERVAL_MS);
    return () => { cancelled = true; clearInterval(interval); };
  }, []);

  const label = state === 'burning'
    ? t('hearth.lit', { count: counts.total })
    : state === 'smoldering'
      ? t('hearth.smoldering', { up: counts.up, total: counts.total })
      : t('hearth.cold');

  return (
    <Tooltip title={label}>
      <Box
        component={Link}
        to="/status"
        aria-label={t('hearth.ariaLabel')}
        sx={() => {
          // Same CSS-variable access pattern as the Brand gradient (works under CssVarsProvider).
          const ember = 'var(--mui-palette-secondary-main)';
          const warn = 'var(--mui-palette-warning-main)';
          const cold = 'var(--mui-palette-text-disabled)';
          return {
            width: 14, height: 14, borderRadius: '50%', flexShrink: 0,
            display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
            '&::after': {
              content: '""',
              width: 8, height: 8, borderRadius: '50%',
              backgroundColor: state === 'burning' ? ember : state === 'smoldering' ? warn : cold,
              boxShadow: state === 'burning' ? `0 0 6px 1px ${ember}` : 'none',
              animation: state === 'burning' ? `${breathe} 4s ease-in-out infinite` : 'none',
              '@media (prefers-reduced-motion: reduce)': { animation: 'none' },
            },
          };
        }}
      />
    </Tooltip>
  );
}
