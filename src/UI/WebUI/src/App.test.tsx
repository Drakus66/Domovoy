// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Experimental_CssVarsProvider as CssVarsProvider } from '@mui/material/styles';
import i18n from 'i18next';
import theme from './theme';
import App from './App';

describe('App', () => {
  it('renders without crashing', () => {
    render(
      <CssVarsProvider theme={theme}>
        <App />
      </CssVarsProvider>
    );
    expect(screen.getAllByText('Domovoy').length).toBeGreaterThan(0);
  });

  it('renders the Devices page by default', () => {
    render(
      <CssVarsProvider theme={theme}>
        <App />
      </CssVarsProvider>
    );
    expect(screen.getByRole('heading', { name: i18n.t('devices:title'), level: 1 })).toBeInTheDocument();
  });
});
