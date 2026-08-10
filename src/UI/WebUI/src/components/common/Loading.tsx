// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

// Loading Component for WebUI
// Validates: Requirements 1.4

import React from 'react';
import { Box, CircularProgress, Typography } from '@mui/material';

interface LoadingProps {
  variant?: 'circular' | 'skeleton';
  size?: number | string;
  message?: string;
}

/**
 * Loading component with circular progress indicator
 */
export const Loading: React.FC<LoadingProps> = ({
  variant = 'circular',
  size = 40,
  message,
}) => {
  if (variant === 'circular') {
    return (
      <Box
        display="flex"
        flexDirection="column"
        alignItems="center"
        justifyContent="center"
        minHeight="200px"
        gap={2}
      >
        <CircularProgress size={size} />
        {message && (
          <Typography variant="body2" color="text.secondary">
            {message}
          </Typography>
        )}
      </Box>
    );
  }

  return null;
};

export default Loading;
