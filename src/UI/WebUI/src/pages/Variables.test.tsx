// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '../test/utils';
import Variables from './Variables';
import { variablesApi, GlobalVariable } from '../api/variables';

vi.mock('../api/variables', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/variables')>()),
  variablesApi: {
    getVariables: vi.fn(),
    getVariable: vi.fn(),
    createVariable: vi.fn(),
    updateVariable: vi.fn(),
    setValue: vi.fn(),
    deleteVariable: vi.fn(),
  },
}));

const mocked = vi.mocked(variablesApi);

const counter: GlobalVariable = {
  id: 'v1', name: 'away_counter', type: 'Number', description: 'times gone Away today',
  value: 3, createdAt: '2026-07-20T00:00:00Z', updatedAt: '2026-07-20T00:00:00Z',
};

beforeEach(() => { vi.clearAllMocks(); });

describe('Variables page (Epic 3E)', () => {
  it('shows the empty state when there are no variables', async () => {
    mocked.getVariables.mockResolvedValue([]);
    render(<Variables />);
    await waitFor(() => expect(screen.getByText(/Переменных пока нет/)).toBeInTheDocument());
  });

  it('lists a variable with its type chip, current value and description', async () => {
    mocked.getVariables.mockResolvedValue([counter]);
    render(<Variables />);
    await waitFor(() => expect(screen.getByText('away_counter')).toBeInTheDocument());
    expect(screen.getByText('Число')).toBeInTheDocument();     // type chip
    expect(screen.getByText('3')).toBeInTheDocument();          // current value chip
    expect(screen.getByText('times gone Away today')).toBeInTheDocument();
  });

  it('creates a new variable through the dialog', async () => {
    mocked.getVariables.mockResolvedValue([]);
    mocked.createVariable.mockResolvedValue(counter);
    render(<Variables />);
    await waitFor(() => expect(screen.getByText(/Переменных пока нет/)).toBeInTheDocument());

    fireEvent.click(screen.getByText('Новая переменная'));
    fireEvent.change(screen.getByLabelText(/Название/), { target: { value: 'boost' } });
    fireEvent.click(screen.getByText('Создать'));

    await waitFor(() => expect(mocked.createVariable).toHaveBeenCalledWith(
      expect.objectContaining({ name: 'boost', type: 'Number' }),
    ));
  });

  it('does not allow creating a variable with a blank name', async () => {
    mocked.getVariables.mockResolvedValue([]);
    render(<Variables />);
    await waitFor(() => expect(screen.getByText(/Переменных пока нет/)).toBeInTheDocument());

    fireEvent.click(screen.getByText('Новая переменная'));
    // name left blank → the Create button is disabled
    expect(screen.getByText('Создать').closest('button')).toBeDisabled();
  });
});
