import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';
import { backendUrl } from './backend-url.ts';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), 'BACKEND_');
  const target = backendUrl(process.env.BACKEND_URL ?? env.BACKEND_URL);
  return {
    plugins: [react()],
    server: { host: '127.0.0.1', port: 5173, proxy: { '/api': { target }, '/health': { target } } },
    preview: { host: '127.0.0.1' },
    build: { rolldownOptions: { output: { manualChunks: id => id.includes('/maplibre-gl/') ? 'maplibre' : undefined } } },
  };
});
