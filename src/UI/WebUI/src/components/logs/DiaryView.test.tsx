// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect } from 'vitest';
import { http, HttpResponse } from 'msw';
import i18n from 'i18next';
import { render, screen, fireEvent } from '../../test/utils';
import { server } from '../../test/setup';
import DiaryView from './DiaryView';
import Logs from '../../pages/Logs';

const oneDay = [{
  id: 'story-2026-07-09',
  date: '2026-07-09T00:00:00Z',
  locale: 'ru',
  paragraph: 'Домочадцы зажгли свет в гостиной.',
  dayScore: 5,
  tier: 0,
}];

describe('DiaryView', () => {
  it('renders a narrated day paragraph', async () => {
    server.use(http.get('*/api/home-story', () => HttpResponse.json(oneDay)));

    render(<DiaryView />);

    expect(await screen.findByText('Домочадцы зажгли свет в гостиной.')).toBeInTheDocument();
  });

  it('shows the spirit-voiced empty state when there is nothing to tell', async () => {
    server.use(http.get('*/api/home-story', () => HttpResponse.json([])));

    render(<DiaryView />);

    expect(await screen.findByText(i18n.t('diary:empty'))).toBeInTheDocument();
  });
});

describe('Logs diary toggle', () => {
  it('switches from Activity to the Diary view', async () => {
    server.use(http.get('*/api/home-story', () => HttpResponse.json(oneDay)));

    render(<Logs />);

    // Activity is the default view; flip to the Diary tab.
    fireEvent.click(screen.getByRole('button', { name: i18n.t('logs:view.diary') }));

    expect(await screen.findByText('Домочадцы зажгли свет в гостиной.')).toBeInTheDocument();
  });
});
