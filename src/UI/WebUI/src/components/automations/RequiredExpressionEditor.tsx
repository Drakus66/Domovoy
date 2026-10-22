// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

// Epic 3E "required expression" editor: a live gate that must hold both to start a rule run AND
// continuously while it executes (Hubitat-style). Reuses the existing ConditionEditor for each guard —
// the expression itself is just those guards combined by index ("C0", "C1", …) with && || ! ( ).

import { Box, Button, Stack, TextField, Typography, IconButton, Paper, Chip } from '@mui/material';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import { useTranslation } from 'react-i18next';
import { RequiredExpression, RuleCondition } from '../../api/automations';
import { CapabilityDevice } from '../../api/capabilityDevices';
import { Zone } from '../../api/zones';
import { newCondition } from '../../data/safetyTemplates';
import { ConditionEditor } from './RuleEditors';

type Caps = (id?: string | null) => CapabilityDevice['capabilities'];

const TOKENS = ['&&', '||', '!', '(', ')'];

export default function RequiredExpressionEditor({ value, onChange, devices, caps, zones = [] }: {
  value: RequiredExpression | null;
  onChange: (r: RequiredExpression | null) => void;
  devices: CapabilityDevice[];
  caps?: Caps;
  zones?: Zone[];
}) {
  const { t } = useTranslation('automations');

  if (value === null) {
    return (
      <Button size="small" startIcon={<AddRoundedIcon />} onClick={() => onChange({ conditions: [], expression: '' })}>
        {t('requiredExpression.add')}
      </Button>
    );
  }

  const setConditions = (conditions: RuleCondition[]) => onChange({ ...value, conditions });
  const insert = (token: string) =>
    onChange({ ...value, expression: value.expression ? `${value.expression} ${token}` : token });

  return (
    <Box>
      <Stack direction="row" alignItems="center" justifyContent="space-between" mb={1}>
        <Typography variant="overline" color="text.secondary">{t('requiredExpression.title')}</Typography>
        <Button size="small" color="error" onClick={() => onChange(null)}>{t('requiredExpression.remove')}</Button>
      </Stack>

      <Stack spacing={1}>
        {value.conditions.map((c, i) => (
          <Box key={i}>
            <Stack direction="row" alignItems="center" spacing={1} mb={0.5}>
              <Chip size="small" label={`C${i}`} />
              <Box flex={1} />
              <IconButton size="small" onClick={() => setConditions(value.conditions.filter((_, x) => x !== i))}>
                <DeleteOutlineRoundedIcon fontSize="small" />
              </IconButton>
            </Stack>
            <Paper variant="outlined" sx={{ p: 1.5, borderRadius: 2 }}>
              <ConditionEditor devices={devices} caps={caps} zones={zones} value={c}
                onChange={(v) => setConditions(value.conditions.map((x, xi) => (xi === i ? v : x)))} />
            </Paper>
          </Box>
        ))}
      </Stack>
      <Button size="small" startIcon={<AddRoundedIcon />} sx={{ mt: 1 }}
        onClick={() => setConditions([...value.conditions, newCondition()])}>
        {t('sections.addCondition')}
      </Button>

      <TextField label={t('requiredExpression.expression')} size="small" fullWidth sx={{ mt: 2 }}
        helperText={t('requiredExpression.hint')}
        value={value.expression}
        onChange={(e) => onChange({ ...value, expression: e.target.value })} />

      <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap sx={{ mt: 1 }}>
        {TOKENS.map((tok) => (
          <Chip key={tok} size="small" label={tok} onClick={() => insert(tok)} variant="outlined" clickable />
        ))}
        {value.conditions.map((_, i) => (
          <Chip key={`C${i}`} size="small" label={`C${i}`} onClick={() => insert(`C${i}`)} variant="outlined" clickable />
        ))}
      </Stack>
    </Box>
  );
}
