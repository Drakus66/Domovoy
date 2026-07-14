// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { Container, Box, Typography, Button } from '@mui/material';
import ExploreOffRoundedIcon from '@mui/icons-material/ExploreOffRounded';
import { useTranslation } from 'react-i18next';
import { Link, useLocation } from 'react-router-dom';

export default function NotFound() {
  const location = useLocation();
  const { t } = useTranslation('common');
  return (
    <Container maxWidth="sm">
      <Box textAlign="center" py={12}>
        <ExploreOffRoundedIcon sx={{ fontSize: 64, color: 'text.disabled', mb: 2 }} />
        <Typography variant="h5" fontWeight={700} gutterBottom>
          {t('notFound.title')}
        </Typography>
        <Typography color="text.secondary" mb={3}>
          {t('notFound.body', { path: location.pathname })}
        </Typography>
        <Button component={Link} to="/" variant="contained">{t('notFound.back')}</Button>
      </Box>
    </Container>
  );
}
