// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Box, Stack, Typography, Card, Button, IconButton, Tooltip, LinearProgress, Alert,
  Dialog, DialogTitle, DialogContent, DialogActions, TextField, Divider,
} from '@mui/material';
import TuneRoundedIcon from '@mui/icons-material/TuneRounded';
import { fmtDate } from '../../i18n/format';
import { diaryApi, DiaryEntry, NarrativeEntity, SynonymForm } from '../../api/diary';

const LOCALE = 'ru';

/** Build a persona override from a single custom name entered by the user. */
function personaEntity(key: 'Spirit' | 'Residents', name: string): NarrativeEntity {
  const plural = key === 'Residents';
  const syn: SynonymForm = {
    text: name,
    gender: plural ? 'm' : 'm',
    number: plural ? 'pl' : 'sg',
    animacy: 'anim',
    subjectCase: name,
    pronoun: plural ? 'они' : 'он',
  };
  return { locale: LOCALE, kind: 'persona', key, synonyms: [syn] };
}

/** Diary personalization: name the house spirit and the residents (Epic 2N). */
function PersonalizeDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { t } = useTranslation('diary');
  const [spirit, setSpirit] = useState('');
  const [residents, setResidents] = useState('');
  const [existing, setExisting] = useState<Record<string, NarrativeEntity>>({});
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (!open) return;
    diaryApi.entities(LOCALE, 'persona').then((rows) => {
      const byKey: Record<string, NarrativeEntity> = {};
      rows.forEach((r) => { byKey[r.key] = r; });
      setExisting(byKey);
      setSpirit(byKey.Spirit?.synonyms?.[0]?.text ?? '');
      setResidents(byKey.Residents?.synonyms?.[0]?.text ?? '');
    }).catch(() => undefined);
  }, [open]);

  const save = useCallback(async () => {
    setSaving(true);
    try {
      const tasks: Promise<unknown>[] = [];
      if (spirit.trim()) tasks.push(diaryApi.saveEntity({ ...existing.Spirit, ...personaEntity('Spirit', spirit.trim()) }));
      if (residents.trim()) tasks.push(diaryApi.saveEntity({ ...existing.Residents, ...personaEntity('Residents', residents.trim()) }));
      await Promise.all(tasks);
      onClose();
    } finally {
      setSaving(false);
    }
  }, [spirit, residents, existing, onClose]);

  return (
    <Dialog open={open} onClose={onClose} maxWidth="xs" fullWidth>
      <DialogTitle>{t('personalize.title')}</DialogTitle>
      <DialogContent>
        <Typography variant="caption" color="text.secondary">{t('personalize.hint')}</Typography>
        <Stack spacing={2} mt={1.5}>
          <TextField label={t('personalize.spirit')} value={spirit} onChange={(e) => setSpirit(e.target.value)}
            placeholder="домовой" size="small" fullWidth />
          <TextField label={t('personalize.residents')} value={residents} onChange={(e) => setResidents(e.target.value)}
            placeholder="домочадцы" size="small" fullWidth />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('personalize.cancel')}</Button>
        <Button variant="contained" onClick={save} disabled={saving}>{t('personalize.save')}</Button>
      </DialogActions>
    </Dialog>
  );
}

/** The House Diary feed (roadmap Epic 2N): a literary daily chronicle rendered by the backend. */
export default function DiaryView() {
  const { t } = useTranslation('diary');
  const [entries, setEntries] = useState<DiaryEntry[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState(false);

  const load = useCallback(async () => {
    setError(null);
    try {
      setEntries(await diaryApi.get({ limit: 60 }));
    } catch {
      setError(t('error'));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => { load(); }, [load]);

  return (
    <Box>
      <Stack direction="row" spacing={1} alignItems="center" mb={2}>
        <Typography variant="body2" color="text.secondary" flex={1}>{t('intro')}</Typography>
        <Tooltip title={t('personalize.title')}>
          <IconButton size="small" onClick={() => setEditing(true)}><TuneRoundedIcon /></IconButton>
        </Tooltip>
      </Stack>

      {loading && <LinearProgress sx={{ mb: 2, borderRadius: 1 }} />}
      {error && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setError(null)}>{error}</Alert>}

      {entries.length === 0 && !loading ? (
        <Box textAlign="center" py={8}>
          <Typography color="text.secondary">{t('empty')}</Typography>
        </Box>
      ) : (
        <Card variant="outlined">
          <Stack divider={<Divider />}>
            {entries.map((e) => (
              <Box key={e.id} sx={{ px: 2.5, py: 2 }}>
                <Typography variant="overline" color="text.secondary">{fmtDate(e.date)}</Typography>
                <Typography variant="body1" sx={{ mt: 0.5, lineHeight: 1.7 }}>{e.paragraph}</Typography>
              </Box>
            ))}
          </Stack>
        </Card>
      )}

      <PersonalizeDialog open={editing} onClose={() => { setEditing(false); load(); }} />
    </Box>
  );
}
