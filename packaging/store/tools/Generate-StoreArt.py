#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Generate the Melotag store artwork (offline, deterministic).

Outputs
-------
images/logo/app-tile-300x300.png
images/logo/box-art-1080x1080.png
images/poster/zh-CN/poster-720x1080.png
images/poster/ja-JP/poster-720x1080.png
images/poster/en-US/poster-720x1080.png
images/hero/super-hero-1920x1080.png

Requirements
------------
Python 3.10+ and Pillow (FreeType build).  The script never touches the network
and only reads the repository logo plus locally installed fonts.

Fonts
-----
Only fonts already installed on this machine and licensed for commercial use
are used:

* Latin copy - Noto Sans Regular/Bold (SIL OFL 1.1), shipped with Windows.
* Chinese and Japanese copy - Source Han Sans SC Regular/Bold (SIL OFL 1.1),
  installed in the per-user Windows font directory.

Microsoft YaHei, Yu Gothic, Segoe UI and other system UI fonts are deliberately
not used.  Every candidate carries its family, style, license, official source
URL and SHA-256; the resolved path, face index and variable-font axes are
printed on every run.  Glyph coverage for all exact copy is verified before
rendering, so a missing character fails the build instead of producing tofu.
See ``packaging/store/images/FONT-LICENSES.md`` for the audit trail.  No font
file is copied into this repository.

Run
---
    python packaging/store/tools/Generate-StoreArt.py

If Pillow is available in a local virtual environment, simply use that
interpreter.  All images are rendered at 2x and downsampled with Lanczos, then
saved as sRGB PNGs with an embedded ICC profile.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import math
import os
import random
import struct
from dataclasses import dataclass
from pathlib import Path

from PIL import Image, ImageChops, ImageCms, ImageDraw, ImageFilter, ImageFont

# --------------------------------------------------------------------------- #
# Paths and constants
# --------------------------------------------------------------------------- #

SS = 2  # supersampling factor for every canvas

ROOT = Path(__file__).resolve().parents[3]
IMAGES = ROOT / "packaging" / "store" / "images"
SOURCE_LOGO = ROOT / "docs" / "branding" / "melotag-logo.png"

PURPLE = (110, 91, 245)       # #6E5BF5
CYAN = (24, 190, 214)         # #18BED6
CYAN_LIGHT = (139, 231, 245)
HEAD = (245, 247, 255)
BODY = (169, 180, 208)
DIM = (128, 140, 171)
INK_TOP = (13, 16, 36)
INK_BOTTOM = (4, 6, 14)

WINDIR = Path(os.environ.get("WINDIR", r"C:\Windows"))
FONT_DIR = WINDIR / "Fonts"
USER_FONT_DIR = Path(
    os.environ.get("LOCALAPPDATA", str(Path.home() / "AppData" / "Local"))
) / "Microsoft" / "Windows" / "Fonts"

OFL_11 = "SIL Open Font License 1.1"
NOTO_SANS_SOURCE = "https://github.com/notofonts/latin-greek-cyrillic"
NOTO_CJK_SOURCE = "https://github.com/notofonts/noto-cjk"
SOURCE_HAN_SOURCE = "https://github.com/adobe-fonts/source-han-sans"
LIBERATION_SOURCE = "https://github.com/liberationfonts/liberation-fonts"


@dataclass(frozen=True)
class FontSource:
    """A locally installed font file plus the evidence needed to reproduce it.

    ``index`` selects a face inside a TrueType Collection; ``axes`` pins a
    variable-font instance (Noto Sans SC defaults to wght=100, so the weight
    must always be set explicitly).  Every entry is SIL OFL 1.1 so the rendered
    artwork is safe for commercial distribution.
    """

    path: Path
    index: int = 0
    axes: tuple[float, ...] | None = None
    family: str = ""
    style: str = ""
    license: str = OFL_11
    source: str = ""
    sha256: str = ""


FONT_CANDIDATES: dict[str, list[FontSource]] = {
    # Latin copy: Noto Sans 2.015 as shipped with Windows (OFL 1.1, no RFN).
    "latin_semibold": [
        FontSource(
            FONT_DIR / "NotoSans-Bold.ttf", 0, None, "Noto Sans", "Bold",
            OFL_11, NOTO_SANS_SOURCE,
            "3a08a47daa00cade516425c15c57615aef2fd418ec9811a7b9f465088f92cc05",
        ),
        FontSource(
            FONT_DIR / "LiberationSans-Bold.ttf", 0, None, "Liberation Sans", "Bold",
            OFL_11, LIBERATION_SOURCE,
            "788abee4c806d660e8aee46689dd8540cd4bb98da03dcc9d171ce3efd99a9173",
        ),
    ],
    "latin_regular": [
        FontSource(
            FONT_DIR / "NotoSans-Regular.ttf", 0, None, "Noto Sans", "Regular",
            OFL_11, NOTO_SANS_SOURCE,
            "f5f552c8c5edb61fe6efb824baf4d4de47b1a8689ab4925ff43f7bd6a4ebece5",
        ),
        FontSource(
            FONT_DIR / "LiberationSans-Regular.ttf", 0, None, "Liberation Sans", "Regular",
            OFL_11, LIBERATION_SOURCE,
            "76d04c18ea243f426b7de1f3ad208e927008f961dc5945e5aad352d0dfde8ee8",
        ),
    ],
    # CJK copy: Source Han Sans SC 2.005R per-user install (OFL 1.1, RFN 'Source').
    # The SC regional OTFs cover all Simplified Chinese and Japanese copy used
    # here; no JP-specific OFL font is installed on this machine.
    "zh_bold": [
        FontSource(
            USER_FONT_DIR / "SourceHanSansSC-Bold.otf", 0, None,
            "Source Han Sans SC", "Bold", OFL_11, SOURCE_HAN_SOURCE,
            "df2b90f5bcc6d01dfc964cec5f6d535d6b6aebd26ed7fd79a9c1b3f2112fcb6b",
        ),
        FontSource(
            USER_FONT_DIR / "SourceHanSansCN-Bold.otf", 0, None,
            "Source Han Sans CN", "Bold", OFL_11, SOURCE_HAN_SOURCE,
            "62383707c086a32f3afd5e293f34c7eff64c7fea31f579fdc6cbe34d920519a6",
        ),
        FontSource(
            FONT_DIR / "NotoSansSC-VF.ttf", 0, (700.0,), "Noto Sans SC", "Bold (wght=700)",
            OFL_11, NOTO_CJK_SOURCE,
            "763146584cf0710223441356b4395e279021b0806c196614377a7a0174ae074a",
        ),
    ],
    "zh_regular": [
        FontSource(
            USER_FONT_DIR / "SourceHanSansSC-Regular.otf", 0, None,
            "Source Han Sans SC", "Regular", OFL_11, SOURCE_HAN_SOURCE,
            "f1d8611151880c6c336aabeac4640ef434fa13cbfbf1ffe82d0a71b2a5637256",
        ),
        FontSource(
            USER_FONT_DIR / "SourceHanSansCN-Regular.otf", 0, None,
            "Source Han Sans CN", "Regular", OFL_11, SOURCE_HAN_SOURCE,
            "e2bc8a2e7f37474b774fff8db758681ece40bb6947a90d571bce9dd60671a8e4",
        ),
        FontSource(
            FONT_DIR / "NotoSansSC-VF.ttf", 0, (400.0,), "Noto Sans SC", "Regular (wght=400)",
            OFL_11, NOTO_CJK_SOURCE,
            "763146584cf0710223441356b4395e279021b0806c196614377a7a0174ae074a",
        ),
    ],
    # Japanese copy uses the same OFL CJK family; the Generation JP subset is not
    # installed.  Kana, JP punctuation and every kanji used by the copy are
    # covered and glyph-checked at runtime.
    "ja_bold": [
        FontSource(
            USER_FONT_DIR / "SourceHanSansSC-Bold.otf", 0, None,
            "Source Han Sans SC", "Bold", OFL_11, SOURCE_HAN_SOURCE,
            "df2b90f5bcc6d01dfc964cec5f6d535d6b6aebd26ed7fd79a9c1b3f2112fcb6b",
        ),
        FontSource(
            USER_FONT_DIR / "SourceHanSansCN-Bold.otf", 0, None,
            "Source Han Sans CN", "Bold", OFL_11, SOURCE_HAN_SOURCE,
            "62383707c086a32f3afd5e293f34c7eff64c7fea31f579fdc6cbe34d920519a6",
        ),
        FontSource(
            FONT_DIR / "NotoSansSC-VF.ttf", 0, (700.0,), "Noto Sans SC", "Bold (wght=700)",
            OFL_11, NOTO_CJK_SOURCE,
            "763146584cf0710223441356b4395e279021b0806c196614377a7a0174ae074a",
        ),
    ],
    "ja_regular": [
        FontSource(
            USER_FONT_DIR / "SourceHanSansSC-Regular.otf", 0, None,
            "Source Han Sans SC", "Regular", OFL_11, SOURCE_HAN_SOURCE,
            "f1d8611151880c6c336aabeac4640ef434fa13cbfbf1ffe82d0a71b2a5637256",
        ),
        FontSource(
            USER_FONT_DIR / "SourceHanSansCN-Regular.otf", 0, None,
            "Source Han Sans CN", "Regular", OFL_11, SOURCE_HAN_SOURCE,
            "e2bc8a2e7f37474b774fff8db758681ece40bb6947a90d571bce9dd60671a8e4",
        ),
        FontSource(
            FONT_DIR / "NotoSansSC-VF.ttf", 0, (400.0,), "Noto Sans SC", "Regular (wght=400)",
            OFL_11, NOTO_CJK_SOURCE,
            "763146584cf0710223441356b4395e279021b0806c196614377a7a0174ae074a",
        ),
    ],
}

_PROFILE = ImageCms.ImageCmsProfile(ImageCms.createProfile("sRGB"))
_PROFILE_BYTES = bytearray(_PROFILE.tobytes())
# Pillow stamps the profile with the current time; pin the ICC header date so
# repeated runs produce byte-identical PNGs.
_PROFILE_BYTES[24:36] = struct.pack(">6H", 2026, 1, 1, 0, 0, 0)
SRGB_ICC = bytes(_PROFILE_BYTES)


def s(v: float) -> int:
    """Scale a final-pixel measurement to canvas pixels."""
    return int(round(v * SS))


# --------------------------------------------------------------------------- #
# Small colour / geometry helpers
# --------------------------------------------------------------------------- #


def clamp01(v: float) -> float:
    return 0.0 if v < 0 else 1.0 if v > 1 else v


def mix(a: tuple[int, int, int], b: tuple[int, int, int], t: float) -> tuple[int, int, int]:
    t = clamp01(t)
    return (
        int(round(a[0] + (b[0] - a[0]) * t)),
        int(round(a[1] + (b[1] - a[1]) * t)),
        int(round(a[2] + (b[2] - a[2]) * t)),
    )


def rgba(color: tuple[int, int, int], alpha: int = 255) -> tuple[int, int, int, int]:
    return (color[0], color[1], color[2], max(0, min(255, int(alpha))))


def diag_gradient(
    w: int,
    h: int,
    c0: tuple[int, int, int],
    c1: tuple[int, int, int],
    p0: tuple[float, float] = (0.0, 0.0),
    p1: tuple[float, float] = (1.0, 1.0),
) -> Image.Image:
    """Smooth linear gradient rendered small then upscaled (banding-free enough)."""
    n = 256
    small = Image.new("RGB", (n, n))
    px = small.load()
    dx, dy = p1[0] - p0[0], p1[1] - p0[1]
    den = dx * dx + dy * dy or 1.0
    for y in range(n):
        v = y / (n - 1)
        for x in range(n):
            u = x / (n - 1)
            t = ((u - p0[0]) * dx + (v - p0[1]) * dy) / den
            px[x, y] = mix(c0, c1, t)
    return small.resize((max(1, w), max(1, h)), Image.Resampling.BICUBIC)


def radial_disc(radius: float, color: tuple[int, int, int], peak: int, power: float = 2.0) -> Image.Image:
    """Soft radial light.  Generated at 256px and scaled to the requested size."""
    n = 256
    small = Image.new("RGBA", (n, n), rgba(color, 0))
    px = small.load()
    c = (n - 1) / 2
    for y in range(n):
        for x in range(n):
            d = math.hypot(x - c, y - c) / c
            if d >= 1.0:
                continue
            a = peak * (1.0 - d * d) ** power
            if a > 0.5:
                px[x, y] = rgba(color, a)
    size = max(4, int(radius * 2))
    return small.resize((size, size), Image.Resampling.LANCZOS)


def add_glow(
    dst: Image.Image,
    cx: float,
    cy: float,
    radius: float,
    color: tuple[int, int, int],
    peak: int,
    power: float = 2.0,
    aspect: float = 1.0,
) -> None:
    disc = radial_disc(radius, color, peak, power)
    if abs(aspect - 1.0) > 1e-3:
        disc = disc.resize((disc.width, max(2, int(disc.height * aspect))), Image.Resampling.LANCZOS)
    dst.paste(disc, (int(round(cx - radius)), int(round(cy - radius * aspect))), disc)


def add_vignette(img: Image.Image, strength: float = 0.40) -> Image.Image:
    n = 256
    small = Image.new("L", (n, n), 0)
    px = small.load()
    for y in range(n):
        for x in range(n):
            nx = (x / (n - 1) - 0.5) * 2.0
            ny = (y / (n - 1) - 0.5) * 2.0
            d = math.hypot(nx, ny) / 1.41421356
            t = clamp01((d - 0.30) / 0.70)
            px[x, y] = int(255 * strength * (t ** 1.7))
    alpha = small.resize(img.size, Image.Resampling.BICUBIC)
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    layer.putalpha(alpha)
    img.paste(layer, (0, 0), layer)
    return img


def add_grain(img: Image.Image, amount: float = 0.018, seed: int = 41) -> Image.Image:
    """Deterministic fine grain (Pillow's effect_noise is not reproducible)."""
    nw, nh = max(1, img.width // 2), max(1, img.height // 2)
    raw = random.Random(seed).randbytes(nw * nh)
    noise = Image.frombytes("L", (nw, nh), raw).convert("RGB")
    noise = noise.resize(img.size, Image.Resampling.BILINEAR)
    return Image.blend(img, noise, amount)


def make_background(
    w: int,
    h: int,
    c_top: tuple[int, int, int],
    c_bottom: tuple[int, int, int],
    glows: list[tuple[float, float, float, tuple[int, int, int], int]],
    vignette: float,
    grain: float = 0.016,
) -> Image.Image:
    """Deep ink background with a diagonal base tint and soft brand lights."""
    img = diag_gradient(w, h, c_top, c_bottom, (0.08, 0.0), (0.92, 1.0)).convert("RGB")
    for gx, gy, gr, gc, gp in glows:
        add_glow(img, gx * w, gy * h, gr * w, gc, gp)
    add_vignette(img, vignette)
    add_grain(img, grain)
    return img


def gradient_bar(w: int, h: int, radius: int, c0=PURPLE, c1=CYAN, alpha: int = 255) -> Image.Image:
    w, h = max(1, int(w)), max(1, int(h))
    mask = Image.new("L", (w, h), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, w - 1, h - 1), radius=min(radius, w // 2, h // 2), fill=alpha)
    out = diag_gradient(w, h, c0, c1, (0.0, 0.0), (1.0, 0.2)).convert("RGBA")
    out.putalpha(mask)
    return out


# --------------------------------------------------------------------------- #
# Fonts and text
# --------------------------------------------------------------------------- #


@dataclass(frozen=True)
class ResolvedFont:
    key: str
    source: FontSource
    fallback_rank: int


FONTS: dict[str, ResolvedFont] = {}
_FONT_CACHE: dict[tuple[str, int], ImageFont.FreeTypeFont] = {}


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def resolve_fonts(verify_hashes: bool = True) -> dict[str, ResolvedFont]:
    """Pick the first installed, license-verified candidate for every role.

    The order is fixed and every candidate is a locally installed SIL OFL 1.1
    family, so a machine without the primary font still renders with a
    commercially usable font instead of silently falling back to a Windows UI
    font.  The resolved primary/fallback choice is printed by ``main``.
    """
    resolved: dict[str, ResolvedFont] = {}
    for key, candidates in FONT_CANDIDATES.items():
        for rank, source in enumerate(candidates):
            if not source.path.exists():
                continue
            if verify_hashes and source.sha256:
                actual = sha256_file(source.path)
                if actual != source.sha256:
                    raise SystemExit(
                        f"Font hash mismatch for '{key}':\n  {source.path}\n"
                        f"  expected {source.sha256}\n  actual   {actual}\n"
                        "Re-verify the license and update Generate-StoreArt.py "
                        "and images/FONT-LICENSES.md, or pass --no-font-hash to "
                        "render with the installed file."
                    )
            resolved[key] = ResolvedFont(key, source, rank)
            break
        else:
            raise SystemExit(
                f"No verified font for '{key}'. Install one of these SIL OFL 1.1 "
                "fonts (the repository never bundles them):\n  "
                + "\n  ".join(
                    f"{c.path}  [{c.family} {c.style}, {c.license}]"
                    for c in candidates
                )
            )
    return resolved


def load_font(key: str, size: int) -> ImageFont.FreeTypeFont:
    if key not in FONTS:
        raise RuntimeError("fonts are not initialised; call resolve_fonts() first")
    size = max(1, int(round(size)))
    cache_key = (key, size)
    if cache_key not in _FONT_CACHE:
        source = FONTS[key].source
        font = ImageFont.truetype(str(source.path), size, index=source.index)
        if source.axes is not None:
            try:
                font.set_variation_by_axes(list(source.axes))
            except Exception as exc:  # pragma: no cover - depends on FreeType
                raise RuntimeError(
                    f"{source.path.name}: cannot apply variable-font axes "
                    f"{source.axes}: {exc}"
                ) from exc
        _FONT_CACHE[cache_key] = font
    return _FONT_CACHE[cache_key]


# Codepoint outside every Unicode block we use; it always maps to .notdef.
_NOTDEF_PROBE = "\U0010FFFD"


def _glyph_signature(
    font: ImageFont.FreeTypeFont,
    char: str,
) -> tuple[tuple[int, int], tuple[int, int, int, int] | None, bytes]:
    mask = font.getmask(char, mode="L")
    # size, ink bbox and pixels: a blank glyph has no ink bbox.
    return mask.size, mask.getbbox(), bytes(mask)


def missing_glyphs(font: ImageFont.FreeTypeFont, texts: list[str]) -> list[str]:
    """Return characters that render as .notdef (a tofu box) or as nothing."""
    notdef = _glyph_signature(font, _NOTDEF_PROBE)
    missing: list[str] = []
    seen: set[str] = set()
    for text in texts:
        for char in text:
            if char.isspace() or char in seen:
                continue
            seen.add(char)
            signature = _glyph_signature(font, char)
            if signature == notdef or signature[1] is None:
                missing.append(char)
    return missing


def audit_text_coverage() -> None:
    """Fail before rendering if any exact copy character lacks a real glyph."""
    required: dict[str, list[str]] = {key: [] for key in FONTS}
    for spec in POSTER_SPECS.values():
        required[spec["font_bold"]].extend(spec["headline"])
        required[spec["font_bold"]].append(spec["eyebrow"])
        required[spec["font_bold"]].extend(label for _, label in spec["pills"])
        required[spec["font_body"]].extend(spec["subhead"])
        required[spec["font_body"]].append(spec["note"])
        if spec["brand_sub"]:
            required[spec["font_bold"]].append(spec["brand_sub"])
    required["latin_semibold"].append("Melotag")
    required["zh_bold"].append("音签")

    print("Glyph coverage (exact poster/box copy):")
    for key, texts in required.items():
        font = load_font(key, s(64))
        missing = missing_glyphs(font, texts)
        if missing:
            source = FONTS[key].source
            raise SystemExit(
                f"Missing {len(missing)} glyph(s) for '{key}' in "
                f"{source.family} {source.style} ({source.path}): "
                f"{''.join(sorted(missing))!r}"
            )
        unique = sorted({ch for text in texts for ch in text if not ch.isspace()})
        print(f"  {key:15s} {len(unique):3d} chars OK  ({FONTS[key].source.family} "
              f"{FONTS[key].source.style})")


def check_fit(label: str, width: float, limit: float) -> None:
    """Fail when a measured text run would leave its safe area."""
    if width > limit + 0.5:
        raise RuntimeError(
            f"{label}: text is {width / SS:.1f}px wide, limit is {limit / SS:.1f}px"
        )


def tracked_width(text: str, font: ImageFont.FreeTypeFont, tracking: float = 0.0) -> float:
    return sum(font.getlength(ch) for ch in text) + tracking * max(0, len(text) - 1)


def draw_tracked(
    draw: ImageDraw.ImageDraw,
    x: float,
    baseline: float,
    text: str,
    font: ImageFont.FreeTypeFont,
    fill: tuple[int, int, int, int],
    tracking: float = 0.0,
    align: str = "left",
) -> float:
    width = tracked_width(text, font, tracking)
    if align == "center":
        x -= width / 2.0
    cx = x
    for ch in text:
        draw.text((cx, baseline), ch, font=font, fill=fill, anchor="ls")
        cx += font.getlength(ch) + tracking
    return width


def fit_headline(
    lines: list[str],
    font_key: str,
    start_size: int,
    max_width: int,
    max_height: int,
    line_factor: float = 1.18,
) -> tuple[ImageFont.FreeTypeFont, int, list[float], int]:
    for size in range(int(start_size), 19, -1):
        font = load_font(font_key, size)
        ascent, descent = font.getmetrics()
        lh = max(int(round(size * line_factor)), int(round((ascent + descent) * 1.02)))
        widths = [font.getlength(line) for line in lines]
        height = ascent + descent + (len(lines) - 1) * lh
        if max(widths) <= max_width and height <= max_height:
            return font, lh, widths, height
    raise RuntimeError(f"Headline does not fit: {lines!r}")


def draw_block(
    draw: ImageDraw.ImageDraw,
    lines: list[str],
    font: ImageFont.FreeTypeFont,
    x: int,
    top: int,
    fill: tuple[int, int, int, int],
    line_height: int,
) -> int:
    ascent, descent = font.getmetrics()
    for i, line in enumerate(lines):
        draw.text((x, top + ascent + i * line_height), line, font=font, fill=fill, anchor="ls")
    return top + ascent + descent + (len(lines) - 1) * line_height


# --------------------------------------------------------------------------- #
# Brand mark, tag silhouettes and decorative pieces
# --------------------------------------------------------------------------- #


def _tile_mask(size: int, hole: bool = True) -> Image.Image:
    mask = Image.new("L", (size, size), 0)
    d = ImageDraw.Draw(mask)
    inset = 11.3 / 512.0 * size
    radius = 110.1 / 512.0 * size
    d.rounded_rectangle((inset, inset, size - inset, size - inset), radius=radius, fill=255)
    if hole:
        cx, cy, r = 115.7 / 512.0 * size, 118.8 / 512.0 * size, 29.7 / 512.0 * size
        d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=0)
    return mask


def raster_mark(size: int) -> Image.Image:
    """The 512px SVG geometry, re-rendered crisp at any size."""
    size = max(8, int(round(size)))
    ss = 3 if size <= 420 else 2
    S = size * ss
    grad = diag_gradient(S, S, PURPLE, CYAN, (0.15, 0.0), (0.85, 1.0)).convert("RGBA")
    mask = _tile_mask(S)
    mark = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    mark.paste(grad, (0, 0), mask)

    d = ImageDraw.Draw(mark)
    bars = (
        (153.6, 210.0, 55.3, 169.0),
        (240.9, 153.6, 55.3, 281.6),
        (328.3, 187.9, 55.3, 213.0),
    )
    for bx, by, bw, bh in bars:
        x0, y0 = bx / 512.0 * S, by / 512.0 * S
        x1, y1 = (bx + bw) / 512.0 * S, (by + bh) / 512.0 * S
        d.rounded_rectangle((x0, y0, x1, y1), radius=(bw / 2.0) / 512.0 * S, fill=(255, 255, 255, 255))
    return mark.resize((size, size), Image.Resampling.LANCZOS)


def add_mark(
    img: Image.Image,
    cx: float,
    cy: float,
    size: int,
    *,
    glow: bool = True,
    shadow: bool = True,
) -> None:
    if glow:
        add_glow(img, cx, cy, size * 0.78, PURPLE, 52, power=2.2)
        add_glow(img, cx, cy, size * 1.06, CYAN, 30, power=2.4)
    mark = raster_mark(size)
    if shadow:
        pad = max(2, int(size * 0.22))
        alpha = Image.new("L", (mark.width + pad * 2, mark.height + pad * 2), 0)
        alpha.paste(mark.getchannel("A"), (pad, pad))
        alpha = alpha.point(lambda v: int(v * 0.50)).filter(
            ImageFilter.GaussianBlur(max(2, size * 0.075))
        )
        shadow_layer = Image.new("RGBA", alpha.size, (0, 0, 0, 0))
        shadow_layer.putalpha(alpha)
        img.paste(
            shadow_layer,
            (
                int(round(cx - size / 2 - pad)),
                int(round(cy - size / 2 + size * 0.038 - pad)),
            ),
            shadow_layer,
        )
    img.paste(mark, (int(round(cx - size / 2)), int(round(cy - size / 2))), mark)


def tag_outline(
    size: int,
    border: int,
    alpha: int,
    rotation: float = 0.0,
    hole: bool = True,
) -> Image.Image:
    """Large, softly glowing silhouette of the brand tag (used as watermark)."""
    S = max(64, int(size) * 2)
    mask = _tile_mask(S, hole=hole)
    eroded = mask.filter(ImageFilter.MinFilter(2 * max(1, int(border)) + 1))
    outline = ImageChops.subtract(mask, eroded)
    grad = diag_gradient(S, S, PURPLE, CYAN, (0.15, 0.0), (0.85, 1.0)).convert("RGBA")
    grad.putalpha(outline.point(lambda v: int(v * alpha / 255.0)))
    if abs(rotation) > 1e-3:
        grad = grad.rotate(rotation, expand=True, resample=Image.Resampling.BICUBIC)
    return grad.resize((max(1, grad.width // 2), max(1, grad.height // 2)), Image.Resampling.LANCZOS)


def draw_gradient_arc(
    draw: ImageDraw.ImageDraw,
    bbox: tuple[float, float, float, float],
    start: float,
    end: float,
    width: int,
    c0: tuple[int, int, int],
    c1: tuple[int, int, int],
    alpha: int,
    segments: int = 110,
) -> None:
    span = end - start
    step = span / segments
    for i in range(segments):
        a0 = start + span * i / segments
        a1 = a0 + step + 0.45
        col = mix(c0, c1, (i + 0.5) / segments)
        draw.arc(bbox, a0, a1, fill=rgba(col, alpha), width=max(1, int(width)))


def draw_corner_ticks(draw: ImageDraw.ImageDraw, w: int, h: int, margin: int, length: int) -> None:
    col = (190, 205, 235, 72)
    wd = max(1, s(0.9))
    for x, y, dx, dy in (
        (margin, margin, 1, 1),
        (w - margin, margin, -1, 1),
        (margin, h - margin, 1, -1),
        (w - margin, h - margin, -1, -1),
    ):
        draw.line((x, y, x + dx * length, y), fill=col, width=wd)
        draw.line((x, y, x, y + dy * length), fill=col, width=wd)


def draw_dot_grid(
    draw: ImageDraw.ImageDraw,
    x0: int,
    y0: int,
    x1: int,
    y1: int,
    step: int,
    radius: int,
    alpha: int = 20,
) -> None:
    for y in range(y0, y1 + 1, step):
        for x in range(x0, x1 + 1, step):
            draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill=(178, 200, 235, alpha))


def add_ribbon(
    img: Image.Image,
    x0: float,
    x1: float,
    base_y: float,
    amp: float,
    thick: float,
    phase: float,
    c0: tuple[int, int, int] = PURPLE,
    c1: tuple[int, int, int] = CYAN,
    alpha: int = 60,
    core_alpha: int = 90,
    glow_radius: float = 0.0,
) -> None:
    W, H = img.size
    step = max(4, s(3.0))
    top: list[tuple[float, float]] = []
    bottom: list[tuple[float, float]] = []
    center: list[tuple[float, float]] = []
    span = max(1.0, x1 - x0)
    for x in range(int(x0), int(x1) + 1, step):
        u = (x - x0) / span
        y = (
            base_y
            + amp * math.sin(u * math.pi * 2.2 + phase)
            + amp * 0.34 * math.sin(u * math.pi * 5.3 + phase * 1.7)
        )
        t = thick * (0.38 + 0.62 * math.sin(math.pi * clamp01(u)) ** 0.6)
        top.append((x, y - t / 2.0))
        bottom.append((x, y + t / 2.0))
        center.append((x, y))

    mask = Image.new("L", (W, H), 0)
    ImageDraw.Draw(mask).polygon(top + bottom[::-1], fill=255)
    mask = mask.filter(ImageFilter.GaussianBlur(max(0.6, s(0.55))))

    grad = diag_gradient(W, H, c0, c1, (0.0, 0.0), (1.0, 0.15)).convert("RGBA")
    grad.putalpha(mask.point(lambda v: int(v * alpha / 255.0)))
    if glow_radius > 0:
        halo = grad.filter(ImageFilter.GaussianBlur(glow_radius))
        img.paste(halo, (0, 0), halo)
    img.paste(grad, (0, 0), grad)

    if core_alpha > 0:
        core = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        ImageDraw.Draw(core).line(
            [(int(x), int(y)) for x, y in center],
            fill=rgba(mix(c0, c1, 0.45), core_alpha),
            width=max(1, s(1.1)),
            joint="curve",
        )
        img.paste(core, (0, 0), core)


def add_eq_bars(
    img: Image.Image,
    x0: float,
    baseline: float,
    heights: list[float],
    bar_w: float,
    gap: float,
    alpha: int = 110,
    c_top: tuple[int, int, int] = PURPLE,
    c_bottom: tuple[int, int, int] = CYAN,
    glow: bool = True,
) -> None:
    W, H = img.size
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    bw = max(2, int(round(bar_w)))
    for i, h in enumerate(heights):
        hh = max(2, int(round(h)))
        bx = int(round(x0 + i * (bar_w + gap)))
        top = int(round(baseline - hh))
        # vertical gradient, transparent at the top, saturated at the base
        cell = Image.new("RGBA", (bw, hh))
        px = cell.load()
        for yy in range(hh):
            t = yy / max(1, hh - 1)
            col = mix(c_top, c_bottom, t)
            a = int(alpha * (0.20 + 0.80 * t ** 0.7))
            for xx in range(bw):
                px[xx, yy] = (*col, a)
        rad = max(1, bw // 2)
        m = Image.new("L", (bw, hh), 0)
        ImageDraw.Draw(m).rounded_rectangle((0, 0, bw - 1, hh + rad), radius=rad, fill=255)
        cell.putalpha(ImageChops.multiply(cell.getchannel("A"), m))
        layer.paste(cell, (bx, top), cell)
    if glow:
        halo = layer.filter(ImageFilter.GaussianBlur(max(1, s(7.0))))
        alpha_halo = halo.getchannel("A").point(lambda v: int(v * 0.55))
        halo.putalpha(alpha_halo)
        img.paste(halo, (0, 0), halo)
    img.paste(layer, (0, 0), layer)


def add_particles(
    layer: Image.Image,
    count: int,
    seed: int,
    clear_center: tuple[float, float] | None = None,
    clear_radius: float = 0.0,
    avoid_rect: tuple[float, float, float, float] | None = None,
) -> None:
    rng = random.Random(seed)
    d = ImageDraw.Draw(layer)
    W, H = layer.size
    placed = 0
    attempts = 0
    while placed < count and attempts < count * 12:
        attempts += 1
        x = rng.uniform(0, W)
        y = rng.uniform(0, H)
        if clear_center is not None:
            if math.hypot(x - clear_center[0], y - clear_center[1]) < clear_radius:
                continue
        if avoid_rect is not None:
            rx0, ry0, rx1, ry1 = avoid_rect
            if rx0 <= x <= rx1 and ry0 <= y <= ry1:
                continue
        placed += 1
        r = rng.uniform(0.8, 2.5) * SS
        col = mix((186, 206, 245), CYAN_LIGHT, rng.random() * 0.85)
        d.ellipse((x - r, y - r, x + r, y + r), fill=rgba(col, rng.randint(22, 92)))
        if rng.random() < 0.10:
            add_glow(layer, x, y, r * 7.0, col, 14)
    # a few brighter accent specks
    for _ in range(max(2, count // 40)):
        x = rng.uniform(0.08, 0.92) * W
        y = rng.uniform(0.08, 0.92) * H
        if avoid_rect is not None:
            rx0, ry0, rx1, ry1 = avoid_rect
            if rx0 <= x <= rx1 and ry0 <= y <= ry1:
                continue
        add_glow(layer, x, y, 7.0 * SS, CYAN_LIGHT, 34)


# --------------------------------------------------------------------------- #
# Poster
# --------------------------------------------------------------------------- #

POSTER_SPECS = {
    "zh-CN": {
        "font_bold": "zh_bold",
        "font_body": "zh_regular",
        "brand_sub": "音签",
        "eyebrow": "本地音乐库整理工具",
        "eyebrow_size": 19,
        "eyebrow_tracking": 0.6,
        "headline": ["让每一首歌", "都有干净的标签"],
        "headline_size": 76,
        "subhead": ["自动匹配标签、歌词与封面，", "批量整理本地音乐库。"],
        "body_size": 25,
        "pills": [("tag", "标签匹配"), ("lyrics", "歌词获取"), ("art", "封面补全"), ("bulk", "批量整理")],
        "pill_size": 19,
        "note": "本地文件处理 · 不上传音频",
        "note_size": 18,
    },
    "ja-JP": {
        "font_bold": "ja_bold",
        "font_body": "ja_regular",
        "brand_sub": "",
        "eyebrow": "音楽ライブラリ整理ツール",
        "eyebrow_size": 19,
        "eyebrow_tracking": 0.7,
        "headline": ["すべての曲に、", "きれいなタグを。"],
        "headline_size": 70,
        "subhead": ["タグ・歌詞・カバーをまとめて取得し、", "ローカル音楽をすっきり整理。"],
        "body_size": 24,
        "pills": [("tag", "タグ編集"), ("lyrics", "歌詞取得"), ("art", "カバー取得"), ("bulk", "一括整理")],
        "pill_size": 19,
        "note": "ローカルファイル対応・音声のアップロードなし",
        "note_size": 17,
    },
    "en-US": {
        "font_bold": "latin_semibold",
        "font_body": "latin_regular",
        "brand_sub": "",
        "eyebrow": "MUSIC LIBRARY ORGANIZER",
        "eyebrow_size": 18,
        "eyebrow_tracking": 2.7,
        "headline": ["Every track,", "perfectly tagged."],
        "headline_size": 72,
        "subhead": ["Match tags, lyrics and cover art —", "then clean up your whole local library."],
        "body_size": 25,
        "pills": [("tag", "Auto tags"), ("lyrics", "Lyrics"), ("art", "Cover art"), ("bulk", "Bulk edit")],
        "pill_size": 19,
        "note": "Local files · no audio upload",
        "note_size": 18,
    },
}

POSTER_W, POSTER_H = 720, 1080
POSTER_MARGIN = 64
POSTER_TEXT_LIMIT = 708  # key copy stays above the bottom third (720)


def draw_feature_icon(draw: ImageDraw.ImageDraw, kind: str, cx: float, cy: float, size: float) -> None:
    col = rgba(CYAN_LIGHT, 235)
    wd = max(1, int(round(size * 0.13)))
    r = size / 2.0
    if kind == "tag":
        pts = [
            (cx - r * 0.90, cy),
            (cx - r * 0.46, cy - r * 0.56),
            (cx + r * 0.90, cy - r * 0.56),
            (cx + r * 0.90, cy + r * 0.56),
            (cx - r * 0.46, cy + r * 0.56),
        ]
        draw.polygon(pts, fill=col)
        draw.ellipse(
            (cx - r * 0.53, cy - r * 0.13, cx - r * 0.27, cy + r * 0.13),
            fill=(13, 18, 36, 255),
        )
    elif kind == "lyrics":
        for i, frac in enumerate((1.0, 0.72, 0.44)):
            y = cy - r * 0.58 + i * r * 0.58
            draw.line((cx - r * 0.86, y, cx - r * 0.86 + 1.72 * r * frac, y), fill=col, width=wd)
    elif kind == "art":
        draw.rounded_rectangle(
            (cx - r * 0.86, cy - r * 0.72, cx + r * 0.86, cy + r * 0.72),
            radius=r * 0.20,
            outline=col,
            width=wd,
        )
        draw.ellipse((cx + r * 0.18, cy - r * 0.46, cx + r * 0.52, cy - r * 0.12), fill=col)
        draw.line(
            (cx - r * 0.70, cy + r * 0.55, cx - r * 0.16, cy - r * 0.10, cx + r * 0.26, cy + r * 0.30, cx + r * 0.70, cy - r * 0.16),
            fill=col,
            width=wd,
            joint="curve",
        )
    else:  # bulk
        back = (cx - r * 0.55, cy - r * 0.82, cx + r * 0.82, cy + r * 0.48)
        front = (cx - r * 0.82, cy - r * 0.42, cx + r * 0.56, cy + r * 0.86)
        draw.rounded_rectangle(back, radius=r * 0.18, outline=rgba(CYAN_LIGHT, 150), width=wd)
        draw.rounded_rectangle(front, radius=r * 0.18, outline=col, width=wd)


def draw_pills(
    layer: ImageDraw.ImageDraw,
    x: int,
    top: int,
    max_width: int,
    items: list[tuple[str, str]],
    font_key: str,
    font_size: int,
) -> int:
    pad = s(15)
    icon = s(16)
    gap = s(9)
    between = s(10)
    height = s(52)
    radius = s(26)
    font = load_font(font_key, font_size)
    ascent, descent = font.getmetrics()
    widths = [pad * 2 + icon + gap + int(round(font.getlength(label))) for _, label in items]
    for (_, label), width in zip(items, widths):
        check_fit(f"feature pill {label!r}", width, max_width)

    rows: list[list[int]] = []
    current: list[int] = []
    used = 0
    for i, w in enumerate(widths):
        extra = w if not current else w + between
        if current and used + extra > max_width:
            rows.append(current)
            current = [i]
            used = w
        else:
            current.append(i)
            used += extra
    if current:
        rows.append(current)

    y = top
    for row in rows:
        total = sum(widths[i] for i in row) + between * (len(row) - 1)
        cx = x
        if len(rows) == 1:
            # single row: keep the content block visually centred in the column
            cx = x + max(0, (max_width - total) // 2)
        for i in row:
            w = widths[i]
            layer.rounded_rectangle(
                (cx, y, cx + w, y + height),
                radius=radius,
                fill=(255, 255, 255, 13),
                outline=(196, 210, 240, 34),
                width=max(1, s(0.8)),
            )
            icx = cx + pad + icon / 2
            icy = y + height / 2
            draw_feature_icon(layer, items[i][0], icx, icy, icon)
            baseline = y + (height - (ascent + descent)) / 2 + ascent
            layer.text((cx + pad + icon + gap, baseline), items[i][1], font=font, fill=rgba((226, 233, 250)), anchor="ls")
            cx += w + between
        y += height + s(10)
    return y - s(10)


def render_poster(lang: str, spec: dict, out_path: Path) -> dict:
    W, H = POSTER_W * SS, POSTER_H * SS
    cx = W / 2

    img = make_background(
        W, H, (14, 18, 40), (4, 6, 13),
        [
            (0.86, 0.05, 0.62, PURPLE, 92),
            (0.02, 0.48, 0.56, CYAN, 52),
            (1.05, 0.72, 0.58, CYAN, 46),
            (0.40, 0.96, 0.70, (42, 64, 158), 60),
        ],
        vignette=0.44,
        grain=0.015,
    )

    # --- background decoration ------------------------------------------- #
    deco = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    dd = ImageDraw.Draw(deco)

    # huge tag outline, cropped by the top-right corner
    watermark = tag_outline(s(560), border=max(2, s(1.3)), alpha=58, rotation=17)
    img.paste(watermark, (int(cx + s(120)), int(-s(150))), watermark)

    # faint engineering dots + corner ticks
    draw_dot_grid(dd, s(438), s(44), s(688), s(250), s(33), max(1, s(1.15)), alpha=22)
    draw_corner_ticks(dd, W, H, s(30), s(11))

    # a single long arc echoes the tag hole
    draw_gradient_arc(
        dd,
        (cx + s(238), -s(122), cx + s(584), s(224)),
        96, 268, max(1, s(1.3)), PURPLE, CYAN, 46, segments=80,
    )
    img.paste(deco, (0, 0), deco)

    # --- bottom-third visual (no copy down here) ------------------------- #
    glow_layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    add_glow(glow_layer, W * 0.80, H * 0.94, W * 0.48, CYAN, 46)
    add_glow(glow_layer, W * 0.10, H * 0.99, W * 0.40, PURPLE, 44)
    img.paste(glow_layer, (0, 0), glow_layer)

    bottom = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    bd = ImageDraw.Draw(bottom)
    for radius, alpha in ((s(250), 52), (s(320), 40), (s(390), 30)):
        draw_gradient_arc(
            bd,
            (s(92) - radius, s(1122) - radius, s(92) + radius, s(1122) + radius),
            186, 306, max(1, s(1.1)), PURPLE, CYAN, alpha, segments=70,
        )
    img.paste(bottom, (0, 0), bottom)

    add_ribbon(
        img,
        -s(40), W + s(40), s(942), s(48), s(22), -0.85,
        PURPLE, CYAN, alpha=82, core_alpha=120, glow_radius=s(18),
    )
    add_glow(img, s(536), s(1000), s(300), (60, 210, 232), 32)
    add_eq_bars(
        img,
        s(410), s(1094),
        [s(v) for v in (46, 102, 168, 78, 222, 134, 278, 98, 200, 124, 294, 158, 88, 234, 146, 64, 210, 114, 256, 84)],
        s(8), s(7), alpha=155,
    )
    add_particles(
        img,
        30, seed=7101,
        avoid_rect=(0, s(24), s(700), s(712)),
    )

    # --- brand row -------------------------------------------------------- #
    mark_size = s(52)
    mark_cx = s(POSTER_MARGIN) + mark_size / 2
    mark_cy = s(58) + mark_size / 2
    add_mark(img, mark_cx, mark_cy, mark_size, glow=False, shadow=False)

    ui = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    du = ImageDraw.Draw(ui)
    text = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    dt = ImageDraw.Draw(text)

    word_font = load_font("latin_semibold", s(30))
    wa, wd_ = word_font.getmetrics()
    word_x = s(POSTER_MARGIN) + mark_size + s(17)
    word_baseline = mark_cy + (wa - wd_) / 2 - s(1)
    word_width = tracked_width("Melotag", word_font, s(0.7))
    check_fit("brand wordmark", word_x + word_width, s(POSTER_W - POSTER_MARGIN))
    draw_tracked(dt, word_x, word_baseline, "Melotag", word_font, rgba(HEAD), tracking=s(0.7))

    if spec["brand_sub"]:
        sub_font = load_font(spec["font_bold"], s(21))
        sx = word_x + word_width + s(14)
        check_fit(
            "brand suffix", sx + sub_font.getlength(spec["brand_sub"]),
            s(POSTER_W - POSTER_MARGIN),
        )
        sa, sd = sub_font.getmetrics()
        dt.text((sx, word_baseline), spec["brand_sub"], font=sub_font, fill=rgba((153, 165, 196)), anchor="ls")
        div_x = sx - s(8)
        du.line(
            (div_x, mark_cy - s(9), div_x, mark_cy + s(9)),
            fill=(170, 185, 215, 70), width=max(1, s(0.8)),
        )

    # --- eyebrow ---------------------------------------------------------- #
    eyebrow_font = load_font(spec["font_bold"], s(spec["eyebrow_size"]))
    ea, ed = eyebrow_font.getmetrics()
    ey_top = s(178)
    dot_r = s(3.4)
    du.ellipse(
        (s(POSTER_MARGIN), ey_top + ea * 0.44 - dot_r, s(POSTER_MARGIN) + dot_r * 2, ey_top + ea * 0.44 + dot_r),
        fill=rgba(CYAN_LIGHT, 235),
    )
    check_fit(
        "eyebrow",
        s(POSTER_MARGIN + 16) + tracked_width(
            spec["eyebrow"], eyebrow_font, s(spec["eyebrow_tracking"])
        ),
        s(POSTER_W - POSTER_MARGIN),
    )
    draw_tracked(
        dt, s(POSTER_MARGIN + 16), ey_top + ea,
        spec["eyebrow"], eyebrow_font, rgba(CYAN_LIGHT, 238),
        tracking=s(spec["eyebrow_tracking"]),
    )

    # --- headline --------------------------------------------------------- #
    head_font, head_lh, widths, head_h = fit_headline(
        spec["headline"], spec["font_bold"], s(spec["headline_size"]),
        max_width=s(POSTER_W - POSTER_MARGIN * 2), max_height=s(212),
    )
    head_top = s(218)
    head_bottom = draw_block(dt, spec["headline"], head_font, s(POSTER_MARGIN), head_top, rgba(HEAD), head_lh)

    bar = gradient_bar(s(88), s(5), s(2.5), alpha=255)
    bar_y = head_bottom + s(22)
    ui.paste(bar, (s(POSTER_MARGIN), bar_y), bar)

    # --- subhead ---------------------------------------------------------- #
    body_font = load_font(spec["font_body"], s(spec["body_size"]))
    ba, bd_ = body_font.getmetrics()
    body_lh = max(s(spec["body_size"] * 1.52), int(round((ba + bd_) * 1.02)))
    body_top = bar_y + s(5) + s(25)
    body_bottom = draw_block(dt, spec["subhead"], body_font, s(POSTER_MARGIN), body_top, rgba(BODY), body_lh)

    # --- feature pills ---------------------------------------------------- #
    pills_top = body_bottom + s(34)
    pills_bottom = draw_pills(
        du, s(POSTER_MARGIN), pills_top, s(POSTER_W - POSTER_MARGIN * 2),
        spec["pills"], spec["font_bold"], s(spec["pill_size"]),
    )
    if pills_bottom > s(POSTER_TEXT_LIMIT):
        raise RuntimeError(
            f"{lang}: feature pills reach y={pills_bottom / SS:.1f}, "
            f"limit is {POSTER_TEXT_LIMIT}"
        )

    # --- footnote --------------------------------------------------------- #
    note_font = load_font(spec["font_body"], s(spec["note_size"]))
    na, nd = note_font.getmetrics()
    note_top = pills_bottom + s(19)
    note_baseline = note_top + na
    check_fit(
        "footnote", s(POSTER_MARGIN + 14) + note_font.getlength(spec["note"]),
        s(POSTER_W - POSTER_MARGIN),
    )
    if note_baseline + nd > s(POSTER_TEXT_LIMIT):
        raise RuntimeError(
            f"{lang}: footnote reaches y={(note_baseline + nd) / SS:.1f}, "
            f"limit is {POSTER_TEXT_LIMIT}"
        )
    du.ellipse(
        (s(POSTER_MARGIN), note_baseline - na * 0.30 - s(2.4), s(POSTER_MARGIN) + s(4.8), note_baseline - na * 0.30 + s(2.4)),
        fill=rgba((139, 158, 195), 210),
    )
    dt.text(
        (s(POSTER_MARGIN + 14), note_baseline), spec["note"],
        font=note_font, fill=rgba(DIM), anchor="ls",
    )

    # --- verify copy stays inside the upper two thirds -------------------- #
    bbox = text.getbbox()
    if bbox is None:
        raise RuntimeError(f"{lang}: no text rendered")
    left, top, right, bottom = bbox
    if left < s(48) or top < s(36) or right > s(672) or bottom > s(POSTER_TEXT_LIMIT):
        raise RuntimeError(
            f"{lang}: text outside safe area: {tuple(round(v / SS, 1) for v in bbox)}"
        )
    if text.crop((0, POSTER_H * SS // 3 * 2, W, H)).getbbox() is not None:
        raise RuntimeError(f"{lang}: text intrudes into the bottom third")

    # --- composite foreground -------------------------------------------- #
    img.paste(ui, (0, 0), ui)

    shadow_alpha = text.getchannel("A").point(lambda v: int(v * 0.46))
    shadow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    shadow.putalpha(shadow_alpha.filter(ImageFilter.GaussianBlur(s(4.0))))
    img.paste(shadow, (0, s(3)), shadow)
    img.paste(text, (0, 0), text)

    img = img.resize((POSTER_W, POSTER_H), Image.Resampling.LANCZOS)
    save_png(img, out_path)
    return {
        "path": out_path,
        "size": (POSTER_W, POSTER_H),
        "text_bbox": tuple(round(v / SS, 1) for v in bbox),
        "headline_px": round(head_lh / SS, 1),
    }


# --------------------------------------------------------------------------- #
# Box art
# --------------------------------------------------------------------------- #


def render_box_art(out_path: Path) -> dict:
    size = 1080
    W = H = size * SS
    cx = W / 2

    img = make_background(
        W, H, (15, 19, 42), (4, 5, 12),
        [
            (0.18, 0.12, 0.58, PURPLE, 82),
            (0.86, 0.84, 0.58, CYAN, 74),
            (0.50, 0.45, 0.70, (52, 62, 166), 36),
            (0.95, 0.12, 0.40, PURPLE, 36),
        ],
        vignette=0.46,
        grain=0.014,
    )

    deco = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    dd = ImageDraw.Draw(deco)
    wm = tag_outline(s(620), border=max(2, s(1.5)), alpha=48, rotation=12, hole=False)
    img.paste(wm, (int(cx - wm.width / 2), int(s(316) - wm.height / 2)), wm)

    for radius, alpha, start, end in ((s(330), 38, 196, 338), (s(420), 26, 20, 160), (s(500), 18, 120, 288)):
        draw_gradient_arc(
            dd, (cx - radius, s(316) - radius, cx + radius, s(316) + radius),
            start, end, max(1, s(1.1)), PURPLE, CYAN, alpha,
        )
    draw_dot_grid(dd, s(58), s(60), s(250), s(250), s(30), max(1, s(1.1)), alpha=20)
    img.paste(deco, (0, 0), deco)

    add_glow(img, cx, s(316), s(320), PURPLE, 34)
    add_mark(img, cx, s(316), s(260), glow=True, shadow=True)

    # bottom third: light and waveform decoration only
    add_glow(img, cx, s(860), s(330), (60, 205, 228), 20)
    add_glow(img, s(220), s(1040), s(330), PURPLE, 18)
    add_glow(img, cx, s(1006), s(300), (60, 205, 228), 30)
    add_ribbon(
        img, -s(20), W + s(20), s(872), s(32), s(12), 1.8,
        PURPLE, CYAN, alpha=52, core_alpha=70, glow_radius=s(14),
    )
    add_ribbon(
        img, -s(20), W + s(20), s(1018), s(21), s(10), -1.2,
        PURPLE, CYAN, alpha=74, core_alpha=104, glow_radius=s(12),
    )
    add_eq_bars(
        img, s(430), s(1102),
        [s(v) for v in (52, 106, 158, 78, 192, 124, 222, 88, 172, 112, 206, 70, 146, 96, 182, 60)],
        s(7), s(7), alpha=130,
    )
    add_particles(
        img, 26, seed=2203,
        clear_center=(cx, s(316)), clear_radius=s(280),
        avoid_rect=(s(230), s(450), s(850), s(710)),
    )

    text = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    dt = ImageDraw.Draw(text)
    ui = Image.new("RGBA", (W, H), (0, 0, 0, 0))

    word_font = load_font("latin_semibold", s(82))
    wa, wd_ = word_font.getmetrics()
    word_tracking = s(2.2)
    word_top = s(480)
    check_fit(
        "box wordmark", tracked_width("Melotag", word_font, word_tracking), s(1000)
    )
    draw_tracked(
        dt, cx, word_top + wa, "Melotag", word_font, rgba(HEAD),
        tracking=word_tracking, align="center",
    )
    word_bottom = word_top + wa + wd_
    line = gradient_bar(s(118), s(4), s(2), alpha=255)
    line_y = word_bottom + s(20)
    ui.paste(line, (int(cx - line.width / 2), line_y), line)

    sub_font = load_font("zh_bold", s(28))
    sa, sd = sub_font.getmetrics()
    sub_top = line_y + s(4) + s(20)
    check_fit("box suffix", tracked_width("音签", sub_font, s(2.0)), s(1000))
    draw_tracked(
        dt, cx, sub_top + sa, "音签", sub_font, rgba((172, 184, 214)),
        tracking=s(2.0), align="center",
    )

    bbox = text.getbbox()
    if bbox is None:
        raise RuntimeError("box art: no text")
    if bbox[0] < s(40) or bbox[1] < s(40) or bbox[2] > s(1040) or bbox[3] > s(1040):
        raise RuntimeError(f"box art: text outside safe area: {tuple(v / SS for v in bbox)}")
    if bbox[3] >= s(720):
        raise RuntimeError(f"box art: text intrudes into the bottom third: {tuple(v / SS for v in bbox)}")
    if text.crop((0, H // 3 * 2, W, H)).getbbox() is not None:
        raise RuntimeError("box art: text present below y=720")

    img.paste(ui, (0, 0), ui)
    shadow_alpha = text.getchannel("A").point(lambda v: int(v * 0.34))
    shadow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    shadow.putalpha(shadow_alpha.filter(ImageFilter.GaussianBlur(s(6.0))))
    img.paste(shadow, (0, s(2)), shadow)
    img.paste(text, (0, 0), text)

    img = img.resize((size, size), Image.Resampling.LANCZOS)
    save_png(img, out_path)
    return {"path": out_path, "size": (size, size), "text_bbox": tuple(round(v / SS, 1) for v in bbox)}


# --------------------------------------------------------------------------- #
# Hero
# --------------------------------------------------------------------------- #


def render_hero(out_path: Path) -> dict:
    width, height = 1920, 1080
    W, H = width * SS, height * SS
    cx, cy = W / 2, H / 2

    img = make_background(
        W, H, (12, 16, 36), (3, 4, 10),
        [
            (0.18, 0.20, 0.50, PURPLE, 100),
            (0.86, 0.74, 0.52, CYAN, 88),
            (0.52, 0.02, 0.46, (118, 74, 240), 42),
            (0.48, 1.06, 0.62, (20, 150, 190), 34),
        ],
        vignette=0.52,
        grain=0.014,
    )

    deco = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    dd = ImageDraw.Draw(deco)

    wm = tag_outline(s(1060), border=max(2, s(1.7)), alpha=22, rotation=-13, hole=False)
    img.paste(wm, (int(cx - wm.width / 2), int(cy - wm.height / 2)), wm)

    rings = (
        (s(300), 78, 190, 342, 1.4),
        (s(382), 58, 12, 152, 1.2),
        (s(500), 44, 158, 302, 1.1),
        (s(632), 26, 0, 360, 0.9),
        (s(760), 20, 232, 330, 0.8),
    )
    for radius, alpha, start, end, lw in rings:
        draw_gradient_arc(
            dd, (cx - radius, cy - radius, cx + radius, cy + radius),
            start, end, max(1, s(lw)), PURPLE, CYAN, alpha, segments=140,
        )

    draw_dot_grid(dd, s(60), s(56), s(360), s(250), s(32), max(1, s(1.1)), alpha=18)
    draw_dot_grid(dd, s(1560), s(830), s(1860), s(1024), s(32), max(1, s(1.1)), alpha=18)
    draw_corner_ticks(dd, W, H, s(54), s(14))
    img.paste(deco, (0, 0), deco)

    add_glow(img, cx, cy, s(470), PURPLE, 38)
    add_glow(img, cx, cy, s(620), CYAN, 22)

    # ribbons sweep behind the mark
    add_ribbon(
        img, -s(100), W + s(100), s(566), s(74), s(36), -0.6,
        PURPLE, CYAN, alpha=54, core_alpha=88, glow_radius=s(24),
    )
    add_ribbon(
        img, -s(80), W + s(80), s(620), s(44), s(14), 2.1,
        (158, 120, 245), (60, 214, 230), alpha=32, core_alpha=46, glow_radius=s(14),
    )

    # soft bokeh depth, deliberately out of focus
    for bx, by, br, bcol, ba in (
        (0.14, 0.30, 0.10, CYAN_LIGHT, 16),
        (0.86, 0.24, 0.075, (170, 150, 255), 18),
        (0.77, 0.86, 0.11, CYAN, 14),
        (0.24, 0.82, 0.07, (150, 130, 255), 15),
    ):
        add_glow(img, bx * W, by * H, br * W, bcol, ba)

    add_mark(img, cx, cy, s(360), glow=True, shadow=True)

    # a soft pool of light beneath the mark
    pedestal = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    add_glow(pedestal, cx, cy + s(252), s(310), CYAN_LIGHT, 20)
    add_glow(pedestal, cx, cy + s(248), s(190), (255, 255, 255), 12)
    img.paste(pedestal, (0, 0), pedestal)

    add_particles(img, 88, seed=1907, clear_center=(cx, cy), clear_radius=s(360))

    img = img.resize((width, height), Image.Resampling.LANCZOS)
    save_png(img, out_path)
    return {"path": out_path, "size": (width, height)}


# --------------------------------------------------------------------------- #
# App tile + IO/verification
# --------------------------------------------------------------------------- #


def render_app_tile(out_path: Path) -> dict:
    if not SOURCE_LOGO.exists():
        raise SystemExit(f"Missing source logo: {SOURCE_LOGO}")
    with Image.open(SOURCE_LOGO) as source:
        source.load()
        if source.size != (512, 512):
            raise RuntimeError(f"Expected a 512x512 logo, got {source.size}")
        tile = source.convert("RGBA").resize((300, 300), Image.Resampling.LANCZOS)
    save_png(tile, out_path)
    return {"path": out_path, "size": (300, 300)}


def save_png(img: Image.Image, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if img.mode not in ("RGB", "RGBA"):
        img = img.convert("RGB")
    img.save(path, format="PNG", optimize=True, icc_profile=SRGB_ICC)


def verify_png(path: Path, expected: tuple[int, int], allow_alpha: bool = False) -> dict:
    if not path.exists():
        raise RuntimeError(f"missing output: {path}")
    raw = path.stat().st_size
    if raw > 50 * 1024 * 1024:
        raise RuntimeError(f"{path.name} exceeds 50 MB ({raw} bytes)")
    with Image.open(path) as im:
        if im.size != expected:
            raise RuntimeError(f"{path.name}: expected {expected}, got {im.size}")
        if im.mode not in (("RGB", "RGBA") if allow_alpha else ("RGB",)):
            raise RuntimeError(f"{path.name}: unexpected mode {im.mode}")
        icc = im.info.get("icc_profile")
        if not icc:
            raise RuntimeError(f"{path.name}: missing ICC profile")
        desc = ImageCms.getProfileDescription(ImageCms.ImageCmsProfile(io.BytesIO(icc))).strip()
        if "srgb" not in desc.lower():
            raise RuntimeError(f"{path.name}: profile is not sRGB ({desc})")
        return {
            "path": path,
            "size": im.size,
            "mode": im.mode,
            "bytes": raw,
            "profile": desc,
        }


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate Melotag store artwork")
    parser.add_argument("--out", type=Path, default=IMAGES, help="output root (default: packaging/store/images)")
    parser.add_argument(
        "--only",
        choices=("all", "tile", "box", "posters", "hero"),
        default="all",
        help="generate only one group",
    )
    parser.add_argument(
        "--no-font-hash",
        action="store_true",
        help="skip the pinned SHA-256 checks for installed fonts (not recommended)",
    )
    args = parser.parse_args()

    global FONTS
    FONTS = resolve_fonts(verify_hashes=not args.no_font_hash)
    _FONT_CACHE.clear()
    audit_text_coverage()

    print("Fonts (installed locally, SIL OFL 1.1, never bundled):")
    for key, resolved in FONTS.items():
        source = resolved.source
        face = f"index {source.index}"
        if source.axes is not None:
            face += f", axes {list(source.axes)}"
        rank = "primary" if resolved.fallback_rank == 0 else f"fallback #{resolved.fallback_rank}"
        print(f"  {key:15s} {source.family} {source.style}")
        print(f"  {'':15s} {source.path} ({face}) [{rank}]")
    print()

    out: Path = args.out
    results: list[dict] = []

    if args.only in ("all", "tile"):
        results.append(render_app_tile(out / "logo" / "app-tile-300x300.png"))
    if args.only in ("all", "box"):
        results.append(render_box_art(out / "logo" / "box-art-1080x1080.png"))
    if args.only in ("all", "posters"):
        for lang, spec in POSTER_SPECS.items():
            results.append(render_poster(lang, spec, out / "poster" / lang / "poster-720x1080.png"))
    if args.only in ("all", "hero"):
        results.append(render_hero(out / "hero" / "super-hero-1920x1080.png"))

    checked = []
    for item in results:
        checked.append(
            verify_png(item["path"], item["size"], allow_alpha=item["path"].name == "app-tile-300x300.png")
        )

    print("Melotag store artwork")
    print("-" * 74)
    for info in checked:
        print(
            f"{info['path'].relative_to(ROOT) if info['path'].is_relative_to(ROOT) else info['path']}"
            f"  {info['size'][0]}x{info['size'][1]}  {info['mode']}"
            f"  {info['bytes'] / 1024:.1f} KiB  [{info['profile']}]"
        )
    print("-" * 74)
    for item in results:
        if "text_bbox" in item:
            print(f"text bbox ({item['path'].name}): {item['text_bbox']}")
    print(f"OK: {len(checked)} PNG files")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
