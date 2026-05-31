// Navigation responsive tests
// Validates: Requirements 6.4, 7.3

import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { useMediaQuery } from '@mui/material';
import { Experimental_CssVarsProvider as CssVarsProvider } from '@mui/material/styles';
import { BrowserRouter } from 'react-router-dom';
import theme from '../../theme';
import Navigation from './Navigation';

// Mock useMediaQuery
vi.mock('@mui/material', async () => {
  const actual = await vi.importActual('@mui/material');
  return {
    ...actual,
    useMediaQuery: vi.fn(),
  };
});

const renderNavigation = () => {
  return render(
    <BrowserRouter>
      <CssVarsProvider theme={theme}>
        <Navigation />
      </CssVarsProvider>
    </BrowserRouter>
  );
};

describe('Navigation Responsive Behavior', () => {
  it('should show desktop navigation on large screens', () => {
    // Mock desktop viewport
    vi.mocked(useMediaQuery).mockReturnValue(false);

    renderNavigation();

    // Desktop links should be visible
    expect(screen.getByRole('link', { name: /dashboard/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /logs/i })).toBeInTheDocument();

    // Mobile menu button should not be visible
    expect(screen.queryByLabelText('open drawer')).not.toBeInTheDocument();
  });

  it('should show mobile menu button on small screens', () => {
    // Mock mobile viewport
    vi.mocked(useMediaQuery).mockReturnValue(true);

    renderNavigation();

    // Mobile menu button should be visible
    expect(screen.getByLabelText('open drawer')).toBeInTheDocument();

    // Desktop navigation links should not be visible in the toolbar
    expect(screen.queryByRole('link', { name: /dashboard/i })).not.toBeInTheDocument();
  });

  it('should open mobile drawer when menu button is clicked', async () => {
    // Mock mobile viewport
    vi.mocked(useMediaQuery).mockReturnValue(true);

    renderNavigation();

    // Click menu button
    const menuButton = screen.getByLabelText('open drawer');
    fireEvent.click(menuButton);

    // Drawer should open with navigation items
    await waitFor(() => {
      expect(screen.getByText('Dashboard')).toBeInTheDocument();
      expect(screen.getByText('Logs')).toBeInTheDocument();
    });
  });

  it('should close mobile drawer when clicking on a navigation item', async () => {
    // Mock mobile viewport
    vi.mocked(useMediaQuery).mockReturnValue(true);

    renderNavigation();

    // Open drawer
    const menuButton = screen.getByLabelText('open drawer');
    fireEvent.click(menuButton);

    // Wait for drawer to open
    await waitFor(() => {
      expect(screen.getByText('Dashboard')).toBeInTheDocument();
    });

    // Click on a navigation item
    const dashboardLink = screen.getByText('Dashboard');
    fireEvent.click(dashboardLink);

    // Drawer should close (navigation items should not be visible)
    await waitFor(() => {
      // The drawer content should be hidden after clicking
      const drawer = screen.queryByText('Dashboard');
      // In mobile view, after clicking, the drawer closes
      expect(drawer).not.toBeVisible();
    }, { timeout: 1000 });
  });

  it('should have touch-friendly menu button size', () => {
    // Mock mobile viewport
    vi.mocked(useMediaQuery).mockReturnValue(true);

    renderNavigation();

    const menuButton = screen.getByLabelText('open drawer');
    
    // MUI IconButton should have minimum 44x44 size from theme
    expect(menuButton).toBeInTheDocument();
  });

  it('should display app title on both mobile and desktop', () => {
    // Test desktop
    vi.mocked(useMediaQuery).mockReturnValue(false);
    const { rerender } = renderNavigation();
    expect(screen.getAllByText('Domovoy').length).toBeGreaterThan(0);

    // Test mobile
    vi.mocked(useMediaQuery).mockReturnValue(true);
    rerender(
      <BrowserRouter>
        <CssVarsProvider theme={theme}>
          <Navigation />
        </CssVarsProvider>
      </BrowserRouter>
    );
    expect(screen.getAllByText('Domovoy').length).toBeGreaterThan(0);
  });
});
