// Resolve the installed design kit's browser dependencies from this workspace.
// No changes to the shared skill installation are needed.
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import { dirname, join } from 'node:path';
const require = createRequire(import.meta.url);
export async function resolve(specifier, context, nextResolve) {
  if (specifier === 'playwright' || specifier === 'axe-core') {
    const file = specifier === 'playwright' ? join(dirname(require.resolve('playwright/package.json')), 'index.mjs') : require.resolve(specifier);
    return { url: pathToFileURL(file).href, shortCircuit: true };
  }
  return nextResolve(specifier, context);
}
