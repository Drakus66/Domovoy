import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import type { CapabilityDevice } from '../../api/capabilityDevices';
import type { CommandFn } from '../devices/CapabilityControls';
import { deviceCategory, type DeviceCategory } from '../devices/deviceVisuals';
import ZoneGroupedGrid from './ZoneGroupedGrid';

/**
 * An auto-sphere tab: every device of one category (light/climate/…), zone-grouped like the
 * All tab. Purely derived — a sphere with zero devices never renders (the tab disappears).
 */
export default function SphereTab({
  category, devices, zoneName, onOpen, onCommand,
}: {
  category: DeviceCategory;
  devices: CapabilityDevice[];
  zoneName: (zoneId?: string | null) => string;
  onOpen: (d: CapabilityDevice) => void;
  onCommand: CommandFn;
}) {
  const { t } = useTranslation('devices');
  const matching = useMemo(
    () => devices.filter((d) => deviceCategory(d) === category),
    [devices, category],
  );

  return (
    <ZoneGroupedGrid
      devices={matching}
      zoneName={zoneName}
      unassignedLabel={t('unassigned')}
      onOpen={onOpen}
      onCommand={onCommand}
    />
  );
}
