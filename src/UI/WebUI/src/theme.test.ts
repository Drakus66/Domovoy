// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

// Theme configuration tests
// Validates: Requirements 7.1, 7.2, 7.3

import { describe, it, expect } from 'vitest';
import theme, { themeOptions, DEFAULT_THEME_ID, getTheme, isThemeId } from './theme';

describe('Theme Responsive Configuration', () => {
  it('should have correct breakpoint values for responsive layouts', () => {
    // Requirement 7.3: Mobile (xs) starts at 0
    expect(theme.breakpoints.values.xs).toBe(0);
    
    // Requirement 7.2: Tablet (sm) starts at 600px
    expect(theme.breakpoints.values.sm).toBe(600);
    
    // Requirement 7.1: Desktop (md) starts at 960px
    expect(theme.breakpoints.values.md).toBe(960);
    
    // Large desktop
    expect(theme.breakpoints.values.lg).toBe(1280);
    
    // Extra large
    expect(theme.breakpoints.values.xl).toBe(1920);
  });

  it('should have touch-friendly button sizes', () => {
    const buttonStyles = theme.components?.MuiButton?.styleOverrides?.root as any;
    
    // Touch-friendly minimum size (44x44 is recommended for touch targets)
    expect(buttonStyles.minHeight).toBe(44);
    expect(buttonStyles.minWidth).toBe(44);
  });

  it('should have touch-friendly icon button sizes', () => {
    const iconButtonStyles = theme.components?.MuiIconButton?.styleOverrides?.root as any;
    
    // Touch-friendly minimum size
    expect(iconButtonStyles.minHeight).toBe(44);
    expect(iconButtonStyles.minWidth).toBe(44);
  });

  it('should have larger slider thumb for touch control', () => {
    const sliderStyles = theme.components?.MuiSlider?.styleOverrides?.root as any;
    
    // Larger thumb for easier touch control
    expect(sliderStyles['& .MuiSlider-thumb'].width).toBe(20);
    expect(sliderStyles['& .MuiSlider-thumb'].height).toBe(20);
  });

  it('should support breakpoint queries', () => {
    // Test that breakpoint helper methods work
    const upMd = theme.breakpoints.up('md');
    expect(upMd).toContain('960px');
    
    const downSm = theme.breakpoints.down('sm');
    // down('sm') returns max-width: 599.95px (just below 600px)
    expect(downSm).toContain('599.95px');
    
    const betweenSmMd = theme.breakpoints.between('sm', 'md');
    expect(betweenSmMd).toContain('600px');
    // between uses max-width: 959.95px (just below 960px)
    expect(betweenSmMd).toContain('959.95px');
  });
});

describe('Theme registry', () => {
  it('has unique ids and both color schemes per theme', () => {
    const ids = themeOptions.map((o) => o.id);
    expect(new Set(ids).size).toBe(ids.length);
    for (const option of themeOptions) {
      expect(option.theme.colorSchemes.light).toBeDefined();
      expect(option.theme.colorSchemes.dark).toBeDefined();
      expect(option.preview.light.primary).toMatch(/^#/);
      expect(option.preview.dark.primary).toMatch(/^#/);
    }
  });

  it('resolves the default theme and falls back on unknown ids', () => {
    expect(isThemeId(DEFAULT_THEME_ID)).toBe(true);
    expect(isThemeId('no-such-theme')).toBe(false);
    expect(getTheme(DEFAULT_THEME_ID)).toBe(theme);
    expect(getTheme('no-such-theme')).toBe(theme);
  });

  it('default theme keeps the shared structural base', () => {
    for (const option of themeOptions) {
      expect(option.theme.breakpoints.values.md).toBe(960);
      expect(option.theme.shape.borderRadius).toBe(12);
    }
  });
});
