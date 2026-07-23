// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Alert, Box, Button, IconButton, InputAdornment, Stack, TextField, Typography,
} from '@mui/material';
import SaveRoundedIcon from '@mui/icons-material/SaveRounded';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import ScheduleRoundedIcon from '@mui/icons-material/ScheduleRounded';
import { settingsApi, type TariffZone } from '../../api/settings';

const pad = (n: number) => n.toString().padStart(2, '0');
const minutesToTime = (m: number) => `${pad(Math.floor((m % 1440) / 60))}:${pad(m % 60)}`;
const timeToMinutes = (s: string) => {
  const [h, m] = s.split(':').map(Number);
  return (Number.isFinite(h) ? h : 0) * 60 + (Number.isFinite(m) ? m : 0);
};

/**
 * Electricity tariff editor (roadmap Epic 3C) — a self-contained Settings section (like KioskSettings). Edits
 * the currency, the flat default price and the tariff zones (each a price + local time-of-day windows). The
 * saved tariff feeds the money endpoint and the cheap-hours block; a zone whose windows are empty is always
 * active, and a window whose end is at/before its start wraps past midnight (a 23:00→07:00 night rate).
 */
export default function TariffEditor() {
  const { t } = useTranslation('settings');
  const [currency, setCurrency] = useState('₽');
  const [defaultPrice, setDefaultPrice] = useState(0);
  const [zones, setZones] = useState<TariffZone[]>([]);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    settingsApi.getTariff()
      .then((s) => {
        setCurrency(s.currency || '₽');
        setDefaultPrice(s.defaultPrice ?? 0);
        setZones(s.zones ?? []);
      })
      .catch(() => { /* keep defaults; a save creates the document */ });
  }, []);

  const touched = () => setSaved(false);
  const mapZones = (fn: (z: TariffZone, i: number) => TariffZone) => { setZones((zs) => zs.map(fn)); touched(); };

  const addZone = () => { setZones((zs) => [...zs, { name: '', pricePerKwh: 0, intervals: [] }]); touched(); };
  const removeZone = (i: number) => { setZones((zs) => zs.filter((_, idx) => idx !== i)); touched(); };
  const patchZone = (i: number, patch: Partial<TariffZone>) =>
    mapZones((z, idx) => (idx === i ? { ...z, ...patch } : z));

  const addInterval = (zi: number) =>
    mapZones((z, idx) => (idx === zi ? { ...z, intervals: [...z.intervals, { startMinute: 0, endMinute: 0 }] } : z));
  const removeInterval = (zi: number, ii: number) =>
    mapZones((z, idx) => (idx === zi ? { ...z, intervals: z.intervals.filter((_, j) => j !== ii) } : z));
  const patchInterval = (zi: number, ii: number, patch: { startMinute?: number; endMinute?: number }) =>
    mapZones((z, idx) =>
      idx === zi ? { ...z, intervals: z.intervals.map((iv, j) => (j === ii ? { ...iv, ...patch } : iv)) } : z);

  const save = async () => {
    setSaving(true); setError(null); setSaved(false);
    try {
      const s = await settingsApi.saveTariff({ currency: currency.trim() || '₽', defaultPrice, zones });
      setCurrency(s.currency); setDefaultPrice(s.defaultPrice); setZones(s.zones);
      setSaved(true);
    } catch {
      setError(t('errors.save'));
    } finally {
      setSaving(false);
    }
  };

  const per = `${currency}/${t('tariff.kwh')}`;

  return (
    <Stack spacing={2}>
      {saved && <Alert severity="success" onClose={() => setSaved(false)}>{t('tariff.saved')}</Alert>}
      {error && <Alert severity="warning" onClose={() => setError(null)}>{error}</Alert>}

      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
        <TextField
          size="small" label={t('tariff.currency')} value={currency}
          onChange={(e) => { setCurrency(e.target.value); touched(); }} sx={{ width: 120 }}
        />
        <TextField
          type="number" size="small" label={t('tariff.defaultPrice')} value={defaultPrice}
          onChange={(e) => { setDefaultPrice(Number(e.target.value)); touched(); }}
          InputProps={{ endAdornment: <InputAdornment position="end">{per}</InputAdornment> }}
          inputProps={{ step: 0.01 }} sx={{ width: 200 }}
        />
      </Stack>

      <Box>
        <Typography variant="body2" fontWeight={600} mb={1}>{t('tariff.zones')}</Typography>
        {zones.length === 0 && (
          <Typography variant="caption" color="text.secondary" display="block" mb={1}>
            {t('tariff.noZones')}
          </Typography>
        )}
        <Stack spacing={1.5}>
          {zones.map((zone, zi) => (
            <Box key={zi} sx={{ p: 1.5, border: '1px solid', borderColor: 'divider', borderRadius: 1.5 }}>
              <Stack direction="row" spacing={1} alignItems="center">
                <TextField
                  size="small" label={t('tariff.zoneName')} value={zone.name}
                  onChange={(e) => patchZone(zi, { name: e.target.value })} sx={{ flex: 1 }}
                />
                <TextField
                  type="number" size="small" label={t('tariff.price')} value={zone.pricePerKwh}
                  onChange={(e) => patchZone(zi, { pricePerKwh: Number(e.target.value) })}
                  InputProps={{ endAdornment: <InputAdornment position="end">{per}</InputAdornment> }}
                  inputProps={{ step: 0.01 }} sx={{ width: 170 }}
                />
                <IconButton aria-label={t('tariff.removeZone')} onClick={() => removeZone(zi)} size="small">
                  <DeleteOutlineRoundedIcon fontSize="small" />
                </IconButton>
              </Stack>

              <Stack spacing={1} mt={1.5}>
                {zone.intervals.map((iv, ii) => (
                  <Stack key={ii} direction="row" spacing={1} alignItems="center">
                    <ScheduleRoundedIcon fontSize="small" color="action" />
                    <TextField
                      type="time" size="small" label={t('tariff.from')} value={minutesToTime(iv.startMinute)}
                      onChange={(e) => patchInterval(zi, ii, { startMinute: timeToMinutes(e.target.value) })}
                      InputLabelProps={{ shrink: true }}
                    />
                    <TextField
                      type="time" size="small" label={t('tariff.to')} value={minutesToTime(iv.endMinute)}
                      onChange={(e) => patchInterval(zi, ii, { endMinute: timeToMinutes(e.target.value) })}
                      InputLabelProps={{ shrink: true }}
                    />
                    <IconButton aria-label={t('tariff.removeInterval')} onClick={() => removeInterval(zi, ii)} size="small">
                      <DeleteOutlineRoundedIcon fontSize="small" />
                    </IconButton>
                  </Stack>
                ))}
                <Button size="small" startIcon={<AddRoundedIcon />} onClick={() => addInterval(zi)} sx={{ alignSelf: 'flex-start' }}>
                  {t('tariff.addInterval')}
                </Button>
              </Stack>
            </Box>
          ))}
        </Stack>

        <Button startIcon={<AddRoundedIcon />} onClick={addZone} sx={{ mt: 1.5 }}>{t('tariff.addZone')}</Button>
        <Typography variant="caption" color="text.secondary" display="block" mt={1}>
          {t('tariff.hint')}
        </Typography>
      </Box>

      <Box sx={{ display: 'flex', justifyContent: 'flex-end' }}>
        <Button variant="contained" startIcon={<SaveRoundedIcon />} onClick={save} disabled={saving}>
          {t('tariff.save')}
        </Button>
      </Box>
    </Stack>
  );
}
