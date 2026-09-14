const fs = require('node:fs/promises');
const path = require('node:path');
const crypto = require('node:crypto');
const origin = 'https://bm-cardbattle-assets.web.app';

async function main() {
  const local = JSON.parse(await fs.readFile('ServerData/resources-manifest.json', 'utf8'));
  const response = await fetch(`${origin}/resources-manifest.json`, { signal: AbortSignal.timeout(30000) });
  if (!response.ok) throw new Error(`Manifest HTTP ${response.status}`);
  const remote = await response.json();
  if (JSON.stringify(remote) !== JSON.stringify(local)) throw new Error('Published manifest differs from local build');
  const files = Object.keys(local.files);
  let index = 0;
  await Promise.all(Array.from({ length: 8 }, async () => {
    while (index < files.length) {
      const file = files[index++];
      const head = await fetch(`${origin}/${file}`, { method: 'HEAD', signal: AbortSignal.timeout(30000) });
      if (!head.ok) throw new Error(`${file}: HTTP ${head.status}`);
      if (head.headers.get('access-control-allow-origin') !== '*') throw new Error(`CORS header: ${file}`);
      const cache = head.headers.get('cache-control') || '';
      if (file.endsWith('.bundle') ? !cache.includes('immutable') || cache.includes('no-cache') : !cache.includes('no-cache'))
        throw new Error(`Cache header ${cache}: ${file}`);
    }
  }));
  const selected = files.filter(file => /catalog.*\.(json|hash)$/.test(file));
  for (const needle of ['remoteui_assets_all_', 'cardart_assets_image_card_', 'cardart_assets_ultrapack_']) {
    const matches = files.filter(file => file.includes(needle) && file.endsWith('.bundle'));
    if (!matches.length) throw new Error(`Missing representative bundle: ${needle}`);
    selected.push(...(needle.startsWith('remoteui') ? matches : matches.slice(0, 1)));
  }
  const verified = [];
  for (const file of selected) {
    const response = await fetch(`${origin}/${file}`, { signal: AbortSignal.timeout(60000) });
    if (!response.ok) throw new Error(`${file}: HTTP ${response.status}`);
    const buffer = Buffer.from(await response.arrayBuffer());
    const hash = crypto.createHash('sha256').update(buffer).digest('hex');
    if (hash !== local.files[file].sha256 || buffer.length !== local.files[file].bytes)
      throw new Error(`Downloaded content mismatch: ${file}`);
    verified.push({ file, bytes: buffer.length, sha256: hash });
  }
  const report = { checkedAt: new Date().toISOString(), origin, filesChecked: files.length, verified };
  const reportPath = path.join('Build', 'ResourceDeploymentChecks', `${Date.now()}.json`);
  await fs.mkdir(path.dirname(reportPath), { recursive: true });
  await fs.writeFile(reportPath, JSON.stringify(report, null, 2) + '\n');
  console.log(`PASS: ${files.length} HTTPS files, CORS and cache headers; ${verified.length} downloads match SHA-256`);
  console.log(`Report: ${reportPath}`);
}
main().catch(error => { console.error(error); process.exitCode = 1; });
