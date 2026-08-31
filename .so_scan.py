import os, re, io, sys

SO_ROOT = 'Assets/SO'
SCAN_EXT = ('.asset', '.unity', '.prefab', '.controller', '.mat', '.playable', '.spriteatlas')

guid_to_path = {}
so_files = []
for dirpath, _, filenames in os.walk(SO_ROOT):
    for name in filenames:
        if not name.endswith('.asset'):
            continue
        path = os.path.join(dirpath, name).replace(os.sep, '/')
        so_files.append(path)
        meta = path + '.meta'
        if not os.path.exists(meta):
            continue
        text = io.open(meta, encoding='utf-8', errors='ignore').read()
        found = re.search(r'^guid:\s*([0-9a-f]{32})', text, re.M)
        if found:
            guid_to_path[found.group(1)] = path

# 참조 수집: SO 자신을 뺀 모든 직렬화 자산 + ProjectSettings(preloaded assets)
referenced = {}
pattern = re.compile(r'[0-9a-f]{32}')
so_set = set(so_files)


def scan(path):
    try:
        text = io.open(path, encoding='utf-8', errors='ignore').read()
    except Exception:
        return
    for guid in pattern.findall(text):
        if guid in guid_to_path:
            referenced.setdefault(guid, set()).add(path)


for root in ('Assets', 'ProjectSettings'):
    for dirpath, _, filenames in os.walk(root):
        for name in filenames:
            if not name.endswith(SCAN_EXT):
                continue
            path = os.path.join(dirpath, name).replace(os.sep, '/')
            if path in so_set:
                # SO끼리의 참조는 세되, 자기 자신은 제외(아래에서 걸러짐)
                pass
            scan(path)

unused = []
for guid, path in sorted(guid_to_path.items(), key=lambda kv: kv[1]):
    refs = referenced.get(guid, set()) - {path}
    if not refs:
        unused.append(path)

print('SO 총 %d개 / 참조 없음 %d개' % (len(so_files), len(unused)))
print('--- 미참조 ---')
for path in unused:
    print(path)
