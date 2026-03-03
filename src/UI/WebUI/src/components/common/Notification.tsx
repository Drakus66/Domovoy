// Notification Component for WebUI
// Validates: Requirements 8.2, 8.3, 8.5

import React from 'react';
import {
  Snackbar,
  Alert,
  AlertColor,
  IconButton,
  Box,
} from '@mui/material';
import { Close as CloseIcon } from '@mui/icons-material';
import { useUIStore, Notification as NotificationData } from '../../store/uiStore';

/**
 * Single notification item component
 */
interface NotificationItemProps {
  notification: NotificationData;
  onClose: (id: string) => void;
}

const NotificationItem: React.FC<NotificationItemProps> = ({ notification, onClose }) => {
  const handleClose = (_event?: React.SyntheticEvent | Event, reason?: string) => {
    // Don't close on clickaway
    if (reason === 'clickaway') {
      return;
    }
    onClose(notification.id);
  };

  return (
    <Snackbar
      open={true}
      autoHideDuration={notification.duration || null}
      onClose={handleClose}
      anchorOrigin={{ vertical: 'top', horizontal: 'right' }}
      sx={{ position: 'relative' }}
    >
      <Alert
        severity={notification.type as AlertColor}
        variant="filled"
        onClose={handleClose}
        action={
          <IconButton
            size="small"
            aria-label="close"
            color="inherit"
            onClick={handleClose}
          >
            <CloseIcon fontSize="small" />
          </IconButton>
        }
        sx={{
          width: '100%',
          minWidth: 300,
          maxWidth: 500,
        }}
      >
        {notification.message}
      </Alert>
    </Snackbar>
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
