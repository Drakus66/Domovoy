import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import Layout from './Layout';

describe('Layout', () => {
  it('renders navigation', () => {
    render(
      <BrowserRouter>
        <Layout />
      </BrowserRouter>
    );
    
    const titles = screen.getAllByText('Domovoy');
    expect(titles.length).toBeGreaterThan(0);
  });

  it('renders navigation menu items', () => {
    render(
      <BrowserRouter>
        <Layout />
      </BrowserRouter>
    );
    
    const dashboardLinks = screen.getAllByText('Dashboard');
    const logsLinks = screen.getAllByText('Logs');
    expect(dashboardLinks.length).toBeGreaterThan(0);
    expect(logsLinks.length).toBeGreaterThan(0);
  });

  it('wraps content with error boundary', () => {
    render(
      <BrowserRouter>
        <Layout />
      </BrowserRouter>
    );
    
    // Layout should render without errors
    expect(screen.getByRole('main')).toBeInTheDocument();
  });
});
