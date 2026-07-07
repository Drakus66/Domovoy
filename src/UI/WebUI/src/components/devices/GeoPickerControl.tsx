import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Box, Button, Dialog, DialogTitle, DialogContent, DialogActions, TextField,
  List, ListItemButton, ListItemText, Stack, Typography, IconButton, InputAdornment,
} from '@mui/material';
import PlaceRoundedIcon from '@mui/icons-material/PlaceRounded';
import SearchRoundedIcon from '@mui/icons-material/SearchRounded';
import ClearRoundedIcon from '@mui/icons-material/ClearRounded';
import 'leaflet/dist/leaflet.css';
import LocationMap from '../settings/LocationMap';
import { settingsApi, GeocodeResult } from '../../api/settings';

/** A writable-location control (capability editor "geo"): shows the current place and opens a map picker. */
export default function GeoPickerControl({
  value, disabled, allowClear, onSet,
}: {
  value: unknown;
  disabled: boolean;
  /** When true, an explicit "clear" resets the value to empty (e.g. Start → back to home). */
  allowClear?: boolean;
  onSet: (value: string) => void;
}) {
  const { t } = useTranslation('devices');
  const parsed = parseLoc(value);

  const [open, setOpen] = useState(false);
  const [lat, setLat] = useState(parsed?.lat ?? DEFAULT.lat);
  const [lon, setLon] = useState(parsed?.lon ?? DEFAULT.lon);
  const [label, setLabel] = useState(parsed?.label ?? '');
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<GeocodeResult[]>([]);
  const [searching, setSearching] = useState(false);

  const openPicker = () => {
    const p = parseLoc(value);
    setLat(p?.lat ?? DEFAULT.lat);
    setLon(p?.lon ?? DEFAULT.lon);
    setLabel(p?.label ?? '');
    setQuery('');
    setResults([]);
    setOpen(true);
  };

  const pick = async (nextLat: number, nextLon: number, nextLabel?: string) => {
    setLat(nextLat);
    setLon(nextLon);
    if (nextLabel !== undefined) {
      setLabel(nextLabel);
    } else {
      // Best-effort reverse-geocode for a human label (online-only; ignored when offline).
      const revLabel = await settingsApi.reverseGeocode(nextLat, nextLon).catch(() => null);
      if (revLabel) setLabel(revLabel);
    }
  };

  const search = async () => {
    if (!query.trim()) return;
    setSearching(true);
    try {
      setResults(await settingsApi.geocode(query.trim()));
    } catch {
      setResults([]);
    } finally {
      setSearching(false);
    }
  };

  const confirm = () => {
    const coords = `${round(lat)},${round(lon)}`;
    onSet(label.trim() ? `${coords}|${label.trim()}` : coords);
    setOpen(false);
  };

  return (
    <>
      <Stack direction="row" spacing={0.5} alignItems="center" sx={{ minWidth: 0 }}>
        <Button
          size="small" variant="outlined" startIcon={<PlaceRoundedIcon />}
          disabled={disabled} onClick={openPicker}
          sx={{ maxWidth: 200, textTransform: 'none' }}
        >
          <Box component="span" sx={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
            {parsed?.label || (parsed ? `${round(parsed.lat)}, ${round(parsed.lon)}` : t('geo.pick'))}
          </Box>
        </Button>
        {allowClear && parsed && (
          <IconButton size="small" disabled={disabled} onClick={() => onSet('')} aria-label={t('geo.clear')}>
            <ClearRoundedIcon fontSize="small" />
          </IconButton>
        )}
      </Stack>

      <Dialog open={open} onClose={() => setOpen(false)} maxWidth="sm" fullWidth>
        <DialogTitle>{t('geo.title')}</DialogTitle>
        <DialogContent>
          <Stack spacing={1.5} sx={{ pt: 0.5 }}>
            <TextField
              size="small" fullWidth placeholder={t('geo.searchPlaceholder')}
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); search(); } }}
              InputProps={{
                endAdornment: (
                  <InputAdornment position="end">
                    <IconButton size="small" onClick={search} disabled={searching} aria-label={t('geo.search')}>
                      <SearchRoundedIcon fontSize="small" />
                    </IconButton>
                  </InputAdornment>
                ),
              }}
            />
            {results.length > 0 && (
              <List dense disablePadding sx={{ maxHeight: 140, overflow: 'auto', bgcolor: 'action.hover', borderRadius: 1 }}>
                {results.map((r) => (
                  <ListItemButton key={`${r.latitude},${r.longitude}`} onClick={() => { pick(r.latitude, r.longitude, r.label); setResults([]); }}>
                    <ListItemText primary={r.label} primaryTypographyProps={{ variant: 'body2', noWrap: true }} />
                  </ListItemButton>
                ))}
              </List>
            )}

            <LocationMap latitude={lat} longitude={lon} onPick={(la, lo) => pick(la, lo)} />

            <Typography variant="caption" color="text.secondary">
              {label ? `${label} · ` : ''}{round(lat)}, {round(lon)}
            </Typography>
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpen(false)}>{t('geo.cancel')}</Button>
          <Button variant="contained" onClick={confirm}>{t('geo.confirm')}</Button>
        </DialogActions>
      </Dialog>
    </>
  );
}

const DEFAULT = { lat: 55.7558, lon: 37.6173 }; // Moscow, until the user picks a spot

const round = (n: number) => Math.round(n * 1e6) / 1e6;

/** Parse a "lat,lon" / "lat,lon|Label" capability value. */
function parseLoc(value: unknown): { lat: number; lon: number; label: string } | null {
  if (typeof value !== 'string' || value.trim() === '') return null;
  const [coords, ...labelParts] = value.split('|');
  const [latS, lonS] = coords.split(',');
  const lat = Number(latS);
  const lon = Number(lonS);
  if (!Number.isFinite(lat) || !Number.isFinite(lon)) return null;
  return { lat, lon, label: labelParts.join('|').trim() };
}
