// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Dialog, DialogTitle, DialogContent, DialogContentText, DialogActions, Button, Stack,
  Table, TableHead, TableRow, TableCell, TableBody, Checkbox, Typography, Box,
} from '@mui/material';
import ArrowRightAltRoundedIcon from '@mui/icons-material/ArrowRightAltRounded';
import { capabilityDevicesApi, CapabilityDevice } from '../../api/capabilityDevices';

/** One proposed rename: the device, its current display name, and the suggested new name. */
export interface RenameProposal {
  device: CapabilityDevice;
  current: string;
  proposed: string;
}

/**
 * Confirmation for renaming device aliases after a zone change (roadmap Epic 3G — points 3-5).
 * A single proposal shows a plain "rename X → Y?" prompt; several show a table where each row can be
 * approved or rejected, with "approve all / reject all / confirm / cancel". Renames are only written
 * on confirm, and only for approved rows — nothing changes on cancel.
 */
export default function DeviceRenameDialog({
  proposals, open, onClose, onApplied,
}: {
  proposals: RenameProposal[];
  open: boolean;
  onClose: () => void;
  onApplied: (updates: Record<string, string>) => void;
}) {
  const { t } = useTranslation('devices');
  const [approved, setApproved] = useState<Set<string>>(new Set());
  const [busy, setBusy] = useState(false);

  // Every device starts approved when a fresh batch opens.
  useEffect(() => {
    if (open) setApproved(new Set(proposals.map((p) => p.device.id)));
  }, [open, proposals]);

  const single = proposals.length === 1;

  const toggle = (id: string) =>
    setApproved((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
  const approveAll = () => setApproved(new Set(proposals.map((p) => p.device.id)));
  const rejectAll = () => setApproved(new Set());

  const confirm = useCallback(async () => {
    setBusy(true);
    const updates: Record<string, string> = {};
    const toApply = proposals.filter((p) => approved.has(p.device.id));
    await Promise.allSettled(
      toApply.map(async (p) => {
        try {
          await capabilityDevicesApi.setAlias(p.device.id, p.proposed);
          updates[p.device.id] = p.proposed;
        } catch {
          // A failed rename is skipped; the zone assignment itself already succeeded.
        }
      }),
    );
    setBusy(false);
    onApplied(updates);
  }, [proposals, approved, onApplied]);

  const approvedCount = proposals.filter((p) => approved.has(p.device.id)).length;

  return (
    <Dialog open={open} onClose={busy ? undefined : onClose} fullWidth maxWidth={single ? 'xs' : 'sm'}>
      <DialogTitle>{single ? t('rename.title') : t('rename.titleMany', { count: proposals.length })}</DialogTitle>
      <DialogContent>
        {single ? (
          <DialogContentText component="div">
            <Trans current={proposals[0].current} proposed={proposals[0].proposed} />
          </DialogContentText>
        ) : (
          <>
            <DialogContentText sx={{ mb: 1 }}>{t('rename.subtitleMany')}</DialogContentText>
            <Stack direction="row" spacing={1} mb={1}>
              <Button size="small" onClick={approveAll}>{t('rename.approveAll')}</Button>
              <Button size="small" color="inherit" onClick={rejectAll}>{t('rename.rejectAll')}</Button>
            </Stack>
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell padding="checkbox" />
                  <TableCell>{t('rename.columns.current')}</TableCell>
                  <TableCell>{t('rename.columns.proposed')}</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {proposals.map((p) => (
                  <TableRow
                    key={p.device.id}
                    hover
                    onClick={() => toggle(p.device.id)}
                    sx={{ cursor: 'pointer' }}
                  >
                    <TableCell padding="checkbox">
                      <Checkbox
                        size="small"
                        checked={approved.has(p.device.id)}
                        onClick={(e) => e.stopPropagation()}
                        onChange={() => toggle(p.device.id)}
                      />
                    </TableCell>
                    <TableCell>
                      <Typography variant="body2" color="text.secondary">{p.current}</Typography>
                    </TableCell>
                    <TableCell>
                      <Typography variant="body2" fontWeight={600}>{p.proposed}</Typography>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={busy}>{t('rename.cancel')}</Button>
        <Button
          variant="contained"
          onClick={confirm}
          disabled={busy || approvedCount === 0}
        >
          {single ? t('rename.confirmOne') : t('rename.confirm')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

/** "«current» → «proposed»" line for the single-device prompt. */
function Trans({ current, proposed }: { current: string; proposed: string }) {
  return (
    <Box component="span" sx={{ display: 'inline-flex', alignItems: 'center', flexWrap: 'wrap', gap: 0.5 }}>
      <Box component="span" color="text.secondary">«{current}»</Box>
      <ArrowRightAltRoundedIcon fontSize="small" sx={{ color: 'text.disabled' }} />
      <Box component="span" fontWeight={700}>«{proposed}»</Box>
    </Box>
  );
}
