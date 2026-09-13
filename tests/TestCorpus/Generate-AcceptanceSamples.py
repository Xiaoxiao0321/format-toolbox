"""Deterministic generated benchmarks; no business documents or measured OCR results baked in."""
from pathlib import Path
import argparse, io, json, hashlib
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter
from reportlab.pdfgen import canvas
from reportlab.lib.utils import ImageReader

parser = argparse.ArgumentParser()
parser.add_argument('--output', default='artifacts/acceptance/generated/samples')
parser.add_argument('--scan-pages', type=int, default=40)
parser.add_argument('--reuse-large', action='store_true')
args = parser.parse_args()
root = Path(args.output).resolve(); root.mkdir(parents=True, exist_ok=True)
font = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 48)
chinese = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 46)
truth = {}
texts = {
    'english': ['Invoice number FT20260913', 'Customer: North River Engineering', 'Delivery date: September 13, 2026', 'Steel plate quantity 128 unit price 25.50', 'Total amount 3264.00 USD', 'Please verify all items before payment.'],
    'chinese': ['格式转换工具箱扫描验收', '客户名称：北方工程有限公司', '交货日期：2026年9月13日', '钢板数量128件，单价25.50元', '应付金额3264.00元', '请核对所有项目后再付款。'],
    'mixed': ['FormatToolbox OCR 2026', '项目名称：设备采购验收', 'Invoice FT20260913', '数量128件 Total 3264.00', '核对文件与页面顺序。']
}
def page(lines):
    im = Image.new('RGB', (1654, 2339), 'white'); draw = ImageDraw.Draw(im)
    for i, line in enumerate(lines):
        draw.text((110, 140+i*105), line, fill='black', font=chinese if any(ord(c)>127 for c in line) else font)
    return im
for name, lines in texts.items():
    im = page(lines); im.save(root/f'ocr-{name}.png', dpi=(200,200))
    truth[f'ocr-{name}.png'] = '\n'.join(lines)
    if name == 'mixed':
        degraded = im.rotate(1.3, fillcolor='white').filter(ImageFilter.GaussianBlur(0.65))
        rng = np.random.default_rng(20260913)
        pixels = np.asarray(degraded).astype(np.int16)
        pixels += rng.normal(0, 8, pixels.shape).astype(np.int16)
        degraded = Image.fromarray(np.clip(pixels, 0,255).astype(np.uint8))
        degraded.save(root/'ocr-degraded.jpg', quality=65, dpi=(200,200))
        truth['ocr-degraded.jpg'] = '\n'.join(lines)

frames = [page(texts['english']), page(texts['chinese']), page(texts['mixed'])]
frames[1] = frames[1].resize((1240,1754)); frames[2] = frames[2].resize((2339,1654))
frames[0].save(root/'multipage-distorted.tiff',save_all=True,append_images=frames[1:],compression='tiff_lzw',dpi=(200,200))
truth['multipage-distorted.tiff'] = '\n'.join('\n'.join(texts[k]) for k in ['english','chinese','mixed'])
# A landscape page uses a wider canvas; preserve glyph aspect ratio in normal fixtures.
frames[2] = Image.new('RGB',(2339,1654),'white');frames[2].paste(page(texts['mixed']).crop((0,0,1654,1654)),(0,0))
for compression in ['raw','tiff_lzw','tiff_adobe_deflate']:
    frames[0].save(root/f'multipage-{compression}.tiff',save_all=True,append_images=frames[1:],compression=compression,dpi=(200,200))
    truth[f'multipage-{compression}.tiff'] = '\n'.join('\n'.join(texts[k]) for k in ['english','chinese','mixed'])
rgba = Image.new('RGBA',(720,480),(20,60,120,0)); d=ImageDraw.Draw(rgba)
d.rectangle((40,40,680,440),fill=(30,160,80,170)); d.text((75,160),'WebP alpha 128',font=font,fill=(255,20,20,255))
rgba.save(root/'webp-alpha.webp',lossless=True)
page(texts['english']).save(root/'webp-lossy.webp',quality=75)
truth['webp-lossy.webp'] = '\n'.join(texts['english'])
frames[0].resize((480,680)).save(root/'webp-animated.webp',save_all=True,append_images=[frames[1].resize((480,680))],duration=180,loop=0)
(root/'webp-damaged.webp').write_bytes(b'RIFF\x10\0\0\0WEBPcorrupt')

pdf = canvas.Canvas(str(root/'ocr-scanned.pdf'), pagesize=(595.28,841.89))
for im in [page(texts[k]) for k in ['english','chinese','mixed']]:
    pdf.drawImage(ImageReader(im),0,0,595.28,841.89);pdf.showPage()
pdf.save(); truth['ocr-scanned.pdf']='\n'.join('\n'.join(texts[k]) for k in ['english','chinese','mixed'])
if args.reuse_large and (root/'large-scanned.pdf').exists():
    # Keep the costly, unchanged full-page scan fixture while regenerating smaller cases.
    args.scan_pages = 0
pdf = canvas.Canvas(str(root/('large-unused.pdf' if args.scan_pages==0 else 'large-scanned.pdf')), pagesize=(595.28,841.89),pageCompression=0)
rng=np.random.default_rng(20260913)
for i in range(args.scan_pages):
    # Unique full-page JPEG scans, rather than reusing one image object on every page.
    pixels = rng.integers(185,256,(2339,1654,3),dtype=np.uint8)
    im=Image.fromarray(pixels); d=ImageDraw.Draw(im)
    d.rectangle((70,70,1580,1000),fill='white')
    for j,line in enumerate(['Scanned archive page %03d'%(i+1)] + texts['english']):d.text((110,120+j*100),line,font=font,fill='black')
    jpg=io.BytesIO();im.save(jpg,format='JPEG',quality=92);jpg.seek(0)
    pdf.drawImage(ImageReader(jpg),0,0,595.28,841.89);pdf.showPage()
pdf.save()
if args.scan_pages==0:
    (root/'large-unused.pdf').unlink()
    from pypdf import PdfReader
    args.scan_pages=len(PdfReader(root/'large-scanned.pdf').pages)
(root/'ground-truth.json').write_text(json.dumps(truth,ensure_ascii=False,indent=2),encoding='utf-8')
manifest={'source':'generated benchmarks, seed 20260913; not real business samples','scan_pages':args.scan_pages,'files':[]}
for path in sorted(root.iterdir()):
    if path.is_file() and path.name!='manifest.json':manifest['files'].append({'name':path.name,'bytes':path.stat().st_size,'sha256':hashlib.sha256(path.read_bytes()).hexdigest()})
(root/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'directory':str(root),'files':len(manifest['files']),'large_scan_bytes':(root/'large-scanned.pdf').stat().st_size}))
