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
    alias: [
      // Иконки: `@mui/icons-material` версии 5 не объявляет `exports`, поэтому подпуть
      // `@mui/icons-material/Menu` резолвится в CJS-файл пакета (поле `module` действует только
      // на корень). Rolldown (сборщик Vite 8) такой CJS-модуль не разворачивает при default-импорте
      // — компонент приезжает объектом `{ default: … }`, и React валится с ошибкой #130 «element
      // type is invalid», отдавая белый экран на любой странице с иконками. Ведём подпути прямо в
      // ESM-сборку пакета, где default-экспорт настоящий. `esm` в шаблоне исключён, чтобы правило
      // не сработало повторно на собственном результате.
      {
        find: /^@mui\/icons-material\/(?!esm\/)([A-Za-z0-9_]+)$/,
        replacement: '@mui/icons-material/esm/$1',
      },
      // import.meta.dirname вместо __dirname: последний не поддерживается нативным загрузчиком
      // конфига, который в Vite станет умолчанием.
      { find: '@', replacement: path.resolve(import.meta.dirname, './src') },
    ],
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
        // Rolldown (сборщик Vite 8) принимает manualChunks только функцией — объектная форма
        // больше не поддерживается. Разбиение сохранено прежним: тяжёлые вендоры выносятся
        // отдельными чанками, чтобы правка кода приложения не инвалидировала их кеш у пользователя.
        manualChunks(id: string) {
          if (!id.includes('node_modules')) return undefined;

          if (/[\\/]node_modules[\\/](react|react-dom|react-router|react-router-dom|@remix-run)[\\/]/.test(id)) {
            return 'react-vendor';
          }
          if (/[\\/]node_modules[\\/]@mui[\\/]/.test(id)) return 'mui-vendor';
          if (/[\\/]node_modules[\\/](recharts|d3-[^\\/]+|victory-[^\\/]+)[\\/]/.test(id)) return 'chart-vendor';

          return undefined;
        },
      },
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: './src/test/setup.ts',
    // Дефолтные 5 с тесноваты: страницы вроде /settings рендерят десяток секций MUI с i18n, и под
    // параллельной нагрузкой они упираются в лимит, хотя логика в порядке. Лимит поднят, чтобы
    // падение теста означало сломанное поведение, а не занятость машины.
    testTimeout: 20000,
    hookTimeout: 20000,
    coverage: {
      provider: 'v8',
      reporter: ['text', 'json', 'html'],
      exclude: ['node_modules/', 'src/test/'],
    },
  },
});
