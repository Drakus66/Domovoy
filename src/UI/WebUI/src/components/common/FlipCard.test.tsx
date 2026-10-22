// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect } from 'vitest';
import userEvent from '@testing-library/user-event';
import { render, screen } from '../../test/utils';
import FlipCard from './FlipCard';

function subject(lazyBack = false) {
  return (
    <FlipCard
      front={<div>FACE</div>}
      back={<div>BACK</div>}
      flipLabel="Подробности"
      backLabel="Вернуться"
      lazyBack={lazyBack}
    />
  );
}

describe('FlipCard', () => {
  it('shows the front and hides the back until flipped', () => {
    render(subject());
    expect(screen.getByText('FACE')).toBeVisible();
    expect(screen.getByText('BACK')).not.toBeVisible();
    expect(screen.getByRole('button', { name: 'Подробности' })).toHaveAttribute('aria-pressed', 'false');
  });

  it('flips on trigger click and keeps the front in the DOM', async () => {
    const user = userEvent.setup();
    render(subject());

    await user.click(screen.getByRole('button', { name: 'Подробности' }));

    expect(screen.getByRole('button', { name: 'Вернуться' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByText('BACK')).toBeVisible();
    // The front stays mounted (tile tests elsewhere rely on its content being findable).
    expect(screen.getByText('FACE')).toBeInTheDocument();
    expect(screen.getByText('FACE')).not.toBeVisible();
  });

  it('flips back on Escape and returns focus to the trigger', async () => {
    const user = userEvent.setup();
    render(subject());

    await user.click(screen.getByRole('button', { name: 'Подробности' }));
    await user.keyboard('{Escape}');

    expect(screen.getByText('FACE')).toBeVisible();
    const trigger = screen.getByRole('button', { name: 'Подробности' });
    expect(trigger).toHaveAttribute('aria-pressed', 'false');
    expect(trigger).toHaveFocus();
  });

  it('mounts a lazy back only after the first flip', async () => {
    const user = userEvent.setup();
    render(subject(true));

    expect(screen.queryByText('BACK')).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Подробности' }));
    expect(screen.getByText('BACK')).toBeVisible();
  });
});
