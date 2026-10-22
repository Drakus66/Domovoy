// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Alert, Box, Button, FormControlLabel, IconButton, InputAdornment, MenuItem, Stack, Switch, TextField, Typography,
} from '@mui/material';
import SaveRoundedIcon from '@mui/icons-material/SaveRounded';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import {
  loadManagementApi, POWER_SOURCES, SINGLE_PHASES, type PhaseLimit, type PowerBudget,
} from '../../api/loadManagement';

/**
 * Load-management editor (roadmap Epic 3C-LM) — a self-contained Settings section (like TariffEditor).
 * Off by default: the master switch gates everything else, and an empty budget list is a no-op. Budgets
 * are per power_source (grid/grid_peak/battery/solar/off); LoadManager picks the one matching the live
 * signal from the Power virtual device. Restore margin/min-dwell tune how eagerly shed loads come back.
 */
export default function LoadManagementEditor() {
  const { t } = useTranslation('settings');
  const [enabled, setEnabled] = useState(false);
  const [budgets, setBudgets] = useState<PowerBudget[]>([]);
  const [phaseLimits, setPhaseLimits] = useState<PhaseLimit[]>([]);
  const [restoreMarginWatts, setRestoreMarginWatts] = useState(100);
  const [minDwellSeconds, setMinDwellSeconds] = useState(120);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    loadManagementApi.getSettings()
      .then((s) => {
        setEnabled(s.enabled);
        setBudgets(s.budgets ?? []);
        setPhaseLimits(s.phaseLimits ?? []);
        setRestoreMarginWatts(s.restoreMarginWatts ?? 100);
        setMinDwellSeconds(s.minDwellSeconds ?? 120);
      })
      .catch(() => { /* keep defaults; a save creates the document */ });
  }, []);

  const touched = () => setSaved(false);

  const addBudget = () => {
    const used = new Set(budgets.map((b) => b.powerSource));
    const next = POWER_SOURCES.find((s) => !used.has(s)) ?? POWER_SOURCES[0];
    setBudgets((bs) => [...bs, { powerSource: next, limitWatts: 0 }]);
    touched();
  };
  const removeBudget = (i: number) => { setBudgets((bs) => bs.filter((_, idx) => idx !== i)); touched(); };
  const patchBudget = (i: number, patch: Partial<PowerBudget>) => {
    setBudgets((bs) => bs.map((b, idx) => (idx === i ? { ...b, ...patch } : b)));
    touched();
  };

  /** Per-phase limit as a form value; '' means "no limit" (the breaker rating applies instead). */
  const phaseLimit = (phase: string) => phaseLimits.find((p) => p.phase === phase)?.limitWatts ?? '';
  const setPhaseLimit = (phase: string, value: string) => {
    setPhaseLimits((cur) => {
      const rest = cur.filter((p) => p.phase !== phase);
      return value === '' ? rest : [...rest, { phase, limitWatts: Number(value) }];
    });
    touched();
  };

  const save = async () => {
    setSaving(true); setError(null); setSaved(false);
    try {
      const s = await loadManagementApi.saveSettings({
        enabled, budgets, restoreMarginWatts, minDwellSeconds, phaseLimits,
      });
      setEnabled(s.enabled); setBudgets(s.budgets); setPhaseLimits(s.phaseLimits ?? []);
      setRestoreMarginWatts(s.restoreMarginWatts); setMinDwellSeconds(s.minDwellSeconds);
      setSaved(true);
    } catch {
      setError(t('errors.save'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Stack spacing={2}>
      {saved && <Alert severity="success" onClose={() => setSaved(false)}>{t('loadManagement.saved')}</Alert>}
      {error && <Alert severity="warning" onClose={() => setError(null)}>{error}</Alert>}

      <FormControlLabel
        control={<Switch checked={enabled} onChange={(e) => { setEnabled(e.target.checked); touched(); }} />}
        label={t('loadManagement.enabled')}
      />
      <Typography variant="caption" color="text.secondary" display="block">
        {t('loadManagement.hint')}
      </Typography>

      <Box>
        <Typography variant="body2" fontWeight={600} mb={1}>{t('loadManagement.budgets')}</Typography>
        {budgets.length === 0 && (
          <Typography variant="caption" color="text.secondary" display="block" mb={1}>
            {t('loadManagement.noBudgets')}
          </Typography>
        )}
        <Stack spacing={1}>
          {budgets.map((b, i) => (
            <Stack key={i} direction="row" spacing={1} alignItems="center">
              <TextField
                select size="small" label={t('loadManagement.powerSource')} value={b.powerSource}
                onChange={(e) => patchBudget(i, { powerSource: e.target.value })} sx={{ width: 180 }}
              >
                {POWER_SOURCES.map((s) => (
                  <MenuItem key={s} value={s}>{t(`loadManagement.powerSources.${s}`)}</MenuItem>
                ))}
              </TextField>
              <TextField
                type="number" size="small" label={t('loadManagement.limitWatts')} value={b.limitWatts}
                onChange={(e) => patchBudget(i, { limitWatts: Number(e.target.value) })}
                InputProps={{ endAdornment: <InputAdornment position="end">{t('loadManagement.watts')}</InputAdornment> }}
                sx={{ width: 180 }}
              />
              <IconButton aria-label={t('loadManagement.removeBudget')} onClick={() => removeBudget(i)} size="small">
                <DeleteOutlineRoundedIcon fontSize="small" />
              </IconButton>
            </Stack>
          ))}
        </Stack>
        <Button startIcon={<AddRoundedIcon />} onClick={addBudget} sx={{ mt: 1.5 }} disabled={budgets.length >= POWER_SOURCES.length}>
          {t('loadManagement.addBudget')}
        </Button>
      </Box>

      {/* Per-phase limits (Epic 3C-D): a 3-phase intake trips per phase long before the household total. */}
      <Box>
        <Typography variant="body2" fontWeight={600} mb={0.5}>{t('loadManagement.phaseLimits')}</Typography>
        <Typography variant="caption" color="text.secondary" display="block" mb={1}>
          {t('loadManagement.phaseLimitsHint')}
        </Typography>
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
          {SINGLE_PHASES.map((phase) => (
            <TextField
              key={phase}
              type="number" size="small" label={phase.toUpperCase()} value={phaseLimit(phase)}
              onChange={(e) => setPhaseLimit(phase, e.target.value)}
              InputProps={{ endAdornment: <InputAdornment position="end">{t('loadManagement.watts')}</InputAdornment> }}
              sx={{ width: 160 }}
            />
          ))}
        </Stack>
      </Box>

      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
        <TextField
          type="number" size="small" label={t('loadManagement.restoreMargin')} value={restoreMarginWatts}
          onChange={(e) => { setRestoreMarginWatts(Number(e.target.value)); touched(); }}
          InputProps={{ endAdornment: <InputAdornment position="end">{t('loadManagement.watts')}</InputAdornment> }}
          sx={{ width: 220 }}
        />
        <TextField
          type="number" size="small" label={t('loadManagement.minDwell')} value={minDwellSeconds}
          onChange={(e) => { setMinDwellSeconds(Number(e.target.value)); touched(); }}
          InputProps={{ endAdornment: <InputAdornment position="end">{t('loadManagement.seconds')}</InputAdornment> }}
          sx={{ width: 220 }}
        />
      </Stack>

      <Box sx={{ display: 'flex', justifyContent: 'flex-end' }}>
        <Button variant="contained" startIcon={<SaveRoundedIcon />} onClick={save} disabled={saving}>
          {t('loadManagement.save')}
        </Button>
      </Box>
    </Stack>
  );
}
