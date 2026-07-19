import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { VitePWA } from 'vite-plugin-pwa';
import path from 'path';

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [
    react(),
    // PWA (mobile-app / remote-access track, Epic 2O.1). The service worker precaches the app shell (hashed
    // assets) so the UI opens instantly and works offline, but device state must never be stale: /api and /hub
    // are excluded from the SW entirely (network-only, they aren't navigations and have no runtime cache), and
    // /locales is NetworkFirst so a translation edit shows up on next load. registerType 'autoUpdate' swaps to a
    // fresh build on the next navigation.
    VitePWA({
      registerType: 'autoUpdate',
      injectRegister: 'auto',
      includeAssets: ['favicon.svg', 'domovoy-icon.svg', 'domovoy-icon-maskable.svg'],
      manifest: {
        name: 'Domovoy',
        short_name: 'Domovoy',
        description: 'Домовой — управление умным домом',
        lang: 'ru',
        theme_color: '#4f46e5',
        background_color: '#0b0f1a',
        display: 'standalone',
        orientation: 'any',
        start_url: '/',
        scope: '/',
        icons: [
          { src: 'domovoy-icon.svg', sizes: 'any', type: 'image/svg+xml', purpose: 'any' },
          { src: 'domovoy-icon-maskable.svg', sizes: 'any', type: 'image/svg+xml', purpose: 'maskable' },
        ],
      },
      workbox: {
        globPatterns: ['**/*.{js,css,html,svg,woff,woff2}'],
        // Sourcemaps are large and never needed offline.
        globIgnores: ['**/*.map'],
        maximumFileSizeToCacheInBytes: 4 * 1024 * 1024,
        navigateFallback: '/index.html',
        // Keep the SPA fallback away from anything that must reach the server (or a different origin).
        navigateFallbackDenylist: [/^\/api/, /^\/hub/, /^\/locales/, /^\/metrics/, /^\/health/],
        runtimeCaching: [
          {
            // Translations: prefer the network (nginx serves them no-cache), fall back to cache when offline.
            urlPattern: ({ url }) => url.pathname.startsWith('/locales/'),
            handler: 'NetworkFirst',
            options: {
              cacheName: 'domovoy-locales',
              expiration: { maxEntries: 40, maxAgeSeconds: 60 * 60 * 24 * 7 },
            },
          },
        ],
      },
      // No SW in dev / unit tests — it only complicates hot-reload and jsdom; it activates in the production build.
      devOptions: { enabled: false },
    }),
  ],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  server: {
    port: 3000,
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
  build: {
    outDir: 'dist',
    sourcemap: true,
    rollupOptions: {
      output: {
        manualChunks: {
          'react-vendor': ['react', 'react-dom', 'react-router-dom'],
          'mui-vendor': ['@mui/material', '@mui/icons-material'],
          'chart-vendor': ['recharts'],
        },
      },
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: './src/test/setup.ts',
    coverage: {
      provider: 'v8',
      reporter: ['text', 'json', 'html'],
      exclude: ['node_modules/', 'src/test/'],
    },
  },
});
