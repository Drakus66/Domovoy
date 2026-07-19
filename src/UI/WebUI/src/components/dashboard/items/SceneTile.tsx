// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Button, Card, CardContent, Stack, Typography } from '@mui/material';
import PlayArrowRoundedIcon from '@mui/icons-material/PlayArrowRounded';
import CheckRoundedIcon from '@mui/icons-material/CheckRounded';
import MovieFilterRoundedIcon from '@mui/icons-material/MovieFilterRounded';
import { scenesApi, type Scene } from '../../../api/scenes';

/**
 * One-tap scene activation tile (Epic 3B) — turns a custom tab into a remote. Self-contained: it resolves
 * the scene name itself and stays empty-but-quiet on error (the Scenes page owns error surfacing), mirroring
 * {@link ModesTile}. A brief check confirms the tap fired; activation never mutates the stored scene.
 */
export default function SceneTile({ sceneId }: { sceneId: string }) {
  const { t } = useTranslation(['dashboards', 'scenes']);
  const [scene, setScene] = useState<Scene | null>(null);
  const [busy, setBusy] = useState(false);
  const [flash, setFlash] = useState(false);
  const flashTimer = useRef<number | undefined>(undefined);

  useEffect(() => {
    if (!sceneId) return;
    let cancelled = false;
    scenesApi.getScene(sceneId)
      .then((s) => { if (!cancelled) setScene(s); })
      .catch(() => { /* quiet: the tile still offers activation by id */ });
    return () => { cancelled = true; };
  }, [sceneId]);

  useEffect(() => () => window.clearTimeout(flashTimer.current), []);

  const activate = async () => {
    if (busy || !sceneId) return;
    setBusy(true);
    try {
      await scenesApi.activate(sceneId);
      setFlash(true);
      flashTimer.current = window.setTimeout(() => setFlash(false), 1500);
    } catch {
      /* quiet */
    } finally {
      setBusy(false);
    }
  };

  const title = scene?.name || t('dashboards:widgets.scene');

  return (
    <Card sx={{ height: '100%' }}>
      <CardContent>
        <Stack direction="row" spacing={1} alignItems="center" mb={1.5}>
          <MovieFilterRoundedIcon fontSize="small" color="action" />
          <Typography variant="subtitle2" fontWeight={700} noWrap>{title}</Typography>
        </Stack>
        <Button
          fullWidth
          variant={flash ? 'contained' : 'outlined'}
          color={flash ? 'success' : 'primary'}
          disabled={busy || !sceneId}
          startIcon={flash ? <CheckRoundedIcon /> : <PlayArrowRoundedIcon />}
          onClick={activate}
        >
          {t('scenes:actions.activate')}
        </Button>
      </CardContent>
    </Card>
  );
}
