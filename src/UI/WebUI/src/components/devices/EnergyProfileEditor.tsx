// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Alert, Box, Button, FormControlLabel, InputAdornment, MenuItem, Stack, Switch, TextField, Typography,
} from '@mui/material';
import SaveRoundedIcon from '@mui/icons-material/SaveRounded';
import type { CapabilityDevice, EnergyProfile } from '../../api/capabilityDevices';
import { energyApi } from '../../api/energy';
import { historyApi } from '../../api/history';
import { powerTopologyApi, type PowerNode } from '../../api/powerTopology';

const ROLES = ['consumer', 'mains'] as const;

/** Well-known 0..100 % regulators, mirroring EnergyScale.Candidates in the AutomationService. */
const SCALE_CANDIDATES = ['brightness', 'fan_speed', 'position'];

/** Typical full-load draw (W) per archetype — mirrors EnergyModel.DefaultPowerW, only a starting point. */
const DEFAULT_POWER_W: Record<string, number> = {
  light: 9,
  thermostat: 1000,
  valve: 5,
  switch: 60,
};

const EMPTY: EnergyProfile = {};

/** Numeric text-field value → number | null (an emptied field means "unset", not zero). */
const num = (v: string): number | null => (v === '' ? null : Number(v));

/**
 * Per-device energy accounting (roadmap Epic 3C-D) — the replacement for the Powercalc/integrator blocks.
 * Turning the toggle on is all a metered device needs; a device without a meter also takes its nameplate
 * watts, and the platform then publishes an estimated `power` plus an integrated `energy` series on the
 * device itself. Shown for controllable devices and anything that already measures power/energy — plain
 * sensors draw too little to be worth accounting.
 */
export default function EnergyProfileEditor({ device }: { device: CapabilityDevice }) {
  const { t } = useTranslation('devices');
  const [profile, setProfile] = useState<EnergyProfile>(device.energyProfile ?? EMPTY);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [measuring, setMeasuring] = useState(false);
  const [circuits, setCircuits] = useState<PowerNode[]>([]);

  useEffect(() => {
    setProfile(device.energyProfile ?? EMPTY);
    setSaved(false);
  }, [device.id, device.energyProfile]);

  // Circuits of the electrical topology (Epic 3C-D) — empty until the user describes the wiring, in which
  // case the picker simply stays hidden.
  useEffect(() => {
    powerTopologyApi.getNodes()
      .then((all) => setCircuits(all.filter((n) => n.kind === 'circuit')))
      .catch(() => { /* quiet: the device still works without a circuit */ });
  }, []);

  // What the device measures on its own — the synthetic series we maintain doesn't count as measurement.
  const metered = device.capabilities.some((c) => c.id === 'energy' && !c.synthetic);
  const hasPowerMeter = device.capabilities.some((c) => c.id === 'power' && !c.synthetic);
  const controllable = device.capabilities.some((c) => c.writable);

  const regulators = useMemo(
    () => device.capabilities.filter((c) => c.kind === 'Number' && c.id !== 'power' && c.id !== 'energy'),
    [device.capabilities],
  );
  const autoRegulator = useMemo(
    () => SCALE_CANDIDATES.find((id) => regulators.some((c) => c.id === id)) ?? null,
    [regulators],
  );

  if (!controllable && !metered && !hasPowerMeter) return null;

  const tracking = profile.track ?? metered;
  // Estimated devices need a nameplate figure; measured ones derive everything from their own readings.
  const needsNameplate = tracking && !metered && !hasPowerMeter;
  const source = metered ? 'metered' : hasPowerMeter ? 'integrated' : 'estimated';

  const patch = (p: Partial<EnergyProfile>) => { setProfile((cur) => ({ ...cur, ...p })); setSaved(false); };

  const toggleTracking = (on: boolean) => {
    // Pre-fill the nameplate from the archetype the first time accounting is enabled — a hint, not a lock.
    const suggested = DEFAULT_POWER_W[device.archetype || device.autoArchetype || ''] ?? null;
    patch({
      track: on,
      maxPowerW: on && profile.maxPowerW == null ? suggested : profile.maxPowerW,
    });
  };

  /** Fill the nameplate from what the device actually drew — the peak of the last week of telemetry. */
  const useMeasuredPeak = async () => {
    setMeasuring(true); setError(null);
    try {
      const from = new Date(Date.now() - 7 * 24 * 3600 * 1000).toISOString();
      const buckets = await historyApi.getAggregate({
        deviceId: device.id, capabilityId: 'power', from, bucket: 'hour', agg: 'max',
      });
      const peak = buckets.reduce((m, b) => Math.max(m, b.max), 0);
      if (peak > 0) patch({ maxPowerW: Math.round(peak) });
      else setError(t('energyProfile.noMeasurement'));
    } catch {
      setError(t('energyProfile.noMeasurement'));
    } finally {
      setMeasuring(false);
    }
  };

  const save = async () => {
    setSaving(true); setError(null);
    try {
      await energyApi.setEnergyProfile(device.id, profile);
      setSaved(true);
    } catch {
      setError(t('energyProfile.saveError'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Box sx={{ mb: 2, p: 1.5, border: '1px solid', borderColor: 'divider', borderRadius: 1.5 }}>
      <Typography variant="body2" fontWeight={600} mb={0.5}>{t('energyProfile.title')}</Typography>
      <Typography variant="caption" color="text.secondary" display="block" mb={1}>
        {t(`energyProfile.source.${source}`)}
      </Typography>

      {saved && <Alert severity="success" sx={{ mb: 1 }} onClose={() => setSaved(false)}>{t('energyProfile.saved')}</Alert>}
      {error && <Alert severity="warning" sx={{ mb: 1 }} onClose={() => setError(null)}>{error}</Alert>}

      <Stack spacing={1.5}>
        <FormControlLabel
          control={<Switch checked={tracking} onChange={(e) => toggleTracking(e.target.checked)} />}
          label={t('energyProfile.track')}
        />

        {tracking && (
          <>
            <TextField
              type="number" size="small" label={t('energyProfile.maxPower')}
              value={profile.maxPowerW ?? ''}
              onChange={(e) => patch({ maxPowerW: num(e.target.value) })}
              helperText={needsNameplate ? t('energyProfile.maxPowerRequired') : t('energyProfile.maxPowerHint')}
              InputProps={{ endAdornment: <InputAdornment position="end">W</InputAdornment> }}
            />

            {hasPowerMeter && (
              <Box>
                <Button size="small" onClick={useMeasuredPeak} disabled={measuring}>
                  {t('energyProfile.useMeasured')}
                </Button>
              </Box>
            )}

            {!metered && (
              <>
                <TextField
                  select size="small" label={t('energyProfile.regulator')}
                  value={profile.scaleCapabilityId ?? ''}
                  onChange={(e) => patch({ scaleCapabilityId: e.target.value || null })}
                  helperText={t('energyProfile.regulatorHint')}
                >
                  <MenuItem value="">
                    <em>
                      {autoRegulator
                        ? t('energyProfile.regulatorAuto', { value: autoRegulator })
                        : t('energyProfile.regulatorNone')}
                    </em>
                  </MenuItem>
                  {regulators.map((c) => (
                    <MenuItem key={c.id} value={c.id}>{c.id}</MenuItem>
                  ))}
                </TextField>

                <TextField
                  type="number" size="small" label={t('energyProfile.minPower')}
                  value={profile.minPowerW ?? ''}
                  onChange={(e) => patch({ minPowerW: num(e.target.value) })}
                  helperText={t('energyProfile.minPowerHint')}
                  InputProps={{ endAdornment: <InputAdornment position="end">W</InputAdornment> }}
                />

                <TextField
                  type="number" size="small" label={t('energyProfile.standbyPower')}
                  value={profile.standbyPowerW ?? ''}
                  onChange={(e) => patch({ standbyPowerW: num(e.target.value) })}
                  helperText={t('energyProfile.standbyPowerHint')}
                  InputProps={{ endAdornment: <InputAdornment position="end">W</InputAdornment> }}
                />
              </>
            )}

            {circuits.length > 0 && (
              <TextField
                select size="small" label={t('energyProfile.circuit')}
                value={profile.circuitId ?? ''}
                onChange={(e) => patch({ circuitId: e.target.value || null })}
                helperText={t('energyProfile.circuitHint')}
              >
                <MenuItem value=""><em>{t('energyProfile.noCircuit')}</em></MenuItem>
                {circuits.map((c) => (
                  <MenuItem key={c.id} value={c.id}>{c.name}</MenuItem>
                ))}
              </TextField>
            )}

            <TextField
              select size="small" label={t('energyProfile.role')}
              value={profile.role ?? ''}
              onChange={(e) => patch({ role: e.target.value || null })}
              helperText={t('energyProfile.roleHint')}
            >
              {ROLES.map((r) => (
                <MenuItem key={r} value={r === 'consumer' ? '' : r}>{t(`energyProfile.roles.${r}`)}</MenuItem>
              ))}
            </TextField>
          </>
        )}

        <Box sx={{ display: 'flex', justifyContent: 'flex-end' }}>
          <Button size="small" variant="contained" startIcon={<SaveRoundedIcon />} onClick={save} disabled={saving}>
            {t('energyProfile.save')}
          </Button>
        </Box>
      </Stack>
    </Box>
  );
}
