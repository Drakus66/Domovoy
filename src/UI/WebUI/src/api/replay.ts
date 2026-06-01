import apiClient from './client';
import { AutomationRule } from './automations';

/** One point in history where a rule's trigger matched (matches AutomationService ReplayHit, Epic 1F). */
export interface ReplayHit {
  timestamp: string;
  triggerSummary: string;
  conditionsMet: boolean;
}

/** Outcome of dry-running a rule over history (matches AutomationService ReplayResult, Epic 1F). */
export interface ReplayResult {
  eventsScanned: number;
  fires: number;
  hits: ReplayHit[];
  notes: string[];
}

export const replayApi = {
  /**
   * Dry-run a rule over the last `days` of history — "when would this have fired?" — without executing
   * anything. The trust bridge before activating a new (or ML-proposed) rule (roadmap Epic 1F).
   */
  run: (rule: AutomationRule, days = 7): Promise<ReplayResult> =>
    apiClient.post<ReplayResult>('/api/replay', { rule, days }).then((r) => r.data),
};
