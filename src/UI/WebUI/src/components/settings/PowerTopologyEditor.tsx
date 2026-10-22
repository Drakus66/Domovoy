// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Alert, Box, Button, IconButton, InputAdornment, MenuItem, Stack, TextField, Typography,
} from '@mui/material';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import {
  powerTopologyApi, orderedTree, nodeDepth,
  POWER_NODE_KINDS, POWER_PHASES, type PowerNode,
} from '../../api/powerTopology';
import { capabilityDevicesApi, type CapabilityDevice } from '../../api/capabilityDevices';
import { deviceLabel } from '../devices/deviceNaming';

/**
 * Electrical-topology editor (roadmap Epic 3C-D) — a self-contained Settings section (like TariffEditor).
 * Describes how the house is actually wired: supply → panel → circuit, each circuit with its phase and
 * breaker rating, optionally metered. Devices are attached to a circuit from their own card; here we only
 * shape the tree. Every edit saves immediately — each node is a small independent document.
 */
export default function PowerTopologyEditor() {
  const { t } = useTranslation('settings');
  const [nodes, setNodes] = useState<PowerNode[]>([]);
  const [meters, setMeters] = useState<CapabilityDevice[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    powerTopologyApi.getNodes().then(setNodes).catch(() => setError(t('powerTopology.loadError')));
    capabilityDevicesApi.getDevices()
      .then((all) => setMeters(all.filter((d) => d.capabilities.some((c) => c.id === 'power' || c.id === 'energy'))))
      .catch(() => { /* the meter picker just stays empty */ });
  }, [t]);

  const byId = useMemo(() => new Map(nodes.map((n) => [n.id, n])), [nodes]);
  const ordered = useMemo(() => orderedTree(nodes), [nodes]);

  const add = async (kind: string) => {
    setError(null);
    try {
      const created = await powerTopologyApi.createNode({
        name: t(`powerTopology.newNames.${kind}`),
        kind,
        parentId: null,
        phase: kind === 'circuit' ? 'l1' : null,
        breakerAmps: null,
        voltage: null,
        meterDeviceId: null,
        powerSourceKind: kind === 'supply' ? 'grid' : null,
        order: nodes.length,
      });
      setNodes((cur) => [...cur, created]);
    } catch {
      setError(t('powerTopology.saveError'));
    }
  };

  const patch = async (node: PowerNode, changes: Partial<PowerNode>) => {
    const next = { ...node, ...changes };
    setNodes((cur) => cur.map((n) => (n.id === node.id ? next : n)));
    setError(null);
    try {
      // The id travels in the route, not the body.
      const body = { ...next, id: undefined } as unknown as Parameters<typeof powerTopologyApi.updateNode>[1];
      await powerTopologyApi.updateNode(node.id, body);
    } catch {
      setError(t('powerTopology.saveError'));
    }
  };

  const remove = async (node: PowerNode) => {
    setError(null);
    try {
      await powerTopologyApi.deleteNode(node.id);
      // The server reparents the children, so re-read rather than guessing the new shape.
      setNodes(await powerTopologyApi.getNodes());
    } catch {
      setError(t('powerTopology.saveError'));
    }
  };

  return (
    <Stack spacing={2}>
      {error && <Alert severity="warning" onClose={() => setError(null)}>{error}</Alert>}
      <Typography variant="caption" color="text.secondary">{t('powerTopology.hint')}</Typography>

      {ordered.map((node) => {
        const depth = nodeDepth(node, byId);
        // A node may hang off any other node except itself (a cycle would break the tree walk).
        const parents = nodes.filter((n) => n.id !== node.id);
        return (
          <Box
            key={node.id}
            sx={{ ml: depth * 2, p: 1.5, border: '1px solid', borderColor: 'divider', borderRadius: 1.5 }}
          >
            <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
              <TextField
                size="small" label={t('powerTopology.name')} value={node.name}
                onChange={(e) => patch(node, { name: e.target.value })}
                sx={{ minWidth: 180 }}
              />
              <TextField
                select size="small" label={t('powerTopology.kind')} value={node.kind}
                onChange={(e) => patch(node, { kind: e.target.value })}
                sx={{ minWidth: 130 }}
              >
                {POWER_NODE_KINDS.map((k) => (
                  <MenuItem key={k} value={k}>{t(`powerTopology.kinds.${k}`)}</MenuItem>
                ))}
              </TextField>
              <TextField
                select size="small" label={t('powerTopology.parent')} value={node.parentId ?? ''}
                onChange={(e) => patch(node, { parentId: e.target.value || null })}
                sx={{ minWidth: 160 }}
              >
                <MenuItem value=""><em>{t('powerTopology.noParent')}</em></MenuItem>
                {parents.map((p) => (
                  <MenuItem key={p.id} value={p.id}>{p.name}</MenuItem>
                ))}
              </TextField>
              <TextField
                select size="small" label={t('powerTopology.phase')} value={node.phase ?? ''}
                onChange={(e) => patch(node, { phase: e.target.value || null })}
                sx={{ minWidth: 140 }}
              >
                <MenuItem value=""><em>{t('powerTopology.phaseInherit')}</em></MenuItem>
                {POWER_PHASES.map((p) => (
                  <MenuItem key={p} value={p}>{t(`powerTopology.phases.${p}`)}</MenuItem>
                ))}
              </TextField>
              <TextField
                type="number" size="small" label={t('powerTopology.breaker')} value={node.breakerAmps ?? ''}
                onChange={(e) => patch(node, { breakerAmps: e.target.value === '' ? null : Number(e.target.value) })}
                InputProps={{ endAdornment: <InputAdornment position="end">A</InputAdornment> }}
                sx={{ width: 130 }}
              />
              <TextField
                select size="small" label={t('powerTopology.meter')} value={node.meterDeviceId ?? ''}
                onChange={(e) => patch(node, { meterDeviceId: e.target.value || null })}
                sx={{ minWidth: 180 }}
              >
                <MenuItem value=""><em>{t('powerTopology.noMeter')}</em></MenuItem>
                {meters.map((d) => (
                  <MenuItem key={d.id} value={d.id}>{deviceLabel(d)}</MenuItem>
                ))}
              </TextField>
              <IconButton size="small" onClick={() => remove(node)} aria-label={t('powerTopology.delete')}>
                <DeleteOutlineRoundedIcon fontSize="small" />
              </IconButton>
            </Stack>
          </Box>
        );
      })}

      <Stack direction="row" spacing={1}>
        {POWER_NODE_KINDS.map((k) => (
          <Button key={k} size="small" startIcon={<AddRoundedIcon />} onClick={() => add(k)}>
            {t(`powerTopology.add.${k}`)}
          </Button>
        ))}
      </Stack>
    </Stack>
  );
}
