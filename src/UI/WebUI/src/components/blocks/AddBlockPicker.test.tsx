// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect, vi } from 'vitest';
import { fireEvent } from '@testing-library/react';
import { render, screen } from '../../test/utils';
import AddBlockPicker from './AddBlockPicker';
import { BlockCatalogEntry } from '../../api/blocks';

// A ready-made composite template with exposed passthrough params (Epic 2Q) + an input to bind and an output.
// The typeId is unknown to the locale files on purpose: the catalog's own strings must render as-is
// (the fallback path — e.g. a composite authored in config or served by a plugin).
const template: BlockCatalogEntry = {
  typeId: 'my_custom_recipe',
  title: 'Термостат из примитивов',
  description: 'EWMA-сглаживание → гистерезисное реле.',
  category: 'template',
  inputs: [{ name: 'temperature', kind: 'Number', description: '' }],
  outputs: [{ id: 'heat', kind: 'Boolean', writable: false }],
  params: [
    { name: 'high', default: 21.5, description: '' },
    { name: 'low', default: 20.5, description: '' },
  ],
};

// A plain primitive — stays in the compact list, not the gallery.
const primitive: BlockCatalogEntry = {
  typeId: 'comparator', title: 'Компаратор', description: 'Сравнение с порогом.',
  category: 'logic', inputs: [], outputs: [], params: [],
};

describe('AddBlockPicker template gallery', () => {
  it('renders templates as gallery cards showing what they tune and drive', () => {
    render(<AddBlockPicker open catalog={[template, primitive]} onPick={() => {}} onClose={() => {}} />);

    expect(screen.getByText('Термостат из примитивов')).toBeInTheDocument();
    expect(screen.getByText('EWMA-сглаживание → гистерезисное реле.')).toBeInTheDocument();
    // Exposed passthrough params are surfaced as chips (name · default) under a "tunable" label.
    expect(screen.getByText('Настраивается')).toBeInTheDocument();
    expect(screen.getByText('high · 21.5')).toBeInTheDocument();
    expect(screen.getByText('low · 20.5')).toBeInTheDocument();
    // Input to bind and output it drives are surfaced too.
    expect(screen.getByText('temperature')).toBeInTheDocument();
    expect(screen.getByText('heat')).toBeInTheDocument();
  });

  it('overlays the locale translation on a known type (server catalog is English)', () => {
    const known: BlockCatalogEntry = {
      ...template,
      typeId: 'smart_thermostat',
      title: 'Thermostat (from primitives)',
      description: 'The classic thermostat rebuilt from primitives.',
    };
    render(<AddBlockPicker open catalog={[known]} onPick={() => {}} onClose={() => {}} />);

    // Title/description come from blocks:type.smart_thermostat, params from blocks:param.smart_thermostat.
    expect(screen.getByText('Термостат (из примитивов)')).toBeInTheDocument();
    expect(screen.queryByText('Thermostat (from primitives)')).not.toBeInTheDocument();
    expect(screen.getByText('Верхний порог · 21.5')).toBeInTheDocument();
  });

  it('hands the picked template to the authoring dialog untouched by localization', () => {
    const onPick = vi.fn();
    render(<AddBlockPicker open catalog={[template]} onPick={onPick} onClose={() => {}} />);

    fireEvent.click(screen.getByText('Термостат из примитивов'));
    expect(onPick).toHaveBeenCalledWith(template);
  });

  it('keeps non-template types in the compact list', () => {
    render(<AddBlockPicker open catalog={[template, primitive]} onPick={() => {}} onClose={() => {}} />);
    expect(screen.getByText('Компаратор')).toBeInTheDocument();
  });
});
