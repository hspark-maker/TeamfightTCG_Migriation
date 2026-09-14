const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const http = require('node:http');
const { validate } = require('./validate-resource-hosting.cjs');

async function fixture(t) {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'cardbattle-hosting-test-'));
  assert.equal(path.dirname(path.resolve(root)), path.resolve(os.tmpdir()));
  assert.ok(path.basename(root).startsWith('cardbattle-hosting-test-'));
  t.after(() => fs.rm(root, { recursive: true, force: true }));
  const origin = 'https://bm-cardbattle-assets.web.app';
  const dir = path.join(root, '1.0/Android');
  await fs.mkdir(dir, { recursive: true });
  const catalog = `${dir}/catalog_test.json`;
  await fs.writeFile(catalog, JSON.stringify({ m_InternalIds: [
    `${origin}/1.0/Android/card_012345.bundle`, '{UnityEngine.AddressableAssets.Addressables.RuntimePath}/local.bundle',
  ] }));
  await fs.writeFile(`${dir}/catalog_test.hash`, 'catalog-hash');
  await fs.writeFile(`${dir}/card_012345.bundle`, 'bundle-data');
  return { root, dir, catalog, origin, offline: true };
}

test('valid remote catalog produces manifest; build state stays private', async t => {
  const f = await fixture(t);
  await fs.writeFile(`${f.dir}/addressables_content_state.bin`, 'private');
  await validate(f);
  const manifest = JSON.parse(await fs.readFile(`${f.root}/resources-manifest.json`, 'utf8'));
  assert.equal(Object.keys(manifest.files).length, 3);
  assert.equal(manifest.files['1.0/Android/card_012345.bundle'].sha256.length, 64);
});

test('missing catalog hash blocks deployment', async t => {
  const f = await fixture(t);
  await fs.unlink(`${f.dir}/catalog_test.hash`);
  await assert.rejects(validate(f), /Missing catalog hash/);
});

test('missing referenced bundle blocks deployment', async t => {
  const f = await fixture(t);
  await fs.rename(`${f.dir}/card_012345.bundle`, `${f.dir}/unrelated.bundle`);
  await assert.rejects(validate(f), /missing resource/);
});

test('foreign URL blocks deployment', async t => {
  const f = await fixture(t);
  await fs.writeFile(f.catalog, JSON.stringify({ m_InternalIds: ['https://other.example/card.bundle'] }));
  await assert.rejects(validate(f), /Unexpected remote URL/);
});

test('empty build and unexpected public files block deployment', async t => {
  const f = await fixture(t);
  await fs.writeFile(`${f.dir}/SpecData.bytes`, 'not-public');
  await assert.rejects(validate(f), /Unexpected public file/);
  const empty = path.join(f.root, 'empty');
  await fs.mkdir(empty);
  await assert.rejects(validate({ root: empty, offline: true }), /Build Firebase Resources/);
});

test('live manifest protects old app files and immutable bundles', async t => {
  const f = await fixture(t);
  let previous;
  const server = http.createServer((req, res) => {
    res.setHeader('Content-Type', 'application/json');
    res.end(JSON.stringify(previous));
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  t.after(() => new Promise(resolve => server.close(resolve)));
  f.origin = `http://127.0.0.1:${server.address().port}`;
  await fs.writeFile(f.catalog, JSON.stringify({ m_InternalIds: [`${f.origin}/1.0/Android/card_012345.bundle`] }));
  await validate(f);
  previous = JSON.parse(await fs.readFile(`${f.root}/resources-manifest.json`, 'utf8'));
  f.offline = false;
  await validate(f);
  previous.files['0.9/Android/old.bundle'] = { bytes: 1, sha256: 'old' };
  await assert.rejects(validate(f), /기존 앱 파일/);
  delete previous.files['0.9/Android/old.bundle'];
  await fs.writeFile(`${f.dir}/card_012345.bundle`, 'changed');
  await assert.rejects(validate(f), /번들의 내용이 변경/);
});
