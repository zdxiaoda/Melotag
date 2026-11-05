# 宣传片配乐

`soft-music-cc0.mp3` 是商店宣传片的背景音乐。文件随仓库提交，因为 CC0 允许再分发；体积 2.1 MB。

| 项目 | 值 |
| --- | --- |
| 曲目 | Soft Music |
| 页面署名 | FlavioConcini |
| 来源 | https://freesound.org/people/DaveJf/sounds/717233/ |
| 许可证 | Creative Commons 0 1.0（CC0，公有领域贡献） |
| 许可证原文 | “You can copy, modify, distribute and perform the sound, even for commercial purposes, all without the need of asking permission to the author.” |
| 本地文件 | `soft-music-cc0.mp3` |
| 规格 | 89.2 秒、44.1 kHz、立体声、约 193 kbps MP3 |
| SHA-256 | `6584e4ccfa29187fb0afe718608214de22a44fec987f50655de13e674aa37a29` |
| 获取日期 | 2026-11-05 |

## 使用方式

- `Build-StoreTrailer.py` 默认读取该文件：截取前 39 秒，归一化到 **-22 LUFS**，首尾淡入淡出，再与画面合成。
- 换曲：`uv run --offline --with pillow python packaging/store/tools/Build-StoreTrailer.py --lang zh-CN --music <文件>`。
- 文件缺失时脚本会退回内置的合成铺底（低频 + 粉噪），只是备选，不用于正式提交。

## 版权说明

- CC0 不要求署名，也不限制商用；这里保留出处只是便于追溯。
- 不使用任何需要署名（CC BY）、禁止商用或需要订阅的音源；不含第三方采样。
- 配乐只嵌入在 `Melotag-<ver>-trailer-<lang>.mp4` 中，不单独作为音频素材分发。
- 重新下载：
  ```bash
  curl -L -o packaging/store/audio/soft-music-cc0.mp3 \
    https://cdn.freesound.org/previews/717/717233_11861866-hq.mp3
  ```
  下载后用 `sha256sum` 核对上表校验和。
