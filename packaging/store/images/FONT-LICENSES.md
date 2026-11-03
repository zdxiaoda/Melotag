# 商店图片字体与许可证记录

本文记录 `packaging/store/tools/Generate-StoreArt.py` 生成商店图时实际使用的字体，核验日期 2026-11-03。

- 字体均为本机已安装字体，本次工作没有安装任何新字体。
- 字体均为 SIL Open Font License 1.1（OFL-1.1），允许商业使用。
- 仓库不复制、不捆绑、不再分发任何字体文件；`packaging/store/images/` 下只包含脚本渲染的 PNG。
- `images/screenshots/` 是应用真实截图，由另一脚本采集，不在本文范围内。

## 实际使用的字体

| 字体家族 / 字重 | 版本 | 本机文件 | 使用位置 |
| --- | --- | --- | --- |
| Noto Sans / Bold | 2.015 | `C:\Windows\Fonts\NotoSans-Bold.ttf` | 英文标题、按钮、品牌字 |
| Noto Sans / Regular | 2.015 | `C:\Windows\Fonts\NotoSans-Regular.ttf` | 英文正文、注释 |
| Source Han Sans SC / Bold | 2.005（2.005R） | `C:\Users\admin\AppData\Local\Microsoft\Windows\Fonts\SourceHanSansSC-Bold.otf` | 中文标题、按钮、box art「音签」；日文标题、按钮 |
| Source Han Sans SC / Regular | 2.005（2.005R） | `C:\Users\admin\AppData\Local\Microsoft\Windows\Fonts\SourceHanSansSC-Regular.otf` | 中文正文、注释；日文正文、注释 |

实际只使用上述 4 个字体文件。日文没有单独的字体文件，复用 Source Han Sans SC 的两个字重。

主文件 SHA-256（`Generate-StoreArt.py` 会核对；字体更新导致不一致时应重新核验许可证）：

```text
NotoSans-Bold.ttf             3a08a47daa00cade516425c15c57615aef2fd418ec9811a7b9f465088f92cc05
NotoSans-Regular.ttf          f5f552c8c5edb61fe6efb824baf4d4de47b1a8689ab4925ff43f7bd6a4ebece5
SourceHanSansSC-Bold.otf      df2b90f5bcc6d01dfc964cec5f6d535d6b6aebd26ed7fd79a9c1b3f2112fcb6b
SourceHanSansSC-Regular.otf   f1d8611151880c6c336aabeac4640ef434fa13cbfbf1ffe82d0a71b2a5637256
```

## 许可证与商用结论

| 字体 | 许可证 | 官方文本 | 商用结论 |
| --- | --- | --- | --- |
| Noto Sans | SIL OFL 1.1 | https://github.com/notofonts/latin-greek-cyrillic/blob/main/OFL.txt | 允许；未声明 Reserved Font Name。 |
| Source Han Sans SC（思源黑体） | SIL OFL 1.1 | https://github.com/adobe-fonts/source-han-sans/blob/master/LICENSE.txt | 允许；Reserved Font Name 为 `Source`，本仓库不修改、不再分发字体软件。 |

OFL-1.1 官方说明：https://openfontlicense.org/ 。许可证义务主要针对字体软件本身的再分发；本仓库只提交渲染后的 PNG，不附带字体二进制或 OFL 文本。

## 字符覆盖与日文字形

渲染前脚本会对三语文案逐字符检查，缺字即中止；本次中文、日文、英文均为 0 缺字。

本机没有安装日文专用 OFL 字体，日文海报使用 Source Han Sans SC（简体中文区域字形）。该字体完整覆盖日文假名、日文标点和所用汉字，无豆腐块或缺字；差异仅限个别汉字的区域字形（笔画风格），不影响可读性和版式。若后续改用 Source Han Sans JP 等日文字体，应同步更新 `FONT_CANDIDATES` 并重新核验覆盖、路径和哈希。

## 复现

在仓库根目录执行：

```bash
uv run --offline --with pillow==13.0.0 python packaging/store/tools/Generate-StoreArt.py
```

脚本只读取本机字体目录，不访问网络；默认重新生成 `packaging/store/images/` 下的商店图。更换字体时需同时满足本机已安装、OFL 或等效可商用许可、覆盖三语全部文案，并更新 `FONT_CANDIDATES` 与本文档。
