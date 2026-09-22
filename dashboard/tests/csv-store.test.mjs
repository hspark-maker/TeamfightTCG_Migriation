import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp, readFile, readdir, rm, symlink, writeFile} from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {createCsvStore} from '../server/csv-store.mjs';

const standard = 'id,name,power,total\nint,string,int,long\n1,첫째,3,9007199254740991\n2,둘째,-4,0\n';
async function fixture(t, text = standard) {
  const directory = await mkdtemp(path.join(os.tmpdir(), 'dashboard-csv-'));
  t.after(() => rm(directory, {recursive: true, force: true}));
  await writeFile(path.join(directory, 'Card_sheet.csv'), text);
  const store = createCsvStore({directory});
  return {directory, store, original: text, file: path.join(directory, 'Card_sheet.csv')};
}
const edit = (baseHash, value, column = 'power', rowId = '1') => ({table: 'Card', baseHash,
  changes: [{rowId, column, value}]});
const code = (expected) => (error) => error.code === expected;

test('list and read support both header formats while enum remains read only', async (t) => {
  const {directory, store} = await fixture(t);
  await writeFile(path.join(directory, 'Description_sheet.csv'), '고유 ID,이름\r\nid,name\r\nint,string\r\n1,테스트\r\n');
  await writeFile(path.join(directory, 'enum_sheet.csv'), '설명,값\nKEY,value:KEY\nNone,0\n');
  await writeFile(path.join(directory, 'not-a-sheet.csv'), 'ignored');
  const {tables} = await store.list();
  assert.deepEqual(tables.map((table) => table.name), ['Card', 'Description', 'enum']);
  assert.equal(tables[0].rows, 2);
  assert.equal(tables[0].columns, 4);
  assert.equal(tables[2].editable, false);
  const description = await store.read('Description');
  assert.equal(description.columns[1].description, '이름');
  const enums = await store.read('enum');
  assert.equal(enums.editable, false);
  assert.ok(enums.reason);
  await assert.rejects(store.save({table: 'enum', baseHash: enums.hash, changes: []}), code('READ_ONLY'));
});

test('Unicode, BOM, CRLF, escaped quotes and multiline cells round trip without touching other bytes', async (t) => {
  const original = '\ufeff"ID 설명",이름,수치,설명\r\nid,name,power,description\r\nint,string,int,string\r\n001,"한글 😀",+003,"첫 줄\r\n둘째 ""인용"""\r\n2,다음,4,끝\r\n\r\n';
  const {store, file} = await fixture(t, original);
  const current = await store.read('Card');
  assert.equal(current.editable, true);
  assert.equal(current.rows[0].cells[3], '첫 줄\r\n둘째 "인용"');
  assert.equal(current.rows[0].id, '1');
  const input = edit(current.hash, '바뀐, "설명"\n새 줄 😀', 'description');
  const preview = await store.preview(input);
  assert.equal(await readFile(file, 'utf8'), original);
  assert.equal(preview.changedCells, 1);
  assert.equal(preview.changes[0].before, '첫 줄\r\n둘째 "인용"');
  const saved = await store.save(input);
  const expected = original.replace('"첫 줄\r\n둘째 ""인용"""', '"바뀐, ""설명""\n새 줄 😀"');
  assert.deepEqual(await readFile(file), Buffer.from(expected));
  assert.equal(saved.hash, preview.nextHash);
  assert.equal(saved.rows[0].cells[3], input.changes[0].value);
});

test('no-op preserves exact bytes and excludes unchanged cells from preview', async (t) => {
  const {store, file, original} = await fixture(t);
  const {hash} = await store.read('Card');
  const preview = await store.preview(edit(hash, '3'));
  assert.equal(preview.changedCells, 0);
  assert.equal(preview.nextHash, hash);
  assert.equal((await store.save(edit(hash, '3'))).changedCells, 0);
  assert.equal(await readFile(file, 'utf8'), original);
});

test('integer constraints, IDs, unknown cells and duplicate edits fail without writes', async (t) => {
  const {store, file, original} = await fixture(t);
  const {hash} = await store.read('Card');
  for (const value of ['', '2.5', '3e2', 'Infinity', '2147483648', '-2147483649', '  ']) {
    await assert.rejects(store.save(edit(hash, value)), code('INVALID_INPUT'));
  }
  await assert.rejects(store.save(edit(hash, '9007199254740992', 'total')), code('INVALID_INPUT'));
  await assert.rejects(store.save(edit(hash, '2', 'id')), code('READ_ONLY'));
  await assert.rejects(store.save(edit(hash, '2', 'missing')), code('INVALID_INPUT'));
  await assert.rejects(store.save(edit(hash, '2', 'power', '99')), code('INVALID_INPUT'));
  await assert.rejects(store.save(edit(hash, '\ud800', 'name')), code('INVALID_INPUT'));
  const duplicate = edit(hash, '6');
  duplicate.changes.push({...duplicate.changes[0]});
  await assert.rejects(store.save(duplicate), code('INVALID_INPUT'));
  await assert.rejects(store.save({...duplicate, changes: Array(1001).fill(duplicate.changes[0])}), code('INVALID_INPUT'));
  assert.equal(await readFile(file, 'utf8'), original);
  assert.equal((await store.preview(edit(hash, ' +2147483647 '))).changedCells, 1);
});

test('stale preview and save hashes preserve externally changed files', async (t) => {
  const {store, file} = await fixture(t);
  const {hash} = await store.read('Card');
  const external = standard.replace('첫째', '외부 수정');
  await writeFile(file, external);
  await assert.rejects(store.preview(edit(hash, '7')), code('CONFLICT'));
  await assert.rejects(store.save(edit(hash, '7')), code('CONFLICT'));
  assert.equal(await readFile(file, 'utf8'), external);
});

test('concurrent same-base saves across store instances allow exactly one winner', async (t) => {
  const {store, directory, file} = await fixture(t);
  const {hash} = await store.read('Card');
  const secondStore = createCsvStore({directory});
  const results = await Promise.allSettled([store.save(edit(hash, '7')), secondStore.save(edit(hash, '8'))]);
  assert.equal(results.filter((result) => result.status === 'fulfilled').length, 1);
  assert.equal(results.find((result) => result.status === 'rejected').reason.code, 'CONFLICT');
  const winner = results.find((result) => result.status === 'fulfilled').value;
  assert.equal((await store.read('Card')).hash, winner.hash);
  assert.equal(await readFile(file, 'utf8'), standard.replace('첫째,3', `첫째,${winner.rows[0].cells[2]}`));
  assert.deepEqual(await readdir(directory), ['Card_sheet.csv']);
});

test('traversal, arbitrary paths and missing files cannot be accessed', async (t) => {
  const {store} = await fixture(t);
  for (const name of ['../Card', 'Card_sheet.csv', '/Card', 'C:\\Card', 'Card/Other', '', 'Card\0']) {
    await assert.rejects(store.read(name), code('INVALID_INPUT'));
  }
  await assert.rejects(store.read('Missing'), code('NOT_FOUND'));
  await assert.rejects(store.preview(edit('invalid', '2')), code('INVALID_INPUT'));
});

test('symlink files and directory junction escapes are refused', async (t) => {
  const {store, directory} = await fixture(t);
  const outside = await mkdtemp(path.join(os.tmpdir(), 'dashboard-csv-outside-'));
  t.after(() => rm(outside, {recursive: true, force: true}));
  const outsideFile = path.join(outside, 'Card_sheet.csv');
  await writeFile(outsideFile, standard);
  const junction = path.join(directory, 'escape');
  await symlink(outside, junction, process.platform === 'win32' ? 'junction' : 'dir');
  const escaped = createCsvStore({directory: junction});
  await assert.rejects(escaped.read('Card'), code('READ_ONLY'));
  await assert.rejects(escaped.list(), code('READ_ONLY'));
  try {
    await symlink(outsideFile, path.join(directory, 'Linked_sheet.csv'), 'file');
  } catch (error) {
    if (error.code !== 'EPERM') throw error;
    t.diagnostic('파일 심볼릭 링크 생성 권한 없음: 폴더 junction 차단은 검증됨');
    return;
  }
  await assert.rejects(store.read('Linked'), code('READ_ONLY'));
  assert.equal((await store.list()).tables.find((table) => table.name === 'Linked').editable, false);
  assert.equal(await readFile(outsideFile, 'utf8'), standard);
});

test('malformed schema, invalid existing values and ambiguous IDs remain read only', async (t) => {
  const {store, file} = await fixture(t);
  const invalid = [
    'id,name,name\nint,string,string\n1,a,b\n',
    'id,power\nint,float\n1,1.5\n',
    'id,power\nint,int\n1,bad\n',
    'id,name\nint,string\n1,a\n01,b\n',
    'id,name\nint,string\n0,a\n',
    'id,name\nint,string\n1,a,b\n',
    'id,name\nint,string\n1,"unclosed\n',
    'id,name\nint,string\n1,a"b\n',
    'id,name\nid,string\n1,a\n',
  ];
  for (const text of invalid) {
    await writeFile(file, text);
    const current = await store.read('Card');
    assert.equal(current.editable, false, text);
    await assert.rejects(store.save(edit(current.hash, '3', 'name')), code('READ_ONLY'));
    assert.equal(await readFile(file, 'utf8'), text);
  }
});

test('multiple replacements preserve headers and generated neighbor sentinels', async (t) => {
  const {store, directory, file} = await fixture(t);
  const sentinels = ['SpecData.bytes', 'SpecDatas.cs', 'SpecDataManager.cs'];
  for (const name of sentinels) await writeFile(path.join(directory, name), 'DO NOT TOUCH');
  const {hash} = await store.read('Card');
  const saved = await store.save({table: 'Card', baseHash: hash, changes: [
    {rowId: '2', column: 'name', value: '길어진 이름'}, {rowId: '1', column: 'power', value: '-2147483648'},
  ]});
  assert.equal(saved.changedCells, 2);
  assert.equal(saved.rows.length, 2);
  assert.equal(await readFile(file, 'utf8'), standard.replace('둘째', '길어진 이름').replace('첫째,3', '첫째,-2147483648'));
  for (const name of sentinels) assert.equal(await readFile(path.join(directory, name), 'utf8'), 'DO NOT TOUCH');
  assert.deepEqual((await readdir(directory)).sort(), ['Card_sheet.csv', ...sentinels].sort());
});
