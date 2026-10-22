// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import {
  Button, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle,
} from '@mui/material';
import { useTranslation } from 'react-i18next';
import { useConfirmStore } from '../../store/confirmStore';

/**
 * Единственная точка монтирования диалога подтверждения (см. `store/confirmStore`). Стоит в `App`
 * рядом с `NotificationContainer`: обе механики — общие для всего интерфейса и не принадлежат
 * конкретной странице.
 */
export function ConfirmDialog() {
  const { t } = useTranslation('common');
  const request = useConfirmStore((s) => s.request);
  const settle = useConfirmStore((s) => s.settle);

  return (
    <Dialog open={request !== null} onClose={() => settle(false)}>
      <DialogTitle>{request?.title ?? t('confirm.title')}</DialogTitle>
      <DialogContent>
        <DialogContentText>{request?.message}</DialogContentText>
      </DialogContent>
      <DialogActions>
        <Button onClick={() => settle(false)}>{request?.cancelLabel ?? t('actions.cancel')}</Button>
        <Button
          variant="contained"
          color={request?.destructive === false ? 'primary' : 'error'}
          onClick={() => settle(true)}
          autoFocus
        >
          {request?.confirmLabel ?? t('actions.confirm')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

export default ConfirmDialog;
