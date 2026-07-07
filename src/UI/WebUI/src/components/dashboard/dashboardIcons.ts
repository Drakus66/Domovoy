// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import type { SvgIconComponent } from '@mui/icons-material';
import SpaceDashboardRoundedIcon from '@mui/icons-material/SpaceDashboardRounded';
import StarRoundedIcon from '@mui/icons-material/StarRounded';
import LightbulbRoundedIcon from '@mui/icons-material/LightbulbRounded';
import ThermostatRoundedIcon from '@mui/icons-material/ThermostatRounded';
import WeekendRoundedIcon from '@mui/icons-material/WeekendRounded';
import TvRoundedIcon from '@mui/icons-material/TvRounded';
import YardRoundedIcon from '@mui/icons-material/YardRounded';
import GarageRoundedIcon from '@mui/icons-material/GarageRounded';
import ShieldRoundedIcon from '@mui/icons-material/ShieldRounded';
import BedRoundedIcon from '@mui/icons-material/BedRounded';

/** Small curated icon set for custom tabs — a stable string key is what's persisted. */
export const DASHBOARD_ICONS: Record<string, SvgIconComponent> = {
  star: StarRoundedIcon,
  light: LightbulbRoundedIcon,
  climate: ThermostatRoundedIcon,
  sofa: WeekendRoundedIcon,
  bed: BedRoundedIcon,
  tv: TvRoundedIcon,
  plant: YardRoundedIcon,
  garage: GarageRoundedIcon,
  shield: ShieldRoundedIcon,
};

export const dashboardIcon = (key?: string | null): SvgIconComponent =>
  (key && DASHBOARD_ICONS[key]) || SpaceDashboardRoundedIcon;
