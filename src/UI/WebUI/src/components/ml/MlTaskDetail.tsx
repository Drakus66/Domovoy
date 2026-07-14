// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Box, Stack, Typography, Chip, Tooltip, IconButton, Divider, TextField, MenuItem, Alert,
  Menu, ListItemText,
} from '@mui/material';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import PushPinOutlinedIcon from '@mui/icons-material/PushPinOutlined';
import { mlApi, DataCheck, MlModel, MlTask } from '../../api/ml';
import { blocksApi, ControlBlock } from '../../api/blocks';
import { toNewBlock } from '../blocks/blockGraphModel';
import { fmtDateTime } from '../../i18n/format';
import ScorecardChart from '../charts/ScorecardChart';

type ScopeGroup = { key: string; level: string; scopeKey: string; models: MlModel[] };

/**
 * Expanded view of one ML task (Epic 2P): its data sufficiency per scope, the version history per scope
 * (delete a stale version, pin a version to a consumer block — the human approving the model version,
 * decision №1), and the per-scope backtest scorecard.
 */
export default function MlTaskDetail({
  task, models, consumerBlocks, onChanged,
}: {
  task: MlTask;
  models: MlModel[];
  consumerBlocks: ControlBlock[];
  onChanged: () => void;
}) {
  const { t } = useTranslation('models');
  const [check, setCheck] = useState<DataCheck | null>(null);
  const [scope, setScope] = useState('global');
  const [error, setError] = useState<string | null>(null);
  const [pinMenu, setPinMenu] = useState<{ anchor: HTMLElement; model: MlModel } | null>(null);

  useEffect(() => {
    let cancelled = false;
    mlApi.dataCheck(task.targetCapability, task.windowDays, task.minSamples, task.trainZoneModels)
      .then((c) => { if (!cancelled) setCheck(c); })
      .catch(() => { if (!cancelled) setCheck(null); });
    return () => { cancelled = true; };
  }, [task.targetCapability, task.windowDays, task.minSamples, task.trainZoneModels]);

  const groups = useMemo<ScopeGroup[]>(() => {
    const byScope = new Map<string, ScopeGroup>();
    for (const m of models) {
      const level = m.scope && m.scope.key ? m.scope.level : 'global';
      const scopeKey = m.scope?.key ?? '';
      const key = level === 'global' ? 'global' : `${level}:${scopeKey}`;
      const g = byScope.get(key) ?? { key, level, scopeKey, models: [] };
      g.models.push(m);
      byScope.set(key, g);
    }
    for (const g of byScope.values()) g.models.sort((a, b) => b.version - a.version);
    // Global first, then zone kinds, then zones.
    const order = (g: ScopeGroup) => (g.level === 'global' ? 0 : g.level === 'zone_kind' ? 1 : 2);
    return [...byScope.values()].sort((a, b) => order(a) - order(b) || a.scopeKey.localeCompare(b.scopeKey));
  }, [models]);

  const scopeLabel = (level: string, key: string): string =>
    level === 'global' ? t('scope.global') : t(level === 'zone_kind' ? 'scope.kind' : 'scope.zone', { key });

  // A version is pinned when some consumer block's model_version param equals it (any scope — pins are
  // per-instance, the serving scope resolves at tick time).
  const pinnedVersions = useMemo(() => {
    const set = new Set<number>();
    for (const b of consumerBlocks) {
      const v = Math.round(b.params.model_version ?? 0);
      if (v > 0) set.add(v);
    }
    return set;
  }, [consumerBlocks]);

  const removeModel = async (m: MlModel) => {
    const warn = pinnedVersions.has(m.version) ? `\n${t('detail.deletePinnedWarn')}` : '';
    if (!window.confirm(t('detail.confirmDelete', { name: m.name, version: m.version }) + warn)) return;
    setError(null);
    try {
      await mlApi.deleteModel(m.id);
      onChanged();
    } catch {
      setError(t('detail.errors.delete'));
    }
  };

  // Pin a model version to a consumer block (Epic 2C model_selection, direct form — decision №1).
  const pinTo = async (block: ControlBlock, version: number) => {
    setPinMenu(null);
    setError(null);
    try {
      const updated = { ...block, params: { ...block.params, model_version: version } };
      await blocksApi.updateBlock(block.id, toNewBlock(updated));
      onChanged();
    } catch {
      setError(t('detail.errors.pin'));
    }
  };

  const selected = groups.find((g) => g.key === scope) ?? groups[0];

  return (
    <Box mt={2}>
      <Divider sx={{ mb: 2 }} />

      {/* Data sufficiency per scope — why it will / won't train (Epic 2P diagnostics). */}
      {check && (
        <Box mb={2}>
          <Typography variant="overline" color="text.secondary">{t('detail.dataTitle')}</Typography>
          <Stack direction="row" spacing={0.75} flexWrap="wrap" useFlexGap mt={0.5}>
            {check.scopes.map((s) => (
              <Tooltip key={`${s.level}:${s.key}`} title={t('detail.dataHint', { samples: s.samples, required: s.required })}>
                <Chip
                  size="small" variant="outlined"
                  color={s.sufficient ? 'success' : 'default'}
                  label={`${scopeLabel(s.level, s.key)}: ${s.samples}`}
                />
              </Tooltip>
            ))}
          </Stack>
          {!check.templateAvailable && (
            <Alert severity="warning" sx={{ mt: 1 }}>{t('wizard.check.noTemplate', { kind: check.kind })}</Alert>
          )}
        </Box>
      )}

      {/* Version history per scope: the registry line the serving model comes from. */}
      <Typography variant="overline" color="text.secondary">{t('detail.modelsTitle')}</Typography>
      {groups.length === 0 ? (
        <Typography variant="body2" color="text.secondary" mb={2}>{t('detail.noModels')}</Typography>
      ) : (
        <Stack spacing={1} mt={0.5} mb={2}>
          {groups.map((g) => (
            <Box key={g.key}>
              <Typography variant="body2" fontWeight={600} mb={0.25}>{scopeLabel(g.level, g.scopeKey)}</Typography>
              <Stack spacing={0.25}>
                {g.models.map((m, i) => (
                  <Stack key={m.id} direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
                    <Chip size="small" variant={i === 0 ? 'filled' : 'outlined'}
                      color={i === 0 ? 'primary' : 'default'} label={`v${m.version}`} />
                    <Typography variant="caption" color="text.secondary">
                      {t('detail.modelLine', {
                        metric: m.metric || 'MAE',
                        score: (m.holdoutScore || m.holdoutMae).toFixed(3),
                        count: m.sampleCount,
                        when: fmtDateTime(m.trainedAt),
                      })}
                    </Typography>
                    <Chip size="small" variant="outlined" label={m.kind} />
                    {pinnedVersions.has(m.version) && (
                      <Chip size="small" variant="outlined" color="primary" label={t('detail.pinnedChip')} />
                    )}
                    <Box flex={1} />
                    {consumerBlocks.length > 0 && (
                      <Tooltip title={t('detail.pinAction')}>
                        <IconButton size="small" onClick={(e) => setPinMenu({ anchor: e.currentTarget, model: m })}>
                          <PushPinOutlinedIcon fontSize="small" />
                        </IconButton>
                      </Tooltip>
                    )}
                    <Tooltip title={t('detail.deleteAction')}>
                      <IconButton size="small" onClick={() => removeModel(m)}>
                        <DeleteOutlineRoundedIcon fontSize="small" />
                      </IconButton>
                    </Tooltip>
                  </Stack>
                ))}
              </Stack>
            </Box>
          ))}
        </Stack>
      )}

      {error && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setError(null)}>{error}</Alert>}

      {/* Per-scope backtest scorecard — "prediction vs fact" for the scope the reviewer picks. */}
      {groups.length > 0 && selected && (
        <Box>
          <Stack direction="row" spacing={1.5} alignItems="center" mb={1}>
            <Typography variant="overline" color="text.secondary">{t('detail.scorecardTitle')}</Typography>
            {groups.length > 1 && (
              <TextField select size="small" value={selected.key} sx={{ minWidth: 200 }}
                onChange={(e) => setScope(e.target.value)}>
                {groups.map((g) => (
                  <MenuItem key={g.key} value={g.key}>{scopeLabel(g.level, g.scopeKey)}</MenuItem>
                ))}
              </TextField>
            )}
          </Stack>
          <ScorecardChart
            days={7}
            target={task.targetCapability}
            level={selected.level === 'global' ? undefined : selected.level}
            scopeKey={selected.level === 'global' ? undefined : selected.scopeKey}
          />
        </Box>
      )}

      {/* Pin target menu: which consumer block should stick to this version (0 = follow latest is the default). */}
      <Menu open={pinMenu !== null} anchorEl={pinMenu?.anchor ?? null} onClose={() => setPinMenu(null)}>
        {consumerBlocks.map((b) => (
          <MenuItem key={b.id} onClick={() => pinMenu && pinTo(b, pinMenu.model.version)}>
            <ListItemText
              primary={t('detail.pinTo', { name: b.name, version: pinMenu?.model.version ?? 0 })}
              secondary={Math.round(b.params.model_version ?? 0) > 0
                ? t('detail.currentPin', { version: Math.round(b.params.model_version ?? 0) })
                : t('detail.followsLatest')}
            />
          </MenuItem>
        ))}
        {consumerBlocks.some((b) => Math.round(b.params.model_version ?? 0) > 0) && (
          <MenuItem onClick={() => {
            const pinned = consumerBlocks.find((b) => Math.round(b.params.model_version ?? 0) > 0);
            if (pinned) pinTo(pinned, 0);
          }}>
            <ListItemText primary={t('detail.unpin')} />
          </MenuItem>
        )}
      </Menu>
    </Box>
  );
}
