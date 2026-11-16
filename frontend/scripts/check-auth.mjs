import { build } from 'esbuild';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

await build({
  entryPoints: ['tests/auth.spec.ts'],
  outfile: '.angular/tests/auth.spec.mjs',
  bundle: true, platform: 'node', format: 'esm', packages: 'external',
  tsconfig: 'tsconfig.json',
});
await import(pathToFileURL(resolve('.angular/tests/auth.spec.mjs')).href);
