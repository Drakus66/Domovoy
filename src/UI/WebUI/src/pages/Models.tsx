// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Container, Box, Typography, Stack, Button, LinearProgress, Alert, Tabs, Tab,
} from '@mui/material';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import ModelTrainingRoundedIcon from '@mui/icons-material/ModelTrainingRounded';
import PsychologyRoundedIcon from '@mui/icons-material/PsychologyRounded';
import { mlApi, MlModel, MlTask } from '../api/ml';
import { blocksApi, BlockCatalogEntry, ControlBlock } from '../api/blocks';
import { capabilityDevicesApi, CapabilityDevice } from '../api/capabilityDevices';
import { zonesApi, Zone } from '../api/zones';
import { consumerBlocksFor, governorTypesFor } from '../components/ml/mlHub';
import MlTaskCard from '../components/ml/MlTaskCard';
import MlTaskWizard from '../components/ml/MlTaskWizard';
import MlApplyWizard from '../components/ml/MlApplyWizard';
import MlLayerControls from '../components/ml/MlLayerControls';
import MlJournal from '../components/ml/MlJournal';

/**
 * The ML hub (Epic 2P): one card per training task — WHAT the house learns, whether it trained (and why not),
 * the models it produced per scope, and the governor blocks consuming them with their authority stage. Tasks
 * are created/edited here (runtime, no env), and a trained model is applied to a device in a wizard — models
 * as tools, not exhibits.
 */
export default function Models() {
  const { t } = useTranslation('models');
  const [tasks, setTasks] = useState<MlTask[]>([]);
  const [models, setModels] = useState<MlModel[]>([]);
  const [catalog, setCatalog] = useState<BlockCatalogEntry[]>([]);
  const [blocks, setBlocks] = useState<ControlBlock[]>([]);
  const [devices, setDevices] = useState<CapabilityDevice[]>([]);
  const [zones, setZones] = useState<Zone[]>([]);
  const [loading, setLoading] = useState(true);
  const [training, setTraining] = useState<string | null>(null); // task id | '*' while a train call runs
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);

  const [wizard, setWizard] = useState<{ open: boolean; task?: MlTask | null; prefillTarget?: string }>({ open: false });
  const [apply, setApply] = useState<MlTask | null>(null);
  const [tab, setTab] = useState(0); // 0 = tasks, 1 = journal (Epic 3I: journal is its own tab, not inline)

  const load = useCallback(async () => {
    setError(null);
    try {
      const [ts, ms, cat, bs, ds, zs] = await Promise.all([
        mlApi.getTasks(), mlApi.getModels(), blocksApi.getCatalog(), blocksApi.getBlocks(),
        capabilityDevicesApi.getDevices(), zonesApi.getZones().catch(() => [] as Zone[]),
      ]);
      setTasks(ts); setModels(ms); setCatalog(cat); setBlocks(bs); setDevices(ds); setZones(zs);
    } catch {
      setError(t('errors.load'));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => { load(); }, [load]);

  const modelsByTarget = useMemo(() => {
    const map = new Map<string, MlModel[]>();
    for (const m of models) {
      const key = m.targetCapability.toLowerCase();
      map.set(key, [...(map.get(key) ?? []), m]);
    }
    return map;
  }, [models]);

  const train = async (task?: MlTask) => {
    setTraining(task?.id ?? '*');
    setError(null); setInfo(null);
    try {
      const results = await mlApi.train(task?.id);
      const ok = results.filter((r) => r.result.trained).length;
      const failed = results.filter((r) => !r.result.trained);
      setInfo(failed.length === 0
        ? t('trainDone', { count: ok })
        : t('trainPartial', { ok, failed: failed.map((f) => `${f.target}: ${f.result.message}`).join('; ') }));
      await load();
    } catch {
      setError(t('errors.train'));
    } finally {
      setTraining(null);
    }
  };

  const toggleEnabled = async (task: MlTask, enabled: boolean) => {
    setError(null);
    try {
      await mlApi.updateTask(task.id, { ...task, enabled });
      await load();
    } catch {
      setError(t('errors.update'));
    }
  };

  const removeTask = async (task: MlTask) => {
    if (!window.confirm(t('confirmDeleteTask', { name: task.name }))) return;
    setError(null);
    try {
      await mlApi.deleteTask(task.id);
      await load();
    } catch {
      setError(t('errors.delete'));
    }
  };

  return (
    <Container maxWidth="lg">
      <Box py={{ xs: 3, md: 4 }}>
        <Stack direction="row" alignItems="center" justifyContent="space-between" mb={2} flexWrap="wrap" useFlexGap>
          <Box>
            <Typography variant="h4" component="h1" fontWeight={700}>{t('title')}</Typography>
            <Typography variant="caption" color="text.secondary">{t('caption')}</Typography>
          </Box>
          <Stack direction="row" spacing={1}>
            <Button variant="outlined" startIcon={<ModelTrainingRoundedIcon />}
              disabled={training !== null || tasks.every((x) => !x.enabled)} onClick={() => train()}>
              {t('actions.trainAll')}
            </Button>
            <Button variant="contained" startIcon={<AddRoundedIcon />}
              onClick={() => setWizard({ open: true })}>
              {t('actions.newTask')}
            </Button>
          </Stack>
        </Stack>

        {(loading || training !== null) && <LinearProgress sx={{ mb: 2, borderRadius: 1 }} />}
        {error && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setError(null)}>{error}</Alert>}
        {info && <Alert severity="info" sx={{ mb: 2 }} onClose={() => setInfo(null)}>{info}</Alert>}

        {/* Epic 3I: the activity journal ("pulse") is its own tab so it never clutters the task list. */}
        <Tabs value={tab} onChange={(_, v) => setTab(v)} sx={{ mb: 2, borderBottom: 1, borderColor: 'divider' }}>
          <Tab label={t('tabs.tasks')} />
          <Tab label={t('tabs.journal')} />
        </Tabs>

        {tab === 0 ? (
          <>
            {/* Layer switches gate training, so they live with the tasks (a prominent alert when off). */}
            <MlLayerControls />

            {tasks.length === 0 && !loading ? (
              <Box textAlign="center" py={8}>
                <PsychologyRoundedIcon sx={{ fontSize: 64, color: 'text.disabled', mb: 2 }} />
                <Typography color="text.secondary" mb={0.5}>{t('empty')}</Typography>
                <Typography variant="caption" color="text.secondary" display="block" mb={2}>
                  {t('emptyHint')}
                </Typography>
                <Button variant="contained" startIcon={<AddRoundedIcon />} onClick={() => setWizard({ open: true })}>
                  {t('actions.newTask')}
                </Button>
              </Box>
            ) : (
              <Stack spacing={1.5}>
                {tasks.map((task) => {
                  const consumerTypes = governorTypesFor(catalog, task.targetCapability);
                  return (
                    <MlTaskCard
                      key={task.id}
                      task={task}
                      models={modelsByTarget.get(task.targetCapability.toLowerCase()) ?? []}
                      consumerTypes={consumerTypes}
                      consumerBlocks={consumerBlocksFor(blocks, consumerTypes)}
                      training={training === task.id || training === '*'}
                      onToggleEnabled={toggleEnabled}
                      onTrain={(x) => train(x)}
                      onEdit={(x) => setWizard({ open: true, task: x })}
                      onDelete={removeTask}
                      onApply={setApply}
                      onChanged={load}
                    />
                  );
                })}
              </Stack>
            )}
          </>
        ) : (
          <MlJournal />
        )}
      </Box>

      <MlTaskWizard
        open={wizard.open}
        task={wizard.task}
        devices={devices}
        existingTargets={tasks.filter((x) => x.id !== wizard.task?.id).map((x) => x.targetCapability)}
        onClose={() => setWizard({ open: false })}
        onSaved={load}
      />

      <MlApplyWizard
        open={apply !== null}
        task={apply}
        tasks={tasks}
        catalog={catalog}
        devices={devices}
        zones={zones}
        onClose={() => setApply(null)}
        onCreated={() => { setInfo(t('apply.created')); load(); }}
      />
    </Container>
  );
}
