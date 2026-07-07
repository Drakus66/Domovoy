import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Box, Typography, Stack, TextField, InputAdornment, Chip,
} from '@mui/material';
import SearchRoundedIcon from '@mui/icons-material/SearchRounded';
import DevicesIcon from '@mui/icons-material/Devices';
import type { CapabilityDevice } from '../../api/capabilityDevices';
import type { CommandFn } from '../devices/CapabilityControls';
import ZoneGroupedGrid from './ZoneGroupedGrid';

/**
 * The "All" tab: the classic zone-grouped view of every device with search + adapter/online
 * filters — extracted verbatim from the pre-tabs Devices page.
 */
export default function AllDevicesTab({
  devices, zoneName, loading, onOpen, onCommand,
}: {
  devices: CapabilityDevice[];
  zoneName: (zoneId?: string | null) => string;
  loading: boolean;
  onOpen: (d: CapabilityDevice) => void;
  onCommand: CommandFn;
}) {
  const { t } = useTranslation('devices');
  const [search, setSearch] = useState('');
  const [adapter, setAdapter] = useState<string>('all');
  const [onlineOnly, setOnlineOnly] = useState(false);

  const adapters = useMemo(
    () => Array.from(new Set(devices.map((d) => d.adapterSource))).sort(),
    [devices],
  );

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return devices.filter((d) =>
      (adapter === 'all' || d.adapterSource === adapter) &&
      (!onlineOnly || d.isOnline) &&
      (!q || d.name.toLowerCase().includes(q) || zoneName(d.zoneId).toLowerCase().includes(q)),
    );
  }, [devices, search, adapter, onlineOnly, zoneName]);

  return (
    <>
      <Stack direction="row" spacing={1.5} mb={3} flexWrap="wrap" useFlexGap alignItems="center">
        <TextField
          size="small"
          placeholder={t('filters.searchPlaceholder')}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          sx={{ minWidth: 240, flex: { xs: '1 1 100%', sm: '0 1 320px' } }}
          InputProps={{
            startAdornment: (
              <InputAdornment position="start"><SearchRoundedIcon fontSize="small" /></InputAdornment>
            ),
          }}
        />
        <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
          <Chip
            label={t('filters.all')}
            variant={adapter === 'all' ? 'filled' : 'outlined'}
            color={adapter === 'all' ? 'primary' : 'default'}
            onClick={() => setAdapter('all')}
          />
          {adapters.map((a) => (
            <Chip
              key={a}
              label={a}
              variant={adapter === a ? 'filled' : 'outlined'}
              color={adapter === a ? 'primary' : 'default'}
              onClick={() => setAdapter(a)}
            />
          ))}
          <Chip
            label={t('filters.onlineOnly')}
            variant={onlineOnly ? 'filled' : 'outlined'}
            color={onlineOnly ? 'success' : 'default'}
            onClick={() => setOnlineOnly((v) => !v)}
          />
        </Stack>
      </Stack>

      {filtered.length === 0 && !loading ? (
        <Box textAlign="center" py={8}>
          <DevicesIcon sx={{ fontSize: 64, color: 'text.disabled', mb: 2 }} />
          <Typography color="text.secondary">
            {devices.length === 0
              ? t('empty.noDevices')
              : t('empty.noMatches')}
          </Typography>
        </Box>
      ) : (
        <ZoneGroupedGrid
          devices={filtered}
          zoneName={zoneName}
          unassignedLabel={t('unassigned')}
          onOpen={onOpen}
          onCommand={onCommand}
        />
      )}
    </>
  );
}
