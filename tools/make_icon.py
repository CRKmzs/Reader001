# -*- coding: utf-8 -*-
"""生成 Windows 图标 app.ico（只用 Python 标准库，无需 Pillow）
用法：python tools/make_icon.py exe/app.ico [--png preview.png] [--size 256]
图案：蓝色圆角方块 + 一本摊开的白色书（书脊在正中）+ 书页上的深蓝色字母 R
说明：16/24 像素只画书本（R 在该尺寸下看不清），32 像素及以上才叠加 R。"""
import math
import os
import struct
import sys
import zlib

BG_TOP = (74.0, 150.0, 240.0)
BG_BOT = (18.0, 84.0, 176.0)
PAGE = (255.0, 255.0, 255.0)
SPINE = (176.0, 208.0, 246.0)
COVER = (13.0, 62.0, 130.0)
INK = (16.0, 74.0, 152.0)
BG_RADIUS = 0.24

# 书页：左页从外缘 PAGE_OUTER 到书脊 0.5，上缘是二次贝塞尔曲线（书口在书脊处下凹）
PAGE_OUTER = 0.115
PAGE_INNER = 0.500
PAGE_TOP_OUTER = 0.248
PAGE_TOP_CTRL = 0.228
PAGE_TOP_INNER = 0.292
PAGE_CORNER = 0.018
SPINE_HALF = 0.008

# 书封（深蓝）：比书页略宽一圈、下方更厚，做出书的厚度
COVER_GROW_X = 0.008
COVER_GROW_TOP = 0.006
COVER_GROW_BOTTOM = 0.028

# 字母 R 的字面框（居中压在书页上）；字形本身按「cap 高 = 1」设计后等比缩放
R_TOP = 0.340
R_BOTTOM = 0.610
R_GLYPH_W = 0.895     # 字形宽度（相对 cap 高）；Arial Bold 的 R 实测 0.899
R_STROKE = 0.200      # 竖干、碗右侧、碗底厚度；Arial Bold 实测 0.199
R_TOP_STROKE = 0.170  # 碗上缘厚度；Arial Bold 实测 0.167
R_BOWL_W = 0.810      # 碗的右外缘；Arial Bold 实测 0.812
R_BOWL_BOTTOM = 0.600  # 碗下缘（斜腿起点）高度；Arial Bold 实测约 0.55-0.60
R_CORNER = 0.190      # 碗右上、右下的外圆角半径
R_LEG_DX = 0.680      # 斜腿的「水平/垂直」比；Arial Bold 实测约 0.71
_half = (R_BOTTOM - R_TOP) * R_GLYPH_W / 2.0
R_LEFT = 0.5 - _half
R_RIGHT = 0.5 + _half

SHOW_R_MIN_SIZE = 32


def lerp(a, b, t):
    return a + (b - a) * t


def in_rrect(x, y, box, r):
    x0, y0, x1, y1 = box
    if x < x0 or x > x1 or y < y0 or y > y1:
        return False
    dx = max(x0 + r - x, x - (x1 - r), 0.0)
    dy = max(y0 + r - y, y - (y1 - r), 0.0)
    return dx * dx + dy * dy <= r * r


def _quad(p0, p1, p2, t):
    u = 1.0 - t
    return u * u * p0 + 2.0 * u * t * p1 + t * t * p2


def page_top_y(x):
    """上缘贝塞尔曲线在 x 处的 y（x 为左页坐标，0.113 .. 0.5）"""
    x0, x1, x2 = PAGE_OUTER, 0.300, PAGE_INNER
    y0, y1, y2 = PAGE_TOP_OUTER, PAGE_TOP_CTRL, PAGE_TOP_INNER
    a = x0 - 2.0 * x1 + x2
    b = 2.0 * (x1 - x0)
    c = x0 - x
    if abs(a) < 1e-12:
        t = -c / b if abs(b) > 1e-12 else 0.0
    else:
        disc = max(b * b - 4.0 * a * c, 0.0)
        sq = math.sqrt(disc)
        t1 = (-b + sq) / (2.0 * a)
        t2 = (-b - sq) / (2.0 * a)
        t = t1 if 0.0 <= t1 <= 1.0 else t2
    t = min(1.0, max(0.0, t))
    return _quad(y0, y1, y2, t)


def in_page(x, y, grow_x=0.0, grow_top=0.0, grow_bottom=0.0):
    """书页剪影：左右页在 x=0.5 相接，外缘两个角做圆角。
    grow_* 用于画书封（比书页略宽、下方更厚）。"""
    if x > PAGE_INNER:
        x = 1.0 - x
    outer = PAGE_OUTER - grow_x
    if x < outer or x > PAGE_INNER:
        return False
    yt = page_top_y(x) - grow_top
    yb = 1.0 - page_top_y(x) + grow_bottom
    if y < yt or y > yb:
        return False
    if x < outer + PAGE_CORNER:
        cx = outer + PAGE_CORNER
        if y < yt + PAGE_CORNER:
            return (x - cx) ** 2 + (y - (yt + PAGE_CORNER)) ** 2 <= PAGE_CORNER ** 2
        if y > yb - PAGE_CORNER:
            return (x - cx) ** 2 + (y - (yb - PAGE_CORNER)) ** 2 <= PAGE_CORNER ** 2
    return True


def in_cover(x, y):
    return in_page(x, y, COVER_GROW_X, COVER_GROW_TOP, COVER_GROW_BOTTOM)


def in_spine(x):
    return abs(x - PAGE_INNER) <= SPINE_HALF


def _arc(cx, cy, r, a0, a1, steps=10):
    pts = []
    for i in range(steps + 1):
        a = a0 + (a1 - a0) * i / float(steps)
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def _pt_on_line(p, d, y):
    return (p[0] + d[0] * (y - p[1]) / d[1], y)


def r_polygons():
    """标准无衬线 R 的字形轮廓（cap 高 = 1，y 向下）。
    返回 (实心多边形列表, 挖空多边形列表)：竖干 + 圆碗 + 斜腿，碗内挖出字腔。"""
    t = R_STROKE
    ts = R_TOP_STROKE
    w = R_GLYPH_W
    bw = R_BOWL_W
    m = R_BOWL_BOTTOM
    c = R_CORNER
    stem = [(0.0, 0.0), (t, 0.0), (t, 1.0), (0.0, 1.0)]
    bowl = [(t, 0.0)]
    bowl += _arc(bw - c, c, c, -math.pi / 2.0, 0.0)        # 右上外圆角
    bowl.append((bw, m - c))
    bowl += _arc(bw - c, m - c, c, 0.0, math.pi / 2.0)     # 右下外圆角
    bowl.append((t, m))
    r_in = (m - t - ts) / 2.0                              # 字腔右侧圆头半径
    hole = [(t, ts)]
    hole += _arc(bw - t - r_in, ts + r_in, r_in, -math.pi / 2.0, math.pi / 2.0)
    hole.append((t, m - t))
    d = (R_LEG_DX, 1.0)
    ln = math.hypot(d[0], d[1])
    off = (d[1] / ln * t, -d[0] / ln * t)                  # 垂直于斜腿、长度 = 笔画厚度
    anchor = (bw - c, m)                                   # 碗下缘右端 = 斜腿右边缘的起点
    left_line = (anchor[0] - off[0], anchor[1] - off[1])
    leg = [_pt_on_line(left_line, d, m), _pt_on_line(anchor, d, m),
           _pt_on_line(anchor, d, 1.0), _pt_on_line(left_line, d, 1.0)]
    return ([stem, bowl, leg], [hole])


_R_GEOMETRY = None


def _r_geometry():
    global _R_GEOMETRY
    if _R_GEOMETRY is None:
        _R_GEOMETRY = r_polygons()
    return _R_GEOMETRY


def _poly_contains(poly, x, y):
    inside = False
    j = len(poly) - 1
    for i in range(len(poly)):
        xi, yi = poly[i]
        xj, yj = poly[j]
        if (yi > y) != (yj > y):
            if x < (xj - xi) * (y - yi) / (yj - yi) + xi:
                inside = not inside
        j = i
    return inside


def r_icon_box():
    """R 在图标坐标系里的字面框 (x0, y0, x1, y1)"""
    return (R_LEFT, R_TOP, R_RIGHT, R_BOTTOM)


def in_r_glyph(x, y):
    """深蓝色字母 R：竖干 + 圆碗（挖出字腔）+ 斜腿"""
    if x < R_LEFT - 0.012 or x > R_RIGHT + 0.012:
        return False
    if y < R_TOP - 0.012 or y > R_BOTTOM + 0.012:
        return False
    gx = (x - R_LEFT) / (R_RIGHT - R_LEFT) * R_GLYPH_W
    gy = (y - R_TOP) / (R_BOTTOM - R_TOP)
    solids, holes = _r_geometry()
    for h in holes:
        if _poly_contains(h, gx, gy):
            return False
    for s in solids:
        if _poly_contains(s, gx, gy):
            return True
    return False


def render(size):
    ss = 6 if size <= 32 else (4 if size <= 64 else 2)
    W = size * ss
    rows = []
    show_r = size >= SHOW_R_MIN_SIZE
    for py in range(W):
        row = []
        y = (py + 0.5) / W
        for px in range(W):
            x = (px + 0.5) / W
            col = None
            if in_rrect(x, y, (0.0, 0.0, 1.0, 1.0), BG_RADIUS):
                col = (lerp(BG_TOP[0], BG_BOT[0], y),
                       lerp(BG_TOP[1], BG_BOT[1], y),
                       lerp(BG_TOP[2], BG_BOT[2], y))
                if in_cover(x, y):
                    col = COVER
                    if in_page(x, y):
                        col = PAGE
                        if in_spine(x):
                            col = SPINE
                        if show_r and in_r_glyph(x, y):
                            col = INK
            row.append(col)
        rows.append(row)
    return rows, W, ss


def downsample(rows, W, size, ss):
    out = []
    n = float(ss * ss)
    for y in range(size):
        orow = []
        for x in range(size):
            r = g = b = 0.0
            a = 0.0
            for j in range(ss):
                srow = rows[y * ss + j]
                for i in range(ss):
                    c = srow[x * ss + i]
                    if c is not None:
                        r += c[0]
                        g += c[1]
                        b += c[2]
                        a += 1.0
            if a == 0.0:
                orow.append((0, 0, 0, 0))
            else:
                orow.append((int(round(r / a)), int(round(g / a)), int(round(b / a)),
                             int(round(a / n * 255.0))))
        out.append(orow)
    return out


def render_pixels(size):
    rows, W, ss = render(size)
    return downsample(rows, W, size, ss)


def image_blob(size, pix):
    """32bpp BGRA（自下而上）+ 1bpp AND 掩码"""
    xor = bytearray()
    for y in range(size - 1, -1, -1):
        for (r, g, b, a) in pix[y]:
            xor += bytes((b, g, r, a))
    stride = ((size + 31) // 32) * 4
    mask = bytearray()
    for y in range(size - 1, -1, -1):
        bits = bytearray(stride)
        for x in range(size):
            if pix[y][x][3] < 128:
                bits[x >> 3] |= (0x80 >> (x & 7))
        mask += bits
    header = struct.pack('<IiiHHIIiiII', 40, size, size * 2, 1, 32, 0,
                         len(xor) + len(mask), 0, 0, 0, 0)
    return bytes(header) + bytes(xor) + bytes(mask)


def build_ico(sizes):
    blobs = []
    for size in sizes:
        pix = render_pixels(size)
        blobs.append((size, image_blob(size, pix)))
    head = struct.pack('<HHH', 0, 1, len(blobs))
    entries = b''
    offset = 6 + 16 * len(blobs)
    data = b''
    for (size, blob) in blobs:
        entries += struct.pack('<BBBBHHII', size % 256, size % 256, 0, 0, 1, 32, len(blob), offset)
        offset += len(blob)
        data += blob
    return head + entries + data


def write_png(path, size, pix):
    """写 RGBA PNG（仅用于人工预览，不进交付物）"""
    raw = bytearray()
    for row in pix:
        raw.append(0)
        for (r, g, b, a) in row:
            raw += bytes((r, g, b, a))

    def chunk(tag, data):
        return (struct.pack('>I', len(data)) + tag + data
                + struct.pack('>I', zlib.crc32(tag + data) & 0xFFFFFFFF))

    png = b'\x89PNG\r\n\x1a\n'
    png += chunk(b'IHDR', struct.pack('>IIBBBBB', size, size, 8, 6, 0, 0, 0))
    png += chunk(b'IDAT', zlib.compress(bytes(raw), 9))
    png += chunk(b'IEND', b'')
    outdir = os.path.dirname(os.path.abspath(path))
    if outdir and not os.path.isdir(outdir):
        os.makedirs(outdir)
    with open(path, 'wb') as f:
        f.write(png)


def main():
    out = 'app.ico'
    preview = None
    preview_size = 256
    args = sys.argv[1:]
    i = 0
    while i < len(args):
        if args[i] == '--png' and i + 1 < len(args):
            preview = args[i + 1]
            i += 2
            continue
        if args[i] == '--size' and i + 1 < len(args):
            preview_size = int(args[i + 1])
            i += 2
            continue
        out = args[i]
        i += 1
    sizes = [16, 24, 32, 48, 64, 128, 256]
    data = build_ico(sizes)
    outdir = os.path.dirname(os.path.abspath(out))
    if outdir and not os.path.isdir(outdir):
        os.makedirs(outdir)
    with open(out, 'wb') as f:
        f.write(data)
    print('已生成 %s (%d 字节, %s)' % (out, len(data), ','.join(str(s) for s in sizes)))
    if preview:
        write_png(preview, preview_size, render_pixels(preview_size))
        print('已生成预览 %s (%d 像素)' % (preview, preview_size))


if __name__ == '__main__':
    main()
