import io
import json
import pathlib
import struct
import sys
from collections import Counter

sys.path.insert(0, str(pathlib.Path(__file__).parent / 'apk-analysis-deps'))
import lz4.block

source = pathlib.Path('Library/Bee/Android/Prj/IL2CPP/Gradle/unityLibrary/src/main/assets/bin/Data/data.unity3d')
stream = io.BytesIO(source.read_bytes())

def number(stream, fmt):
    return struct.unpack('>' + fmt, stream.read(struct.calcsize('>' + fmt)))[0]

def string(stream):
    result = bytearray()
    while (value := stream.read(1)) != b'\0':
        if not value:
            raise EOFError()
        result.extend(value)
    return result.decode('utf-8')

assert string(stream) == 'UnityFS'
version = number(stream, 'I')
string(stream)
string(stream)
total = number(stream, 'Q')
compressed_info = number(stream, 'I')
raw_info = number(stream, 'I')
flags = number(stream, 'I')
if version >= 7:
    stream.seek((stream.tell() + 15) // 16 * 16)
assert not flags & 128
info = io.BytesIO(lz4.block.decompress(stream.read(compressed_info), uncompressed_size=raw_info))
info.read(16)
blocks = [(number(info, 'I'), number(info, 'I'), number(info, 'H')) for _ in range(number(info, 'I'))]
nodes = [(number(info, 'Q'), number(info, 'Q'), number(info, 'I'), string(info)) for _ in range(number(info, 'I'))]
if flags & 512:
    stream.seek((stream.tell() + 15) // 16 * 16)
output = bytearray()
hc_total = 0
for raw_size, compressed_size, block_flags in blocks:
    data = stream.read(compressed_size)
    if block_flags & 63 in (2, 3):
        data = lz4.block.decompress(data, uncompressed_size=raw_size)
    else:
        assert block_flags & 63 == 0
    assert len(data) == raw_size
    output.extend(data)
    hc_total += min(raw_size, len(lz4.block.compress(data, mode='high_compression', compression=12, store_size=False)))
original_blocks = sum(block[1] for block in blocks)
result = {
    'sourceBytes': total,
    'rawDataBytes': len(output),
    'originalCompressedBlocks': original_blocks,
    'lz4hc12CompressedBlocks': hc_total,
    'estimatedSavingBytes': original_blocks - hc_total,
    'blockTypes': dict(Counter(block[2] & 63 for block in blocks)),
    'files': []
}
for offset, size, node_flags, name in nodes:
    data = output[offset:offset + size]
    compressed = sum(min(len(data[i:i + 131072]), len(lz4.block.compress(bytes(data[i:i + 131072]), store_size=False))) for i in range(0, size, 131072))
    result['files'].append({'name': name, 'rawBytes': size, 'lz4StandaloneEstimate': compressed})
pathlib.Path('.codex-temp/apk-compression-estimate.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps(result, indent=2))
