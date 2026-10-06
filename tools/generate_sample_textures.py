#!/usr/bin/env python3
"""Basic 샘플 임시 UI 텍스처(9-slice) 생성기.

흰색·초록빛 회색으로 된 스프라이트 4장(Panel, Card, Button, Pill)을 코드로 그린다.
색은 Unity에서 Image.color를 곱해서(tint) 입힌다. 1배율(캔버스 1920x1080 기준 텍스처 1px = 캔버스 1단위)로 만든다.

    python tools/generate_sample_textures.py

필요: Python 3.8+, Pillow 9.1+ (Image.Resampling). 실행 위치와 무관하게 저장소 기준 경로에 저장한다.
PNG만 다시 만든다. 9-slice 보더(L, B, R, T)·PPU 같은 임포트 설정은 Unity가 만든 .png.meta에 있다.
가운데 늘어나는 띠가 고르지 않은 텍스처가 하나라도 있으면(보더가 모서리·립을 다 덮지 못함) 아무것도 저장하지 않고 종료 코드 1로 끝난다.

모양 상수를 바꿔 보더가 달라지면 아래 순서로 .meta를 맞춘다. .meta는 손으로 고치지 않는다.
Samples~는 Unity가 임포트하지 않아 그 자리에서는 임포트 설정을 바꿀 수 없다.
  1. 이 스크립트를 실행한다.
  2. Package Manager(또는 Sample.FindByPackage(...).Import())로 Basic 샘플을 Import한다 (Assets/Samples/... 에 사본이 생긴다).
  3. 사본 *.png의 TextureImporter.spriteBorder를 Vector4(L, B, R, T) = 아래 *_BORDER 값으로 바꾸고 SaveAndReimport한다
     (에디터 코드나 MCP execute_code로). 이 저장소에는 2D Sprite 패키지가 없어 Sprite Editor를 쓸 수 없다.
     패키지가 있는 프로젝트에서 Sprite Editor로 고칠 때는 창이 L, T, R, B 순서이니 B와 T를 바꿔 넣는다.
  4. Unity가 만든 사본의 *.png.meta만 Samples~/Basic/Resources/CyKimScrollerBasic으로 복사한다.
  5. Import한 샘플 사본을 지운다 (커밋하지 않는다).

C# 상수와 묶인 값: PANEL_RING·CARD_LIP·BUTTON_LIP을 바꾸면
Samples~/Basic/SampleUiFactory.cs의 PANEL_EDGE·CARD_LIP·BUTTON_LIP도 같은 값으로 바꾼다.
"""

from __future__ import annotations

import math
import sys
from collections import deque
from dataclasses import dataclass
from pathlib import Path

from PIL import Image

REPO_ROOT = Path(__file__).resolve().parent.parent
OUT_DIR = REPO_ROOT / "Packages" / "com.cykim.scroller" / "Samples~" / "Basic" / "Resources" / "CyKimScrollerBasic"

# 4배로 그린 뒤 줄여 가장자리를 부드럽게 한다.
# 줄이기는 면적 평균(BOX). LANCZOS는 가장자리 밖에 알파 1~5 잔물결과 안쪽 테두리 번짐이 남는다.
SUPERSAMPLE = 4
RESAMPLE = Image.Resampling.BOX

# 배수(shade)를 초록빛 회색으로 만들 때 G 채널이 덜 어두워지는 비율. 0이면 무채색 회색.
GREEN_KEEP = 0.2

# Panel: 스크롤러 배경. 흰 면 + 안쪽 테두리. #f0f3f0 tint에서 테두리가 #dae1da 근처로 보인다.
PANEL_SIZE = (64, 64)
PANEL_RADIUS = 20
PANEL_RING = 2
PANEL_RING_SHADE = 0.908
PANEL_BORDER = (24, 24, 24, 24)  # L, B, R, T

# Card: 목록 셀·캐러셀 카드. 흰 면 + 테두리 + 아래 립.
CARD_SIZE = (64, 64)
CARD_RADIUS = 14
CARD_RING = 2
CARD_RING_SHADE = 0.90
CARD_LIP = 4
CARD_LIP_SHADE = 0.84
CARD_BORDER = (18, 22, 18, 18)  # L, B, R, T (T + B ≤ 최소 셀 높이 60)

# Button: 가운데 버튼 열. Card보다 립이 두껍고 진하다.
BUTTON_SIZE = (64, 64)
BUTTON_RADIUS = 12
BUTTON_RING = 2
BUTTON_RING_SHADE = 0.86
BUTTON_LIP = 5
BUTTON_LIP_SHADE = 0.74
BUTTON_BORDER = (14, 19, 14, 14)  # L, B, R, T (T + B ≤ 버튼 높이 40)

# Pill: 휠 피커 가운데 하이라이트. 높이 56에 늘리면 캡슐. 반투명 면 + 불투명 테두리, RGB는 흰색.
PILL_SIZE = (96, 64)
PILL_RADIUS = 28
PILL_RING = 2
PILL_FACE_ALPHA = 0.35
PILL_BORDER = (28, 28, 28, 28)  # L, B, R, T

WHITE = (1.0, 1.0, 1.0)


@dataclass(frozen=True)
class Slot:
    """둥근 사각 면 + 테두리 + (있으면) 아래 립. 립은 같은 둥근 사각을 lip만큼 아래로 내려 깐 띠라 세로 두께가 고르다."""

    name: str
    size: tuple[int, int]
    radius: float
    ring: float
    ring_shade: float
    lip: float
    lip_shade: float
    border: tuple[int, int, int, int]


@dataclass(frozen=True)
class Pill:
    name: str
    size: tuple[int, int]
    radius: float
    ring: float
    face_alpha: float
    border: tuple[int, int, int, int]


def sage_grey(k: float) -> tuple[float, float, float]:
    """밝기 배수 k를 초록빛 회색 배수로 바꾼다."""
    return (k, k + (1.0 - k) * GREEN_KEEP, k)


def lerp3(a, b, t):
    return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t)


def rounded_rect_distance(px, py, x0, y0, x1, y1, r):
    """둥근 사각까지의 부호 거리(안쪽 음수). 좌표는 위에서 아래로."""
    hx = (x1 - x0) * 0.5
    hy = (y1 - y0) * 0.5
    qx = abs(px - (x0 + hx)) - (hx - r)
    qy = abs(py - (y0 + hy)) - (hy - r)
    return math.hypot(max(qx, 0.0), max(qy, 0.0)) + min(max(qx, qy), 0.0) - r


def coverage(distance):
    """슈퍼샘플 1칸 폭으로 부드럽게 자른 덮임 정도."""
    return min(max(0.5 - distance * SUPERSAMPLE, 0.0), 1.0)


def slot_sampler(style: Slot):
    w, h = style.size
    r, ring, lip = style.radius, style.ring, style.lip
    ring_rgb = sage_grey(style.ring_shade)
    lip_rgb = sage_grey(style.lip_shade)

    def sample(x, y):
        c_base = coverage(rounded_rect_distance(x, y, 0, 0, w, h, r))
        c_cap = coverage(rounded_rect_distance(x, y, 0, 0, w, h - lip, r))
        c_face = coverage(rounded_rect_distance(x, y, ring, ring, w - ring, h - lip - ring, r - ring))
        alpha = max(c_base, c_cap)
        cap = lerp3(ring_rgb, WHITE, c_face)
        # 위·옆 가장자리는 면과 립이 겹치므로 덮임 비율로 섞어야 립 색이 새지 않는다.
        rgb = lerp3(lip_rgb, cap, c_cap / alpha) if alpha > 0.0 else cap
        return rgb, alpha

    return sample


def pill_sampler(style: Pill):
    w, h = style.size
    r, ring = style.radius, style.ring

    def sample(x, y):
        c_outer = coverage(rounded_rect_distance(x, y, 0, 0, w, h, r))
        c_inner = coverage(rounded_rect_distance(x, y, ring, ring, w - ring, h - ring, r - ring))
        return WHITE, c_outer * (1.0 + (style.face_alpha - 1.0) * c_inner)

    return sample


def render(size, sample):
    """슈퍼샘플로 그리고 premultiplied 상태로 줄인 뒤 직선 알파 RGBA로 되돌린다."""
    w, h = size
    sw, sh = w * SUPERSAMPLE, h * SUPERSAMPLE
    planes = [[0.0] * (sw * sh) for _ in range(4)]
    for sy in range(sh):
        y = (sy + 0.5) / SUPERSAMPLE
        row = sy * sw
        for sx in range(sw):
            rgb, a = sample((sx + 0.5) / SUPERSAMPLE, y)
            i = row + sx
            planes[0][i] = rgb[0] * a
            planes[1][i] = rgb[1] * a
            planes[2][i] = rgb[2] * a
            planes[3][i] = a

    small = []
    for plane in planes:
        img = Image.new("F", (sw, sh))
        img.putdata(plane)
        small.append(flatten(img.resize((w, h), RESAMPLE)))

    pixels = [None] * (w * h)
    for i in range(w * h):
        a = min(max(small[3][i], 0.0), 1.0)
        a8 = round(a * 255)
        if a8 == 0:
            continue
        rgb = tuple(min(max(small[c][i] / a, 0.0), 1.0) for c in range(3))
        pixels[i] = (*(round(v * 255) for v in rgb), a8)

    fill_transparent(pixels, w, h)
    out = Image.new("RGBA", (w, h))
    out.putdata(pixels)
    return out


def flatten(img):
    # 새 Pillow는 getdata 대신 get_flattened_data를 쓴다(getdata는 사라질 예정).
    return list(img.get_flattened_data() if hasattr(img, "get_flattened_data") else img.getdata())


def fill_transparent(pixels, w, h):
    """투명 픽셀 RGB를 가장 가까운 보이는 픽셀 색으로 채운다. bilinear 필터에서 어두운 테두리가 생기지 않게 한다."""
    queue = deque(i for i, p in enumerate(pixels) if p is not None)
    while queue:
        i = queue.popleft()
        x, y = i % w, i // w
        r, g, b, _ = pixels[i]
        for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
            if 0 <= nx < w and 0 <= ny < h:
                j = ny * w + nx
                if pixels[j] is None:
                    pixels[j] = (r, g, b, 0)
                    queue.append(j)
    for i, p in enumerate(pixels):
        if p is None:
            pixels[i] = (255, 255, 255, 0)


def check_center_uniform(name, img, border) -> bool:
    """가운데 늘어나는 띠가 고른지 본다. 고르지 않으면 보더를 키워야 한다. 고르면 True."""
    left, bottom, right, top = border
    w, h = img.size
    px = img.load()
    problems = []
    for y in range(h):
        row = {px[x, y] for x in range(left, w - right)}
        if len(row) > 1:
            problems.append(f"row {y}")
    for x in range(w):
        col = {px[x, y] for y in range(top, h - bottom)}
        if len(col) > 1:
            problems.append(f"col {x}")
    if problems:
        print(f"  오류: {name} 가운데 띠가 고르지 않다. *_BORDER를 키운다 ({', '.join(problems[:6])} ...)")
        return False
    return True


def main():
    styles = [
        Slot("Panel", PANEL_SIZE, PANEL_RADIUS, PANEL_RING, PANEL_RING_SHADE, 0, 1.0, PANEL_BORDER),
        Slot("Card", CARD_SIZE, CARD_RADIUS, CARD_RING, CARD_RING_SHADE, CARD_LIP, CARD_LIP_SHADE, CARD_BORDER),
        Slot("Button", BUTTON_SIZE, BUTTON_RADIUS, BUTTON_RING, BUTTON_RING_SHADE, BUTTON_LIP, BUTTON_LIP_SHADE,
             BUTTON_BORDER),
        Pill("Pill", PILL_SIZE, PILL_RADIUS, PILL_RING, PILL_FACE_ALPHA, PILL_BORDER),
    ]
    rendered = []
    ok = True
    for style in styles:
        sampler = slot_sampler(style) if isinstance(style, Slot) else pill_sampler(style)
        img = render(style.size, sampler)
        ok = check_center_uniform(style.name, img, style.border) and ok
        rendered.append((style, img))
    if not ok:
        # 9-slice가 깨진 텍스처가 성공처럼 남지 않게 하나도 저장하지 않는다.
        print("저장하지 않았다.")
        sys.exit(1)

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    for style, img in rendered:
        path = OUT_DIR / f"{style.name}.png"
        img.save(path, optimize=True)
        print(f"{path.relative_to(REPO_ROOT)}  {style.size[0]}x{style.size[1]}  border L,B,R,T = {style.border}")


if __name__ == "__main__":
    main()
