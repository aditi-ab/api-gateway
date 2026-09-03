import { fileURLToPath, URL } from 'node:url';
import VueI18nPlugin from '@intlify/unplugin-vue-i18n/vite';
import tailwindcss from '@tailwindcss/vite';
import vue from '@vitejs/plugin-vue';
import { defineConfig, loadEnv } from 'vite';

export default defineConfig(({ mode }) => {
  const environment = loadEnv(mode, process.cwd(), '');
  const managementUrl = environment.API_GATEWAY_MANAGEMENT_URL || 'http://localhost:61551';
  const proxy = { target: managementUrl, changeOrigin: true };

  return {
    base: '/admin/',
    plugins: [tailwindcss(), vue(), VueI18nPlugin({ include: fileURLToPath(new URL('./src/locales/**', import.meta.url)) })],
    resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) }, dedupe: ['vue', 'reka-ui', '@lucide/vue'] },
    build: { outDir: '../ApiGateway.Management/wwwroot/admin', emptyOutDir: true },
    server: { proxy: { '/graphql': proxy, '/admin/auth': proxy, '/admin/identity': proxy, '/admin/config.json': proxy } },
    test: { environment: 'node' },
  };
});
