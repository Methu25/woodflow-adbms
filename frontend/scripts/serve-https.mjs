import { existsSync } from 'node:fs';
import { resolve } from 'node:path';
import { spawn } from 'node:child_process';

const certificate = resolve('.certs/localhost.pem');
const key = resolve('.certs/localhost.key');
if (!existsSync(certificate) || !existsSync(key)) {
  console.error('Trusted HTTPS certificate files are missing. Follow the HTTPS setup in frontend/README.md.');
  process.exit(1);
}
// Trust the exported .NET certificate when the Angular proxy connects to Kestrel.
// Certificate verification remains enabled; no HTTP or insecure TLS fallback.
const child = spawn(process.execPath, [
  resolve('node_modules/@angular/cli/bin/ng.js'), 'serve', ...process.argv.slice(2),
], {
  stdio: 'inherit',
  windowsHide: true,
  env: { ...process.env, NODE_EXTRA_CA_CERTS: certificate },
});
child.on('exit', code => process.exit(code ?? 1));
for (const event of ['SIGINT', 'SIGTERM']) process.on(event, () => child.kill(event));
