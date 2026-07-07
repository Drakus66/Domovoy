import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Card, CardContent, Chip, Stack, Typography } from '@mui/material';
import { modeApi, type HomeState } from '../../../api/mode';
import { modeIcon } from '../../modes/modeVisuals';

/**
 * Compact home-mode switcher widget (Epic 1G) — turns a custom tab into a remote, not just a
 * device list. Optimistic: the tapped chip highlights immediately, reverts on failure.
 */
export default function ModesTile() {
  const { t } = useTranslation(['dashboards', 'modes']);
  const [state, setState] = useState<HomeState | null>(null);
  const [options, setOptions] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);

  const nameFor = (mode: string) => t(`modes:names.${mode}`, { defaultValue: mode });

  useEffect(() => {
    let cancelled = false;
    Promise.all([modeApi.getMode(), modeApi.getOptions()])
      .then(([s, opts]) => {
        if (cancelled) return;
        setState(s);
        setOptions(opts);
      })
      .catch(() => { /* the widget stays empty-but-quiet; the Modes page owns error surfacing */ });
    return () => { cancelled = true; };
  }, []);

  const switchTo = useCallback(async (mode: string) => {
    if (busy || mode === state?.mode) return;
    const before = state;
    setBusy(true);
    setState((s) => (s ? { ...s, mode } : s));
    try {
      setState(await modeApi.setMode(mode));
    } catch {
      setState(before);
    } finally {
      setBusy(false);
    }
  }, [busy, state]);

  return (
    <Card sx={{ height: '100%' }}>
      <CardContent>
        <Typography variant="subtitle2" fontWeight={700} mb={1.5}>
          {t('dashboards:widgets.modes')}
        </Typography>
        <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
          {options.map((mode) => {
            const selected = mode === state?.mode;
            return (
              <Chip
                key={mode}
                icon={modeIcon(mode)}
                label={nameFor(mode)}
                color={selected ? 'primary' : 'default'}
                variant={selected ? 'filled' : 'outlined'}
                disabled={busy}
                onClick={() => switchTo(mode)}
                sx={{ fontWeight: selected ? 700 : 500, '& .MuiChip-icon': { color: 'inherit' } }}
              />
            );
          })}
        </Stack>
      </CardContent>
    </Card>
  );
}
