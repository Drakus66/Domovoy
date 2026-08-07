// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { ChangeEvent, ReactNode, useCallback, useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Container, Box, Typography, Stack, Button, TextField, Card, CardContent, CardActionArea, Collapse, Alert,
  LinearProgress, Switch, FormControlLabel, List, ListItem, ListItemButton, ListItemText, InputAdornment,
  IconButton, Chip, ToggleButton, ToggleButtonGroup,
} from '@mui/material';
import SearchRoundedIcon from '@mui/icons-material/SearchRounded';
import PlaceRoundedIcon from '@mui/icons-material/PlaceRounded';
import SaveRoundedIcon from '@mui/icons-material/SaveRounded';
import CalendarMonthRoundedIcon from '@mui/icons-material/CalendarMonthRounded';
import PaletteRoundedIcon from '@mui/icons-material/PaletteRounded';
import ExpandMoreRoundedIcon from '@mui/icons-material/ExpandMoreRounded';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import DownloadRoundedIcon from '@mui/icons-material/DownloadRounded';
import PersonRoundedIcon from '@mui/icons-material/PersonRounded';
import BackupRoundedIcon from '@mui/icons-material/BackupRounded';
import RestoreRoundedIcon from '@mui/icons-material/RestoreRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import UploadFileRoundedIcon from '@mui/icons-material/UploadFileRounded';
import TabletMacRoundedIcon from '@mui/icons-material/TabletMacRounded';
import BoltRoundedIcon from '@mui/icons-material/BoltRounded';
import SpeedRoundedIcon from '@mui/icons-material/SpeedRounded';
import AccountTreeRoundedIcon from '@mui/icons-material/AccountTreeRounded';
import PsychologyRoundedIcon from '@mui/icons-material/PsychologyRounded';
import NotificationsActiveRoundedIcon from '@mui/icons-material/NotificationsActiveRounded';
import SystemUpdateAltRoundedIcon from '@mui/icons-material/SystemUpdateAltRounded';
import { settingsApi, GeocodeResult } from '../api/settings';
import { securityApi, User } from '../api/security';
import { backupsApi, BackupListItem, BackupSettings } from '../api/backups';
import { getCurrentUserId, setCurrentUserId } from '../api/currentUser';
import KioskSettings from '../components/kiosk/KioskSettings';
import TariffEditor from '../components/settings/TariffEditor';
import LoadManagementEditor from '../components/settings/LoadManagementEditor';
import IntelligenceEditor from '../components/settings/IntelligenceEditor';
import NotificationSettingsEditor from '../components/settings/NotificationSettingsEditor';
import PowerTopologyEditor from '../components/settings/PowerTopologyEditor';
import UpdatesEditor from '../components/settings/UpdatesEditor';

// A collapsible settings card. Collapsed by default so a long section (e.g. the location
// map) doesn't dominate the page — the header stays a compact, clickable summary row.
// Children stay mounted (no unmountOnExit) so form state survives a collapse.
function Section({
  icon, title, caption, defaultOpen = false, id, lazy = false, children,
}: {
  icon: ReactNode; title: string; caption?: string; defaultOpen?: boolean; id?: string;
  /** Не монтировать содержимое, пока секцию не раскрыли. Для секций, которые при монтировании
   *  делают дорогие запросы: Collapse только прячет детей, но продолжает их рендерить. */
  lazy?: boolean;
  children: ReactNode;
}) {
  // Секция с id адресуема якорем: уведомление «доступно обновление» ведёт на /settings#updates,
  // и по такой ссылке нужная секция должна открыться сама, а не встретить свёрнутым заголовком.
  const anchored = id !== undefined && typeof window !== 'undefined' && window.location.hash === `#${id}`;
  const [open, setOpen] = useState(defaultOpen || anchored);
  const ref = useRef<HTMLDivElement | null>(null);
  // Раз открыв, содержимое больше не размонтируем — иначе сворачивание теряло бы введённое.
  const [everOpened, setEverOpened] = useState(defaultOpen || anchored);

  useEffect(() => {
    if (open) setEverOpened(true);
  }, [open]);

  useEffect(() => {
    if (anchored) ref.current?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }, [anchored]);

  return (
    <Card variant="outlined" sx={{ mb: 3 }} id={id} ref={ref}>
      <CardActionArea onClick={() => setOpen((o) => !o)} aria-expanded={open}>
        <Box sx={{ px: 2, py: 1.5, display: 'flex', alignItems: 'center', gap: 1.5 }}>
          {icon}
          <Typography fontWeight={700} sx={{ flex: 1 }}>{title}</Typography>
          <ExpandMoreRoundedIcon
            sx={{ color: 'text.secondary', transition: 'transform 0.2s', transform: open ? 'rotate(180deg)' : 'none' }}
          />
        </Box>
      </CardActionArea>
      <Collapse in={open} timeout="auto">
        <CardContent sx={{ pt: 0 }}>
          {caption && (
            <Typography variant="caption" color="text.secondary" display="block" mb={2}>{caption}</Typography>
          )}
          {lazy && !everOpened ? null : children}
        </CardContent>
      </Collapse>
    </Card>
  );
}

function formatBytes(n: number): string {
  if (n >= 1024 ** 3) return `${(n / 1024 ** 3).toFixed(1)} GB`;
  if (n >= 1024 ** 2) return `${(n / 1024 ** 2).toFixed(1)} MB`;
  if (n >= 1024) return `${Math.round(n / 1024)} KB`;
  return `${n} B`;
}

// Weekday toggles, ordered Mon→Sun; values are System.DayOfWeek ints (0=Sun…6=Sat), matching the backend.
const WEEKDAYS: { value: number; key: string }[] = [
  { value: 1, key: 'mon' }, { value: 2, key: 'tue' }, { value: 3, key: 'wed' },
  { value: 4, key: 'thu' }, { value: 5, key: 'fri' }, { value: 6, key: 'sat' }, { value: 0, key: 'sun' },
];
import LocationMap from '../components/settings/LocationMap';
import ColorModeToggle from '../components/theme/ColorModeToggle';
import ThemePicker from '../components/theme/ThemePicker';
import LanguagePicker from '../components/i18n/LanguagePicker';
import { confirmAction } from '../store/confirmStore';

export default function Settings() {
  const { t } = useTranslation('settings');
  const { t: tk } = useTranslation('kiosk');

  const [lat, setLat] = useState(55.7558);
  const [lon, setLon] = useState(37.6173);
  const [label, setLabel] = useState('');
  const [tzAuto, setTzAuto] = useState(true);
  const [tz, setTz] = useState<string>('');

  const [query, setQuery] = useState('');
  const [results, setResults] = useState<GeocodeResult[]>([]);
  const [searching, setSearching] = useState(false);
  const [noResults, setNoResults] = useState(false);

  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  // Self-declared user for attribution (Epic 2G tail — NOT auth, Phase 3 replaces this).
  const [users, setUsers] = useState<User[]>([]);
  const [currentUser, setCurrentUser] = useState<string>(getCurrentUserId() ?? '');

  // Calendar sensor settings (Epic 2L).
  const [weekendDays, setWeekendDays] = useState<number[]>([6, 0]);
  const [holidays, setHolidays] = useState<string[]>([]);
  const [newHoliday, setNewHoliday] = useState('');
  const [country, setCountry] = useState('');
  const [year, setYear] = useState(new Date().getFullYear());
  const [calendarSaving, setCalendarSaving] = useState(false);
  const [calendarSaved, setCalendarSaved] = useState(false);
  const [importing, setImporting] = useState(false);

  // Backups (Epic 3A): schedule/retention + the bundle list.
  const [backupEnabled, setBackupEnabled] = useState(true);
  const [backupTime, setBackupTime] = useState('03:30');
  const [backupKeep, setBackupKeep] = useState(7);
  const [backupStatus, setBackupStatus] = useState<BackupSettings | null>(null);
  const [backups, setBackups] = useState<BackupListItem[]>([]);
  const [backupSaving, setBackupSaving] = useState(false);
  const [backupBusy, setBackupBusy] = useState(false);
  const [backupNotice, setBackupNotice] = useState<string | null>(null);

  // Local users (Epic 2E model) for the self-declaration picker.
  useEffect(() => {
    securityApi.getUsers().then(setUsers).catch(() => setUsers([]));
  }, []);

  const loadBackups = useCallback(() => {
    backupsApi
      .getSettings()
      .then((s) => {
        setBackupEnabled(s.enabled);
        setBackupTime(s.time);
        setBackupKeep(s.keepCount);
        setBackupStatus(s);
      })
      .catch(() => { /* section still renders with defaults */ });
    backupsApi.list().then(setBackups).catch(() => setBackups([]));
  }, []);

  useEffect(() => { loadBackups(); }, [loadBackups]);

  const backupErrorOf = (e: unknown) =>
    (e as { response?: { status?: number } })?.response?.status === 409
      ? t('backups.busy')
      : t('backups.error');

  const saveBackupSettings = async () => {
    setBackupSaving(true); setError(null); setBackupNotice(null);
    try {
      const s = await backupsApi.saveSettings({ enabled: backupEnabled, time: backupTime, keepCount: backupKeep });
      setBackupStatus(s);
      setBackupNotice(t('backups.saved'));
    } catch {
      setError(t('errors.save'));
    } finally {
      setBackupSaving(false);
    }
  };

  const runBackup = async () => {
    setBackupBusy(true); setError(null); setBackupNotice(null);
    try {
      const r = await backupsApi.runNow();
      setBackupNotice(t('backups.ran', { file: r.file }));
      loadBackups();
    } catch (e) {
      setError(backupErrorOf(e));
    } finally {
      setBackupBusy(false);
    }
  };

  const restoreBackup = async (file: string) => {
    if (!await confirmAction({ message: t('backups.restoreConfirm', { file }) })) return;
    setBackupBusy(true); setError(null); setBackupNotice(null);
    try {
      const r = await backupsApi.restore(file);
      setBackupNotice(t('backups.restored', { file, documents: r.documents }));
    } catch (e) {
      setError(backupErrorOf(e));
    } finally {
      setBackupBusy(false);
    }
  };

  const deleteBackup = async (file: string) => {
    if (!await confirmAction({ message: t('backups.deleteConfirm', { file }) })) return;
    setError(null); setBackupNotice(null);
    try {
      await backupsApi.remove(file);
      loadBackups();
    } catch {
      setError(t('backups.error'));
    }
  };

  const uploadBackup = async (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = '';
    if (!file) return;
    setBackupBusy(true); setError(null); setBackupNotice(null);
    try {
      const r = await backupsApi.upload(file);
      setBackupNotice(t('backups.uploaded', { file: r.file }));
      loadBackups();
    } catch (err) {
      setError(backupErrorOf(err));
    } finally {
      setBackupBusy(false);
    }
  };

  const backupLastRunText = !backupStatus?.lastRunAt
    ? t('backups.lastRunNever')
    : backupStatus.lastResult === 'error'
      ? t('backups.lastRunError', {
          time: new Date(backupStatus.lastRunAt).toLocaleString(),
          error: backupStatus.lastError ?? '',
        })
      : t('backups.lastRun', {
          time: new Date(backupStatus.lastRunAt).toLocaleString(),
          file: backupStatus.lastFile ?? '',
        });

  const pickUser = (id: string) => {
    setCurrentUser(id);
    setCurrentUserId(id || null);
  };

  // Load the persisted location on mount.
  useEffect(() => {
    settingsApi
      .getLocation()
      .then((loc) => {
        setLat(loc.latitude);
        setLon(loc.longitude);
        setLabel(loc.label ?? '');
        setTzAuto(loc.timeZoneAuto);
        setTz(loc.timeZoneId ?? '');
      })
      .catch(() => setError(t('errors.load')))
      .finally(() => setLoading(false));
  }, [t]);

  // Load the calendar settings on mount.
  useEffect(() => {
    settingsApi
      .getCalendar()
      .then((c) => {
        setWeekendDays(c.weekendDays ?? [6, 0]);
        setHolidays(c.holidays ?? []);
      })
      .catch(() => { /* keep defaults; a save will create the document */ });
  }, []);

  // When coordinates change and the timezone is on auto, preview the offline-derived tz.
  useEffect(() => {
    if (!tzAuto || loading) return;
    let cancelled = false;
    settingsApi.getTimezone(lat, lon).then((id) => {
      if (!cancelled && id) setTz(id);
    }).catch(() => { /* offline: keep the last tz */ });
    return () => { cancelled = true; };
  }, [lat, lon, tzAuto, loading]);

  const pick = useCallback((newLat: number, newLon: number) => {
    setLat(Number(newLat.toFixed(5)));
    setLon(Number(newLon.toFixed(5)));
    setSaved(false);
  }, []);

  const search = async () => {
    if (!query.trim()) return;
    setSearching(true); setNoResults(false); setResults([]);
    try {
      const hits = await settingsApi.geocode(query.trim());
      setResults(hits);
      setNoResults(hits.length === 0);
    } catch {
      setNoResults(true);
    } finally {
      setSearching(false);
    }
  };

  const choose = (r: GeocodeResult) => {
    pick(r.latitude, r.longitude);
    setLabel(r.label);
    setResults([]);
    setQuery('');
  };

  const save = async () => {
    setSaving(true); setError(null); setSaved(false);
    try {
      const loc = await settingsApi.saveLocation({
        latitude: lat,
        longitude: lon,
        label: label.trim() || null,
        timeZoneId: tz.trim() || null,
        timeZoneAuto: tzAuto,
      });
      setTz(loc.timeZoneId ?? '');
      setSaved(true);
    } catch {
      setError(t('errors.save'));
    } finally {
      setSaving(false);
    }
  };

  const toggleWeekend = (_: unknown, next: number[]) => { setWeekendDays(next); setCalendarSaved(false); };

  const addHoliday = () => {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(newHoliday) || holidays.includes(newHoliday)) return;
    setHolidays([...holidays, newHoliday].sort());
    setNewHoliday('');
    setCalendarSaved(false);
  };

  const removeHoliday = (d: string) => { setHolidays(holidays.filter((h) => h !== d)); setCalendarSaved(false); };

  const saveCalendar = async () => {
    setCalendarSaving(true); setError(null); setCalendarSaved(false);
    try {
      const c = await settingsApi.saveCalendar({ weekendDays, holidays });
      setWeekendDays(c.weekendDays); setHolidays(c.holidays);
      setCalendarSaved(true);
    } catch {
      setError(t('errors.save'));
    } finally {
      setCalendarSaving(false);
    }
  };

  const importHolidays = async () => {
    if (!country.trim()) return;
    setImporting(true); setError(null); setCalendarSaved(false);
    try {
      const r = await settingsApi.importHolidays(country.trim(), year);
      setHolidays(r.settings.holidays);
      setCalendarSaved(true);
    } catch {
      setError(t('calendar.importError'));
    } finally {
      setImporting(false);
    }
  };

  const coordsValid = lat >= -90 && lat <= 90 && lon >= -180 && lon <= 180;

  return (
    <Container maxWidth="lg">
      <Box py={{ xs: 3, md: 4 }}>
        <Box mb={2}>
          <Typography variant="h4" component="h1" fontWeight={700}>{t('title')}</Typography>
          <Typography variant="caption" color="text.secondary">{t('caption')}</Typography>
        </Box>

        {loading && <LinearProgress sx={{ mb: 2, borderRadius: 1 }} />}
        {error && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setError(null)}>{error}</Alert>}
        {saved && <Alert severity="success" sx={{ mb: 2 }} onClose={() => setSaved(false)}>{t('location.saved')}</Alert>}

        {/* ── Location ─────────────────────────────────────────────── */}
        <Section icon={<PlaceRoundedIcon color="primary" />} title={t('location.title')} caption={t('location.caption')}>
            {/* Place search (online-only; the fields below always work offline). */}
            <TextField
              fullWidth
              size="small"
              label={t('location.searchLabel')}
              placeholder={t('location.searchPlaceholder')}
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); search(); } }}
              InputProps={{
                endAdornment: (
                  <InputAdornment position="end">
                    <IconButton aria-label={t('location.searchAction')} onClick={search} disabled={searching} edge="end">
                      <SearchRoundedIcon />
                    </IconButton>
                  </InputAdornment>
                ),
              }}
            />
            {searching && <LinearProgress sx={{ mt: 1, borderRadius: 1 }} />}
            {noResults && (
              <Typography variant="caption" color="text.secondary" sx={{ mt: 1, display: 'block' }}>
                {t('location.noResults')}
              </Typography>
            )}
            {results.length > 0 && (
              <List dense sx={{ mt: 1, border: '1px solid', borderColor: 'divider', borderRadius: 1 }}>
                {results.map((r, i) => (
                  <ListItemButton key={`${r.latitude},${r.longitude},${i}`} onClick={() => choose(r)}>
                    <ListItemText
                      primary={r.label}
                      secondary={`${r.latitude.toFixed(4)}, ${r.longitude.toFixed(4)}`}
                      primaryTypographyProps={{ variant: 'body2' }}
                    />
                  </ListItemButton>
                ))}
              </List>
            )}

            <Box sx={{ mt: 2 }}>
              <LocationMap latitude={lat} longitude={lon} onPick={pick} />
            </Box>

            {/* Manual entry — the offline path. */}
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ mt: 2 }}>
              <TextField
                type="number"
                size="small"
                label={t('location.latitude')}
                value={lat}
                onChange={(e) => { setLat(Number(e.target.value)); setSaved(false); }}
                inputProps={{ step: 0.0001, min: -90, max: 90 }}
                fullWidth
              />
              <TextField
                type="number"
                size="small"
                label={t('location.longitude')}
                value={lon}
                onChange={(e) => { setLon(Number(e.target.value)); setSaved(false); }}
                inputProps={{ step: 0.0001, min: -180, max: 180 }}
                fullWidth
              />
            </Stack>

            <TextField
              size="small"
              label={t('location.label')}
              value={label}
              onChange={(e) => { setLabel(e.target.value); setSaved(false); }}
              sx={{ mt: 2 }}
              fullWidth
            />

            {/* Timezone — auto (offline, from coordinates) with a manual override. */}
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} alignItems={{ sm: 'center' }} sx={{ mt: 2 }}>
              <FormControlLabel
                control={<Switch checked={tzAuto} onChange={(e) => { setTzAuto(e.target.checked); setSaved(false); }} />}
                label={t('location.tzAuto')}
              />
              <TextField
                size="small"
                label={t('location.timezone')}
                value={tz}
                onChange={(e) => { setTz(e.target.value); setSaved(false); }}
                disabled={tzAuto}
                fullWidth
              />
            </Stack>

            <Box sx={{ mt: 2, display: 'flex', justifyContent: 'flex-end' }}>
              <Button
                variant="contained"
                startIcon={<SaveRoundedIcon />}
                onClick={save}
                disabled={saving || !coordsValid}
              >
                {t('location.save')}
              </Button>
            </Box>
        </Section>

        {/* ── Calendar ─────────────────────────────────────────────── */}
        <Section icon={<CalendarMonthRoundedIcon color="primary" />} title={t('calendar.title')} caption={t('calendar.caption')}>
            {calendarSaved && (
              <Alert severity="success" sx={{ mb: 2 }} onClose={() => setCalendarSaved(false)}>
                {t('calendar.saved')}
              </Alert>
            )}

            {/* Weekend weekdays */}
            <Typography variant="body2" fontWeight={600} mb={1}>{t('calendar.weekend')}</Typography>
            <ToggleButtonGroup value={weekendDays} onChange={toggleWeekend} size="small" sx={{ flexWrap: 'wrap' }}>
              {WEEKDAYS.map((d) => (
                <ToggleButton key={d.value} value={d.value} sx={{ px: 1.5 }}>
                  {t(`calendar.days.${d.key}`)}
                </ToggleButton>
              ))}
            </ToggleButtonGroup>

            {/* Holidays */}
            <Typography variant="body2" fontWeight={600} mt={2.5} mb={1}>{t('calendar.holidays')}</Typography>
            {holidays.length > 0 ? (
              <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap mb={1.5}>
                {holidays.map((d) => (
                  <Chip key={d} label={d} onDelete={() => removeHoliday(d)} size="small" />
                ))}
              </Stack>
            ) : (
              <Typography variant="caption" color="text.secondary" display="block" mb={1.5}>
                {t('calendar.noHolidays')}
              </Typography>
            )}
            <Stack direction="row" spacing={1} alignItems="center">
              <TextField
                type="date"
                size="small"
                label={t('calendar.addHoliday')}
                InputLabelProps={{ shrink: true }}
                value={newHoliday}
                onChange={(e) => setNewHoliday(e.target.value)}
              />
              <IconButton aria-label={t('calendar.addHoliday')} onClick={addHoliday} disabled={!newHoliday}>
                <AddRoundedIcon />
              </IconButton>
            </Stack>

            {/* Optional online import */}
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} alignItems={{ sm: 'center' }} sx={{ mt: 2 }}>
              <TextField
                size="small"
                label={t('calendar.country')}
                placeholder="RU, DE, US…"
                value={country}
                onChange={(e) => setCountry(e.target.value)}
                sx={{ width: 140 }}
              />
              <TextField
                type="number"
                size="small"
                label={t('calendar.year')}
                value={year}
                onChange={(e) => setYear(Number(e.target.value))}
                sx={{ width: 120 }}
              />
              <Button variant="outlined" startIcon={<DownloadRoundedIcon />} onClick={importHolidays} disabled={importing || !country.trim()}>
                {t('calendar.import')}
              </Button>
            </Stack>
            {importing && <LinearProgress sx={{ mt: 1, borderRadius: 1 }} />}

            <Box sx={{ mt: 2, display: 'flex', justifyContent: 'flex-end' }}>
              <Button variant="contained" startIcon={<SaveRoundedIcon />} onClick={saveCalendar} disabled={calendarSaving}>
                {t('calendar.save')}
              </Button>
            </Box>
        </Section>

        {/* ── Backups (Epic 3A) ────────────────────────────────────── */}
        <Section icon={<BackupRoundedIcon color="primary" />} title={t('backups.title')} caption={t('backups.caption')}>
          {backupNotice && (
            <Alert severity="success" sx={{ mb: 2 }} onClose={() => setBackupNotice(null)}>{backupNotice}</Alert>
          )}

          {/* Schedule + retention */}
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} alignItems={{ sm: 'center' }}>
            <FormControlLabel
              control={<Switch checked={backupEnabled} onChange={(e) => setBackupEnabled(e.target.checked)} />}
              label={t('backups.auto')}
            />
            <TextField
              type="time"
              size="small"
              label={t('backups.time')}
              value={backupTime}
              onChange={(e) => setBackupTime(e.target.value)}
              InputLabelProps={{ shrink: true }}
              sx={{ width: 130 }}
            />
            <TextField
              type="number"
              size="small"
              label={t('backups.keep')}
              value={backupKeep}
              onChange={(e) => setBackupKeep(Number(e.target.value))}
              inputProps={{ min: 1, max: 365 }}
              sx={{ width: 130 }}
            />
            <Box sx={{ flex: 1 }} />
            <Button
              variant="outlined"
              startIcon={<SaveRoundedIcon />}
              onClick={saveBackupSettings}
              disabled={backupSaving || !backupTime || backupKeep < 1}
            >
              {t('backups.save')}
            </Button>
          </Stack>
          <Typography variant="caption" color="text.secondary" display="block" mt={1}>
            {backupLastRunText}
          </Typography>

          {/* Manual run + host-migration upload */}
          <Stack direction="row" spacing={1} sx={{ mt: 2 }} flexWrap="wrap" useFlexGap>
            <Button
              variant="contained"
              startIcon={<BackupRoundedIcon />}
              onClick={runBackup}
              disabled={backupBusy}
            >
              {t('backups.runNow')}
            </Button>
            <Button variant="outlined" component="label" startIcon={<UploadFileRoundedIcon />} disabled={backupBusy}>
              {t('backups.upload')}
              <input type="file" accept=".zip" hidden onChange={uploadBackup} />
            </Button>
          </Stack>
          {backupBusy && <LinearProgress sx={{ mt: 1, borderRadius: 1 }} />}

          {/* Bundle list */}
          <Typography variant="body2" fontWeight={600} mt={2.5} mb={1}>{t('backups.list')}</Typography>
          {backups.length === 0 ? (
            <Typography variant="caption" color="text.secondary" display="block">{t('backups.empty')}</Typography>
          ) : (
            <List dense sx={{ border: '1px solid', borderColor: 'divider', borderRadius: 1 }}>
              {backups.map((b) => (
                <ListItem
                  key={b.fileName}
                  secondaryAction={
                    <Stack direction="row" spacing={0.5}>
                      <IconButton
                        component="a"
                        href={backupsApi.downloadUrl(b.fileName)}
                        aria-label={t('backups.download')}
                        size="small"
                      >
                        <DownloadRoundedIcon fontSize="small" />
                      </IconButton>
                      <IconButton
                        onClick={() => restoreBackup(b.fileName)}
                        aria-label={t('backups.restore')}
                        size="small"
                        disabled={backupBusy || !b.valid}
                      >
                        <RestoreRoundedIcon fontSize="small" />
                      </IconButton>
                      <IconButton
                        onClick={() => deleteBackup(b.fileName)}
                        aria-label={t('backups.delete')}
                        size="small"
                        disabled={backupBusy}
                      >
                        <DeleteOutlineRoundedIcon fontSize="small" />
                      </IconButton>
                    </Stack>
                  }
                >
                  <ListItemText
                    primary={b.fileName}
                    secondary={[
                      new Date(b.createdAt).toLocaleString(),
                      formatBytes(b.sizeBytes),
                      b.reason ? t(`backups.reason.${b.reason}`, { defaultValue: b.reason }) : null,
                      b.valid ? null : t('backups.invalid'),
                    ].filter(Boolean).join(' · ')}
                    primaryTypographyProps={{ variant: 'body2', sx: { wordBreak: 'break-all', pr: 10 } }}
                  />
                </ListItem>
              ))}
            </List>
          )}
        </Section>

        {/* ── Updates (Epic 3K) ────────────────────────────────────── */}
        <Section
          id="updates"
          lazy
          icon={<SystemUpdateAltRoundedIcon color="primary" />}
          title={t('updates.title')}
          caption={t('updates.caption')}
        >
          <UpdatesEditor />
        </Section>

        {/* ── Tariff (Epic 3C) ─────────────────────────────────────── */}
        <Section icon={<BoltRoundedIcon color="primary" />} title={t('tariff.title')} caption={t('tariff.caption')}>
          <TariffEditor />
        </Section>

        {/* ── Electrical topology (Epic 3C-D) ─────────────────────── */}
        <Section
          icon={<AccountTreeRoundedIcon color="primary" />}
          title={t('powerTopology.title')}
          caption={t('powerTopology.caption')}
        >
          <PowerTopologyEditor />
        </Section>

        {/* ── Load management (Epic 3C-LM) ────────────────────────── */}
        <Section icon={<SpeedRoundedIcon color="primary" />} title={t('loadManagement.title')} caption={t('loadManagement.caption')}>
          <LoadManagementEditor />
        </Section>

        {/* ── Intelligence layer (Epic 3I) ────────────────────────── */}
        <Section icon={<PsychologyRoundedIcon color="primary" />} title={t('intelligence.title')} caption={t('intelligence.caption')}>
          <IntelligenceEditor />
        </Section>

        {/* ── Notification discipline (Epic 3F) ────────────────────── */}
        <Section icon={<NotificationsActiveRoundedIcon color="primary" />} title={t('notifications.title')} caption={t('notifications.caption')}>
          <NotificationSettingsEditor />
        </Section>

        {/* ── Who am I (self-declared attribution, not auth) ────────── */}
        <Section icon={<PersonRoundedIcon color="primary" />} title={t('whoami.title')} caption={t('whoami.caption')}>
          <TextField
            select size="small" sx={{ minWidth: 260 }}
            label={t('whoami.label')}
            value={currentUser}
            onChange={(e) => pickUser(e.target.value)}
            SelectProps={{ native: true }}
            InputLabelProps={{ shrink: true }}
          >
            <option value="">{t('whoami.anonymous')}</option>
            {users.map((u) => (
              <option key={u.id} value={u.id}>{u.displayName}</option>
            ))}
          </TextField>
        </Section>

        {/* ── Appearance ───────────────────────────────────────────── */}
        <Section icon={<PaletteRoundedIcon color="primary" />} title={t('appearance.title')} caption={t('appearance.caption')}>
            <Stack direction="row" spacing={1} alignItems="center">
              <LanguagePicker />
              <ThemePicker />
              <ColorModeToggle />
            </Stack>
        </Section>

        <Section icon={<TabletMacRoundedIcon color="primary" />} title={tk('title')} caption={tk('caption')}>
          <KioskSettings />
        </Section>
      </Box>
    </Container>
  );
}
