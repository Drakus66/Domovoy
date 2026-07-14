// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import apiClient from './client';

/** A named bundle of permissions (matches DbGateway Role, roadmap Epic 2E). */
export interface Role {
  id: string;
  name: string;
  description?: string | null;
  isBuiltIn: boolean;
  permissions: string[];
  createdAt: string;
  updatedAt: string;
}

/** Create/update payload for a role (id/isBuiltIn are server-owned). */
export interface RoleInput {
  name: string;
  description?: string | null;
  permissions: string[];
}

/** A local household member with assigned roles (matches DbGateway User). No login/credentials in Phase 2. */
export interface User {
  id: string;
  displayName: string;
  email?: string | null;
  roleIds: string[];
  enabled: boolean;
  createdAt: string;
  updatedAt: string;
}

/** Create/update payload for a user (id is server/route assigned). */
export interface UserInput {
  displayName: string;
  email?: string | null;
  roleIds: string[];
  enabled: boolean;
}

export const securityApi = {
  // --- roles ---
  getRoles: (): Promise<Role[]> =>
    apiClient.get<Role[]>('/api/roles').then((r) => r.data),

  getPermissions: (): Promise<string[]> =>
    apiClient.get<string[]>('/api/roles/permissions').then((r) => r.data),

  createRole: (input: RoleInput): Promise<Role> =>
    apiClient.post<Role>('/api/roles', input).then((r) => r.data),

  updateRole: (id: string, input: RoleInput): Promise<void> =>
    apiClient.put(`/api/roles/${encodeURIComponent(id)}`, input).then(() => undefined),

  deleteRole: (id: string): Promise<void> =>
    apiClient.delete(`/api/roles/${encodeURIComponent(id)}`).then(() => undefined),

  // --- users ---
  getUsers: (): Promise<User[]> =>
    apiClient.get<User[]>('/api/users').then((r) => r.data),

  createUser: (input: UserInput): Promise<User> =>
    apiClient.post<User>('/api/users', input).then((r) => r.data),

  updateUser: (id: string, input: UserInput): Promise<void> =>
    apiClient.put(`/api/users/${encodeURIComponent(id)}`, input).then(() => undefined),

  deleteUser: (id: string): Promise<void> =>
    apiClient.delete(`/api/users/${encodeURIComponent(id)}`).then(() => undefined),
};
