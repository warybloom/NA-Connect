import { build } from 'vite';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

// Independent entrypoint: verifies both new modules without changing App or shared routing.
const root = fileURLToPath(new URL('../../../', import.meta.url));
process.chdir(root);
await build({
  root,
  configFile: path.join(root, 'vite.config.ts'),
  build: {
    outDir: 'dist/weekly-planning-preview',
    rollupOptions: { input: path.join(root, 'src/features/planning/preview.html') },
  },
});
