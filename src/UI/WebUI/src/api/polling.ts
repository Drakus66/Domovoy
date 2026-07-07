// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

// Polling Service
// Implements batched polling with Page Visibility API and exponential backoff
// Validates: Requirements 3.2, 9.4

type PollingCallback = () => Promise<void>;

interface PollingOptions {
  interval?: number; // Polling interval in milliseconds (default: 1000ms)
  maxRetries?: number; // Maximum number of retries on failure (default: 3)
  onError?: (error: Error) => void; // Error callback
}

export class PollingService {
  private intervalId: number | null = null;
  private isPolling = false;
  private isPaused = false;
  private callbacks: PollingCallback[] = [];
  private interval: number;
  private maxRetries: number;
  private currentRetries = 0;
  private onError?: (error: Error) => void;

  constructor(options: PollingOptions = {}) {
    this.interval = options.interval || 1000;
    this.maxRetries = options.maxRetries || 3;
    this.onError = options.onError;

    // Setup Page Visibility API listener
    this.setupVisibilityListener();
  }

  /**
   * Setup Page Visibility API to pause/resume polling when tab is hidden/visible
   */
  private setupVisibilityListener(): void {
    if (typeof document !== 'undefined') {
      document.addEventListener('visibilitychange', () => {
        if (document.hidden) {
          this.pause();
        } else {
          this.resume();
        }
      });
    }
  }

  /**
   * Register a callback to be executed on each poll
   * @param callback - Async function to execute
   */
  public register(callback: PollingCallback): void {
    this.callbacks.push(callback);
  }

  /**
   * Unregister a callback
   * @param callback - The callback to remove
   */
  public unregister(callback: PollingCallback): void {
    this.callbacks = this.callbacks.filter(cb => cb !== callback);
  }

  /**
   * Start polling with batched execution of all registered callbacks
   */
  public start(): void {
    if (this.isPolling) {
      return;
    }

    this.isPolling = true;
    this.currentRetries = 0;

    // Execute immediately on start
    this.executeBatch();

    // Then setup interval
    this.intervalId = window.setInterval(() => {
      if (!this.isPaused) {
        this.executeBatch();
      }
    }, this.interval);
  }

  /**
   * Stop polling
   */
  public stop(): void {
    if (this.intervalId !== null) {
      clearInterval(this.intervalId);
      this.intervalId = null;
    }
    this.isPolling = false;
    this.isPaused = false;
    this.currentRetries = 0;
  }

  /**
   * Pause polling (used by Page Visibility API)
   */
  private pause(): void {
    this.isPaused = true;
  }

  /**
   * Resume polling (used by Page Visibility API)
   */
  private resume(): void {
    this.isPaused = false;
    // Execute immediately when resuming
    if (this.isPolling) {
      this.executeBatch();
    }
  }

  /**
   * Execute all registered callbacks in batch
   * Implements exponential backoff on failures
   */
  private async executeBatch(): Promise<void> {
    try {
      // Execute all callbacks in parallel (batched)
      await Promise.all(this.callbacks.map(callback => callback()));
      
      // Reset retry counter on success
      this.currentRetries = 0;
    } catch (error) {
      this.currentRetries++;

      // Handle error with exponential backoff
      if (this.currentRetries <= this.maxRetries) {
        const backoffDelay = Math.pow(2, this.currentRetries) * 1000; // 2s, 4s, 8s
        
        console.warn(
          `Polling failed (attempt ${this.currentRetries}/${this.maxRetries}). ` +
          `Retrying in ${backoffDelay}ms...`,
          error
        );

        // Temporarily increase interval for backoff
        if (this.intervalId !== null) {
          clearInterval(this.intervalId);
          
          setTimeout(() => {
            if (this.isPolling) {
              this.intervalId = window.setInterval(() => {
                if (!this.isPaused) {
                  this.executeBatch();
                }
              }, this.interval);
            }
          }, backoffDelay);
        }
      } else {
        console.error(
          `Polling failed after ${this.maxRetries} retries. Stopping polling.`,
          error
        );
        
        // Call error handler if provided
        if (this.onError && error instanceof Error) {
          this.onError(error);
        }
        
        // Stop polling after max retries
        this.stop();
      }
    }
  }

  /**
   * Update polling interval
   * @param newInterval - New interval in milliseconds
   */
  public setInterval(newInterval: number): void {
    this.interval = newInterval;
    
    // Restart polling with new interval if currently polling
    if (this.isPolling) {
      this.stop();
      this.start();
    }
  }

  /**
   * Get current polling status
   */
  public getStatus(): { isPolling: boolean; isPaused: boolean; currentRetries: number } {
    return {
      isPolling: this.isPolling,
      isPaused: this.isPaused,
      currentRetries: this.currentRetries,
    };
  }
}

// Export singleton instance for global use
export const pollingService = new PollingService({
  interval: parseInt(import.meta.env.VITE_POLL_INTERVAL || '1000', 10),
});
