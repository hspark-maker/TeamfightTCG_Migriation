// Firebase predeploy: 카탈로그 누락·깨진 참조·기존 앱 번들 삭제를 업로드 전에 차단한다.
const fs = require('node:fs/promises');
const path = require('node:path');
const crypto = require('node:crypto');

const manifestName = 'resources-manifest.json';

async function walk(dir, prefix = '') {
  const files = [];
  for (const item of await fs.readdir(dir, { withFileTypes: true })) {
    if (item.name.startsWith('.')) continue;
    if (item.isSymbolicLink()) throw new Error(`Symbolic links are not allowed: ${item.name}`);
    const relative = prefix + item.name;
    if (item.isDirectory()) files.push(...await walk(path.join(dir, item.name), relative + '/'));
    else if (item.isFile() && relative !== manifestName && !relative.endsWith('.meta') &&
             item.name !== 'addressables_content_state.bin') files.push(relative);
  }
  return files;
}

async function validate({ root = path.resolve(__dirname, '../ServerData'),
                          origin = 'https://bm-cardbattle-assets.web.app', offline = false,
                          pruneToCatalog = '' } = {}) {
  if (pruneToCatalog && offline) throw new Error('Pruning requires the deployed manifest check.');
  const files = await walk(root).catch(error => {
    if (error.code === 'ENOENT') throw new Error('ServerData가 없습니다. Unity에서 Build Firebase Resources를 먼저 실행하세요.');
    throw error;
  });
  const catalogs = files.filter(file => /(^|\/)catalog[^/]*\.json$/.test(file));
  if (!catalogs.length || !files.some(file => file.endsWith('.bundle')))
    throw new Error('먼저 Unity의 Tools > Addressables > Build Firebase Resources를 실행하세요.');

  const records = {};
  for (const file of files) {
    if (!/^[A-Za-z0-9._/-]+$/.test(file) || file.split('/').includes('..'))
      throw new Error(`Unsupported resource path: ${file}`);
    if (!/\.(bundle|json|hash)$/.test(file)) throw new Error(`Unexpected public file: ${file}`);
    const data = await fs.readFile(path.join(root, file));
    if (!data.length) throw new Error(`Empty resource: ${file}`);
    if (data.length > 2 * 1024 ** 3) throw new Error(`Hosting file exceeds 2 GB: ${file}`);
    records[file] = { bytes: data.length, sha256: crypto.createHash('sha256').update(data).digest('hex') };
  }

  const referenced = new Set();
  for (const file of catalogs) {
    referenced.add(file);
    referenced.add(file.replace(/\.json$/, '.hash'));
    if (!records[file.replace(/\.json$/, '.hash')]) throw new Error(`Missing catalog hash: ${file}`);
    const catalog = JSON.parse(await fs.readFile(path.join(root, file), 'utf8'));
    if (!Array.isArray(catalog.m_InternalIds)) throw new Error(`Invalid Addressables catalog: ${file}`);
    let remoteBundles = 0;
    for (const id of catalog.m_InternalIds) {
      if (/^(?:(?:\.\/)?Library\/|[A-Za-z]:\/|file:|\/)/i.test(id.replace(/\\/g, '/')))
        throw new Error(`Local editor path in Addressables catalog ${file}: ${id}`);
      if (!/^https?:\/\//.test(id)) continue;
      if (!id.startsWith(origin + '/')) throw new Error(`Unexpected remote URL: ${id}`);
      const resource = id.slice(origin.length + 1);
      referenced.add(resource);
      if (!records[resource]) throw new Error(`Catalog references a missing resource: ${resource}`);
      if (resource.endsWith('.bundle')) remoteBundles++;
    }
    if (!remoteBundles) throw new Error(`Catalog has no Firebase bundles: ${file}`);
  }
  if (pruneToCatalog && (catalogs.length !== 1 || catalogs[0] !== pruneToCatalog ||
      files.some(file => !referenced.has(file))))
    throw new Error('Pruning must contain exactly the selected catalog, hash and referenced resources.');

  if (!offline) {
    const response = await fetch(`${origin}/${manifestName}`, { signal: AbortSignal.timeout(30000) });
    if (response.ok) {
      const previous = await response.json();
      if (!previous.files || previous.version !== 1) throw new Error('Invalid deployed resource manifest.');
      if (pruneToCatalog) {
        for (const file of files) {
          if (records[file].sha256 !== previous.files[file]?.sha256)
            throw new Error(`Pruning must preserve deployed file bytes: ${file}`);
        }
      }
      for (const [file, record] of Object.entries(previous.files)) {
        if (!records[file]) {
          if (pruneToCatalog) continue;
          throw new Error(`기존 앱 파일을 복원한 뒤 배포하세요: ServerData/${file}`);
        }
        if (file.endsWith('.bundle') && records[file].sha256 !== record.sha256)
          throw new Error(`이미 배포한 번들의 내용이 변경되었습니다: ${file}`);
      }
    } else if (pruneToCatalog || response.status !== 404) {
      throw new Error(`Deployed manifest check failed: HTTP ${response.status}`);
    }
  }

  await fs.writeFile(path.join(root, manifestName), JSON.stringify({ version: 1, files: records }, null, 2) + '\n');
  const bytes = Object.values(records).reduce((sum, record) => sum + record.bytes, 0);
  console.log(`Firebase resources verified: ${catalogs.length} catalog(s), ${files.length} files, ${(bytes / 1048576).toFixed(1)} MiB`);
}

module.exports = { validate };
if (require.main === module)
  validate({ offline: process.argv.includes('--offline'),
             pruneToCatalog: process.env.FIREBASE_RESOURCE_PRUNE_CATALOG || '' })
    .catch(error => { console.error(error.message); process.exitCode = 1; });
