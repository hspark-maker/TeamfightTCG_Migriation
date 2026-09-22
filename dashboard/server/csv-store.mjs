import {createHash, randomUUID} from 'node:crypto';
import {lstat, readFile, readdir, realpath, rename, unlink, writeFile} from 'node:fs/promises';
import path from 'node:path';

const locks = new Map();
const digest = (bytes) => createHash('sha256').update(bytes).digest('hex');
const fail = (code, message) => Object.assign(new Error(message), {code});
const pathKey = (value) => process.platform === 'win32' ? value.toLowerCase() : value;
const tablePattern = /^[A-Za-z][A-Za-z0-9_]*$/;

// Keep source spans, including original quoting, so an edit never rewrites other cells.
function parseCsv(text) {
  const records = [];
  let index = text.charCodeAt(0) === 0xfeff ? 1 : 0;
  while (index < text.length) {
    const record = [];
    while (true) {
      const start = index;
      let value = '';
      if (text[index] === '"') {
        index++;
        let closed = false;
        while (index < text.length) {
          if (text[index] !== '"') value += text[index++];
          else if (text[index + 1] === '"') { value += '"'; index += 2; }
          else { index++; closed = true; break; }
        }
        if (!closed || (index < text.length && !',\r\n'.includes(text[index]))) {
          throw fail('READ_ONLY', 'CSV 따옴표 형식이 올바르지 않습니다.');
        }
      } else {
        while (index < text.length && !',\r\n'.includes(text[index])) {
          if (text[index] === '"') throw fail('READ_ONLY', 'CSV 셀 내부 따옴표 형식이 올바르지 않습니다.');
          value += text[index++];
        }
      }
      record.push({value, start, end: index});
      if (text[index] === ',') { index++; continue; }
      if (text[index] === '\r') {
        if (text[index + 1] !== '\n') throw fail('READ_ONLY', '단독 CR 줄바꿈은 지원하지 않습니다.');
        index += 2;
      } else if (text[index] === '\n') index++;
      break;
    }
    records.push(record);
  }
  return records;
}

function validateValue(value, type, label) {
  if (type === 'string') return;
  const number = Number(value);
  if (!/^[+-]?\d+$/.test(value.trim()) || !Number.isSafeInteger(number) ||
      (type === 'int' && (number < -2147483648 || number > 2147483647))) {
    throw fail('INVALID_INPUT', `${label}: ${type === 'int' ? '32비트 정수' : '안전한 범위의 정수'}가 필요합니다.`);
  }
}

function inspect(table, bytes) {
  const file = `${table}_sheet.csv`;
  const result = {name: table, file, hash: digest(bytes), columns: [], rows: [], editable: false};
  let text;
  try {
    text = new TextDecoder('utf-8', {fatal: true, ignoreBOM: true}).decode(bytes);
    const records = parseCsv(text);
    const candidates = records.slice(0, 2).map((row, i) => row[0]?.value === 'id' ? i : -1).filter((i) => i >= 0);
    if (candidates.length !== 1) throw fail('READ_ONLY', 'id 헤더와 타입 행으로 구성된 일반 스펙 CSV만 편집할 수 있습니다.');
    const header = candidates[0];
    const names = records[header].map((cell) => cell.value);
    const types = records[header + 1]?.map((cell) => cell.value) ?? [];
    if (names.some((name) => !name.trim()) || new Set(names).size !== names.length ||
        names.length !== types.length || types[0] !== 'int' ||
        types.some((type) => !['int', 'long', 'string'].includes(type)) ||
        (header === 1 && records[0].length !== names.length)) {
      throw fail('READ_ONLY', '헤더·타입·설명 행의 스키마가 지원 형식과 다릅니다.');
    }
    result.columns = names.map((name, i) => ({name, type: types[i], description: header === 1 ? records[0][i].value : ''}));
    const data = records.slice(header + 2).filter((row) => row.some((cell) => cell.value.trim()));
    const ids = new Set();
    const rowSpans = new Map();
    for (const row of data) {
      if (row.length !== names.length) throw fail('READ_ONLY', '데이터 행의 열 수가 헤더와 다릅니다.');
      row.forEach((cell, i) => validateValue(cell.value, types[i], names[i]));
      const id = String(Number(row[0].value));
      if (Number(id) <= 0 || ids.has(id)) throw fail('READ_ONLY', 'ID는 중복 없는 양의 정수여야 합니다.');
      ids.add(id);
      result.rows.push({id, cells: row.map((cell) => cell.value)});
      rowSpans.set(id, row);
    }
    if (!data.length) throw fail('READ_ONLY', '데이터 행이 없는 CSV는 편집할 수 없습니다.');
    result.editable = true;
    return {result, text, rowSpans};
  } catch (error) {
    result.reason = error.code === 'ERR_ENCODING_INVALID_ENCODED_DATA' ? '유효한 UTF-8 CSV만 지원합니다.' : error.message;
    return {result, text};
  }
}

function prepare(snapshot, input) {
  const {result, text, rowSpans} = snapshot;
  if (input.baseHash !== result.hash) throw fail('CONFLICT', 'CSV가 변경되었습니다. 다시 불러온 뒤 수정하세요.');
  if (!result.editable) throw fail('READ_ONLY', result.reason);
  if (!Array.isArray(input.changes) || input.changes.length > 1000) {
    throw fail('INVALID_INPUT', '변경 셀은 배열이며 한 번에 최대 1,000개까지 가능합니다.');
  }
  const seen = new Set();
  const changes = [];
  const replacements = [];
  for (const change of input.changes) {
    if (!change || typeof change.rowId !== 'string' || typeof change.column !== 'string' || typeof change.value !== 'string') {
      throw fail('INVALID_INPUT', '각 변경에는 문자열 rowId, column, value가 필요합니다.');
    }
    const columnIndex = result.columns.findIndex((column) => column.name === change.column);
    const row = rowSpans.get(change.rowId);
    if (!row || columnIndex < 0) throw fail('INVALID_INPUT', '기존 데이터의 셀만 수정할 수 있습니다.');
    if (columnIndex === 0) throw fail('READ_ONLY', 'ID는 수정할 수 없습니다.');
    const key = JSON.stringify([change.rowId, change.column]);
    if (seen.has(key)) throw fail('INVALID_INPUT', '같은 셀의 중복 변경은 허용하지 않습니다.');
    seen.add(key);
    validateValue(change.value, result.columns[columnIndex].type, change.column);
    // JSON can contain lone UTF-16 surrogates; encoding them would silently replace data.
    if (Buffer.from(change.value, 'utf8').toString('utf8') !== change.value) {
      throw fail('INVALID_INPUT', '유효하지 않은 유니코드 문자열입니다.');
    }
    const cell = row[columnIndex];
    if (cell.value === change.value) continue;
    const quote = text[cell.start] === '"' || /[",\r\n]/.test(change.value);
    const encoded = quote ? `"${change.value.replaceAll('"', '""')}"` : change.value;
    replacements.push({start: cell.start, end: cell.end, encoded});
    changes.push({rowId: change.rowId, column: change.column, before: cell.value, after: change.value});
  }
  let nextText = text;
  for (const replacement of replacements.sort((a, b) => b.start - a.start)) {
    nextText = nextText.slice(0, replacement.start) + replacement.encoded + nextText.slice(replacement.end);
  }
  const bytes = Buffer.from(nextText, 'utf8');
  const next = inspect(result.name, bytes);
  if (!next.result.editable || next.result.rows.length !== result.rows.length) {
    throw fail('INVALID_INPUT', next.result.reason ?? 'CSV 행 구조를 변경할 수 없습니다.');
  }
  return {bytes, next: next.result, preview: {table: result.name, baseHash: result.hash,
    nextHash: next.result.hash, changes, changedCells: changes.length}};
}

export function createCsvStore({directory}) {
  const root = path.resolve(directory);

  async function safeRoot() {
    try {
      const stat = await lstat(root);
      if (!stat.isDirectory() || stat.isSymbolicLink() || pathKey(await realpath(root)) !== pathKey(root)) {
        throw fail('READ_ONLY', 'CSV 폴더의 심볼릭 링크·우회 경로는 허용하지 않습니다.');
      }
    } catch (error) {
      if (error.code === 'ENOENT') throw fail('NOT_FOUND', 'CSV 폴더를 찾을 수 없습니다.');
      throw error;
    }
  }

  async function safeFile(table) {
    if (typeof table !== 'string' || !tablePattern.test(table)) throw fail('INVALID_INPUT', '올바른 테이블 이름이 필요합니다.');
    await safeRoot();
    const filename = path.join(root, `${table}_sheet.csv`);
    try {
      const stat = await lstat(filename);
      if (!stat.isFile() || stat.isSymbolicLink() || pathKey(await realpath(filename)) !== pathKey(filename)) {
        throw fail('READ_ONLY', '일반 CSV 파일만 사용할 수 있습니다.');
      }
    } catch (error) {
      if (error.code === 'ENOENT') throw fail('NOT_FOUND', 'CSV 파일을 찾을 수 없습니다.');
      throw error;
    }
    return filename;
  }

  async function snapshot(table) {
    const filename = await safeFile(table);
    try { return inspect(table, await readFile(filename)); }
    catch (error) {
      if (error.code === 'ENOENT') throw fail('NOT_FOUND', 'CSV 파일을 찾을 수 없습니다.');
      throw error;
    }
  }

  function validateInput(input) {
    if (!input || typeof input !== 'object' || typeof input.baseHash !== 'string' || !/^[a-f0-9]{64}$/.test(input.baseHash)) {
      throw fail('INVALID_INPUT', '원본 CSV의 SHA-256 해시가 필요합니다.');
    }
  }

  return {
    async list() {
      await safeRoot();
      const entries = await readdir(root);
      const tables = [];
      for (const file of entries.filter((entry) => /^[A-Za-z][A-Za-z0-9_]*_sheet\.csv$/.test(entry)).sort()) {
        const name = file.slice(0, -'_sheet.csv'.length);
        try {
          const {result} = await snapshot(name);
          tables.push({name, file, rows: result.rows.length, columns: result.columns.length, editable: result.editable,
            ...(result.reason ? {error: result.reason} : {})});
        } catch (error) {
          tables.push({name, file, rows: 0, columns: 0, editable: false, error: error.message});
        }
      }
      return {tables};
    },
    async read(table) { return (await snapshot(table)).result; },
    async preview(input) {
      validateInput(input);
      return prepare(await snapshot(input.table), input).preview;
    },
    async save(input) {
      validateInput(input);
      const filename = await safeFile(input.table);
      const key = pathKey(filename);
      const previous = locks.get(key) ?? Promise.resolve();
      const task = previous.catch(() => {}).then(async () => {
        const prepared = prepare(await snapshot(input.table), input);
        if (!prepared.preview.changedCells) return {...prepared.next, changedCells: 0};
        const temporary = path.join(root, `.${input.table}.${randomUUID()}.tmp`);
        let created = false;
        try {
          await safeRoot();
          await writeFile(temporary, prepared.bytes, {flag: 'wx'});
          created = true;
          // Recheck after writing the temporary file, immediately before replacement.
          const latest = await snapshot(input.table);
          if (latest.result.hash !== input.baseHash) throw fail('CONFLICT', '저장 중 CSV가 변경되었습니다. 다시 불러오세요.');
          await rename(temporary, filename);
          created = false;
          return {...prepared.next, changedCells: prepared.preview.changedCells};
        } finally {
          if (created) await unlink(temporary).catch(() => {});
        }
      });
      locks.set(key, task);
      try { return await task; }
      finally { if (locks.get(key) === task) locks.delete(key); }
    },
  };
}
