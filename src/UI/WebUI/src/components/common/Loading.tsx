// Loading Component for WebUI
// Validates: Requirements 1.4

import React from 'react';
import {
  Box,
  CircularProgress,
  Skeleton,
  Card,
  CardContent,
  Grid,
  Typography,
} from '@mui/material';

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

interface CardSkeletonProps {
  count?: number;
}

/**
 * Skeleton loader for device cards
 */
export const CardSkeleton: React.FC<CardSkeletonProps> = ({ count = 1 }) => {
  return (
    <Grid container spacing={3}>
      {Array.from({ length: count }).map((_, index) => (
        <Grid item xs={12} sm={6} md={4} key={index} data-testid="card-skeleton">
          <Card>
            <CardContent>
              {/* Device name */}
              <Skeleton variant="text" width="60%" height={32} />
              
              {/* Device type and status */}
              <Box display="flex" gap={1} mt={1} mb={2}>
                <Skeleton variant="circular" width={24} height={24} />
                <Skeleton variant="text" width="40%" height={24} />
              </Box>
              
              {/* Control area */}
              <Skeleton variant="rectangular" width="100%" height={60} sx={{ borderRadius: 1 }} />
              
              {/* Last seen */}
              <Box mt={2}>
                <Skeleton variant="text" width="50%" height={20} />
              </Box>
            </CardContent>
          </Card>
        </Grid>
      ))}
    </Grid>
  );
};

export default Loading;
