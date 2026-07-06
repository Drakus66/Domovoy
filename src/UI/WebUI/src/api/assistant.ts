import apiClient from './client';

/** Assistant capability status (matches AutomationService AssistantStatus, Epic 2H). */
export interface AssistantStatus {
  available: boolean;
  provider: string;
  capabilities: string[];
}

/** Result of a natural-language authoring attempt (stub returns available=false). */
export interface AssistantAuthorResult {
  available: boolean;
  ruleId?: string | null;
  message: string;
}

/** Result of a plain-language explanation attempt (stub returns available=false). */
export interface AssistantExplainResult {
  available: boolean;
  explanation?: string | null;
  message: string;
}

/**
 * Client for the natural-language assistant extension point (Epic 2H). The capability is a stub behind a
 * feature flag — a real backend plugs into the same endpoints later. The UI reads `status` to decide whether
 * to offer authoring/explaining affordances at all.
 */
export const assistantApi = {
  getStatus: (): Promise<AssistantStatus> =>
    apiClient.get<AssistantStatus>('/api/assistant/status').then((r) => r.data),

  authorRule: (prompt: string): Promise<AssistantAuthorResult> =>
    apiClient.post<AssistantAuthorResult>('/api/assistant/author-rule', { prompt }).then((r) => r.data),

  explain: (req: { deviceId?: string; ruleId?: string; decisionId?: string }): Promise<AssistantExplainResult> =>
    apiClient.post<AssistantExplainResult>('/api/assistant/explain', req).then((r) => r.data),
};
