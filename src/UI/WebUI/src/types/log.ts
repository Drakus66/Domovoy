// Log Types for WebUI
// Validates: Requirements 5.1

export type LogLevel = 'info' | 'warning' | 'error' | 'debug';

export interface LogEntry {
  id: string;
  timestamp: Date;
  level: LogLevel;
  source: string;
  message: string;
  details?: Record<string, any>;
}
