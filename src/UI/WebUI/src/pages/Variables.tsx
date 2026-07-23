// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

// Epic 3E global variables: named, persistent values a rule can read/write like any device (each is also
// projected server-side as its own virtual capability device with a single `value` capability — no special
// UI is needed elsewhere for that; this page is just the CRUD editor for the variable itself). Structure
// mirrors Scenes.tsx: a flat list + a create/edit dialog, nothing fancier.

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Container, Box, Typography, Stack, Button, IconButton, LinearProgress, Alert,
  Card, CardContent, Tooltip, Chip, Dialog, DialogTitle, DialogContent, DialogActions,
  TextField, MenuItem, Switch,
} from '@mui/material';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import EditRoundedIcon from '@mui/icons-material/EditRounded';
import DataObjectRoundedIcon from '@mui/icons-material/DataObjectRounded';
import { variablesApi, GlobalVariable, NewVariable, VariableType } from '../api/variables';
import { fmt } from '../components/automations/ruleValues';

const VARIABLE_TYPES: VariableType[] = ['Number', 'Boolean', 'String', 'DateTime', 'List'];

/** The variable-builder draft: keeps description as a plain string for the field. */
interface VariableDraft {
  id?: string;
  name: string;
  type: VariableType;
  description: string;
  value: unknown;
}

/** A sensible starting value for a freshly picked type. */
const defaultValueForType = (type: VariableType): unknown => {
  if (type === 'Number') return 0;
  if (type === 'Boolean') return false;
  if (type === 'List') return '[]';
  return '';
};

const emptyDraft = (): VariableDraft => ({ name: '', type: 'Number', description: '', value: 0 });

const draftFromVariable = (v: GlobalVariable): VariableDraft => ({
  id: v.id, name: v.name, type: v.type, description: v.description ?? '', value: v.value,
});

const isDraftValid = (d: VariableDraft): boolean => !!d.name.trim();

export default function Variables() {
  const { t } = useTranslation('variables');
  const [variables, setVariables] = useState<GlobalVariable[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [draft, setDraft] = useState<VariableDraft | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      setVariables(await variablesApi.getVariables());
    } catch {
      setError(t('errors.load'));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => { load(); }, [load]);

  const remove = async (v: GlobalVariable) => {
    if (!window.confirm(t('confirm.delete', { name: v.name }))) return;
    try { await variablesApi.deleteVariable(v.id); await load(); }
    catch { setError(t('errors.delete')); }
  };

  const save = async () => {
    if (!draft || !isDraftValid(draft)) return;
    const payload: NewVariable = {
      name: draft.name.trim(),
      type: draft.type,
      description: draft.description.trim() || null,
      value: draft.value,
    };
    try {
      if (draft.id) {
        const existing = variables.find((v) => v.id === draft.id);
        if (existing) await variablesApi.updateVariable(draft.id, { ...existing, ...payload });
      } else {
        await variablesApi.createVariable(payload);
      }
      setDraft(null);
      await load();
    } catch {
      setError(draft.id ? t('errors.update') : t('errors.create'));
    }
  };

  const sorted = useMemo(() => [...variables].sort((a, b) => a.name.localeCompare(b.name)), [variables]);

  const formatValue = (v: GlobalVariable): string => {
    if (v.value === null || v.value === undefined || v.value === '') return t('empty.noValue');
    return fmt(v.value);
  };

  return (
    <Container maxWidth="lg">
      <Box py={{ xs: 3, md: 4 }}>
        <Stack direction="row" alignItems="center" justifyContent="space-between" mb={3}>
          <Box>
            <Typography variant="h4" component="h1" fontWeight={700}>{t('title')}</Typography>
            <Typography variant="caption" color="text.secondary">{t('subtitle')}</Typography>
          </Box>
          <Button variant="contained" startIcon={<AddRoundedIcon />} onClick={() => setDraft(emptyDraft())}>
            {t('actions.newVariable')}
          </Button>
        </Stack>

        {loading && <LinearProgress sx={{ mb: 2, borderRadius: 1 }} />}
        {error && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setError(null)}>{error}</Alert>}

        {sorted.length === 0 && !loading ? (
          <Box textAlign="center" py={8}>
            <DataObjectRoundedIcon sx={{ fontSize: 64, color: 'text.disabled', mb: 2 }} />
            <Typography color="text.secondary">{t('empty.none')}</Typography>
          </Box>
        ) : (
          <Stack spacing={1.5}>
            {sorted.map((v) => (
              <Card key={v.id} variant="outlined">
                <CardContent sx={{ py: 1.5, '&:last-child': { pb: 1.5 } }}>
                  <Stack direction="row" alignItems="flex-start" spacing={2}>
                    <Box flex={1} minWidth={0}>
                      <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap mb={0.5}>
                        <Typography fontWeight={700}>{v.name}</Typography>
                        <Chip size="small" variant="outlined" label={t(`types.${v.type}`)} />
                        <Chip size="small" label={formatValue(v)} />
                      </Stack>
                      {v.description && (
                        <Typography variant="body2" color="text.secondary">{v.description}</Typography>
                      )}
                    </Box>
                    <Stack direction="row" alignItems="center" spacing={0.5}>
                      <Tooltip title={t('actions.edit')}>
                        <IconButton onClick={() => setDraft(draftFromVariable(v))}><EditRoundedIcon /></IconButton>
                      </Tooltip>
                      <Tooltip title={t('actions.delete')}>
                        <IconButton onClick={() => remove(v)}><DeleteOutlineRoundedIcon /></IconButton>
                      </Tooltip>
                    </Stack>
                  </Stack>
                </CardContent>
              </Card>
            ))}
          </Stack>
        )}
      </Box>

      <VariableDialog draft={draft} onChange={setDraft} onClose={() => setDraft(null)} onSave={save} />
    </Container>
  );
}

/** Create/edit dialog: name + type + optional description + a type-aware editor for the current value. */
function VariableDialog({
  draft, onChange, onClose, onSave,
}: {
  draft: VariableDraft | null;
  onChange: (d: VariableDraft) => void;
  onClose: () => void;
  onSave: () => void;
}) {
  const { t } = useTranslation('variables');
  const valid = draft ? isDraftValid(draft) : false;
  const editing = !!draft?.id;

  return (
    <Dialog open={draft !== null} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>{editing ? t('dialog.editTitle') : t('dialog.createTitle')}</DialogTitle>
      <DialogContent>
        {draft && (
          <Stack spacing={2.5} mt={1}>
            <TextField label={t('dialog.name')} value={draft.name} autoFocus required fullWidth
              onChange={(e) => onChange({ ...draft, name: e.target.value })} />
            <TextField select label={t('dialog.type')} value={draft.type} fullWidth
              onChange={(e) => {
                const type = e.target.value as VariableType;
                onChange({ ...draft, type, value: defaultValueForType(type) });
              }}>
              {VARIABLE_TYPES.map((ty) => <MenuItem key={ty} value={ty}>{t(`types.${ty}`)}</MenuItem>)}
            </TextField>
            <TextField label={t('dialog.description')} value={draft.description} fullWidth multiline minRows={2}
              onChange={(e) => onChange({ ...draft, description: e.target.value })} />
            <ValueField type={draft.type} value={draft.value} onChange={(value) => onChange({ ...draft, value })} />
          </Stack>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('actions.cancel')}</Button>
        <Button variant="contained" onClick={onSave} disabled={!valid}>
          {editing ? t('actions.save') : t('actions.create')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

/** Type-aware control for the current value: switch for Boolean, number field for Number, else text. */
function ValueField({ type, value, onChange }: {
  type: VariableType; value: unknown; onChange: (v: unknown) => void;
}) {
  const { t } = useTranslation('variables');

  if (type === 'Boolean') {
    return (
      <Stack direction="row" alignItems="center" spacing={1.5}>
        <Typography variant="body2" color="text.secondary">{t('dialog.value')}</Typography>
        <Switch checked={value === true} onChange={(e) => onChange(e.target.checked)} />
      </Stack>
    );
  }
  if (type === 'Number') {
    return (
      <TextField type="number" label={t('dialog.value')} fullWidth
        value={value === null || value === undefined ? '' : String(value)}
        onChange={(e) => onChange(e.target.value === '' ? '' : Number(e.target.value))} />
    );
  }
  if (type === 'DateTime') {
    return (
      <TextField label={t('dialog.value')} fullWidth helperText={t('dialog.dateTimeHint')}
        value={typeof value === 'string' ? value : ''}
        onChange={(e) => onChange(e.target.value)} />
    );
  }
  if (type === 'List') {
    return (
      <TextField label={t('dialog.value')} fullWidth multiline minRows={3} helperText={t('dialog.listHint')}
        value={typeof value === 'string' ? value : ''}
        onChange={(e) => onChange(e.target.value)} />
    );
  }
  // String
  return (
    <TextField label={t('dialog.value')} fullWidth
      value={typeof value === 'string' ? value : ''}
      onChange={(e) => onChange(e.target.value)} />
  );
}
