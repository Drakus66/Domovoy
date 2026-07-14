// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink } from 'react-router-dom';
import {
  Dialog, DialogTitle, DialogContent, DialogActions, Button, Stack, TextField, MenuItem,
  Typography, Chip, Alert, Stepper, Step, StepLabel, Box,
} from '@mui/material';
import { blocksApi, BlockCatalogEntry } from '../../api/blocks';
import { CapabilityDevice, effectiveArchetype, isUnassignedZone } from '../../api/capabilityDevices';
import { MlTask } from '../../api/ml';
import type { Zone } from '../../api/zones';
import {
  applicableTypesFor, boundOutputOf, governorTypesFor, measuredInputOf, measuredSourceCandidates,
  suitableDevicesFor,
} from './mlHub';

/**
 * "Apply a model" wizard (Epic 2P, decision №4): turns a trained model into a working control in three steps —
 * pick where (a device exposing the governor's writable output; entry A from a task card) or which model
 * (entry B from a device drawer), confirm the measured-signal source for the drift monitor, then name the new
 * governor block. It starts in Shadow: the model only proposes until the human raises the authority stage.
 */
export default function MlApplyWizard({
  open, task, device, tasks, catalog, devices, zones, onClose, onCreated, onRequestCreateTask,
}: {
  open: boolean;
  /** Entry A: apply THIS task's models to some device. */
  task?: MlTask | null;
  /** Entry B: apply some applicable model to THIS device. */
  device?: CapabilityDevice | null;
  tasks: MlTask[];
  catalog: BlockCatalogEntry[];
  devices: CapabilityDevice[];
  zones: Zone[];
  onClose: () => void;
  onCreated: () => void;
  /** Entry B with no task for the chosen target: let the caller open the task wizard pre-filled. */
  onRequestCreateTask?: (target: string) => void;
}) {
  const { t } = useTranslation('models');
  const [step, setStep] = useState(0);
  const [typeId, setTypeId] = useState('');
  const [governedId, setGovernedId] = useState('');
  const [sourceId, setSourceId] = useState('');
  const [name, setName] = useState('');
  const [stage, setStage] = useState(0);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Candidate governor types: entry A → the task's target's types; entry B → types the device can be governed by.
  const types = useMemo(() => {
    if (task) return governorTypesFor(catalog, task.targetCapability);
    if (device) return applicableTypesFor(catalog, device);
    return [];
  }, [task, device, catalog]);

  useEffect(() => {
    if (!open) return;
    setStep(0);
    setTypeId(types.length === 1 ? types[0].typeId : '');
    setGovernedId(device?.id ?? '');
    setSourceId('');
    setName('');
    setStage(0);
    setError(null);
  }, [open, types, device]);

  const type = types.find((x) => x.typeId === typeId) ?? null;
  const output = type ? boundOutputOf(type) : undefined;
  const measured = type ? measuredInputOf(type) : undefined;
  // The task feeding the chosen type (entry B resolves it from the type's target).
  const effectiveTask = task
    ?? (type ? tasks.find((x) => x.targetCapability.toLowerCase() === (type.mlTargetCapability ?? '').toLowerCase()) : null);

  const governable = useMemo(
    () => (type ? suitableDevicesFor(type, devices) : []),
    [type, devices],
  );
  const governed = governable.find((d) => d.id === governedId) ?? null;
  const sources = useMemo(
    () => (type ? measuredSourceCandidates(type, devices, governed) : []),
    [type, devices, governed],
  );

  // Auto-suggest the best measured-signal source once the governed device is known.
  useEffect(() => {
    if (!open || sourceId || sources.length === 0) return;
    setSourceId(sources[0].id);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, governed?.id, sources.length]);

  const zoneName = (id?: string | null): string =>
    isUnassignedZone(id) ? t('apply.noZone') : zones.find((z) => z.id === id)?.name ?? id!;

  const create = async () => {
    if (!type || !output || !measured || !governed) return;
    setSaving(true);
    setError(null);
    try {
      await blocksApi.createBlock({
        name: name.trim() || t('apply.defaultName', { type: type.title, device: governed.name }),
        typeId: type.typeId,
        enabled: true,
        params: {
          ...Object.fromEntries(type.params.map((p) => [p.name, p.default])),
          stage,
        },
        inputs: sourceId ? { [measured]: { deviceId: sourceId, capabilityId: measured } } : {},
        outputs: { [output]: { deviceId: governed.id, capabilityId: output } },
        zoneId: isUnassignedZone(governed.zoneId) ? undefined : governed.zoneId,
      });
      onCreated();
      onClose();
    } catch {
      setError(t('apply.errors.create'));
    } finally {
      setSaving(false);
    }
  };

  const steps = [t('apply.steps.where'), t('apply.steps.signal'), t('apply.steps.confirm')];
  const canNext = step === 0 ? !!type && !!governed && !!effectiveTask : step === 1 ? !!sourceId : true;

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>
        {task ? t('apply.titleFromTask', { name: task.name }) : t('apply.titleFromDevice', { name: device?.name ?? '' })}
      </DialogTitle>
      <DialogContent>
        <Stepper activeStep={step} sx={{ my: 2 }}>
          {steps.map((label) => <Step key={label}><StepLabel>{label}</StepLabel></Step>)}
        </Stepper>

        {step === 0 && (
          <Stack spacing={2}>
            {types.length === 0 ? (
              <Alert severity="info">{t('apply.noTypes')}</Alert>
            ) : (
              <>
                {types.length > 1 && (
                  <TextField select fullWidth label={t('apply.governorType')} value={typeId}
                    onChange={(e) => { setTypeId(e.target.value); setGovernedId(device?.id ?? ''); }}>
                    {types.map((x) => <MenuItem key={x.typeId} value={x.typeId}>{x.title}</MenuItem>)}
                  </TextField>
                )}
                {type && <Typography variant="caption" color="text.secondary">{type.description}</Typography>}

                {/* Entry B: which task/model will feed this governor. */}
                {device && type && (
                  effectiveTask ? (
                    <Alert severity="success" icon={false}>
                      <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
                        <Typography variant="body2">{t('apply.feedingTask', { name: effectiveTask.name })}</Typography>
                        {effectiveTask.status?.lastTrainOk
                          ? <Chip size="small" color="success" variant="outlined" label={t('apply.taskTrained')} />
                          : <Chip size="small" color="warning" variant="outlined"
                              label={effectiveTask.status?.lastMessage ?? t('apply.taskNotTrained')} />}
                      </Stack>
                    </Alert>
                  ) : (
                    <Alert
                      severity="warning"
                      action={onRequestCreateTask && type.mlTargetCapability ? (
                        <Button size="small" onClick={() => onRequestCreateTask(type.mlTargetCapability!)}>
                          {t('apply.createTask')}
                        </Button>
                      ) : undefined}
                    >
                      {t('apply.noTask', { target: type.mlTargetCapability })}
                    </Alert>
                  )
                )}

                {/* Entry A: where to apply — devices exposing the governor's writable output. */}
                {!device && type && (
                  governable.length === 0 ? (
                    <Alert severity="info">
                      {t('apply.noDevices', { capability: output })}
                      <Typography variant="caption" display="block" mt={0.5}>
                        {t('apply.noDevicesHint')}
                      </Typography>
                      <Button size="small" component={RouterLink} to="/blocks" sx={{ mt: 1 }}>
                        {t('apply.openBlocks')}
                      </Button>
                    </Alert>
                  ) : (
                    <TextField select fullWidth label={t('apply.device')} value={governedId}
                      helperText={t('apply.deviceHelp', { capability: output })}
                      onChange={(e) => { setGovernedId(e.target.value); setSourceId(''); }}>
                      {governable.map((d) => (
                        <MenuItem key={d.id} value={d.id}>
                          <Stack direction="row" spacing={1} alignItems="center" width="100%">
                            <span>{d.name}</span>
                            <Box flex={1} />
                            <Chip size="small" variant="outlined" label={effectiveArchetype(d).replace(/_/g, ' ')} />
                            <Chip size="small" variant="outlined" label={zoneName(d.zoneId)} />
                          </Stack>
                        </MenuItem>
                      ))}
                    </TextField>
                  )
                )}
              </>
            )}
          </Stack>
        )}

        {step === 1 && type && (
          <Stack spacing={2}>
            <Typography variant="body2" color="text.secondary">{t('apply.signalCaption')}</Typography>
            {sources.length === 0 ? (
              <Alert severity="warning">{t('apply.noSignal', { capability: measured })}</Alert>
            ) : (
              <TextField select fullWidth label={t('apply.signal', { capability: measured })} value={sourceId}
                onChange={(e) => setSourceId(e.target.value)}>
                {sources.map((d) => (
                  <MenuItem key={d.id} value={d.id}>
                    <Stack direction="row" spacing={1} alignItems="center" width="100%">
                      <span>{d.name}</span>
                      <Box flex={1} />
                      {d.id === governed?.id && <Chip size="small" color="primary" variant="outlined" label={t('apply.sameDevice')} />}
                      <Chip size="small" variant="outlined" label={zoneName(d.zoneId)} />
                    </Stack>
                  </MenuItem>
                ))}
              </TextField>
            )}
          </Stack>
        )}

        {step === 2 && type && governed && (
          <Stack spacing={2}>
            <TextField
              fullWidth label={t('apply.name')} value={name}
              placeholder={t('apply.defaultName', { type: type.title, device: governed.name })}
              onChange={(e) => setName(e.target.value)}
            />
            <TextField select fullWidth label={t('apply.stage')} value={stage}
              helperText={t('apply.stageHelp')}
              onChange={(e) => setStage(Number(e.target.value))}>
              <MenuItem value={0}>{t('stage.shadow')}</MenuItem>
              <MenuItem value={1}>{t('stage.bounded')}</MenuItem>
              <MenuItem value={2}>{t('stage.full')}</MenuItem>
            </TextField>
            <Alert severity="info" icon={false}>
              <Typography variant="body2">
                {t('apply.summary', { device: governed.name, capability: output, signal: sources.find((s) => s.id === sourceId)?.name ?? '—' })}
              </Typography>
              {effectiveTask && (effectiveTask.clampMin != null || effectiveTask.clampMax != null) && (
                <Typography variant="caption" display="block" mt={0.5}>
                  {t('apply.clamps', { min: effectiveTask.clampMin ?? '—', max: effectiveTask.clampMax ?? '—' })}
                </Typography>
              )}
            </Alert>
            {error && <Alert severity="warning" onClose={() => setError(null)}>{error}</Alert>}
          </Stack>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('apply.cancel')}</Button>
        {step > 0 && <Button onClick={() => setStep(step - 1)}>{t('apply.back')}</Button>}
        {step < 2
          ? <Button variant="contained" disabled={!canNext} onClick={() => setStep(step + 1)}>{t('apply.next')}</Button>
          : <Button variant="contained" disabled={saving || !governed} onClick={create}>{t('apply.create')}</Button>}
      </DialogActions>
    </Dialog>
  );
}
