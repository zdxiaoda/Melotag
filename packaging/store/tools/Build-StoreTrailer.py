#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Build the Microsoft Store trailer from a raw capture take.

Input
-----
packaging/store/trailer/<lang>/raw.mp4 + beats.json, produced by
Capture-StoreScreenshots.ps1 -Trailer.

Output
------
packaging/store/trailer/<lang>/Melotag-<version>-trailer-<lang>.mp4
packaging/store/trailer/<lang>/thumbnail-1920x1080.png

The finished video is 1920x1080 / 30 fps / H.264 High + AAC, with a title
card, the real app footage on a blurred backdrop, timed captions and a
synthesised ambient bed. No third-party audio, fonts or footage is used.

Requirements
------------
Python 3.10+, Pillow (FreeType build) and ffmpeg/ffprobe on PATH. Runs
offline; card text uses the same locally installed, OFL-licensed fonts as
Generate-StoreArt.py.

Run
---
    uv run --offline --with pillow python packaging/store/tools/Build-StoreTrailer.py
    uv run --offline --with pillow python packaging/store/tools/Build-StoreTrailer.py --lang zh-CN --dry-run
"""

from __future__ import annotations

import argparse
import importlib.util
import json
import re
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any

from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[3]
TOOLS = Path(__file__).resolve().parent
TRAILER_ROOT = ROOT / "packaging" / "store" / "trailer"
MUSIC_DEFAULT = ROOT / "packaging" / "store" / "audio" / "soft-music-cc0.mp3"
BUILD_PROPS = ROOT / "Directory.Build.props"

OUT_W, OUT_H = 1920, 1080
FPS = 30
TITLE_SECONDS = 4.0
END_SECONDS = 4.6
OUTRO_HOLD = 2.4                  # seconds of footage held after the last beat

# Caption text per language, keyed by the beat names written by the capture
# script. A beat without a caption is skipped; each caption runs until the
# next beat.
CAPTIONS: dict[str, dict[str, str]] = {
    "zh-CN": {
        "import": "整库载入，一眼看清状态",
        "select": "选中曲目，右侧直接编辑标签",
        "lyrics": "歌词：时间轴整理、导入与导出",
        "rename": "重命名：模板或正则，先预览再执行",
        "settings": "设置：在线源、封面、写入与界面",
        "outro": "标签在本地读写，音频不上传",
    },
    "en-US": {
        "import": "Drop in a folder — the whole library loads",
        "select": "Pick a track, edit its tags on the right",
        "lyrics": "Lyrics: clean time tags, import and export",
        "rename": "Rename with templates or regex — preview first",
        "settings": "Settings for sources, covers, writing and UI",
        "outro": "Tags are read and written locally. No uploads.",
    },
    "ja-JP": {
        "import": "フォルダーを投入、ライブラリを一括表示",
        "select": "曲を選んで右側でタグを直接編集",
        "lyrics": "歌詞：タイムタグ整理と入出力",
        "rename": "テンプレート / 正規表現で名前変更をプレビュー",
        "settings": "設定：オンラインソース、カバー、書き込み、UI",
        "outro": "タグはローカルで読み書き。音声は送信しません。",
    },
}

TITLE: dict[str, tuple[str, str, str]] = {
    "zh-CN": ("音签 Melotag", "本地音乐标签编辑器", "批量匹配 · 封面 · 歌词 · 重命名"),
    "en-US": ("Melotag", "Local music tag editor", "Match · covers · lyrics · rename"),
    "ja-JP": ("Melotag", "ローカル音楽タグエディター", "一括取得 · カバー · 歌詞 · 名前変更"),
}

END: dict[str, tuple[str, str]] = {
    "zh-CN": ("免费 · Windows 10 / 11 · x64 与 arm64", "github.com/zdxiaoda/Melotag"),
    "en-US": ("Free · Windows 10 / 11 · x64 and arm64", "github.com/zdxiaoda/Melotag"),
    "ja-JP": ("無料 · Windows 10 / 11 · x64 と arm64", "github.com/zdxiaoda/Melotag"),
}


# --------------------------------------------------------------------------- #
# Helpers
# --------------------------------------------------------------------------- #

def run(command: list[str], *, cwd: Path | None = None) -> str:
    result = subprocess.run(
        command, cwd=str(cwd) if cwd else None,
        stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8",
    )
    if result.returncode != 0:
        raise SystemExit(f"command failed ({result.returncode}): {' '.join(command)}\n{result.stdout[-4000:]}")
    return result.stdout


def ffmpeg() -> str:
    binary = shutil.which("ffmpeg")
    if not binary:
        raise SystemExit("ffmpeg was not found on PATH.")
    return binary


def ffprobe() -> str:
    binary = shutil.which("ffprobe")
    if not binary:
        raise SystemExit("ffprobe was not found on PATH.")
    return binary


def app_version() -> str:
    text = BUILD_PROPS.read_text(encoding="utf-8")
    match = re.search(r"<Version>\s*([^<\s]+)\s*</Version>", text)
    return match.group(1) if match else "0.0.0"


def load_store_art():
    """Import Generate-StoreArt.py so cards share its palette and fonts."""
    spec = importlib.util.spec_from_file_location("store_art", TOOLS / "Generate-StoreArt.py")
    if spec is None or spec.loader is None:
        raise SystemExit("Could not load Generate-StoreArt.py.")
    module = importlib.util.module_from_spec(spec)
    # dataclasses look their module up in sys.modules during class creation.
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    try:
        module.FONTS = module.resolve_fonts()
    except SystemExit:
        module.FONTS = module.resolve_fonts(verify_hashes=False)
    return module


def draw_centered_text(
    draw: ImageDraw.ImageDraw,
    art: Any,
    text: str,
    font_key: str,
    size: int,
    cy: float,
    fill: tuple[int, int, int, int],
    tracking: float = 0.0,
    width: int = OUT_W,
) -> None:
    font = art.load_font(font_key, art.s(size))
    art.draw_tracked(
        draw, width * art.SS / 2, cy * art.SS, text, font, fill,
        tracking=art.s(tracking), align="center",
    )


def background(art: Any, W: int, H: int) -> Image.Image:
    img = art.make_background(
        W, H, (12, 16, 36), (3, 4, 10),
        [
            (0.18, 0.20, 0.50, art.PURPLE, 100),
            (0.86, 0.74, 0.52, art.CYAN, 88),
            (0.52, 0.02, 0.46, (118, 74, 240), 42),
            (0.48, 1.06, 0.62, (20, 150, 190), 34),
        ],
        vignette=0.52,
        grain=0.014,
    )
    deco = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    dd = ImageDraw.Draw(deco)
    art.draw_dot_grid(dd, art.s(60), art.s(56), art.s(360), art.s(250), art.s(32), max(1, art.s(1.1)), alpha=18)
    art.draw_dot_grid(dd, art.s(1560), art.s(830), art.s(1860), art.s(1024), art.s(32), max(1, art.s(1.1)), alpha=18)
    art.draw_corner_ticks(dd, W, H, art.s(54), art.s(14))
    img.paste(deco, (0, 0), deco)
    art.add_particles(img, 72, seed=1907, clear_center=(W / 2, H * 0.52), clear_radius=art.s(260))
    return img


def font_keys(lang: str) -> tuple[str, str]:
    """Bold and regular font roles for the language's copy.

    Japanese copy needs CJK coverage too; only Latin copy can use Noto Sans.
    """
    if lang == "zh-CN":
        return "zh_bold", "zh_regular"
    if lang == "ja-JP":
        return "ja_bold", "ja_regular"
    return "latin_semibold", "latin_regular"


def render_title_card(art: Any, lang: str, path: Path) -> None:
    W, H = OUT_W * art.SS, OUT_H * art.SS
    img = background(art, W, H)
    art.add_mark(img, W / 2, 0.44 * H, art.s(260), glow=True, shadow=True)

    draw = ImageDraw.Draw(img)
    bold, regular = font_keys(lang)
    name, tagline, chips = TITLE[lang]
    draw_centered_text(draw, art, name, bold, 88, 0.655 * 1080, art.rgba(art.HEAD), tracking=0.6)
    draw_centered_text(draw, art, tagline, regular, 38, 0.758 * 1080, art.rgba(art.BODY))
    draw_centered_text(draw, art, chips, regular, 26, 0.815 * 1080, art.rgba(art.DIM), tracking=1.2)

    bar = Image.new("RGBA", img.size, (0, 0, 0, 0))
    bd = ImageDraw.Draw(bar)
    bx0, bx1 = W / 2 - art.s(120), W / 2 + art.s(120)
    by = int(0.712 * H)
    for x in range(int(bx0), int(bx1)):
        t = (x - bx0) / max(1.0, bx1 - bx0)
        color = art.mix(art.PURPLE, art.CYAN, t)
        bd.line([(x, by), (x, by + art.s(2))], fill=art.rgba(color, 200))
    img.paste(bar, (0, 0), bar)

    img = img.resize((OUT_W, OUT_H), Image.Resampling.LANCZOS)
    art.save_png(img, path)


def render_end_card(art: Any, lang: str, path: Path) -> None:
    W, H = OUT_W * art.SS, OUT_H * art.SS
    img = background(art, W, H)
    art.add_mark(img, W / 2, 0.335 * H, art.s(210), glow=True, shadow=True)

    draw = ImageDraw.Draw(img)
    bold, regular = font_keys(lang)
    name, _, _ = TITLE[lang]
    draw_centered_text(draw, art, name, bold, 72, 0.505 * 1080, art.rgba(art.HEAD), tracking=0.6)
    requirement, url = END[lang]
    draw_centered_text(draw, art, requirement, regular, 34, 0.585 * 1080, art.rgba(art.BODY))
    draw_centered_text(draw, art, url, "latin_regular", 26, 0.645 * 1080, art.rgba(art.DIM), tracking=0.8)

    img = img.resize((OUT_W, OUT_H), Image.Resampling.LANCZOS)
    art.save_png(img, path)


def render_thumbnail(art: Any, lang: str, screenshot: Path, path: Path) -> None:
    W, H = OUT_W * art.SS, OUT_H * art.SS
    img = background(art, W, H)
    draw = ImageDraw.Draw(img)

    name, tagline, chips = TITLE[lang]
    left_x = art.s(120)
    mark_size = art.s(150)
    art.add_mark(img, left_x + mark_size / 2, 0.325 * H, mark_size, glow=True, shadow=True)

    bold, regular = font_keys(lang)
    font = art.load_font(bold, art.s(76))
    draw.text((left_x, 0.455 * 1080 * art.SS), name, font=font, fill=art.rgba(art.HEAD), anchor="ls")

    font = art.load_font(regular, art.s(38))
    draw.text((left_x, 0.535 * 1080 * art.SS), tagline, font=font, fill=art.rgba(art.BODY), anchor="ls")

    font = art.load_font(regular, art.s(24))
    for i, chip in enumerate(chips.split(" · ")):
        y = 0.63 * 1080 + i * 46
        art.draw_tracked(draw, left_x, y * art.SS, "· " + chip, font, art.rgba(art.DIM), tracking=art.s(0.8))

    # Framed app screenshot on the right.
    with Image.open(screenshot) as shot:
        shot = shot.convert("RGB")
        target_w, target_h = 1000 * art.SS, 562 * art.SS
        shot = shot.resize((target_w, target_h), Image.Resampling.LANCZOS)
    shadow = Image.new("RGBA", (target_w + art.s(80), target_h + art.s(80)), (0, 0, 0, 0))
    sd = ImageDraw.Draw(shadow)
    sd.rounded_rectangle(
        (art.s(40), art.s(44), art.s(40) + target_w, art.s(44) + target_h),
        radius=art.s(18), fill=(0, 0, 0, 150),
    )
    shadow = shadow.filter(ImageFilter.GaussianBlur(art.s(20)))
    shot_x, shot_y = 0.445 * W, (H - target_h) / 2 + art.s(10)
    img.paste(shadow, (int(shot_x - art.s(40)), int(shot_y - art.s(44))), shadow)
    img.paste(shot, (int(shot_x), int(shot_y)))
    dd = ImageDraw.Draw(img)
    dd.rounded_rectangle(
        (shot_x, shot_y, shot_x + target_w, shot_y + target_h),
        radius=art.s(12), outline=art.rgba(art.CYAN_LIGHT, 90), width=max(1, art.s(1.4)),
    )

    img = img.resize((OUT_W, OUT_H), Image.Resampling.LANCZOS)
    art.save_png(img, path)


def render_scrim(art: Any, path: Path) -> None:
    """Bottom gradient that keeps captions readable over the app's status bar."""
    height = OUT_H * art.SS
    width = OUT_W * art.SS
    image = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    ramp = 210 * art.SS
    for offset in range(ramp):
        y = height - ramp + offset
        progress = offset / max(1, ramp - 1)
        alpha = int(96 * (progress ** 1.25))
        draw.line([(0, y), (width, y)], fill=(6, 8, 16, alpha))
    image = image.resize((OUT_W, OUT_H), Image.Resampling.LANCZOS)
    art.save_png(image, path)


def measure_integrated_lufs(path: Path) -> float:
    output = run([
        ffmpeg(), "-hide_banner", "-nostats", "-i", str(path),
        "-af", "ebur128=peak=true", "-f", "null", "-",
    ])
    match = re.search(r"Integrated loudness:\s*I:\s*(-?[\d.]+) LUFS", output)
    return float(match.group(1)) if match else -20.0


def make_music_bed(music: Path, path: Path, seconds: float, target_lufs: float = -22.0) -> dict[str, float]:
    """Trim the CC0 track, normalise it to a fixed loudness and fade the ends."""
    trimmed = path.with_name("music-trim.wav")
    run([
        ffmpeg(), "-hide_banner", "-loglevel", "error", "-y",
        "-i", str(music),
        "-af", f"atrim=0:{seconds:.3f},asetpts=PTS-STARTPTS,aresample=48000",
        "-ac", "2", str(trimmed),
    ])
    source_lufs = measure_integrated_lufs(trimmed)
    gain_db = max(-24.0, min(24.0, target_lufs - source_lufs))
    run([
        ffmpeg(), "-hide_banner", "-loglevel", "error", "-y",
        "-i", str(trimmed),
        "-af", (
            f"volume={gain_db:.2f}dB,"
            "afade=t=in:st=0:d=1.6,"
            f"afade=t=out:st={max(0.0, seconds - 3.2):.2f}:d=3.2"
        ),
        "-ar", "48000", "-ac", "2", str(path),
    ])
    trimmed.unlink(missing_ok=True)
    levels = measure_audio(path)
    levels["integrated_lufs"] = measure_integrated_lufs(path)
    levels["source_lufs"] = source_lufs
    levels["gain_db"] = gain_db
    return levels


def make_ambient_bed(path: Path, seconds: float, target_peak_db: float = -19.0) -> dict[str, float]:
    """Synthesise a quiet ambient bed and normalise it to a fixed peak.

    ffmpeg's sine generator peaks at about -18 dBFS, so the mix is levelled by
    measurement instead of hand-tuned absolute volumes.
    """
    durations = f"{seconds:.3f}"
    raw = path.with_name("ambient-raw.wav")
    run([
        ffmpeg(), "-hide_banner", "-loglevel", "error", "-y",
        "-f", "lavfi", "-i", f"sine=frequency=110:sample_rate=48000:duration={durations}",
        "-f", "lavfi", "-i", f"sine=frequency=164.81:sample_rate=48000:duration={durations}",
        "-f", "lavfi", "-i", f"sine=frequency=220.5:sample_rate=48000:duration={durations}",
        "-f", "lavfi", "-i", f"sine=frequency=329.63:sample_rate=48000:duration={durations}",
        "-f", "lavfi", "-i", f"anoisesrc=color=pink:sample_rate=48000:amplitude=0.4:duration={durations}",
        "-filter_complex",
        (
            "[0:a]volume=1.6,tremolo=f=0.10:d=0.25[a0];"
            "[1:a]volume=1.0,tremolo=f=0.12:d=0.30[a1];"
            "[2:a]volume=0.5,tremolo=f=0.14:d=0.20[a2];"
            "[3:a]volume=0.28[a3];"
            "[4:a]lowpass=f=650,volume=0.06[air];"
            "[a0][a1][a2][a3][air]amix=inputs=5:normalize=0,"
            "lowpass=f=1300,aecho=0.7:0.55:120|260:0.22|0.14"
        ),
        "-ar", "48000", "-ac", "2", str(raw),
    ])

    peak_db = detect_peak_db(raw)
    gain_db = max(-6.0, min(24.0, target_peak_db - peak_db))
    run([
        ffmpeg(), "-hide_banner", "-loglevel", "error", "-y",
        "-i", str(raw),
        "-af", (
            f"volume={gain_db:.2f}dB,"
            f"afade=t=in:st=0:d=2.6,"
            f"afade=t=out:st={max(0.0, seconds - 3.4):.2f}:d=3.4"
        ),
        "-ar", "48000", "-ac", "2", str(path),
    ])
    raw.unlink(missing_ok=True)
    return measure_audio(path)


def detect_peak_db(path: Path) -> float:
    output = run([
        ffmpeg(), "-hide_banner", "-nostats", "-i", str(path),
        "-af", "volumedetect", "-f", "null", "-",
    ])
    match = re.search(r"max_volume:\s*(-?[\d.]+) dB", output)
    return float(match.group(1)) if match else -18.0


def measure_audio(path: Path) -> dict[str, float]:
    probe = ffprobe()
    output = run([
        probe, "-v", "error", "-show_entries", "stream=sample_rate,channels", "-of", "json", str(path),
    ])
    stream = json.loads(output)["streams"][0]
    stats = run([
        ffmpeg(), "-hide_banner", "-nostats", "-i", str(path),
        "-af", "volumedetect", "-f", "null", "-",
    ])
    peak = re.search(r"max_volume:\s*(-?[\d.]+) dB", stats)
    rms = re.search(r"mean_volume:\s*(-?[\d.]+) dB", stats)
    return {
        "sample_rate": float(stream["sample_rate"]),
        "channels": float(stream["channels"]),
        "peak_db": float(peak.group(1)) if peak else 0.0,
        "rms_db": float(rms.group(1)) if rms else 0.0,
    }


def build(
    lang: str,
    *,
    dry_run: bool,
    work: Path,
    keep_work: bool,
    music: Path | None = None,
) -> dict[str, Any]:
    take_dir = TRAILER_ROOT / lang
    raw = take_dir / "raw.mp4"
    beats_path = take_dir / "beats.json"
    if not raw.exists() or not beats_path.exists():
        raise SystemExit(f"Missing take in {take_dir}. Run Capture-StoreScreenshots.ps1 -Trailer -Languages {lang}")

    meta = json.loads(beats_path.read_text(encoding="utf-8"))
    beats = [b for b in meta["beats"] if b["name"] in CAPTIONS[lang]]
    if not beats:
        raise SystemExit("No captioned beat in beats.json.")

    raw_duration = float(meta["duration"])
    footage_end = min(raw_duration - 0.2, beats[-1]["t"] + OUTRO_HOLD)
    total = TITLE_SECONDS + footage_end + END_SECONDS

    art = load_store_art()
    work.mkdir(parents=True, exist_ok=True)
    title_png = work / "title.png"
    end_png = work / "end.png"
    scrim_png = work / "scrim.png"
    thumbnail_png = take_dir / "thumbnail-1920x1080.png"
    bed_wav = work / "ambient.wav"
    render_title_card(art, lang, title_png)
    render_end_card(art, lang, end_png)
    render_scrim(art, scrim_png)
    render_thumbnail(art, lang, ROOT / "packaging" / "store" / "images" / "screenshots" / lang / "01-main.png", thumbnail_png)
    track = music or MUSIC_DEFAULT
    if track.exists():
        levels = make_music_bed(track, bed_wav, total)
        music_source = track.name
    else:
        print(f"music not found ({track}); falling back to the synthesised bed", file=sys.stderr)
        levels = make_ambient_bed(bed_wav, total)
        music_source = "synthesised"

    captions = []
    for index, beat in enumerate(beats):
        # Beat times are relative to the footage segment (which starts at t=0
        # after trim/setpts), not to the finished video.
        start = float(beat["t"])
        stop = float(beats[index + 1]["t"]) if index + 1 < len(beats) else footage_end
        if stop - start < 0.4:
            continue
        text = CAPTIONS[lang][beat["name"]]
        text_file = work / f"caption-{index:02d}.txt"
        text_file.write_text(text, encoding="utf-8")
        captions.append((text_file.name, text, start, stop))

    version = app_version()
    output = take_dir / f"Melotag-{version}-trailer-{lang}.mp4"

    # drawtext needs a font file with CJK coverage; Source Han Sans SC is OFL.
    caption_key, _ = font_keys(lang)
    font_path = art.FONTS[caption_key].source.path
    caption_font = art.load_font(caption_key, 36)
    # Anchor the plate to the bottom edge: some dialogs draw their own status
    # bar right at the bottom of the frame.
    plate_x, plate_y = 0, OUT_H - 92
    plate_h = OUT_H - plate_y
    text_x, text_y = 48, plate_y + 24
    draw = []
    for name, text, start, stop in captions:
        text_width = int(round(caption_font.getlength(text)))
        draw.append(
            "drawbox=x={plate_x}:y={plate_y}:w={plate_w}:h={plate_h}:color=0x05070E@0.95:t=fill:"
            "enable='between(t,{start:.3f},{stop:.3f})'".format(
                plate_x=plate_x, plate_y=plate_y, plate_w=(text_x - plate_x) + text_width + 32,
                plate_h=plate_h, start=start, stop=stop,
            )
        )
        draw.append(
            "drawbox=x={plate_x}:y={plate_y}:w=6:h={plate_h}:color=0x38BDF8@0.95:t=fill:"
            "enable='between(t,{start:.3f},{stop:.3f})'".format(
                plate_x=plate_x, plate_y=plate_y, plate_h=plate_h, start=start, stop=stop,
            )
        )
        draw.append(
            "drawtext=textfile={file}:fontfile='{font}':fontsize=36:fontcolor=0xEAF1FF:"
            "shadowcolor=0x000000@0.4:shadowx=0:shadowy=1:"
            "x={text_x}:y={text_y}:enable='between(t,{start:.3f},{stop:.3f})'".format(
                file=name,
                font=str(font_path).replace("\\", "/").replace(":", "\\:"),
                text_x=text_x,
                text_y=text_y,
                start=start,
                stop=stop,
            )
        )

    filters = (
        "[0:v]fade=t=in:st=0:d=0.5,"
        f"fade=t=out:st={TITLE_SECONDS - 0.7:.2f}:d=0.7,"
        "fps=30,format=yuv420p,settb=AVTB[v0];"
        f"[1:v]trim=start=0:end={footage_end:.3f},setpts=PTS-STARTPTS,fps=30,"
        f"scale={OUT_W}:{OUT_H}:flags=lanczos[comp];"
        "[3:v]format=rgba[scrim];"
        "[comp][scrim]overlay=0:0:format=auto[withscrim];"
        "[withscrim]" + ",".join(draw)
        + f",fade=t=in:st=0:d=0.5,fade=t=out:st={footage_end - 0.4:.2f}:d=0.4"
        + ",format=yuv420p,settb=AVTB[v1];"
        "[2:v]fade=t=in:st=0:d=0.6,fps=30,format=yuv420p,settb=AVTB[v2];"
        "[v0][v1][v2]concat=n=3:v=1:a=0,fps=30,format=yuv420p[v]"
    )

    command = [
        ffmpeg(), "-hide_banner", "-loglevel", "error", "-y",
        "-loop", "1", "-t", f"{TITLE_SECONDS:.3f}", "-i", str(title_png),
        "-i", str(raw),
        "-loop", "1", "-t", f"{END_SECONDS:.3f}", "-i", str(end_png),
        "-loop", "1", "-t", f"{footage_end:.3f}", "-i", str(scrim_png),
        "-i", str(bed_wav),
        "-filter_complex", filters,
        "-map", "[v]", "-map", "4:a",
        "-c:v", "libx264", "-preset", "slow", "-crf", "17", "-profile:v", "high",
        "-maxrate", "18M", "-bufsize", "36M", "-g", "60",
        "-c:a", "aac", "-b:a", "160k", "-ar", "48000",
        "-t", f"{total:.3f}",
        "-movflags", "+faststart",
        str(output),
    ]

    if dry_run:
        print(f"output: {output}")
        print(f"duration: {total:.2f}s (title {TITLE_SECONDS}s + footage {footage_end:.2f}s + end {END_SECONDS}s)")
        print(f"beat offsets: {[(b['name'], round(TITLE_SECONDS + b['t'], 2)) for b in beats]}")
        print(f"music: {music_source}, peak {levels['peak_db']:.1f} dBFS, "
              f"rms {levels['rms_db']:.1f} dBFS, "
              f"lufs {levels.get('integrated_lufs', 0.0):.1f}")
        return {"output": str(output), "dry_run": True}

    run(command, cwd=work)

    probe = json.loads(run([
        ffprobe(), "-v", "error", "-show_streams", "-show_format", "-of", "json", str(output),
    ]))
    video = next(s for s in probe["streams"] if s["codec_type"] == "video")
    audio = next(s for s in probe["streams"] if s["codec_type"] == "audio")
    num, den = video["r_frame_rate"].split("/")
    fps = float(num) / float(den)
    duration = float(probe["format"]["duration"])
    faststart = output.read_bytes()[:4096].find(b"moov") != -1

    problems = []
    if (video["width"], video["height"]) != (OUT_W, OUT_H):
        problems.append(f"size {video['width']}x{video['height']}")
    if abs(fps - FPS) > 0.01:
        problems.append(f"fps {fps}")
    if video["pix_fmt"] != "yuv420p":
        problems.append(f"pix_fmt {video['pix_fmt']}")
    if audio["codec_name"] != "aac":
        problems.append(f"audio {audio['codec_name']}")
    if not 12 <= duration <= 60:
        problems.append(f"duration {duration:.1f}s")
    size_mb = output.stat().st_size / (1024 * 1024)
    if size_mb > 200:
        problems.append(f"size {size_mb:.1f} MB")
    if problems:
        raise SystemExit("Output validation failed: " + ", ".join(problems))

    if not keep_work:
        for item in work.iterdir():
            item.unlink()

    return {
        "output": str(output),
        "thumbnail": str(thumbnail_png),
        "duration": round(duration, 2),
        "size_mb": round(size_mb, 1),
        "fps": round(fps, 3),
        "faststart": faststart,
        "music": music_source,
        "music_peak_db": round(levels["peak_db"], 1),
        "music_rms_db": round(levels["rms_db"], 1),
        "music_lufs": round(levels.get("integrated_lufs", 0.0), 1),
        "captions": len(captions),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--lang", default="zh-CN", choices=sorted(CAPTIONS))
    parser.add_argument("--music", default="", help="background track (default: packaging/store/audio/soft-music-cc0.mp3)")
    parser.add_argument("--work", default="", help="scratch directory (default: <take>/.build)")
    parser.add_argument("--keep-work", action="store_true", help="keep generated cards, captions and the bed wav")
    parser.add_argument("--dry-run", action="store_true", help="print the plan without encoding")
    arguments = parser.parse_args()

    take_dir = TRAILER_ROOT / arguments.lang
    work = Path(arguments.work) if arguments.work else take_dir / ".build"
    music = Path(arguments.music) if arguments.music else None
    result = build(arguments.lang, dry_run=arguments.dry_run, work=work, keep_work=arguments.keep_work, music=music)
    print(json.dumps(result, indent=2, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
