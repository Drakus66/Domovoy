import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Alert, Box, Button, Dialog, DialogActions, DialogContent, DialogTitle,
  FormControlLabel, LinearProgress, MenuItem, Select, Slider, Stack, Switch,
  TextField, Typography,
} from '@mui/material';
import { pluginsApi, PluginSetting } from '../../api/plugins';

/**
 * Settings dialog for a plugin (Epic 2M tail). Fetches the schema the plugin announced and renders one control
 * per setting (switch / slider / dropdown / text / password); Save persists to the supervisor, which broadcasts
 * the values to the plugin over the bus — applied live, no restart. Secrets come back blank and are only sent
 * when the operator types a new value (leaving them blank keeps the stored key).
 */
export default function PluginSettingsDialog({
  pluginId, pluginName, open, onClose,
}: {
  pluginId: string;
  pluginName: string;
  open: boolean;
  onClose: () => void;
}) {
  const { t } = useTranslation('plugins');
  const [settings, setSettings] = useState<PluginSetting[]>([]);
  const [values, setValues] = useState<Record<string, unknown>>({});
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await pluginsApi.getSettings(pluginId);
      setSettings(res.settings);
      setValues(res.values ?? {});
    } catch {
      setError(t('settings.loadError'));
    } finally {
      setLoading(false);
    }
  }, [pluginId, t]);

  useEffect(() => { if (open) load(); }, [open, load]);

  const setValue = (key: string, value: unknown) =>
    setValues((prev) => ({ ...prev, [key]: value }));

  const save = async () => {
    setSaving(true);
    setError(null);
    try {
      await pluginsApi.updateSettings(pluginId, values);
      onClose();
    } catch {
      setError(t('settings.saveError'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Dialog open={open} onClose={saving ? undefined : onClose} fullWidth maxWidth="sm">
      <DialogTitle>{t('settings.title', { name: pluginName })}</DialogTitle>
      <DialogContent dividers>
        {loading && <LinearProgress sx={{ mb: 2, borderRadius: 1 }} />}
        {error && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setError(null)}>{error}</Alert>}
        {!loading && settings.length === 0 && (
          <Typography color="text.secondary">{t('settings.empty')}</Typography>
        )}
        <Stack spacing={2.5}>
          {settings.map((s) => (
            <SettingControl key={s.key} setting={s} value={values[s.key]} onChange={(v) => setValue(s.key, v)} />
          ))}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={saving}>{t('settings.cancel')}</Button>
        <Button variant="contained" onClick={save} disabled={loading || saving}>
          {saving ? t('settings.saving') : t('settings.save')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

/** Renders a single setting control chosen by its kind (+ editor hint). */
function SettingControl({
  setting, value, onChange,
}: {
  setting: PluginSetting;
  value: unknown;
  onChange: (value: unknown) => void;
}) {
  const label = setting.label || setting.key;

  if (setting.kind === 'boolean') {
    return (
      <Box>
        <FormControlLabel
          control={<Switch checked={Boolean(value)} onChange={(e) => onChange(e.target.checked)} />}
          label={label}
        />
        {setting.description && (
          <Typography variant="caption" color="text.secondary" display="block">{setting.description}</Typography>
        )}
      </Box>
    );
  }

  if (setting.kind === 'enum' && setting.values) {
    return (
      <Box>
        <Typography variant="body2" mb={0.5}>{label}</Typography>
        <Select size="small" fullWidth value={String(value ?? '')} onChange={(e) => onChange(e.target.value)}>
          {setting.values.map((v) => (
            <MenuItem key={v} value={v}>{v}</MenuItem>
          ))}
        </Select>
        {setting.description && (
          <Typography variant="caption" color="text.secondary" display="block">{setting.description}</Typography>
        )}
      </Box>
    );
  }

  if (setting.kind === 'number') {
    const num = typeof value === 'number' ? value : Number(value ?? setting.default ?? 0);
    const hasRange = setting.min != null && setting.max != null;
    return (
      <Box>
        <Typography variant="body2" gutterBottom>
          {label}
          {': '}
          <b>{Number.isFinite(num) ? num : 0}</b>
          {setting.unit ? ` ${setting.unit}` : ''}
        </Typography>
        {hasRange ? (
          <Slider
            value={Number.isFinite(num) ? num : setting.min ?? 0}
            min={setting.min ?? 0}
            max={setting.max ?? 100}
            step={setting.step ?? 1}
            valueLabelDisplay="auto"
            onChange={(_, v) => onChange(Array.isArray(v) ? v[0] : v)}
          />
        ) : (
          <TextField
            size="small" type="number" fullWidth
            value={Number.isFinite(num) ? num : ''}
            inputProps={{ min: setting.min ?? undefined, max: setting.max ?? undefined, step: setting.step ?? undefined }}
            onChange={(e) => onChange(e.target.value === '' ? '' : Number(e.target.value))}
          />
        )}
        {setting.description && (
          <Typography variant="caption" color="text.secondary" display="block">{setting.description}</Typography>
        )}
      </Box>
    );
  }

  // text / secret (+ time editor)
  const isSecret = setting.kind === 'secret' || setting.secret;
  const isTime = setting.editor === 'time';
  return (
    <Box>
      <Typography variant="body2" mb={0.5}>{label}</Typography>
      <TextField
        size="small" fullWidth
        type={isSecret ? 'password' : isTime ? 'time' : 'text'}
        value={String(value ?? '')}
        placeholder={isSecret ? '••••••' : undefined}
        onChange={(e) => onChange(e.target.value)}
      />
      {setting.description && (
        <Typography variant="caption" color="text.secondary" display="block">{setting.description}</Typography>
      )}
    </Box>
  );
}
