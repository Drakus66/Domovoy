// API Response Types for WebUI
// Validates: Requirements 9.1

export interface ApiResponse<T> {
  data: T;
  success: boolean;
  message?: string;
}

export interface PaginatedResponse<T> {
  data: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface ApiError {
  code: string;
  message: string;
  details?: Record<string, any>;
}

export interface ValidationError extends ApiError {
  code: 'VALIDATION_ERROR';
  fields: Record<string, string[]>;
}

export interface NetworkError extends ApiError {
  code: 'NETWORK_ERROR';
  statusCode?: number;
}

export interface TimeoutError extends ApiError {
  code: 'TIMEOUT_ERROR';
  timeout: number;
}
