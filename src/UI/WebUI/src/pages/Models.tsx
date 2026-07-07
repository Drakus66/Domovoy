// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Container, Box, Typography, Stack, Button, LinearProgress, Alert, Card, CardContent, Chip,
} from '@mui/material';
import ModelTrainingRoundedIcon from '@mui/icons-material/ModelTrainingRounded';
import PsychologyRoundedIcon from '@mui/icons-material/PsychologyRounded';
import CategoryRoundedIcon from '@mui/icons-material/CategoryRounded';
import { mlApi, MlModel, ModelScope, ArchetypeDisagreement } from '../api/ml';
import ScorecardChart from '../components/charts/ScorecardChart';
import { fmtDateTime } from '../i18n/format';

// Whether the scope is the global (default) bucket vs a zone/zone_kind scope (Epic 2I).
const isGlobalScope = (s?: ModelScope | null): boolean => !s || s.level === 'global' || !s.key;

export default function Models() {
  const { t } = useTranslation('models');
  const [models, setModels] = useState<MlModel[]>([]);
  const [loading, setLoading] = useState(true);
  const [training, setTraining] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  const [classifying, setClassifying] = useState(false);
  const [disagreements, setDisagreements] = useState<ArchetypeDisagreement[] | null>(null);

  // Scope label along the zone → zone_kind → global chain (Epic 2I).
  const scopeLabel = (s?: ModelScope | null): string =>
    isGlobalScope(s)
      ? t('scope.global')
      : t(s!.level === 'zone_kind' ? 'scope.kind' : 'scope.zone', { key: s!.key });

  const load = useCallback(async () => {
    setError(null);
    try {
      setModels(await mlApi.getModels());
    } catch {
      setError(t('errors.load'));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => { load(); }, [load]);

  const train = async () => {
    setTraining(true); setError(null); setInfo(null);
    try {
      const r = await mlApi.train();
      setInfo(r.trained
        ? t('trained', {
            name: r.model?.name,
            version: r.model?.version,
            count: r.model?.sampleCount,
            rmse: r.model?.rmse.toFixed(3),
          })
        : t('notTrained', { message: r.message }));
      await load();
    } catch {
      setError(t('errors.train'));
    } finally {
      setTraining(false);
    }
  };

  const classify = async () => {
    setClassifying(true); setError(null); setInfo(null); setDisagreements(null);
    try {
      const r = await mlApi.classifyArchetypes();
      if (!r.trained) {
        setInfo(t('classify.notTrained', { note: r.note }));
      } else {
        setDisagreements(r.disagreements);
        setInfo(t('classify.done', { trainedOn: r.trainedOn, count: r.disagreements.length }));
      }
    } catch {
      setError(t('classify.error'));
    } finally {
      setClassifying(false);
    }
  };

  return (
    <Container maxWidth="lg">
      <Box py={{ xs: 3, md: 4 }}>
        <Stack direction="row" alignItems="center" justifyContent="space-between" mb={2} flexWrap="wrap" useFlexGap>
          <Box>
            <Typography variant="h4" component="h1" fontWeight={700}>{t('title')}</Typography>
            <Typography variant="caption" color="text.secondary">
              {t('caption')}
            </Typography>
          </Box>
          <Stack direction="row" spacing={1}>
            <Button variant="outlined" startIcon={<CategoryRoundedIcon />} onClick={classify} disabled={classifying}>
              {t('classify.action')}
            </Button>
            <Button variant="contained" startIcon={<ModelTrainingRoundedIcon />} onClick={train} disabled={training}>
              {t('actions.trainNow')}
            </Button>
          </Stack>
        </Stack>

        {(loading || training || classifying) && <LinearProgress sx={{ mb: 2, borderRadius: 1 }} />}
        {error && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setError(null)}>{error}</Alert>}
        {info && <Alert severity="info" sx={{ mb: 2 }} onClose={() => setInfo(null)}>{info}</Alert>}

        {disagreements && disagreements.length > 0 && (
          <Card variant="outlined" sx={{ mb: 2 }}>
            <CardContent sx={{ py: 1.5, '&:last-child': { pb: 1.5 } }}>
              <Typography variant="subtitle2" gutterBottom>{t('classify.reviewTitle')}</Typography>
              <Stack spacing={0.75}>
                {disagreements.map((d) => (
                  <Stack key={d.deviceId} direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
                    <Typography variant="body2" fontWeight={600}>{d.name}</Typography>
                    <Chip size="small" variant="outlined" label={d.current} />
                    <Typography variant="caption" color="text.secondary">→</Typography>
                    <Chip size="small" color="primary" label={d.predicted} />
                    <Typography variant="caption" color="text.secondary">{(d.confidence * 100).toFixed(0)}%</Typography>
                  </Stack>
                ))}
              </Stack>
            </CardContent>
          </Card>
        )}

        {models.length === 0 && !loading ? (
          <Box textAlign="center" py={8}>
            <PsychologyRoundedIcon sx={{ fontSize: 64, color: 'text.disabled', mb: 2 }} />
            <Typography color="text.secondary">
              {t('empty')}
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
                        color={isGlobalScope(m.scope) ? 'default' : 'primary'}
                        variant={isGlobalScope(m.scope) ? 'outlined' : 'filled'}
                        label={scopeLabel(m.scope)}
                      />
                      {m.features && m.features !== 'time' && (
                        <Chip size="small" variant="outlined" label={m.features} />
                      )}
                      {m.algorithm && <Chip size="small" variant="outlined" label={m.algorithm} />}
                    </Stack>
                    <Typography variant="caption" color="text.secondary">
                      {t('card.target', { capability: m.targetCapability, count: m.sampleCount })}
                      {m.holdoutSampleCount > 0
                        ? t('card.backtest', { metric: m.metric || 'MAE', score: (m.holdoutScore || m.holdoutMae).toFixed(3) })
                        : t('card.rmse', { value: m.rmse.toFixed(3) })}
                      {t('card.trained', { when: fmtDateTime(m.trainedAt) })}
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
              <Typography fontWeight={700} gutterBottom>{t('scorecard.title')}</Typography>
              <Typography variant="caption" color="text.secondary" display="block" mb={1.5}>
                {t('scorecard.caption')}
              </Typography>
              <ScorecardChart days={7} />
            </CardContent>
          </Card>
        )}
      </Box>
    </Container>
  );
}
