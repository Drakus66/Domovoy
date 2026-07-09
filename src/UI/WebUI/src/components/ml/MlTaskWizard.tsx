// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Dialog, DialogTitle, DialogContent, DialogActions, Button, Stack, TextField, MenuItem,
  Typography, Chip, Alert, Skeleton, FormControlLabel, Switch, Box,
} from '@mui/material';
import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded';
import WarningAmberRoundedIcon from '@mui/icons-material/WarningAmberRounded';
import { mlApi, MlTask, NewMlTask, DataCheck } from '../../api/ml';
import { CapabilityDevice } from '../../api/capabilityDevices';

/** Capability kinds the template registry can model (Number → regression, Boolean → binary, Enum → multiclass). */
const TRAINABLE_KINDS = new Set(['Number', 'Boolean', 'Enum']);

const defaultDraft = (): NewMlTask => ({
  name: '',
  targetCapability: '',
  enabled: true,
  windowDays: 30,
  minSamples: 20,
  trainIntervalHours: 24,
  trainZoneModels: true,
  zonePromotionMargin: 0.25,
  clampMin: null,
  clampMax: null,
  keepLastVersions: 10,
});

/**
 * Create/edit dialog for an ML training task (Epic 2P): pick WHAT to learn from the live capability list and
 * see instantly whether there is enough history to train (the data-sufficiency check), then tune FROM WHAT
 * (window, zone models) and WITHIN WHICH LIMITS (clamps, numeric targets only). Too little data warns but
 * never blocks — the task simply starts training once history accrues.
 */
export default function MlTaskWizard({
  open, task, devices, existingTargets, onClose, onSaved,
}: {
  open: boolean;
  /** Present ⇒ edit an existing task; absent ⇒ create. */
  task?: MlTask | null;
  devices: CapabilityDevice[];
  /** Targets already taken by other tasks (case-insensitive), for the duplicate warning. */
  existingTargets: string[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const { t } = useTranslation('models');
  const [draft, setDraft] = useState<NewMlTask>(defaultDraft());
  const [check, setCheck] = useState<DataCheck | null>(null);
  const [checking, setChecking] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setError(null);
    setCheck(null);
    setDraft(task
      ? {
          name: task.name,
          targetCapability: task.targetCapability,
          enabled: task.enabled,
          windowDays: task.windowDays,
          minSamples: task.minSamples,
          trainIntervalHours: task.trainIntervalHours,
          trainZoneModels: task.trainZoneModels,
          zonePromotionMargin: task.zonePromotionMargin,
          clampMin: task.clampMin ?? null,
          clampMax: task.clampMax ?? null,
          keepLastVersions: task.keepLastVersions,
        }
      : defaultDraft());
  }, [open, task]);

  // Distinct capabilities across the house, annotated with their value-type — the "what can be learned" menu.
  const capabilities = useMemo(() => {
    const byId = new Map<string, { id: string; kind: string; trainable: boolean }>();
    for (const d of devices) {
      for (const c of d.capabilities) {
        if (!byId.has(c.id)) byId.set(c.id, { id: c.id, kind: String(c.kind), trainable: TRAINABLE_KINDS.has(String(c.kind)) });
      }
    }
    return [...byId.values()].sort((a, b) => Number(b.trainable) - Number(a.trainable) || a.id.localeCompare(b.id));
  }, [devices]);

  const selectedKind = capabilities.find((c) => c.id === draft.targetCapability)?.kind
    ?? check?.kind ?? '';
  const isNumeric = selectedKind === 'Number';
  const duplicate = !task && existingTargets.some((x) => x.toLowerCase() === draft.targetCapability.toLowerCase());

  // Instant data-sufficiency check (debounced) whenever the target or the window changes (Epic 2P).
  useEffect(() => {
    if (!open || !draft.targetCapability) { setCheck(null); return; }
    setChecking(true);
    const timer = setTimeout(() => {
      mlApi.dataCheck(draft.targetCapability, draft.windowDays, draft.minSamples, draft.trainZoneModels)
        .then(setCheck)
        .catch(() => setCheck(null))
        .finally(() => setChecking(false));
    }, 400);
    return () => { clearTimeout(timer); setChecking(false); };
  }, [open, draft.targetCapability, draft.windowDays, draft.minSamples, draft.trainZoneModels]);

  const save = async () => {
    setSaving(true);
    setError(null);
    const payload: NewMlTask = {
      ...draft,
      name: draft.name.trim() || draft.targetCapability,
      clampMin: isNumeric ? draft.clampMin : null,
      clampMax: isNumeric ? draft.clampMax : null,
    };
    try {
      if (task) await mlApi.updateTask(task.id, payload);
      else await mlApi.createTask(payload);
      onSaved();
      onClose();
    } catch {
      setError(t(task ? 'wizard.errors.update' : 'wizard.errors.create'));
    } finally {
      setSaving(false);
    }
  };

  const global = check?.scopes.find((s) => s.level === 'global');

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{t(task ? 'wizard.editTitle' : 'wizard.title')}</DialogTitle>
      <DialogContent>
        <Stack spacing={2.5} mt={1}>
          <Typography variant="caption" color="text.secondary">{t('wizard.caption')}</Typography>

          <TextField
            select fullWidth required label={t('wizard.target')} value={draft.targetCapability}
            helperText={duplicate ? t('wizard.duplicate') : t('wizard.targetHelp')}
            error={duplicate}
            onChange={(e) => setDraft({ ...draft, targetCapability: e.target.value })}
          >
            {capabilities.map((c) => (
              <MenuItem key={c.id} value={c.id} disabled={!c.trainable}>
                <Stack direction="row" spacing={1} alignItems="center" width="100%">
                  <span>{c.id}</span>
                  <Box flex={1} />
                  <Chip size="small" variant="outlined" label={c.kind} />
                  {!c.trainable && <Chip size="small" variant="outlined" label={t('wizard.noTemplate')} />}
                </Stack>
              </MenuItem>
            ))}
          </TextField>

          {/* Instant "will this train?" feedback (Epic 2P). */}
          {draft.targetCapability && (
            checking || (!check && draft.targetCapability) ? (
              checking ? <Skeleton variant="rounded" height={56} /> : null
            ) : check && (
              <Alert
                icon={global?.sufficient ? <CheckCircleRoundedIcon /> : <WarningAmberRoundedIcon />}
                severity={check.templateAvailable ? (global?.sufficient ? 'success' : 'warning') : 'error'}
              >
                {!check.templateAvailable
                  ? t('wizard.check.noTemplate', { kind: check.kind })
                  : global?.sufficient
                    ? t('wizard.check.enough', { samples: global.samples, days: draft.windowDays })
                    : t('wizard.check.notEnough', { samples: global?.samples ?? 0, required: draft.minSamples })}
                {check.templateAvailable && draft.trainZoneModels && (
                  <Typography variant="caption" display="block" mt={0.5}>
                    {t('wizard.check.zones', {
                      ready: check.scopes.filter((s) => s.level !== 'global' && s.sufficient).length,
                      total: check.scopes.filter((s) => s.level !== 'global').length,
                    })}
                  </Typography>
                )}
              </Alert>
            )
          )}

          <TextField
            fullWidth label={t('wizard.name')} value={draft.name}
            placeholder={draft.targetCapability} helperText={t('wizard.nameHelp')}
            onChange={(e) => setDraft({ ...draft, name: e.target.value })}
          />

          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5}>
            <TextField
              type="number" fullWidth label={t('wizard.windowDays')} value={draft.windowDays}
              onChange={(e) => setDraft({ ...draft, windowDays: Math.max(1, Number(e.target.value)) })}
            />
            <TextField
              type="number" fullWidth label={t('wizard.minSamples')} value={draft.minSamples}
              onChange={(e) => setDraft({ ...draft, minSamples: Math.max(1, Number(e.target.value)) })}
            />
            <TextField
              type="number" fullWidth label={t('wizard.intervalHours')} value={draft.trainIntervalHours}
              onChange={(e) => setDraft({ ...draft, trainIntervalHours: Math.max(1, Number(e.target.value)) })}
            />
          </Stack>

          {isNumeric && (
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5}>
              <TextField
                type="number" fullWidth label={t('wizard.clampMin')} value={draft.clampMin ?? ''}
                helperText={t('wizard.clampHelp')}
                onChange={(e) => setDraft({ ...draft, clampMin: e.target.value === '' ? null : Number(e.target.value) })}
              />
              <TextField
                type="number" fullWidth label={t('wizard.clampMax')} value={draft.clampMax ?? ''}
                onChange={(e) => setDraft({ ...draft, clampMax: e.target.value === '' ? null : Number(e.target.value) })}
              />
            </Stack>
          )}

          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} alignItems={{ sm: 'center' }}>
            <FormControlLabel
              control={<Switch checked={draft.trainZoneModels}
                onChange={(e) => setDraft({ ...draft, trainZoneModels: e.target.checked })} />}
              label={t('wizard.zoneModels')}
            />
            <TextField
              type="number" size="small" label={t('wizard.keepVersions')} value={draft.keepLastVersions}
              sx={{ maxWidth: 180 }}
              onChange={(e) => setDraft({ ...draft, keepLastVersions: Math.max(1, Number(e.target.value)) })}
            />
          </Stack>

          {error && <Alert severity="warning" onClose={() => setError(null)}>{error}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('wizard.cancel')}</Button>
        <Button
          variant="contained" onClick={save}
          disabled={!draft.targetCapability || duplicate || saving
            || (draft.clampMin != null && draft.clampMax != null && draft.clampMin >= draft.clampMax)}
        >
          {t(task ? 'wizard.save' : 'wizard.create')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
