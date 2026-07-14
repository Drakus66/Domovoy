// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { ReactNode, useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Container, Box, Typography, Stack, Button, TextField, Card, CardContent, CardActionArea, Collapse, Alert,
  LinearProgress, Switch, FormControlLabel, List, ListItemButton, ListItemText, InputAdornment, IconButton,
  Chip, ToggleButton, ToggleButtonGroup,
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
import { settingsApi, GeocodeResult } from '../api/settings';
import { securityApi, User } from '../api/security';
import { getCurrentUserId, setCurrentUserId } from '../api/currentUser';

// A collapsible settings card. Collapsed by default so a long section (e.g. the location
// map) doesn't dominate the page — the header stays a compact, clickable summary row.
// Children stay mounted (no unmountOnExit) so form state survives a collapse.
function Section({
  icon, title, caption, defaultOpen = false, children,
}: {
  icon: ReactNode; title: string; caption?: string; defaultOpen?: boolean; children: ReactNode;
}) {
  const [open, setOpen] = useState(defaultOpen);
  return (
    <Card variant="outlined" sx={{ mb: 3 }}>
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
          {children}
        </CardContent>
      </Collapse>
    </Card>
  );
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

export default function Settings() {
  const { t } = useTranslation('settings');

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

  // Local users (Epic 2E model) for the self-declaration picker.
  useEffect(() => {
    securityApi.getUsers().then(setUsers).catch(() => setUsers([]));
  }, []);

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
      </Box>
    </Container>
  );
}
