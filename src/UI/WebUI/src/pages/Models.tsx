import { useCallback, useEffect, useState } from 'react';
import {
  Container, Box, Typography, Stack, Button, LinearProgress, Alert, Card, CardContent, Chip,
} from '@mui/material';
import ModelTrainingRoundedIcon from '@mui/icons-material/ModelTrainingRounded';
import PsychologyRoundedIcon from '@mui/icons-material/PsychologyRounded';
import { mlApi, MlModel, ModelScope } from '../api/ml';
import ScorecardChart from '../components/charts/ScorecardChart';

const fmt = (iso: string) => {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
};

// Scope label along the zone → zone_kind → global chain (Epic 2I).
const scopeLabel = (s?: ModelScope | null): string =>
  !s || s.level === 'global' || !s.key ? 'global' : `${s.level === 'zone_kind' ? 'kind' : 'zone'}: ${s.key}`;

export default function Models() {
  const [models, setModels] = useState<MlModel[]>([]);
  const [loading, setLoading] = useState(true);
  const [training, setTraining] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      setModels(await mlApi.getModels());
    } catch {
      setError('Failed to load models. Check ApiGateway / DbGateway connection.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  const train = async () => {
    setTraining(true); setError(null); setInfo(null);
    try {
      const r = await mlApi.train();
      setInfo(r.trained
        ? `Trained "${r.model?.name}" v${r.model?.version} on ${r.model?.sampleCount} samples (RMSE ${r.model?.rmse.toFixed(3)}).`
        : `Not trained: ${r.message}.`);
      await load();
    } catch {
      setError('Training request failed — is the AutomationService reachable?');
    } finally {
      setTraining(false);
    }
  };

  return (
    <Container maxWidth="lg">
      <Box py={{ xs: 3, md: 4 }}>
        <Stack direction="row" alignItems="center" justifyContent="space-between" mb={2} flexWrap="wrap" useFlexGap>
          <Box>
            <Typography variant="h4" component="h1" fontWeight={700}>ML models</Typography>
            <Typography variant="caption" color="text.secondary">
              Learned models on the telemetry feature store (ML.NET). An ML control block serves the latest
              model (e.g. a learned setpoint schedule). Training needs accumulated history.
            </Typography>
          </Box>
          <Button variant="contained" startIcon={<ModelTrainingRoundedIcon />} onClick={train} disabled={training}>
            Train now
          </Button>
        </Stack>

        {(loading || training) && <LinearProgress sx={{ mb: 2, borderRadius: 1 }} />}
        {error && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setError(null)}>{error}</Alert>}
        {info && <Alert severity="info" sx={{ mb: 2 }} onClose={() => setInfo(null)}>{info}</Alert>}

        {models.length === 0 && !loading ? (
          <Box textAlign="center" py={8}>
            <PsychologyRoundedIcon sx={{ fontSize: 64, color: 'text.disabled', mb: 2 }} />
            <Typography color="text.secondary">
              No models yet. Once enough telemetry has accrued, "Train now" registers the first model.
            </Typography>
          </Box>
        ) : (
          <Stack spacing={1.5}>
            {models.map((m) => (
              <Card key={m.id} variant="outlined">
                <CardContent sx={{ display: 'flex', alignItems: 'center', gap: 2, py: 1.5, '&:last-child': { pb: 1.5 } }}>
                  <PsychologyRoundedIcon color="primary" />
                  <Box flex={1} minWidth={0}>
                    <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap mb={0.25}>
                      <Typography fontWeight={700}>{m.name}</Typography>
                      <Chip size="small" variant="outlined" label={`v${m.version}`} />
                      <Chip size="small" variant="outlined" label={m.kind} />
                      <Chip
                        size="small"
                        color={scopeLabel(m.scope) === 'global' ? 'default' : 'primary'}
                        variant={scopeLabel(m.scope) === 'global' ? 'outlined' : 'filled'}
                        label={scopeLabel(m.scope)}
                      />
                      {m.features && m.features !== 'time' && (
                        <Chip size="small" variant="outlined" label={m.features} />
                      )}
                      {m.algorithm && <Chip size="small" variant="outlined" label={m.algorithm} />}
                    </Stack>
                    <Typography variant="caption" color="text.secondary">
                      target {m.targetCapability} · {m.sampleCount} samples
                      {m.holdoutSampleCount > 0
                        ? ` · backtest ${m.metric || 'MAE'} ${(m.holdoutScore || m.holdoutMae).toFixed(3)}`
                        : ` · RMSE ${m.rmse.toFixed(3)}`}
                      {' · trained '}{fmt(m.trainedAt)}
                    </Typography>
                  </Box>
                </CardContent>
              </Card>
            ))}
          </Stack>
        )}

        {models.length > 0 && (
          <Card variant="outlined" sx={{ mt: 3 }}>
            <CardContent>
              <Typography fontWeight={700} gutterBottom>Backtest scorecard</Typography>
              <Typography variant="caption" color="text.secondary" display="block" mb={1.5}>
                The loaded model's prediction vs actual telemetry — the signal to read before promoting an ML
                block from Shadow to an active stage (Epic 2B).
              </Typography>
              <ScorecardChart days={7} />
            </CardContent>
          </Card>
        )}
      </Box>
    </Container>
  );
}
