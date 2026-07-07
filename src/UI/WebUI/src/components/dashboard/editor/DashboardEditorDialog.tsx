import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Box, Button, Card, Dialog, DialogActions, DialogContent, DialogTitle,
  IconButton, List, ListItem, ListItemIcon, ListItemText, MenuItem, Select,
  Stack, TextField, Tooltip, Typography, useMediaQuery, useTheme, Chip,
} from '@mui/material';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import ArrowUpwardRoundedIcon from '@mui/icons-material/ArrowUpwardRounded';
import ArrowDownwardRoundedIcon from '@mui/icons-material/ArrowDownwardRounded';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import type { CapabilityDevice } from '../../../api/capabilityDevices';
import type { Dashboard, DashboardInput, DashboardItem, DashboardSection } from '../../../api/dashboards';
import { useDashboardStore } from '../../../store/dashboardStore';
import { capabilityIcon, capabilityLabel } from '../../devices/deviceVisuals';
import { DASHBOARD_ICONS, dashboardIcon } from '../dashboardIcons';
import AddItemDialog from './AddItemDialog';

const blankDraft = (): DashboardInput => ({ name: '', icon: null, sections: [] });

const draftFrom = (dashboard: Dashboard): DashboardInput => ({
  name: dashboard.name,
  icon: dashboard.icon ?? null,
  // Deep-copy so edits never mutate the store's cached object.
  sections: dashboard.sections.map((s) => ({ title: s.title, items: s.items.map((i) => ({ ...i })) })),
});

const moveInPlace = <T,>(list: T[], index: number, delta: number): T[] => {
  const target = index + delta;
  if (target < 0 || target >= list.length) return list;
  const next = [...list];
  [next[index], next[target]] = [next[target], next[index]];
  return next;
};

/**
 * The picker+sections tab editor (v1 — deliberately no drag-and-drop grid): name + icon,
 * named sections, ordered items with up/down/remove. Edits a local draft; Save commits
 * through the dashboard store (create or update), Delete asks for confirmation first.
 */
export default function DashboardEditorDialog({
  open, dashboard, devices, onClose, onSaved, onDeleted,
}: {
  open: boolean;
  /** null → creating a new tab. */
  dashboard: Dashboard | null;
  devices: CapabilityDevice[];
  onClose: () => void;
  onSaved?: (dashboard: Dashboard) => void;
  onDeleted?: (id: string) => void;
}) {
  const { t } = useTranslation('dashboards');
  const theme = useTheme();
  const fullScreen = useMediaQuery(theme.breakpoints.down('sm'));
  const { create, update, remove } = useDashboardStore();

  const [draft, setDraft] = useState<DashboardInput>(blankDraft());
  const [addTarget, setAddTarget] = useState<number | null>(null);
  const [saving, setSaving] = useState(false);

  // Re-seed the draft whenever the dialog opens for a different target.
  useEffect(() => {
    if (open) setDraft(dashboard ? draftFrom(dashboard) : blankDraft());
  }, [open, dashboard]);

  const patchSection = (index: number, patch: Partial<DashboardSection>) =>
    setDraft((d) => ({
      ...d,
      sections: d.sections.map((s, i) => (i === index ? { ...s, ...patch } : s)),
    }));

  const deviceName = (id?: string | null) =>
    (id && devices.find((d) => d.id === id)?.name) || id || '';

  const itemPrimary = (item: DashboardItem) =>
    item.type === 'modes'
      ? t('widgets.modes')
      : item.capabilityId
        ? `${deviceName(item.deviceId)} · ${capabilityLabel(item.capabilityId)}`
        : deviceName(item.deviceId);

  const save = async () => {
    setSaving(true);
    try {
      if (dashboard) {
        if (await update(dashboard.id, draft)) onClose();
      } else {
        const created = await create(draft);
        if (created) {
          onSaved?.(created);
          onClose();
        }
      }
    } finally {
      setSaving(false);
    }
  };

  const del = async () => {
    if (!dashboard) return;
    if (!window.confirm(t('editor.deleteConfirm', { name: dashboard.name }))) return;
    if (await remove(dashboard.id)) {
      onDeleted?.(dashboard.id);
      onClose();
    }
  };

  const IconPreview = dashboardIcon(draft.icon);

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm" fullScreen={fullScreen}>
      <DialogTitle sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
        {dashboard ? t('editor.editTitle') : t('editor.createTitle')}
        <Box flex={1} />
        <IconButton size="small" onClick={onClose}><CloseRoundedIcon fontSize="small" /></IconButton>
      </DialogTitle>
      <DialogContent dividers>
        <Stack spacing={3}>
          <Stack direction="row" spacing={1.5}>
            <TextField
              label={t('editor.name')}
              size="small"
              fullWidth
              value={draft.name}
              onChange={(e) => setDraft((d) => ({ ...d, name: e.target.value }))}
              autoFocus={!dashboard}
            />
            <Select
              size="small"
              value={draft.icon ?? ''}
              displayEmpty
              onChange={(e) => setDraft((d) => ({ ...d, icon: e.target.value || null }))}
              sx={{ minWidth: 76 }}
              renderValue={() => <IconPreview fontSize="small" sx={{ display: 'block' }} />}
              aria-label={t('editor.icon')}
            >
              <MenuItem value="">
                <em>{t('editor.icon')}</em>
              </MenuItem>
              {Object.entries(DASHBOARD_ICONS).map(([key, Icon]) => (
                <MenuItem key={key} value={key}><Icon fontSize="small" /></MenuItem>
              ))}
            </Select>
          </Stack>

          <Box>
            <Stack direction="row" alignItems="center" mb={1}>
              <Typography variant="subtitle2" fontWeight={700}>{t('editor.sections')}</Typography>
              <Box flex={1} />
              <Button
                size="small"
                startIcon={<AddRoundedIcon />}
                onClick={() => setDraft((d) => ({ ...d, sections: [...d.sections, { title: '', items: [] }] }))}
              >
                {t('actions.addSection')}
              </Button>
            </Stack>

            {draft.sections.length === 0 && (
              <Typography variant="body2" color="text.secondary">{t('editor.noSections')}</Typography>
            )}

            <Stack spacing={2}>
              {draft.sections.map((section, si) => (
                <Card key={si} variant="outlined" sx={{ p: 1.5 }}>
                  <Stack direction="row" spacing={0.5} alignItems="center" mb={1}>
                    <TextField
                      size="small"
                      fullWidth
                      placeholder={t('editor.sectionTitle')}
                      value={section.title}
                      onChange={(e) => patchSection(si, { title: e.target.value })}
                    />
                    <Tooltip title={t('actions.moveUp')}>
                      <span>
                        <IconButton size="small" disabled={si === 0} aria-label={t('actions.moveUp')}
                          onClick={() => setDraft((d) => ({ ...d, sections: moveInPlace(d.sections, si, -1) }))}>
                          <ArrowUpwardRoundedIcon sx={{ fontSize: 16 }} />
                        </IconButton>
                      </span>
                    </Tooltip>
                    <Tooltip title={t('actions.moveDown')}>
                      <span>
                        <IconButton size="small" disabled={si === draft.sections.length - 1} aria-label={t('actions.moveDown')}
                          onClick={() => setDraft((d) => ({ ...d, sections: moveInPlace(d.sections, si, 1) }))}>
                          <ArrowDownwardRoundedIcon sx={{ fontSize: 16 }} />
                        </IconButton>
                      </span>
                    </Tooltip>
                    <Tooltip title={t('actions.remove')}>
                      <IconButton size="small" aria-label={t('actions.remove')}
                        onClick={() => setDraft((d) => ({ ...d, sections: d.sections.filter((_, i) => i !== si) }))}>
                        <DeleteOutlineRoundedIcon sx={{ fontSize: 18 }} />
                      </IconButton>
                    </Tooltip>
                  </Stack>

                  {section.items.length === 0 ? (
                    <Typography variant="caption" color="text.secondary" display="block" mb={1}>
                      {t('editor.emptySection')}
                    </Typography>
                  ) : (
                    <List dense disablePadding sx={{ mb: 1 }}>
                      {section.items.map((item, ii) => {
                        const ItemIcon = item.capabilityId
                          ? capabilityIcon(item.capabilityId)
                          : dashboardIcon(null);
                        return (
                          <ListItem
                            key={ii}
                            disableGutters
                            secondaryAction={
                              <Stack direction="row" spacing={0}>
                                <IconButton size="small" disabled={ii === 0} aria-label={t('actions.moveUp')}
                                  onClick={() => patchSection(si, { items: moveInPlace(section.items, ii, -1) })}>
                                  <ArrowUpwardRoundedIcon sx={{ fontSize: 15 }} />
                                </IconButton>
                                <IconButton size="small" disabled={ii === section.items.length - 1} aria-label={t('actions.moveDown')}
                                  onClick={() => patchSection(si, { items: moveInPlace(section.items, ii, 1) })}>
                                  <ArrowDownwardRoundedIcon sx={{ fontSize: 15 }} />
                                </IconButton>
                                <IconButton size="small" aria-label={t('actions.remove')}
                                  onClick={() => patchSection(si, { items: section.items.filter((_, i) => i !== ii) })}>
                                  <CloseRoundedIcon sx={{ fontSize: 15 }} />
                                </IconButton>
                              </Stack>
                            }
                          >
                            <ListItemIcon sx={{ minWidth: 32, color: 'text.secondary' }}>
                              <ItemIcon fontSize="small" />
                            </ListItemIcon>
                            <ListItemText
                              primary={itemPrimary(item)}
                              primaryTypographyProps={{ noWrap: true, variant: 'body2' }}
                            />
                            <Chip size="small" variant="outlined" label={t(`editor.types.${item.type}`)} sx={{ mr: 1 }} />
                          </ListItem>
                        );
                      })}
                    </List>
                  )}

                  <Button size="small" startIcon={<AddRoundedIcon />} onClick={() => setAddTarget(si)}>
                    {t('actions.addItem')}
                  </Button>
                </Card>
              ))}
            </Stack>
          </Box>
        </Stack>
      </DialogContent>
      <DialogActions>
        {dashboard && (
          <Button color="error" onClick={del}>{t('actions.delete')}</Button>
        )}
        <Box flex={1} />
        <Button onClick={onClose}>{t('actions.cancel')}</Button>
        <Button variant="contained" disabled={saving || !draft.name.trim()} onClick={save}>
          {t('actions.save')}
        </Button>
      </DialogActions>

      <AddItemDialog
        open={addTarget !== null}
        devices={devices}
        onClose={() => setAddTarget(null)}
        onAdd={(items) => {
          if (addTarget === null) return;
          setDraft((d) => ({
            ...d,
            sections: d.sections.map((s, i) =>
              (i === addTarget ? { ...s, items: [...s.items, ...items] } : s)),
          }));
        }}
      />
    </Dialog>
  );
}
