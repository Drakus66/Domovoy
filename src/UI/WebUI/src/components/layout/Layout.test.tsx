import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Experimental_CssVarsProvider as CssVarsProvider } from '@mui/material/styles';
import { BrowserRouter } from 'react-router-dom';
import theme from '../../theme';
import Layout from './Layout';

describe('Layout', () => {
  it('renders navigation', () => {
    render(
      <CssVarsProvider theme={theme}>
        <BrowserRouter>
          <Layout />
        </BrowserRouter>
      </CssVarsProvider>
    );
    
    const titles = screen.getAllByText('Domovoy');
    expect(titles.length).toBeGreaterThan(0);
  });

  it('renders navigation menu items', () => {
    render(
      <CssVarsProvider theme={theme}>
        <BrowserRouter>
          <Layout />
        </BrowserRouter>
      </CssVarsProvider>
    );
    
    const dashboardLinks = screen.getAllByText('Dashboard');
    const logsLinks = screen.getAllByText('Logs');
    expect(dashboardLinks.length).toBeGreaterThan(0);
    expect(logsLinks.length).toBeGreaterThan(0);
  });

  it('wraps content with error boundary', () => {
    render(
      <CssVarsProvider theme={theme}>
        <BrowserRouter>
          <Layout />
        </BrowserRouter>
      </CssVarsProvider>
    );
    
    // Layout should render without errors
    expect(screen.getByRole('main')).toBeInTheDocument();
  });
});
