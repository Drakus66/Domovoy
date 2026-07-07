// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useEffect, useState } from 'react';
import { Typography, Link as MuiLink } from '@mui/material';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { modeApi } from '../../api/mode';
import { activityApi } from '../../api/activity';
import { proposalsApi } from '../../api/proposals';

const REFRESH_INTERVAL_MS = 60_000;

// One line under the dashboard title, spoken by the house spirit: current home
// rhythm + what it did today + what awaits approval. Every segment is optional —
// a failed fetch just drops its part instead of breaking the header.
export default function DomovoyDigest() {
  const { t } = useTranslation('common');
  const [mode, setMode] = useState<string | null>(null);
  const [actionsToday, setActionsToday] = useState<number | null>(null);
  const [pending, setPending] = useState<number | null>(null);

  useEffect(() => {
    let cancelled = false;
    const load = () => {
      modeApi.getMode()
        .then((s) => { if (!cancelled) setMode(s.mode); })
        .catch(() => undefined);
      const midnight = new Date();
      midnight.setHours(0, 0, 0, 0);
      activityApi.get({ source: 'automation', from: midnight.toISOString(), limit: 500 })
        .then((entries) => { if (!cancelled) setActionsToday(entries.length); })
        .catch(() => undefined);
      proposalsApi.list('Proposed')
        .then((list) => { if (!cancelled) setPending(list.length); })
        .catch(() => undefined);
    };
    load();
    const interval = setInterval(load, REFRESH_INTERVAL_MS);
    return () => { cancelled = true; clearInterval(interval); };
  }, []);

  const parts: React.ReactNode[] = [];
  if (mode) {
    parts.push(t([`digest.greeting.${mode}`, 'digest.greeting.mode'], { mode }));
  }
  if (actionsToday !== null) {
    parts.push(actionsToday > 0
      ? t('digest.adjustmentsToday', { count: actionsToday })
      : t('digest.quietDay'));
  }
  if (pending !== null && pending > 0) {
    parts.push(
      <MuiLink key="proposals" component={Link} to="/proposals" color="inherit" underline="hover">
        {t('digest.suggestionsWaiting', { count: pending })}
      </MuiLink>,
    );
  }
  if (parts.length === 0) return null;

  return (
    <Typography variant="body2" color="text.secondary" mt={0.25}>
      {parts.map((p, i) => (
        <span key={i}>{i > 0 && ' · '}{p}</span>
      ))}
    </Typography>
  );
}
