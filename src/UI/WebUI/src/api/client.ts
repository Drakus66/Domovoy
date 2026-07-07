// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import axios, { AxiosError, AxiosRequestConfig, InternalAxiosRequestConfig } from 'axios';

// Create axios instance with base configuration
const apiClient = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000',
  timeout: 30000,
  headers: {
    'Content-Type': 'application/json',
  },
});

// Request interceptor
apiClient.interceptors.request.use(
  (config: InternalAxiosRequestConfig) => {
    // Future: Add authentication token if available
    // const token = localStorage.getItem('authToken');
    // if (token) {
    //   config.headers.Authorization = `Bearer ${token}`;
    // }
    
    return config;
  },
  (error: AxiosError) => {
    console.error('Request error:', error);
    return Promise.reject(error);
  }
);

// Response interceptor with retry logic
apiClient.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const config = error.config as AxiosRequestConfig & { _retry?: boolean; _retryCount?: number };
    
    // Handle network errors with retry logic
    if (!error.response && config && !config._retry) {
      config._retry = true;
      config._retryCount = config._retryCount || 0;
      
      // Retry up to 3 times with exponential backoff
      if (config._retryCount < 3) {
        config._retryCount++;
        const delay = Math.pow(2, config._retryCount) * 1000; // 2s, 4s, 8s
        
        await new Promise(resolve => setTimeout(resolve, delay));
        return apiClient.request(config);
      }
    }
    
    // Handle specific HTTP error codes
    if (error.response) {
      const { status, data } = error.response;
      
      switch (status) {
        case 400:
          console.error('Bad Request:', data);
          break;
        case 401:
          console.error('Unauthorized - redirecting to login');
          // Future: Redirect to login page
          break;
        case 403:
          console.error('Forbidden - insufficient permissions');
          break;
        case 404:
          console.error('Resource not found');
          break;
        case 500:
          console.error('Server error');
          break;
        case 503:
          console.error('Service unavailable');
          break;
        default:
          console.error(`HTTP Error ${status}:`, data);
      }
    } else if (error.code === 'ECONNABORTED') {
      console.error('Request timeout');
    } else {
      console.error('Network error:', error.message);
    }
    
    return Promise.reject(error);
  }
);

export default apiClient;
