import { ReactElement } from 'react';
import { render, RenderOptions } from '@testing-library/react';
import { Experimental_CssVarsProvider as CssVarsProvider } from '@mui/material/styles';
import { MemoryRouter } from 'react-router-dom';
import theme from '../theme';

// Custom render function that includes providers
function customRender(
  ui: ReactElement,
  options?: Omit<RenderOptions, 'wrapper'> & { initialEntries?: string[] }
) {
  const { initialEntries = ['/'], ...renderOptions } = options || {};
  
  function Wrapper({ children }: { children: React.ReactNode }) {
    return (
      <CssVarsProvider theme={theme}>
        <MemoryRouter initialEntries={initialEntries}>
          {children}
        </MemoryRouter>
      </CssVarsProvider>
    );
  }

  return render(ui, { wrapper: Wrapper, ...renderOptions });
}

// Re-export everything from React Testing Library
export * from '@testing-library/react';
export { customRender as render };
