const { spawnSync } = require('child_process');
const path = require('path');
const run = spawnSync(process.execPath, [path.join(__dirname, '..', '..', 'tools', 'Scan-UiText.js'), '--check'], { encoding: 'utf8' });
process.stdout.write(run.stdout || '');
process.stderr.write(run.stderr || '');
if (run.status !== 0) { console.error('ui-text testi KALDI'); process.exit(1); }
console.log('ui-text testi gecti');
