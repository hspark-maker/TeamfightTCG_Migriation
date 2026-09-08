from pathlib import Path
from PIL import Image, ImageChops, ImageDraw, ImageFilter

rgb = Image.open('output/balloonpeng-cutout-extended/source-limbs.png').convert('RGB')
r, g, b = rgb.split()
darkest = ImageChops.darker(ImageChops.darker(r, g), b)
barriers = darkest.point(lambda value: 255 if value < 205 else 0)
ImageDraw.floodfill(barriers, (0, 0), 128)
alpha = barriers.point(lambda value: 0 if value == 128 else 255).filter(ImageFilter.GaussianBlur(0.35))
rgba = rgb.convert('RGBA')
rgba.putalpha(alpha)
dest = Path('Assets/ArtPrototypes/BalloonPengIsometric/Cutout/BalloonPeng_LimbsExtended.png')
rgba.save(dest)
print('saved', dest, rgba.size, 'alpha', alpha.getextrema())
preview = Image.new('RGBA', rgba.size, (143,168,110,255))
preview.alpha_composite(rgba)
preview.convert('RGB').save('output/balloonpeng-cutout-extended/limbs-preview.png')
