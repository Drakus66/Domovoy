// Logs API Service
// Implements logs-related API calls
// Validates: Requirements 5.1, 5.4

import apiClient from './client';
import { LogEntry, LogLevel } from '../types/log';
import { PaginatedResponse } from '../types/api';

export interface GetLogsParams {
  level?: LogLevel;
  page?: number;
  size?: number;
}

/**
 * Get system logs with optional filtering and pagination
 * @param params - Query parameters for filtering and pagination
 * @returns Promise with paginated log entries
 */
export const getLogs = async (
  params: GetLogsParams = {}
): Promise<PaginatedResponse<LogEntry>> => {
  const { level, page = 1, size = 50 } = params;
  
  const response = await apiClient.get<PaginatedResponse<LogEntry>>('/api/logs', {
    params: {
      ...(level && { level }),
      page,
      size,
    },
  });
  
  return response.data;
};
