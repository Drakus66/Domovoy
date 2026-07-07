import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Container, Box, Typography, Stack, LinearProgress, Alert, Card, CardActionArea,
  CardContent, Chip, Divider, List, ListItem, ListItemText,
} from '@mui/material';
import { modeApi, HomeState } from '../api/mode';
import { historyApi, EventLogEntry } from '../api/history';
import { fmtDateTime } from '../i18n/format';
import { MODE_ICONS, modeIcon as iconFor } from '../components/modes/modeVisuals';

export default function Modes() {
  const { t } = useTranslation('modes');
  const [state, setState] = useState<HomeState | null>(null);
  const [options, setOptions] = useState<string[]>([]);
  const [history, setHistory] = useState<EventLogEntry[]>([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const since = useMemo(() => new Date(Date.now() - 30 * 24 * 3600 * 1000).toISOString(), []);

  // Backend mode values render as user-facing labels: map known ones, fall back to the raw value.
  const nameFor = (mode: string) => t(`names.${mode}`, { defaultValue: mode });
  const blurbFor = (mode: string) =>
    mode in MODE_ICONS ? t(`blurbs.${mode}`) : t('blurbs.custom');

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
      setError(t('error.load'));
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
      setError(t('error.switch', { mode: nameFor(mode) }));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Container maxWidth="lg">
      <Box py={{ xs: 3, md: 4 }}>
        <Stack direction="row" alignItems="center" justifyContent="space-between" mb={3}>
          <Box>
            <Typography variant="h4" component="h1" fontWeight={700}>{t('title')}</Typography>
            <Typography variant="caption" color="text.secondary">
              {t('subtitle')}
            </Typography>
          </Box>
          {state && (
            <Chip
              color="primary"
              icon={iconFor(state.mode)}
              label={t('chipLabel', { mode: nameFor(state.mode), source: state.source })}
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
                      {iconFor(mode)}
                    </Box>
                    <Typography fontWeight={700} mt={1}>{nameFor(mode)}</Typography>
                    <Typography variant="caption" color="text.secondary">{blurbFor(mode)}</Typography>
                  </CardContent>
                </CardActionArea>
              </Card>
            );
          })}
        </Box>

        <Typography variant="h6" fontWeight={700} mb={1}>{t('recentChanges')}</Typography>
        <Card variant="outlined">
          {history.length === 0 ? (
            <Box textAlign="center" py={4}>
              <Typography color="text.secondary">{t('noChanges')}</Typography>
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
                            {h.oldValue ? nameFor(h.oldValue as string) : '—'} → {h.newValue ? nameFor(h.newValue as string) : '—'}
                          </Typography>
                          <Chip size="small" variant="outlined" label={t('changedBy', { source: h.triggerSource })} />
                        </Stack>
                      }
                      secondary={fmtDateTime(h.timestamp)}
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
