import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Box, Typography, Switch, Slider, Chip, Button, Stack, LinearProgress } from '@mui/material';
import type { Capability, CapabilityDevice } from '../../api/capabilityDevices';
import {
  asBool, asNum, capabilityIcon, capabilityLabel, formatCapabilityValue,
} from './deviceVisuals';

export type CommandFn = (deviceId: string, set: Record<string, unknown>) => void;

const toHex = (v: unknown): string => {
  if (typeof v === 'string' && /^#?[0-9a-fA-F]{6}$/.test(v)) return v.startsWith('#') ? v : `#${v}`;
  return '#ffffff';
};

/** Writable number → slider that only commits on release (avoids command spam while dragging). */
function NumberSlider({
  device, cap, value, onCommand,
}: { device: CapabilityDevice; cap: Capability; value: unknown; onCommand: CommandFn }) {
  const [local, setLocal] = useState(asNum(value));
  const dragging = useRef(false);

  useEffect(() => {
    if (!dragging.current) setLocal(asNum(value));
  }, [value]);

  const min = cap.min ?? 0;
  const max = cap.max ?? 100;
  const unit = cap.unit ? ` ${cap.unit}` : '';

  return (
    <Box>
      <Box display="flex" justifyContent="space-between" alignItems="center" mb={0.5}>
        <Typography variant="body2" color="text.secondary">{capabilityLabel(cap.id)}</Typography>
        <Typography variant="body2" fontWeight={600}>{local}{unit}</Typography>
      </Box>
      <Slider
        size="small"
        min={min}
        max={max}
        value={local}
        disabled={!device.isOnline}
        valueLabelDisplay="auto"
        onChange={(_, v) => { dragging.current = true; setLocal(v as number); }}
        onChangeCommitted={(_, v) => { dragging.current = false; onCommand(device.id, { [cap.id]: v as number }); }}
      />
    </Box>
  );
}

/**
 * Full control for one capability, chosen by kind + writability.
 * Shared by the detail drawer (and reusable by custom dashboard widgets later).
 */
export default function CapabilityControl({
  device, cap, value, onCommand,
}: { device: CapabilityDevice; cap: Capability; value: unknown; onCommand: CommandFn }) {
  const { t } = useTranslation('devices');
  const Icon = capabilityIcon(cap.id);
  const label = capabilityLabel(cap.id);

  // Writable boolean → toggle
  if (cap.kind === 'Boolean' && cap.writable) {
    const on = asBool(value);
    return (
      <Row icon={<Icon fontSize="small" />} label={label}>
        <Switch
          checked={on}
          color="success"
          disabled={!device.isOnline}
          onChange={(e) => onCommand(device.id, { [cap.id]: e.target.checked })}
        />
      </Row>
    );
  }

  // Writable number → slider (full width row)
  if (cap.kind === 'Number' && cap.writable) {
    return (
      <Box display="flex" gap={1.5} alignItems="flex-start">
        <Box sx={{ color: 'text.secondary', mt: 0.25 }}><Icon fontSize="small" /></Box>
        <Box flex={1}><NumberSlider device={device} cap={cap} value={value} onCommand={onCommand} /></Box>
      </Box>
    );
  }

  // Writable color → native color picker
  if (cap.kind === 'Color' && cap.writable) {
    const hex = toHex(value);
    return (
      <Row icon={<Icon fontSize="small" />} label={label}>
        <Box
          component="input"
          type="color"
          value={hex}
          disabled={!device.isOnline}
          onChange={(e: React.ChangeEvent<HTMLInputElement>) => onCommand(device.id, { [cap.id]: e.target.value })}
          sx={{
            width: 40, height: 32, p: 0, border: 'none', borderRadius: 1.5,
            background: 'none', cursor: 'pointer',
          }}
        />
      </Row>
    );
  }

  // Action → run button
  if (cap.kind === 'Action') {
    return (
      <Row icon={<Icon fontSize="small" />} label={label}>
        <Button size="small" variant="outlined" disabled={!device.isOnline}
          onClick={() => onCommand(device.id, { [cap.id]: true })}>
          {t('actions.run')}
        </Button>
      </Row>
    );
  }

  // Read-only number with a known range → value + thin bar
  if (cap.kind === 'Number' && cap.min != null && cap.max != null) {
    const pct = Math.max(0, Math.min(100, ((asNum(value) - cap.min) / (cap.max - cap.min)) * 100));
    return (
      <Box display="flex" gap={1.5} alignItems="center">
        <Box sx={{ color: 'text.secondary' }}><Icon fontSize="small" /></Box>
        <Box flex={1}>
          <Box display="flex" justifyContent="space-between" mb={0.5}>
            <Typography variant="body2" color="text.secondary">{label}</Typography>
            <Typography variant="body2" fontWeight={600}>{formatCapabilityValue(cap, value)}</Typography>
          </Box>
          <LinearProgress variant="determinate" value={pct} sx={{ height: 6, borderRadius: 3 }} />
        </Box>
      </Box>
    );
  }

  // Read-only fallback → labelled value chip
  return (
    <Row icon={<Icon fontSize="small" />} label={label}>
      <Chip size="small" variant="outlined" label={formatCapabilityValue(cap, value)} />
    </Row>
  );
}

function Row({ icon, label, children }: { icon: React.ReactNode; label: string; children: React.ReactNode }) {
  return (
    <Stack direction="row" alignItems="center" justifyContent="space-between" spacing={1.5}>
      <Stack direction="row" alignItems="center" spacing={1.5} sx={{ minWidth: 0 }}>
        <Box sx={{ color: 'text.secondary', display: 'flex' }}>{icon}</Box>
        <Typography variant="body2" color="text.secondary" noWrap>{label}</Typography>
      </Stack>
      {children}
    </Stack>
  );
}
