from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter
import shutil

root = Path(__file__).resolve().parents[2]
folder = root / 'Assets/Assets/Images/Cards/Silhouettes'
backup = root / 'output/imagegen/pre-cleanup'
backup.mkdir(exist_ok=True)
done = []
for path in sorted(folder.glob('*.png')):
    saved = backup / path.name
    if saved.exists():
        continue
    shutil.copy2(path, saved)
    source = Image.open(path).convert('RGB')
    mask = source.convert('L').point(lambda v: 255 if v < 100 else 0)
    if 'Mapletail' in path.name:
        # Fill only the unwanted enclosed spiral, leaving the three leaf holes.
        ImageDraw.floodfill(mask, (round(mask.width * .691), round(mask.height * .575)), 255)
    if any(name in path.name for name in ('Crackmon', 'DizzyOctopus', 'Honeybee')):
        mask = mask.filter(ImageFilter.MaxFilter(5)).filter(ImageFilter.MinFilter(5))
    mask = mask.filter(ImageFilter.GaussianBlur(.35))
    foreground = Image.new('RGB', source.size, '#20202A')
    background = Image.new('RGB', source.size, '#B0B0B0')
    Image.composite(foreground, background, mask).save(path, optimize=True)
    done.append(path.name)
print(f'Finished {len(done)} images; pre-cleanup copies retained in output/imagegen/pre-cleanup.')
