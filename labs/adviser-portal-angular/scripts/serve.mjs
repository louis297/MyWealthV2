import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const target = join(root, 'src/assets/runtime-config.json');

mkdirSync(dirname(target), { recursive: true });
writeFileSync(
  target,
  `${JSON.stringify(
    {
      webApiBaseUrl: (process.env.WEBAPI_BASE_URL ?? '').replace(/\/$/, ''),
      identityAuthority: (process.env.IDENTITY_AUTHORITY ?? '').replace(/\/$/, ''),
    },
    null,
    2,
  )}\n`,
);

const port = process.env.PORT || '4200';
const child = spawn(
  'npx',
  ['ng', 'serve', '--host', '0.0.0.0', '--port', String(port), '--disable-host-check'],
  { cwd: root, stdio: 'inherit', shell: true },
);

child.on('exit', (code) => {
  process.exit(code ?? 1);
});
