"""Découpe le bouclier de la maquette (1.jpg) et rend le fond transparent."""
import sys
from collections import deque

import numpy as np
from PIL import Image, ImageFilter

source, out_dir = sys.argv[1], sys.argv[2]
image = Image.open(source).convert('RGB')
pixels = np.asarray(image).astype(int)
r, g, b = pixels[..., 0], pixels[..., 1], pixels[..., 2]
luma = 0.299 * r + 0.587 * g + 0.114 * b
saturation = pixels.max(axis=2) - pixels.min(axis=2)

# Fond : gris sombre peu saturé (y compris le filigrane et l'ombre portée du bouclier).
background = (luma < 75) & (saturation < 28)

# Zone du grand bouclier, avec une marge.
x0, y0, x1, y1 = 120, 270, 720, 950
h, w = y1 - y0, x1 - x0
region = background[y0:y1, x0:x1]

# Extérieur = fond relié aux bords de la zone (remplissage par diffusion).
outside = np.zeros((h, w), dtype=bool)
queue = deque()
for x in range(w):
    for y in (0, h - 1):
        if region[y, x] and not outside[y, x]:
            outside[y, x] = True
            queue.append((y, x))
for y in range(h):
    for x in (0, w - 1):
        if region[y, x] and not outside[y, x]:
            outside[y, x] = True
            queue.append((y, x))
while queue:
    y, x = queue.popleft()
    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        ny, nx = y + dy, x + dx
        if 0 <= ny < h and 0 <= nx < w and region[ny, nx] and not outside[ny, nx]:
            outside[ny, nx] = True
            queue.append((ny, nx))

alpha = Image.fromarray(np.where(outside, 0, 255).astype(np.uint8))
# Bord adouci : un léger flou, puis on resserre pour ne pas garder de halo sombre.
alpha = alpha.filter(ImageFilter.MinFilter(3)).filter(ImageFilter.GaussianBlur(1.2))

logo = image.crop((x0, y0, x1, y1)).convert('RGBA')
logo.putalpha(alpha)
box = logo.getbbox()
logo = logo.crop(box)

# Carré, avec une petite marge, pour l'icône.
side = max(logo.size) + 16
square = Image.new('RGBA', (side, side), (0, 0, 0, 0))
square.paste(logo, ((side - logo.width) // 2, (side - logo.height) // 2), logo)
square.save(f'{out_dir}/logo-source.png')

for size in (512, 256, 128, 96, 64, 48):
    square.resize((size, size), Image.LANCZOS).save(f'{out_dir}/logo-{size}.png')

icon_sizes = [(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)]
square.resize((256, 256), Image.LANCZOS).save(f'{out_dir}/OnionGuard.ico', sizes=icon_sizes)

# Aperçu sur fond clair et sur fond sombre, pour contrôler le détourage.
preview = Image.new('RGBA', (2 * 300, 300), (255, 255, 255, 255))
preview.paste((30, 31, 35, 255), (300, 0, 600, 300))
small = square.resize((280, 280), Image.LANCZOS)
preview.paste(small, (10, 10), small)
preview.paste(small, (310, 10), small)
preview.save(f'{out_dir}/preview.png')
print('logo', logo.size, 'carré', square.size)
