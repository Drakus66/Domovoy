// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Card, CardContent, Stack, Box, Typography, Chip, Switch, Tooltip, IconButton, Button, Collapse,
  Dialog, DialogTitle, DialogContent, DialogActions, TextField, MenuItem, Alert,
} from '@mui/material';
import PsychologyRoundedIcon from '@mui/icons-material/PsychologyRounded';
import ModelTrainingRoundedIcon from '@mui/icons-material/ModelTrainingRounded';
import EditOutlinedIcon from '@mui/icons-material/EditOutlined';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import ExpandMoreRoundedIcon from '@mui/icons-material/ExpandMoreRounded';
import PlayForWorkRoundedIcon from '@mui/icons-material/PlayForWorkRounded';
import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded';
import ErrorOutlineRoundedIcon from '@mui/icons-material/ErrorOutlineRounded';
import { MlModel, MlTask } from '../../api/ml';
import { blocksApi, BlockCatalogEntry, ControlBlock } from '../../api/blocks';
import { toNewBlock } from '../blocks/blockGraphModel';
import { fmtDateTime } from '../../i18n/format';
import MlTaskDetail from './MlTaskDetail';

const stageKey = (s: number) => (s >= 2 ? 'stage.full' : s === 1 ? 'stage.bounded' : 'stage.shadow');

/**
 * One ML task on the hub (Epic 2P): what it learns, whether/when it last trained (and why not), which models
 * it produced per scope, and — the "make it a tool" part — the consumer governor blocks with their authority
 * stage, editable right here behind an explicit confirmation (the human approves stage + version, decision №1).
 */
export default function MlTaskCard({
  task, models, consumerTypes, consumerBlocks, training, onToggleEnabled, onTrain, onEdit, onDelete, onApply,
  onChanged,
}: {
  task: MlTask;
  /** Registered models of this task's target. */
  models: MlModel[];
  /** Governor block types consuming this target. */
  consumerTypes: BlockCatalogEntry[];
  /** Governor block instances consuming this target. */
  consumerBlocks: ControlBlock[];
  training: boolean;
  onToggleEnabled: (task: MlTask, enabled: boolean) => void;
  onTrain: (task: MlTask) => void;
  onEdit: (task: MlTask) => void;
  onDelete: (task: MlTask) => void;
  onApply: (task: MlTask) => void;
  /** Reload after a block edit (stage/pin) or model deletion. */
  onChanged: () => void;
}) {
  const { t } = useTranslation('models');
  const [expanded, setExpanded] = useState(false);
  const [stageEdit, setStageEdit] = useState<{ block: ControlBlock; next: number } | null>(null);
  const [error, setError] = useState<string | null>(null);

  const status = task.status;
  const scopes = new Set(models.map((m) => (m.scope && m.scope.level !== 'global' ? `${m.scope.level}:${m.scope.key}` : 'global')));
  const latestGlobal = models
    .filter((m) => !m.scope || m.scope.level === 'global' || !m.scope.key)
    .sort((a, b) => b.version - a.version)[0];

  const applyStage = async () => {
    if (!stageEdit) return;
    setError(null);
    try {
      const updated = { ...stageEdit.block, params: { ...stageEdit.block.params, stage: stageEdit.next } };
      await blocksApi.updateBlock(stageEdit.block.id, toNewBlock(updated));
      setStageEdit(null);
      onChanged();
    } catch {
      setError(t('consumers.errors.stage'));
    }
  };

  return (
    <Card variant="outlined">
      <CardContent sx={{ py: 1.75, '&:last-child': { pb: 1.75 } }}>
        <Stack direction="row" spacing={1.5} alignItems="flex-start">
          <PsychologyRoundedIcon color={task.enabled ? 'primary' : 'disabled'} sx={{ mt: 0.5 }} />
          <Box flex={1} minWidth={0}>
            <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
              <Typography fontWeight={700}>{task.name}</Typography>
              <Chip size="small" variant="outlined" label={task.targetCapability} />
              {latestGlobal && (
                <Tooltip title={t('card.qualityHint', { metric: latestGlobal.metric || 'MAE' })}>
                  <Chip size="small" variant="outlined" color="primary"
                    label={t('card.quality', {
                      metric: latestGlobal.metric || 'MAE',
                      score: (latestGlobal.holdoutScore || latestGlobal.holdoutMae).toFixed(2),
                    })} />
                </Tooltip>
              )}
              {models.length > 0 && (
                <Chip size="small" variant="outlined"
                  label={t('card.models', { count: models.length, scopes: scopes.size })} />
              )}
            </Stack>

            {/* Last training attempt — success or the exact reason it didn't train (Epic 2P diagnostics). */}
            <Stack direction="row" spacing={0.75} alignItems="center" mt={0.5} flexWrap="wrap" useFlexGap>
              {status?.lastTrainAt ? (
                <>
                  {status.lastTrainOk
                    ? <CheckCircleRoundedIcon sx={{ fontSize: 16 }} color="success" />
                    : <ErrorOutlineRoundedIcon sx={{ fontSize: 16 }} color="warning" />}
                  <Typography variant="caption" color="text.secondary">
                    {status.lastTrainOk
                      ? t('card.trainedAt', { when: fmtDateTime(status.lastTrainAt), count: status.lastSampleCount })
                      : t('card.trainFailed', { when: fmtDateTime(status.lastTrainAt), message: status.lastMessage ?? '' })}
                  </Typography>
                </>
              ) : (
                <Typography variant="caption" color="text.secondary">{t('card.neverTrained')}</Typography>
              )}
            </Stack>

            {/* Consumers — the blocks this task's models drive, with the authority stage right here. */}
            <Stack spacing={0.5} mt={1}>
              {consumerBlocks.length === 0 ? (
                <Typography variant="caption" color="text.secondary">
                  {consumerTypes.length === 0 ? t('consumers.noTypes') : t('consumers.none')}
                </Typography>
              ) : consumerBlocks.map((b) => {
                const stage = Math.round(b.params.stage ?? 0);
                const pinned = Math.round(b.params.model_version ?? 0);
                return (
                  <Stack key={b.id} direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
                    <Typography variant="body2">{b.name}</Typography>
                    <Tooltip title={t('consumers.stageHint')}>
                      <Chip
                        size="small" variant="outlined" color={stage === 0 ? 'default' : 'primary'}
                        label={t(stageKey(stage))}
                        onClick={() => setStageEdit({ block: b, next: stage })}
                      />
                    </Tooltip>
                    {pinned > 0 && (
                      <Tooltip title={t('consumers.pinnedHint')}>
                        <Chip size="small" variant="outlined" label={t('consumers.pinned', { version: pinned })} />
                      </Tooltip>
                    )}
                    {!b.enabled && <Chip size="small" variant="outlined" label={t('consumers.disabled')} />}
                  </Stack>
                );
              })}
            </Stack>

            {error && <Alert severity="warning" sx={{ mt: 1 }} onClose={() => setError(null)}>{error}</Alert>}
          </Box>

          <Stack direction="row" spacing={0.25} alignItems="center" sx={{ flexShrink: 0 }}>
            <Tooltip title={t('actions.applyHint')}>
              <span>
                <Button size="small" variant="outlined" startIcon={<PlayForWorkRoundedIcon />}
                  disabled={consumerTypes.length === 0} onClick={() => onApply(task)}>
                  {t('actions.apply')}
                </Button>
              </span>
            </Tooltip>
            <Tooltip title={t('actions.trainNowHint')}>
              <span>
                <IconButton disabled={training || !task.enabled} onClick={() => onTrain(task)}>
                  <ModelTrainingRoundedIcon />
                </IconButton>
              </span>
            </Tooltip>
            <Tooltip title={t('actions.editTask')}>
              <IconButton onClick={() => onEdit(task)}><EditOutlinedIcon /></IconButton>
            </Tooltip>
            <Tooltip title={t('actions.deleteTask')}>
              <IconButton onClick={() => onDelete(task)}><DeleteOutlineRoundedIcon /></IconButton>
            </Tooltip>
            <Tooltip title={t(task.enabled ? 'actions.disable' : 'actions.enable')}>
              <Switch size="small" checked={task.enabled}
                onChange={(e) => onToggleEnabled(task, e.target.checked)} />
            </Tooltip>
            <IconButton
              onClick={() => setExpanded(!expanded)}
              sx={{ transform: expanded ? 'rotate(180deg)' : 'none', transition: '0.2s' }}
              aria-label={t('actions.expand')}
            >
              <ExpandMoreRoundedIcon />
            </IconButton>
          </Stack>
        </Stack>

        <Collapse in={expanded} unmountOnExit>
          <MlTaskDetail task={task} models={models} consumerBlocks={consumerBlocks} onChanged={onChanged} />
        </Collapse>
      </CardContent>

      {/* Explicit human approval of an authority-stage change (decision №1): confirm dialog, not a silent toggle. */}
      <Dialog open={stageEdit !== null} onClose={() => setStageEdit(null)} fullWidth maxWidth="xs">
        <DialogTitle>{t('stageDialog.title', { name: stageEdit?.block.name ?? '' })}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} mt={1}>
            <TextField select fullWidth label={t('stageDialog.stage')} value={stageEdit?.next ?? 0}
              onChange={(e) => stageEdit && setStageEdit({ ...stageEdit, next: Number(e.target.value) })}>
              <MenuItem value={0}>{t('stage.shadow')}</MenuItem>
              <MenuItem value={1}>{t('stage.bounded')}</MenuItem>
              <MenuItem value={2}>{t('stage.full')}</MenuItem>
            </TextField>
            <Typography variant="caption" color="text.secondary">
              {t(`stageDialog.hint.${stageEdit ? ['shadow', 'bounded', 'full'][Math.min(2, Math.max(0, stageEdit.next))] : 'shadow'}`)}
            </Typography>
            <Alert severity="info" icon={false}>{t('stageDialog.approval')}</Alert>
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setStageEdit(null)}>{t('stageDialog.cancel')}</Button>
          <Button variant="contained" onClick={applyStage}
            disabled={!stageEdit || stageEdit.next === Math.round(stageEdit.block.params.stage ?? 0)}>
            {t('stageDialog.confirm')}
          </Button>
        </DialogActions>
      </Dialog>
    </Card>
  );
}
