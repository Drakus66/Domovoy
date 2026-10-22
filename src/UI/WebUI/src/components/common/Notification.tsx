// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

// Notification Component for WebUI
// Validates: Requirements 8.2, 8.3, 8.5

import React, { useState } from 'react';
import {
  Alert,
  AlertColor,
  IconButton,
  Box,
  Button,
  Stack,
} from '@mui/material';
import { Close as CloseIcon } from '@mui/icons-material';
import { useTranslation } from 'react-i18next';
import { useUIStore, Notification as NotificationData, NotificationActionData } from '../../store/uiStore';
import { notificationsApi } from '../../api/notifications';

/**
 * Single notification item component. Rendered directly inside the fixed column container below (no MUI
 * Snackbar wrapper): a Snackbar carries its own fixed top-right anchor, so several at once would stack on the
 * same spot and hide each other. As a plain Alert it flows in the flex column and banners stack vertically.
 * Auto-dismiss is driven by the store timer (uiStore.showNotification), so no per-item timer is needed here.
 */
interface NotificationItemProps {
  notification: NotificationData;
  onClose: (id: string) => void;
}

const NotificationItem: React.FC<NotificationItemProps> = ({ notification, onClose }) => {
  const { t } = useTranslation('common');
  const [busy, setBusy] = useState(false);
  const handleClose = () => onClose(notification.id);

  // Run an actionable button (Epic 3F). Client-only kinds are handled here; server-side kinds post to the action
  // endpoint (which executes the command with actor attribution). The banner is dismissed once the action fires.
  const runAction = async (action: NotificationActionData) => {
    if (action.kind === 'dismiss') { handleClose(); return; }
    if (action.kind === 'open') {
      // The banner renders outside the router, so navigate the hard way.
      window.location.assign(action.params?.route || '/');
      return;
    }
    setBusy(true);
    try {
      await notificationsApi.executeAction(action);
    } catch {
      /* best-effort — the command endpoint logs failures; the banner just closes */
    } finally {
      setBusy(false);
      handleClose();
    }
  };

  return (
    <Alert
      severity={notification.type as AlertColor}
      variant="filled"
      action={
        <IconButton
          size="small"
          aria-label={t('actions.close')}
          color="inherit"
          onClick={handleClose}
        >
          <CloseIcon fontSize="small" />
        </IconButton>
      }
      sx={{
        minWidth: 300,
        maxWidth: 420,
        boxShadow: 6,
      }}
    >
      {notification.message}
      {notification.actions && notification.actions.length > 0 && (
        <Stack direction="row" spacing={1} mt={1} flexWrap="wrap" useFlexGap>
          {notification.actions.map((action) => (
            <Button
              key={action.id}
              size="small"
              variant="outlined"
              color="inherit"
              disabled={busy}
              onClick={() => runAction(action)}
              sx={{ borderColor: 'currentColor' }}
            >
              {action.label}
            </Button>
          ))}
        </Stack>
      )}
    </Alert>
  );
};

/**
 * Notification container component that displays all active notifications
 * Integrates with uiStore to show toast notifications with auto-dismiss
 */
export const NotificationContainer: React.FC = () => {
  const notifications = useUIStore((state) => state.notifications);
  const dismissNotification = useUIStore((state) => state.dismissNotification);

  if (notifications.length === 0) {
    return null;
  }

  return (
    <Box
      sx={{
        position: 'fixed',
        top: 16,
        right: 16,
        zIndex: 9999,
        display: 'flex',
        flexDirection: 'column',
        gap: 1,
        pointerEvents: 'none',
        '& > *': {
          pointerEvents: 'auto',
        },
      }}
    >
      {notifications.map((notification) => (
        <NotificationItem
          key={notification.id}
          notification={notification}
          onClose={dismissNotification}
        />
      ))}
    </Box>
  );
};

export default NotificationContainer;
