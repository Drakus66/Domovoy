// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Alert, Box, Button, FormControlLabel, MenuItem, Stack, Switch, TextField, Typography,
} from '@mui/material';
import SaveRoundedIcon from '@mui/icons-material/SaveRounded';
import type { CapabilityDevice } from '../../api/capabilityDevices';
import { loadManagementApi, type LoadSheddingProfile } from '../../api/loadManagement';

const MODES = ['Home', 'Away', 'Night', 'Vacation'];
const TIERS = ['unmanaged', 'sheddable', 'critical'] as const;

const DEFAULT_PROFILE: LoadSheddingProfile = {
  enabled: false,
  protected: false,
  controlCapabilityId: 'on_off',
  curtailable: false,
  curtailedValue: null,
  restoreValue: null,
  modeTier: {},
  modePriority: {},
};

/**
 * Load-shedding profile editor (roadmap Epic 3C-LM) — shown in the device drawer only for devices with a
 * writable `on_off` capability (the capability LoadManager always commands to fully shed a load). Self-
 * contained like EnergyProfileEditor, but with enough fields (per-mode tier/priority, curtailing) that it
 * saves explicitly rather than on every keystroke.
 */
export default function LoadSheddingProfileEditor({ device }: { device: CapabilityDevice }) {
  const { t } = useTranslation('devices');
  const [profile, setProfile] = useState<LoadSheddingProfile>(device.loadShedding ?? DEFAULT_PROFILE);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setProfile(device.loadShedding ?? DEFAULT_PROFILE);
    setSaved(false);
  }, [device.id, device.loadShedding]);

  if (!device.capabilities.some((c) => c.id === 'on_off' && c.writable)) return null;

  const numericCapabilities = device.capabilities.filter((c) => c.writable && c.kind === 'Number');

  const patch = (p: Partial<LoadSheddingProfile>) => { setProfile((cur) => ({ ...cur, ...p })); setSaved(false); };
  const patchTier = (mode: string, tier: string) => {
    setProfile((cur) => ({ ...cur, modeTier: { ...cur.modeTier, [mode]: tier } }));
    setSaved(false);
  };
  const patchPriority = (mode: string, priority: number) => {
    setProfile((cur) => ({ ...cur, modePriority: { ...cur.modePriority, [mode]: priority } }));
    setSaved(false);
  };

  const save = async () => {
    setSaving(true); setError(null);
    try {
      await loadManagementApi.setDeviceProfile(device.id, profile);
      setSaved(true);
    } catch {
      setError(t('loadShedding.saveError'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Box sx={{ mb: 2, p: 1.5, border: '1px solid', borderColor: 'divider', borderRadius: 1.5 }}>
      <Typography variant="body2" fontWeight={600} mb={0.5}>{t('loadShedding.title')}</Typography>
      <Typography variant="caption" color="text.secondary" display="block" mb={1}>{t('loadShedding.hint')}</Typography>

      {saved && <Alert severity="success" sx={{ mb: 1 }} onClose={() => setSaved(false)}>{t('loadShedding.saved')}</Alert>}
      {error && <Alert severity="warning" sx={{ mb: 1 }} onClose={() => setError(null)}>{error}</Alert>}

      <Stack spacing={1.5}>
        <FormControlLabel
          control={<Switch checked={profile.enabled} onChange={(e) => patch({ enabled: e.target.checked })} />}
          label={t('loadShedding.enabled')}
        />

        {profile.enabled && (
          <>
            <FormControlLabel
              control={<Switch checked={profile.protected} onChange={(e) => patch({ protected: e.target.checked })} />}
              label={t('loadShedding.protected')}
            />
            <Typography variant="caption" color="text.secondary">{t('loadShedding.protectedHint')}</Typography>

            {/* Watts live in the energy profile (Epic 3C-D) — one place, used by both accounting and shedding. */}
            <Typography variant="caption" color="text.secondary">{t('loadShedding.powerFromProfile')}</Typography>

            <FormControlLabel
              control={<Switch checked={profile.curtailable} onChange={(e) => patch({ curtailable: e.target.checked })} />}
              label={t('loadShedding.curtailable')}
              disabled={numericCapabilities.length === 0}
            />

            {profile.curtailable && numericCapabilities.length > 0 && (
              <Stack spacing={1.5} sx={{ pl: 2, borderLeft: '2px solid', borderColor: 'divider' }}>
                <TextField
                  select size="small" label={t('loadShedding.controlCapability')}
                  value={profile.controlCapabilityId}
                  onChange={(e) => patch({ controlCapabilityId: e.target.value })}
                >
                  {numericCapabilities.map((c) => (
                    <MenuItem key={c.id} value={c.id}>{c.id}</MenuItem>
                  ))}
                </TextField>
                <TextField
                  type="number" size="small" label={t('loadShedding.curtailedValue')}
                  value={profile.curtailedValue ?? ''}
                  onChange={(e) => patch({ curtailedValue: e.target.value === '' ? null : Number(e.target.value) })}
                />
                <TextField
                  type="number" size="small" label={t('loadShedding.restoreValue')}
                  value={profile.restoreValue ?? ''}
                  onChange={(e) => patch({ restoreValue: e.target.value === '' ? null : Number(e.target.value) })}
                />
              </Stack>
            )}

            <Box>
              <Typography variant="caption" fontWeight={600} display="block" mb={0.5}>{t('loadShedding.modeMatrix')}</Typography>
              <Stack spacing={1}>
                {MODES.map((mode) => {
                  const tier = profile.modeTier[mode] ?? 'unmanaged';
                  return (
                    <Stack key={mode} direction="row" spacing={1} alignItems="center">
                      <Typography variant="body2" sx={{ width: 80 }}>{t(`loadShedding.modes.${mode}`, { defaultValue: mode })}</Typography>
                      <TextField
                        select size="small" value={tier} onChange={(e) => patchTier(mode, e.target.value)} sx={{ width: 160 }}
                      >
                        {TIERS.map((tr) => (
                          <MenuItem key={tr} value={tr}>{t(`loadShedding.tiers.${tr}`)}</MenuItem>
                        ))}
                      </TextField>
                      {tier === 'sheddable' && (
                        <TextField
                          type="number" size="small" label={t('loadShedding.priority')}
                          value={profile.modePriority[mode] ?? 0}
                          onChange={(e) => patchPriority(mode, Number(e.target.value))}
                          sx={{ width: 110 }}
                        />
                      )}
                    </Stack>
                  );
                })}
              </Stack>
            </Box>
          </>
        )}

        <Box sx={{ display: 'flex', justifyContent: 'flex-end' }}>
          <Button size="small" variant="contained" startIcon={<SaveRoundedIcon />} onClick={save} disabled={saving}>
            {t('loadShedding.save')}
          </Button>
        </Box>
      </Stack>
    </Box>
  );
}
