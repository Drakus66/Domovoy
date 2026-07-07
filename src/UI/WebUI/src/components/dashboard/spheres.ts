import type { CapabilityDevice } from '../../api/capabilityDevices';
import { deviceCategory, type DeviceCategory } from '../devices/deviceVisuals';

/**
 * Auto-sphere tabs: the distinctive zero-config half of custom dashboards. Each sphere is a
 * device category (light/climate/…) derived from the backend archetype (Epic 2D) with the
 * capability heuristic as fallback — the same mapping the tiles use, so a device always lands
 * on the sphere whose icon it wears.
 */
export interface Sphere {
  /** Tab id, e.g. "sphere:light" (used in the /t/:tabId route). */
  id: string;
  category: DeviceCategory;
  count: number;
}

/** Fixed display order — mirrors CATEGORY_ACCENT/deviceCategory's vocabulary. */
export const SPHERE_ORDER: DeviceCategory[] = [
  'light', 'switch', 'climate', 'sensor', 'security', 'energy', 'other',
];

export const sphereTabId = (category: DeviceCategory): string => `sphere:${category}`;

export const sphereCategoryFromTabId = (tabId: string): DeviceCategory | null => {
  if (!tabId.startsWith('sphere:')) return null;
  const category = tabId.slice('sphere:'.length) as DeviceCategory;
  return SPHERE_ORDER.includes(category) ? category : null;
};

/**
 * Spheres that exist for this home right now: a category with at least one device, minus the
 * ones the user hid. Pass `hidden: []` to enumerate all existing spheres (the manage dialog
 * needs the full list to offer show/hide toggles).
 */
export function deriveSpheres(devices: CapabilityDevice[], hidden: string[]): Sphere[] {
  const counts = new Map<DeviceCategory, number>();
  for (const device of devices) {
    const category = deviceCategory(device);
    counts.set(category, (counts.get(category) ?? 0) + 1);
  }
  return SPHERE_ORDER
    .filter((category) => (counts.get(category) ?? 0) > 0 && !hidden.includes(category))
    .map((category) => ({ id: sphereTabId(category), category, count: counts.get(category)! }));
}
