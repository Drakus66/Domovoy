import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { Box, Button, Chip, Divider, Grid, Stack, Typography } from '@mui/material';
import EditRoundedIcon from '@mui/icons-material/EditRounded';
import AutoAwesomeRoundedIcon from '@mui/icons-material/AutoAwesomeRounded';
import type { CapabilityDevice } from '../../api/capabilityDevices';
import type { Dashboard } from '../../api/dashboards';
import type { CommandFn } from '../devices/CapabilityControls';
import DashboardItemView, { itemGridWidth } from './items/DashboardItemView';

/**
 * A user-curated tab: named sections, each an auto-laid-out grid of items (device tiles,
 * capability tiles, charts, mode switcher). Layout is the same responsive grid as everywhere
 * else — v1 has no drag-and-drop; order comes from the editor.
 */
export default function CustomDashboardTab({
  dashboard, devices, onOpen, onCommand, onEdit,
}: {
  dashboard: Dashboard;
  devices: CapabilityDevice[];
  onOpen: (d: CapabilityDevice) => void;
  onCommand: CommandFn;
  onEdit: () => void;
}) {
  const { t } = useTranslation('dashboards');
  const deviceById = useMemo(() => new Map(devices.map((d) => [d.id, d])), [devices]);

  const hasItems = dashboard.sections.some((s) => s.items.length > 0);

  if (!hasItems) {
    return (
      <Box textAlign="center" py={8}>
        <AutoAwesomeRoundedIcon sx={{ fontSize: 64, color: 'text.disabled', mb: 2 }} />
        <Typography color="text.secondary" mb={2}>{t('empty.dashboard')}</Typography>
        <Button variant="outlined" startIcon={<EditRoundedIcon />} onClick={onEdit}>
          {t('actions.editTab')}
        </Button>
      </Box>
    );
  }

  return (
    <Stack spacing={4}>
      {dashboard.sections.map((section, si) => (
        <Box key={`${section.title}-${si}`}>
          {(section.title || dashboard.sections.length > 1) && (
            <Stack direction="row" alignItems="center" spacing={1.5} mb={1.5}>
              <Typography variant="h6" fontWeight={700}>{section.title}</Typography>
              <Chip size="small" label={section.items.length} variant="outlined" />
              <Divider sx={{ flex: 1 }} />
            </Stack>
          )}
          {section.items.length === 0 ? (
            <Typography variant="body2" color="text.secondary">{t('empty.section')}</Typography>
          ) : (
            <Grid container spacing={2}>
              {section.items.map((item, ii) => (
                <Grid item {...itemGridWidth(item)} key={ii}>
                  <DashboardItemView
                    item={item}
                    deviceById={deviceById}
                    onOpen={onOpen}
                    onCommand={onCommand}
                  />
                </Grid>
              ))}
            </Grid>
          )}
        </Box>
      ))}
    </Stack>
  );
}
