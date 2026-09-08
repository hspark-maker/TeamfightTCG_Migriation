from pathlib import Path
from PIL import Image, ImageChops, ImageDraw, ImageFilter

source = Path('output/balloonpeng-cutout/source-atlas.png')
destination = Path('Assets/ArtPrototypes/BalloonPengIsometric/Cutout/BalloonPeng_Parts.png')
rgb = Image.open(source).convert('RGB')
r, g, b = rgb.split()
darkest = ImageChops.darker(ImageChops.darker(r, g), b)
# The dark closed ink outlines are barriers. Flood only exterior checkerboard,
# preserving the enclosed white belly, eye highlights and pale scarf highlights.
barriers = darkest.point(lambda value: 255 if value < 205 else 0)
ImageDraw.floodfill(barriers, (0, 0), 128)
alpha = barriers.point(lambda value: 0 if value == 128 else 255)
alpha = alpha.filter(ImageFilter.GaussianBlur(0.35))
rgba = rgb.convert('RGBA')
rgba.putalpha(alpha)
destination.parent.mkdir(parents=True, exist_ok=True)
rgba.save(destination)
print('saved', destination, rgba.size, 'alpha', alpha.getextrema())
for name, rect in [('Body',(0,0,512,640)),('WingFar',(512,0,1024,512)),('WingNear',(1024,0,1536,512)),('FootFar',(0,640,512,1024)),('FootNear',(512,512,1024,1024))]:
    print(name, alpha.crop(rect).getbbox())
