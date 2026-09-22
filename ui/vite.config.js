import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Relative paths, because the app loads these files from its own folder through WebView2.
export default defineConfig({
  base: './',
  plugins: [react()],
  build: { outDir: 'dist', emptyOutDir: true, assetsInlineLimit: 0 }
});