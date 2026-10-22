// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useCallback, useState } from 'react';
import DeviceRenameDialog, { RenameProposal } from './DeviceRenameDialog';

/**
 * Wires up the device-rename confirmation flow (Epic 3G): a caller assembles rename proposals (after a
 * zone change / zone rename) and calls `requestRename`; no-op proposals are dropped and the dialog only
 * opens when something would actually change. Renders its own dialog — drop `dialog` into the tree and
 * pass `onAliasesChanged` to reflect applied aliases in local state.
 */
export function useDeviceRename(onAliasesChanged?: (updates: Record<string, string>) => void) {
  const [proposals, setProposals] = useState<RenameProposal[]>([]);

  const requestRename = useCallback((next: RenameProposal[]) => {
    const changed = next.filter(
      (p) => p.proposed.trim().length > 0 && p.proposed.trim() !== p.current.trim(),
    );
    setProposals(changed);
  }, []);

  const close = useCallback(() => setProposals([]), []);

  const dialog = (
    <DeviceRenameDialog
      proposals={proposals}
      open={proposals.length > 0}
      onClose={close}
      onApplied={(updates) => { onAliasesChanged?.(updates); close(); }}
    />
  );

  return { dialog, requestRename };
}
