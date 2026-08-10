// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useEffect, useState } from 'react';
import { Box, Tooltip } from '@mui/material';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { metricsApi } from '../../api/metrics';
import { activityApi } from '../../api/activity';
import { homeMode, pendingProposals } from '../../store/liveData';
import HearthAvatar, { HearthStatus } from '../common/HearthAvatar';

const REFRESH_INTERVAL_MS = 30_000;
// An automation acting within this window keeps the spirit visibly busy between polls.
const ACTIVE_WINDOW_MS = 60_000;

/**
 * The brand avatar in the sidebar header: HearthAvatar carrying the old HearthIndicator duty —
 * avatar and status ember are now one mark. Status priority: metrics unreachable → offline,
 * degraded services → alert, pending proposals → attention, an automation just acted → active,
 * night mode → sleeping, otherwise calm. Links to /status like the ember did.
 */
export default function BrandHearth() {
  const { t } = useTranslation('common');
  const [services, setServices] = useState<{ up: number; total: number; reachable: boolean }>(
    { up: 0, total: 0, reachable: true },
  );
  const [busy, setBusy] = useState(false);

  // Предложения и режим — из общего слоя (их читает ещё и навигация вокруг этого очага, и дайджест).
  const pending = pendingProposals.use().data?.length ?? 0;
  const night = homeMode.use().data?.mode === 'Night';

  useEffect(() => {
    let cancelled = false;
    const poll = () => {
      metricsApi.getServicesStatus()
        .then((list) => {
          if (cancelled) return;
          setServices({ up: list.filter((s) => s.isUp).length, total: list.length, reachable: true });
        })
        .catch(() => { if (!cancelled) setServices({ up: 0, total: 0, reachable: false }); });
      // Собственное короткое окно «только что сработало» — общего ресурса с такими границами нет.
      activityApi.get({ source: 'automation', from: new Date(Date.now() - ACTIVE_WINDOW_MS).toISOString(), limit: 5 })
        .then((entries) => { if (!cancelled) setBusy(entries.length > 0); })
        .catch(() => { if (!cancelled) setBusy(false); });
    };
    poll();
    const interval = setInterval(poll, REFRESH_INTERVAL_MS);
    return () => { cancelled = true; clearInterval(interval); };
  }, []);

  let status: HearthStatus;
  let label: string;
  if (!services.reachable || services.total === 0) {
    status = 'offline';
    label = t('hearth.cold');
  } else if (services.up < services.total) {
    status = 'alert';
    label = t('hearth.smoldering', { up: services.up, total: services.total });
  } else if (pending > 0) {
    status = 'attention';
    label = t('hearth.attention', { count: pending });
  } else if (busy) {
    status = 'active';
    label = t('hearth.active');
  } else if (night) {
    status = 'sleeping';
    label = t('hearth.sleeping');
  } else {
    status = 'calm';
    label = t('hearth.lit', { count: services.total });
  }

  return (
    <Tooltip title={label}>
      <Box
        component={Link}
        to="/status"
        aria-label={t('hearth.ariaLabel')}
        sx={{ display: 'inline-flex', borderRadius: '50%', flexShrink: 0 }}
      >
        <HearthAvatar size={34} status={status} />
      </Box>
    </Tooltip>
  );
}
