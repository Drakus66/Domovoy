import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Container, Box, Typography, Stack, LinearProgress, Alert, Card, CardActionArea,
  CardContent, Chip, Divider, List, ListItem, ListItemText,
} from '@mui/material';
import HomeRoundedIcon from '@mui/icons-material/HomeRounded';
import DirectionsWalkRoundedIcon from '@mui/icons-material/DirectionsWalkRounded';
import BedtimeRoundedIcon from '@mui/icons-material/BedtimeRounded';
import BeachAccessRoundedIcon from '@mui/icons-material/BeachAccessRounded';
import TuneRoundedIcon from '@mui/icons-material/TuneRounded';
import { modeApi, HomeState } from '../api/mode';
import { historyApi, EventLogEntry } from '../api/history';

/** Visual + copy per well-known mode; unknown custom modes fall back to a generic look. */
const MODE_META: Record<string, { icon: JSX.Element; blurb: string }> = {
  Home: { icon: <HomeRoundedIcon />, blurb: 'Someone is home — normal interactive behaviour.' },
  Away: { icon: <DirectionsWalkRoundedIcon />, blurb: 'Nobody home — energy setbacks, security-leaning.' },
  Night: { icon: <BedtimeRoundedIcon />, blurb: 'Dimmed and quiet while occupants sleep.' },
  Vacation: { icon: <BeachAccessRoundedIcon />, blurb: 'Extended absence — deeper setbacks. Manual only.' },
};

const metaFor = (mode: string) => MODE_META[mode] ?? { icon: <TuneRoundedIcon />, blurb: 'Custom mode.' };

const fmt = (iso: string) => {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
};

export default function Modes() {
  const [state, setState] = useState<HomeState | null>(null);
  const [options, setOptions] = useState<string[]>([]);
  const [history, setHistory] = useState<EventLogEntry[]>([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const since = useMemo(() => new Date(Date.now() - 30 * 24 * 3600 * 1000).toISOString(), []);

  const refresh = useCallback(async () => {
    setError(null);
    try {
      const [s, opts, hist] = await Promise.all([
        modeApi.getMode(),
        modeApi.getOptions(),
        historyApi.getEvents({ kind: 'mode_change', capabilityId: 'home_mode', from: since, limit: 20 }),
      ]);
      setState(s);
      setOptions(opts);
      setHistory(hist);
    } catch {
      setError('Failed to load home mode. Check ApiGateway / DbGateway connection.');
    } finally {
      setLoading(false);
    }
  }, [since]);

  useEffect(() => { refresh(); }, [refresh]);

  const switchTo = async (mode: string) => {
    if (busy || mode === state?.mode) return;
    setBusy(true);
    setError(null);
    try {
      setState(await modeApi.setMode(mode));
      // The event-log record is written asynchronously; re-pull shortly after.
      setTimeout(refresh, 600);
    } catch {
      setError(`Failed to switch to ${mode}.`);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Container maxWidth="lg">
      <Box py={{ xs: 3, md: 4 }}>
        <Stack direction="row" alignItems="center" justifyContent="space-between" mb={3}>
          <Box>
            <Typography variant="h4" component="h1" fontWeight={700}>Home mode</Typography>
            <Typography variant="caption" color="text.secondary">
              The house context — drives automations and climate, and is stamped on every event for ML.
            </Typography>
          </Box>
          {state && (
            <Chip
              color="primary"
              icon={metaFor(state.mode).icon}
              label={`${state.mode} · by ${state.source}`}
              sx={{ fontWeight: 700, '& .MuiChip-icon': { color: 'inherit' } }}
            />
          )}
        </Stack>

        {(loading || busy) && <LinearProgress sx={{ mb: 2, borderRadius: 1 }} />}
        {error && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setError(null)}>{error}</Alert>}

        <Box
          sx={{
            display: 'grid',
            gap: 2,
            gridTemplateColumns: { xs: '1fr 1fr', md: 'repeat(4, 1fr)' },
            mb: 4,
          }}
        >
          {options.map((mode) => {
            const selected = mode === state?.mode;
            const meta = metaFor(mode);
            return (
              <Card
                key={mode}
                variant="outlined"
                sx={{
                  borderColor: selected ? 'primary.main' : 'divider',
                  bgcolor: selected ? 'action.selected' : 'background.paper',
                }}
              >
                <CardActionArea onClick={() => switchTo(mode)} disabled={busy} sx={{ height: '100%' }}>
                  <CardContent sx={{ textAlign: 'center', py: 3 }}>
                    <Box sx={{ color: selected ? 'primary.main' : 'text.secondary', '& svg': { fontSize: 40 } }}>
                      {meta.icon}
                    </Box>
                    <Typography fontWeight={700} mt={1}>{mode}</Typography>
                    <Typography variant="caption" color="text.secondary">{meta.blurb}</Typography>
                  </CardContent>
                </CardActionArea>
              </Card>
            );
          })}
        </Box>

        <Typography variant="h6" fontWeight={700} mb={1}>Recent changes</Typography>
        <Card variant="outlined">
          {history.length === 0 ? (
            <Box textAlign="center" py={4}>
              <Typography color="text.secondary">No mode changes recorded yet.</Typography>
            </Box>
          ) : (
            <List dense disablePadding>
              {history.map((h, i) => (
                <Box key={`${h.timestamp}-${i}`}>
                  {i > 0 && <Divider component="li" />}
                  <ListItem>
                    <ListItemText
                      primary={
                        <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
                          <Typography component="span" fontWeight={600}>
                            {(h.oldValue as string) || '—'} → {(h.newValue as string) || '—'}
                          </Typography>
                          <Chip size="small" variant="outlined" label={`by ${h.triggerSource}`} />
                        </Stack>
                      }
                      secondary={fmt(h.timestamp)}
                    />
                  </ListItem>
                </Box>
              ))}
            </List>
          )}
        </Card>
      </Box>
    </Container>
  );
}
