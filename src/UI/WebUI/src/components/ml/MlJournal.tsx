// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Box, Card, CardContent, Chip, Divider, Stack, Tooltip, Typography,
} from '@mui/material';
import HistoryToggleOffRoundedIcon from '@mui/icons-material/HistoryToggleOffRounded';
import { mlApi, MlActivityEntry } from '../../api/ml';
import { fmtDateTime, fmtRelativeShort } from '../../i18n/format';

/**
 * The ML-activity journal (roadmap Epic 3I) — the visible record of what the trainer and proposers have been
 * doing, kept OUT of the operational logs. Its own tab on the ML page so it never clutters the task list.
 * Self-contained: fetches its own feed and hides on error.
 */
export default function MlJournal() {
  const { t } = useTranslation('models');
  const [activity, setActivity] = useState<MlActivityEntry[] | null>(null);

  const load = useCallback(() => {
    mlApi.getActivity(50).then(setActivity).catch(() => setActivity((prev) => prev ?? []));
  }, []);

  useEffect(() => { load(); }, [load]);

  // A localized one-liner from the entry's reason slug + counters; falls back to the English note.
  const line = (e: MlActivityEntry): string => {
    const m = e.metrics ?? {};
    return t(`journal.reason.${e.reason}`, {
      tasks: Math.round(m.tasks ?? 0),
      created: Math.round(m.created ?? 0),
      candidates: Math.round(m.candidates ?? m.hypotheses ?? 0),
      historyDays: (m.historyDays ?? 0).toFixed(1),
      requiredDays: Math.round(m.requiredDays ?? 0),
      defaultValue: e.note ?? e.reason,
    });
  };

  const outcomeColor = (o: string): 'success' | 'default' | 'info' | 'error' =>
    o === 'ok' ? 'success' : o === 'error' ? 'error' : o === 'skipped' ? 'info' : 'default';

  return (
    <Card variant="outlined">
      <CardContent sx={{ '&:last-child': { pb: 2 } }}>
        <Stack direction="row" alignItems="center" spacing={1} mb={2}>
          <HistoryToggleOffRoundedIcon fontSize="small" color="disabled" />
          <Typography variant="subtitle2">{t('journal.title')}</Typography>
          <Typography variant="caption" color="text.secondary">· {t('journal.caption')}</Typography>
        </Stack>

        {activity && activity.length === 0 ? (
          <Typography variant="body2" color="text.secondary">{t('journal.empty')}</Typography>
        ) : (
          <Stack divider={<Divider flexItem />} spacing={1.25}>
            {(activity ?? []).map((e) => (
              <Stack key={e.id} direction="row" spacing={1.5} alignItems="baseline">
                <Tooltip title={fmtDateTime(e.timestamp)}>
                  <Typography variant="caption" sx={{ color: 'text.disabled', flexShrink: 0, width: 72 }}>
                    {fmtRelativeShort(e.timestamp)}
                  </Typography>
                </Tooltip>
                <Chip
                  size="small"
                  variant="outlined"
                  color={outcomeColor(e.outcome)}
                  label={t(`journal.source.${e.source}`, { defaultValue: e.source })}
                  sx={{ flexShrink: 0 }}
                />
                <Box sx={{ minWidth: 0 }}>
                  <Typography variant="body2" color="text.secondary">{line(e)}</Typography>
                </Box>
              </Stack>
            ))}
          </Stack>
        )}
      </CardContent>
    </Card>
  );
}
