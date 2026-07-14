// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Checkbox, Divider, IconButton, ListItemIcon, ListItemText, ListSubheader,
  Menu, MenuItem, Stack, Tab, Tabs, Tooltip, Typography,
} from '@mui/material';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import EditRoundedIcon from '@mui/icons-material/EditRounded';
import MoreVertRoundedIcon from '@mui/icons-material/MoreVertRounded';
import ArrowUpwardRoundedIcon from '@mui/icons-material/ArrowUpwardRounded';
import ArrowDownwardRoundedIcon from '@mui/icons-material/ArrowDownwardRounded';
import type { Dashboard } from '../../api/dashboards';
import { capabilityIconForCategory } from '../devices/deviceVisuals';
import { dashboardIcon } from './dashboardIcons';
import type { Sphere } from './spheres';

/**
 * The main-page tab strip: All · auto-spheres · custom tabs, plus "+" (new tab), an edit
 * affordance for the active custom tab, and a manage menu (sphere visibility, tab order).
 */
export default function DashboardTabs({
  spheres, allSpheres, dashboards, hiddenSpheres, activeId,
  onSelect, onCreate, onEdit, onToggleSphere, onReorder,
}: {
  /** Visible spheres (hidden ones filtered out). */
  spheres: Sphere[];
  /** Every sphere that exists (for the manage menu's show/hide toggles). */
  allSpheres: Sphere[];
  dashboards: Dashboard[];
  hiddenSpheres: string[];
  activeId: string;
  onSelect: (id: string) => void;
  onCreate: () => void;
  onEdit: (dashboard: Dashboard) => void;
  onToggleSphere: (category: string, hidden: boolean) => void;
  onReorder: (ids: string[]) => void;
}) {
  const { t } = useTranslation('dashboards');
  const [menuAnchor, setMenuAnchor] = useState<HTMLElement | null>(null);

  const knownIds = useMemo(
    () => new Set(['all', ...spheres.map((s) => s.id), ...dashboards.map((d) => d.id)]),
    [spheres, dashboards],
  );
  const activeDashboard = dashboards.find((d) => d.id === activeId) ?? null;

  const move = (index: number, delta: number) => {
    const ids = dashboards.map((d) => d.id);
    const target = index + delta;
    if (target < 0 || target >= ids.length) return;
    [ids[index], ids[target]] = [ids[target], ids[index]];
    onReorder(ids);
  };

  return (
    <Stack direction="row" alignItems="center" spacing={0.5} mb={3}>
      <Tabs
        value={knownIds.has(activeId) ? activeId : false}
        onChange={(_, v) => onSelect(v as string)}
        variant="scrollable"
        scrollButtons="auto"
        allowScrollButtonsMobile
        sx={{ flex: 1, minHeight: 44, '& .MuiTab-root': { minHeight: 44 } }}
      >
        <Tab value="all" label={t('tabs.all')} />
        {spheres.map((sphere) => {
          const Icon = capabilityIconForCategory(sphere.category);
          return (
            <Tab
              key={sphere.id}
              value={sphere.id}
              icon={<Icon fontSize="small" />}
              iconPosition="start"
              label={`${t(`spheres.${sphere.category}`)} · ${sphere.count}`}
            />
          );
        })}
        {dashboards.map((dashboard) => {
          const Icon = dashboardIcon(dashboard.icon);
          return (
            <Tab
              key={dashboard.id}
              value={dashboard.id}
              icon={<Icon fontSize="small" />}
              iconPosition="start"
              label={dashboard.name}
            />
          );
        })}
      </Tabs>

      {activeDashboard && (
        <Tooltip title={t('actions.editTab')}>
          <IconButton size="small" onClick={() => onEdit(activeDashboard)}>
            <EditRoundedIcon fontSize="small" />
          </IconButton>
        </Tooltip>
      )}
      <Tooltip title={t('actions.newTab')}>
        <IconButton size="small" onClick={onCreate} aria-label={t('actions.newTab')}>
          <AddRoundedIcon fontSize="small" />
        </IconButton>
      </Tooltip>
      <Tooltip title={t('actions.manage')}>
        <IconButton size="small" onClick={(e) => setMenuAnchor(e.currentTarget)} aria-label={t('actions.manage')}>
          <MoreVertRoundedIcon fontSize="small" />
        </IconButton>
      </Tooltip>

      <Menu anchorEl={menuAnchor} open={menuAnchor !== null} onClose={() => setMenuAnchor(null)}>
        <ListSubheader disableSticky>{t('manage.spheres')}</ListSubheader>
        <Typography variant="caption" color="text.secondary" sx={{ px: 2, pb: 0.5, display: 'block', maxWidth: 280 }}>
          {t('manage.sphereHint')}
        </Typography>
        {allSpheres.map((sphere) => {
          const hidden = hiddenSpheres.includes(sphere.category);
          return (
            <MenuItem
              key={sphere.id}
              dense
              onClick={() => onToggleSphere(sphere.category, !hidden)}
            >
              <ListItemIcon>
                <Checkbox edge="start" size="small" checked={!hidden} disableRipple tabIndex={-1} />
              </ListItemIcon>
              <ListItemText>{t(`spheres.${sphere.category}`)}</ListItemText>
            </MenuItem>
          );
        })}
        <Divider />
        <ListSubheader disableSticky>{t('manage.customTabs')}</ListSubheader>
        {dashboards.length === 0 ? (
          <MenuItem dense disabled>{t('manage.noCustomTabs')}</MenuItem>
        ) : (
          dashboards.map((dashboard, index) => (
            <MenuItem key={dashboard.id} dense onClick={() => { setMenuAnchor(null); onEdit(dashboard); }}>
              <ListItemText sx={{ mr: 1 }}>{dashboard.name}</ListItemText>
              <IconButton
                size="small" edge="end" disabled={index === 0} aria-label={t('actions.moveUp')}
                onClick={(e) => { e.stopPropagation(); move(index, -1); }}
              >
                <ArrowUpwardRoundedIcon sx={{ fontSize: 16 }} />
              </IconButton>
              <IconButton
                size="small" edge="end" disabled={index === dashboards.length - 1} aria-label={t('actions.moveDown')}
                onClick={(e) => { e.stopPropagation(); move(index, 1); }}
              >
                <ArrowDownwardRoundedIcon sx={{ fontSize: 16 }} />
              </IconButton>
            </MenuItem>
          ))
        )}
      </Menu>
    </Stack>
  );
}
