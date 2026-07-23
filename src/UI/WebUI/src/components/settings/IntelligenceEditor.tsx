// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Alert, Box, Button, FormControlLabel, Stack, Switch, TextField, Typography } from '@mui/material';
import SaveRoundedIcon from '@mui/icons-material/SaveRounded';
import { mlApi } from '../../api/ml';

/**
 * Intelligence-layer switches (roadmap Epic 3I). Mirrors LoadManagementEditor: self-contained GET/PUT of the
 * ml_settings document. The master switch turns the whole ML layer off (training, model serving, all
 * proposers); the proposals switch silences just the proactive suggestions while learning keeps running; the
 * maturity gate holds the proposers back until enough history has accrued on a fresh install.
 */
export default function IntelligenceEditor() {
  const { t } = useTranslation('settings');
  const [enabled, setEnabled] = useState(true);
  const [proposalsEnabled, setProposalsEnabled] = useState(true);
  const [minHistoryDays, setMinHistoryDays] = useState(7);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    mlApi.getSettings()
      .then((s) => { setEnabled(s.enabled); setProposalsEnabled(s.proposalsEnabled); setMinHistoryDays(s.minHistoryDays); })
      .catch(() => { /* keep defaults; a save creates the document */ });
  }, []);

  const touched = () => setSaved(false);

  const save = async () => {
    setSaving(true); setError(null); setSaved(false);
    try {
      const s = await mlApi.saveSettings({ enabled, proposalsEnabled, minHistoryDays });
      setEnabled(s.enabled); setProposalsEnabled(s.proposalsEnabled); setMinHistoryDays(s.minHistoryDays);
      setSaved(true);
    } catch {
      setError(t('errors.save'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Stack spacing={2}>
      {saved && <Alert severity="success" onClose={() => setSaved(false)}>{t('intelligence.saved')}</Alert>}
      {error && <Alert severity="warning" onClose={() => setError(null)}>{error}</Alert>}

      <FormControlLabel
        control={<Switch checked={enabled} onChange={(e) => { setEnabled(e.target.checked); touched(); }} />}
        label={t('intelligence.enabled')}
      />
      <Typography variant="caption" color="text.secondary" sx={{ mt: -1.5, ml: 6 }}>
        {t('intelligence.enabledHint')}
      </Typography>

      <FormControlLabel
        control={
          <Switch
            checked={enabled && proposalsEnabled}
            disabled={!enabled}
            onChange={(e) => { setProposalsEnabled(e.target.checked); touched(); }}
          />
        }
        label={t('intelligence.proposals')}
      />
      <Typography variant="caption" color="text.secondary" sx={{ mt: -1.5, ml: 6 }}>
        {t('intelligence.proposalsHint')}
      </Typography>

      <Box>
        <TextField
          label={t('intelligence.minHistoryDays')}
          type="number"
          size="small"
          value={minHistoryDays}
          disabled={!enabled}
          onChange={(e) => { setMinHistoryDays(Math.max(0, Number(e.target.value))); touched(); }}
          inputProps={{ min: 0, max: 365 }}
          sx={{ width: 200 }}
        />
        <Typography variant="caption" color="text.secondary" display="block" sx={{ mt: 0.5 }}>
          {t('intelligence.minHistoryDaysHint')}
        </Typography>
      </Box>

      <Box>
        <Button variant="contained" startIcon={<SaveRoundedIcon />} onClick={save} disabled={saving}>
          {t('intelligence.save')}
        </Button>
      </Box>
    </Stack>
  );
}
