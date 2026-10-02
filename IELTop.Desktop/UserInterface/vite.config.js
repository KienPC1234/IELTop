import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { resolve } from 'path'

const here = import.meta.dirname

// Two pages: the main app (index.html) and the exam window (exam.html), which
// the host opens as its own native window. Base is relative so the same bundle
// works from any port the host picks on loopback.
export default defineConfig({
  plugins: [react()],
  base: './',
  build: {
    outDir: '../wwwroot',
    emptyOutDir: true,
    target: 'es2022',
    cssCodeSplit: false,
    sourcemap: false,
    reportCompressedSize: true,
    rollupOptions: {
      input: {
        main: resolve(here, 'index.html'),
        exam: resolve(here, 'exam.html'),
      },
      output: {
        entryFileNames: 'assets/[name].js',
        chunkFileNames: 'assets/[name].js',
        assetFileNames: 'assets/[name][extname]',
      },
    },
  },
})

