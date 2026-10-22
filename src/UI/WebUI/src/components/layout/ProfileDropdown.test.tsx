// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect, beforeEach } from 'vitest';
import userEvent from '@testing-library/user-event';
import { render, screen } from '../../test/utils';
import ProfileDropdown from './ProfileDropdown';
import { useAuthStore } from '../../store/authStore';
import { useThemeStore } from '../../store/themeStore';

const realUser = {
  id: 'u1', username: 'ann', displayName: 'Ann', email: null,
  roleIds: ['admin'], permissions: [],
};

describe('ProfileDropdown', () => {
  beforeEach(() => {
    useAuthStore.setState({ status: 'authenticated', user: null });
    useThemeStore.getState().setThemeId('domovoy');
  });

  it('works without a signed-in account: no sign-out, local-access label', async () => {
    const user = userEvent.setup();
    render(<ProfileDropdown variant="sidebar" />);

    await user.click(screen.getByRole('button', { name: 'Профиль и настройки' }));

    expect(screen.getByRole('menu')).toBeInTheDocument();
    expect(screen.getAllByText('Локальный доступ').length).toBeGreaterThan(0);
    expect(screen.queryByText('Выйти')).not.toBeInTheDocument();
    expect(screen.queryByText('Сменить пароль')).not.toBeInTheDocument();
    expect(screen.getByText('Настройки')).toBeInTheDocument();
  });

  it('shows the account section for a real user', async () => {
    useAuthStore.setState({ status: 'authenticated', user: realUser });
    const user = userEvent.setup();
    render(<ProfileDropdown variant="sidebar" />);

    await user.click(screen.getByRole('button', { name: 'Профиль и настройки' }));

    expect(screen.getByText('Выйти')).toBeInTheDocument();
    expect(screen.getByText('Сменить пароль')).toBeInTheDocument();
    expect(screen.getAllByText('Ann').length).toBeGreaterThan(0);
  });

  it('changes the theme through the nested theme menu', async () => {
    const user = userEvent.setup();
    render(<ProfileDropdown variant="sidebar" />);

    await user.click(screen.getByRole('button', { name: 'Профиль и настройки' }));
    await user.click(screen.getByText('Тема'));
    await user.click(await screen.findByText('Jarvis'));

    expect(useThemeStore.getState().themeId).toBe('jarvis');
  });

  it('toggles light/dark without closing the menu', async () => {
    const user = userEvent.setup();
    render(<ProfileDropdown variant="bar" />);

    await user.click(screen.getByRole('button', { name: 'Профиль и настройки' }));
    const item = screen.getByText(/Переключить на (светлую|тёмную) тему/);
    await user.click(item);

    expect(screen.getByRole('menu')).toBeInTheDocument();
  });
});
