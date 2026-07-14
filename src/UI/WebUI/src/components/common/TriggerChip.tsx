// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useState, MouseEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { Box, Button, Chip, Popover, Stack, Typography } from '@mui/material';
import PersonRoundedIcon from '@mui/icons-material/PersonRounded';
import BoltRoundedIcon from '@mui/icons-material/BoltRounded';
import AccountTreeRoundedIcon from '@mui/icons-material/AccountTreeRounded';
import DevicesRoundedIcon from '@mui/icons-material/DevicesRounded';
import SensorOccupiedRoundedIcon from '@mui/icons-material/SensorOccupiedRounded';
import PsychologyRoundedIcon from '@mui/icons-material/PsychologyRounded';
import LaunchRoundedIcon from '@mui/icons-material/LaunchRounded';

const KIND_ICON: Record<string, JSX.Element> = {
  user: <PersonRoundedIcon fontSize="small" />,
  rule: <BoltRoundedIcon fontSize="small" />,
  block: <AccountTreeRoundedIcon fontSize="small" />,
  device: <DevicesRoundedIcon fontSize="small" />,
  presence: <SensorOccupiedRoundedIcon fontSize="small" />,
  ml: <PsychologyRoundedIcon fontSize="small" />,
};

/** Route to the initiator's page/entity, or null when there is nowhere to navigate. */
function targetFor(kind: string, id?: string | null): string | null {
  if (!id) return kind === 'user' ? null : null;
  switch (kind) {
    case 'rule': return `/automations?focus=${encodeURIComponent(id)}`;
    case 'block': return `/blocks?focus=${encodeURIComponent(id)}`;
    case 'device':
    case 'presence': return `/?device=${encodeURIComponent(id)}`;
    case 'user': return '/users';
    default: return null;
  }
}

export interface TriggerChipProps {
  /** Coarse initiator bucket: user | rule | block | device | presence | ml. */
  kind: string;
  /** Concrete initiator id (rule/block/user/presence-sensor), when known. */
  id?: string | null;
  /** Resolved display name, when known (server-side in the activity feed; best-effort elsewhere). */
  name?: string | null;
  size?: 'small' | 'medium';
}

/**
 * "Who did it" chip (attribution, Epic 2G tail): shows the initiator kind/name, and on click opens a
 * popover with the details and — when the initiator is a navigable entity — a "go to" action
 * (rule → /automations, block → /blocks, device/presence sensor → device drawer, user → /users).
 */
export default function TriggerChip({ kind, id, name, size = 'small' }: TriggerChipProps) {
  const { t } = useTranslation('common');
  const navigate = useNavigate();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);

  const kindLabel = t(`trigger.${kind}`, { defaultValue: kind });
  const target = targetFor(kind, id);

  const open = (e: MouseEvent<HTMLElement>) => {
    e.stopPropagation();
    setAnchor(e.currentTarget);
  };

  return (
    <>
      <Chip
        size={size}
        variant="outlined"
        icon={KIND_ICON[kind] ?? <DevicesRoundedIcon fontSize="small" />}
        label={name ? `${kindLabel}: ${name}` : kindLabel}
        onClick={open}
        sx={{ maxWidth: 260 }}
      />
      <Popover
        open={anchor !== null}
        anchorEl={anchor}
        onClose={() => setAnchor(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'left' }}
      >
        <Box px={2} py={1.5} minWidth={220} maxWidth={340}>
          <Stack direction="row" spacing={1} alignItems="center" mb={0.5}>
            {KIND_ICON[kind] ?? <DevicesRoundedIcon fontSize="small" />}
            <Typography variant="body2" fontWeight={700}>{kindLabel}</Typography>
          </Stack>
          <Typography variant="body2" sx={{ wordBreak: 'break-word' }}>
            {name ?? t('trigger.unknownName')}
          </Typography>
          {id && (
            <Typography variant="caption" color="text.secondary" sx={{ wordBreak: 'break-all', display: 'block' }}>
              {id}
            </Typography>
          )}
          {kind === 'presence' && (
            <Typography variant="caption" color="text.secondary" display="block" mt={0.5}>
              {t('trigger.presenceHint')}
            </Typography>
          )}
          {target && (
            <Button
              size="small"
              startIcon={<LaunchRoundedIcon />}
              sx={{ mt: 1 }}
              onClick={() => { setAnchor(null); navigate(target); }}
            >
              {t('trigger.goTo')}
            </Button>
          )}
        </Box>
      </Popover>
    </>
  );
}
