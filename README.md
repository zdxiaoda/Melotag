# 音签 Melotag 1.0.0

<p>
  <img src="docs/branding/melotag-logo.png" width="88" alt="音签 Melotag">
</p>

跨平台音乐标签编辑器的独立实现（Windows / Linux 桌面，Android 暂缓）。功能规格参考 MusicTag v1.0.9.0 的公开行为，代码与素材全部独立实现。

- 项目图表：[docs/charts.html](docs/charts.html)（架构、代码规模、功能完成度、在线源能力矩阵、容器支持）
- 功能规格：[docs/功能清单与复刻方案.md](docs/功能清单与复刻方案.md)
- 跨平台设计：[docs/跨平台重写方案.md](docs/跨平台重写方案.md)
- 品牌资源：[docs/branding/](docs/branding/)（可编辑的 draw.io / SVG 源文件）

> 分析阶段使用的原程序二进制、反编译产物与截图均未随仓库分发。

## 运行

```bash
dotnet build Melotag.slnx
dotnet test  Melotag.slnx
dotnet run --project src/Melotag.Desktop -- "D:\music"      # 参数可为文件/目录，也支持"打开方式"
dotnet run --project tools/Melotag.Probe -- "晴天" "周杰伦"   # 在线源诊断
dotnet run --project tools/Melotag.Branding -- emit .        # 重新生成 LOGO 与 .ico
```

数据目录：`%APPDATA%\Melotag`（设置 `settings.json`、历史 `history.db`），可用 `MELOTAG_DATA_DIR` 覆盖。

## 构建与发布

- **CI**（`.github/workflows/ci.yml`）：push 到 `main` 或提交 PR 时，在 Ubuntu 与 Windows 上做 Release 构建并跑全部单元测试。
- **发布**（`.github/workflows/release.yml`）：推 `v*` 标签（如 `v1.0.0`）即自动 测试 → 打包 → 创建 GitHub Release；手动触发只打包、不出 Release。
  - `Melotag-<版本>-win-x64.zip`：自包含单文件 `Melotag.exe`，无需安装 .NET
  - `Melotag-<版本>-linux-x64.tar.gz`：自包含 `Melotag` + 图标 + `.desktop` + `install.sh`
  - `melotag_<版本>_amd64.deb`：Debian / Ubuntu 安装包，`sudo apt install ./melotag_1.0.0_amd64.deb`
  - `SHA256SUMS.txt`

```bash
git tag v1.0.1 && git push origin v1.0.1   # 触发自动发布
```

## 已实现功能

### 文件与列表
- 添加文件 / 添加文件夹（递归）/ 拖放添加 / 命令行与"打开方式"
- 关键字过滤（文件名、标题、艺术家、专辑）
- 重新读取、从列表移除、全部保存、清空标签（内存）、批量提取封面
- 状态列（已读取/已修改/已保存/失败/跳过）+ 错误提示

### 标签
- 读取/写入：标题、艺术家、专辑、专辑艺术家、年份、音轨、碟号、流派、注释、作曲、歌词、封面
- 音频属性：格式、时长、码率、采样率、声道、文件大小
- 写选项：ID3v2.3/2.4、保留文件修改时间、歌词写入标签、封面替换/清除
- 支持格式：MP3 / FLAC / APE / WavPack / TTA / MPC / OGG / Opus / MP4 / M4A / WAV / AIFF / WMA / DSF / DFF（实际写入能力取决于 TagLib# 与容器）

### 在线标签匹配
- 国内源：网易云音乐（标签/封面/歌词）、QQ 音乐（标签/歌词）、酷狗（标签/封面/歌词）、酷我（标签/封面）
- 国际源：MusicBrainz（遵守 1 req/s + 可配置 UA）、Cover Art Archive、iTunes Search API（国家码可配，失败自动回落 US）、Last.fm（需自备 API key）、LRCLIB（歌词）
- 单文件候选对话框：查询条件预填、多源结果列表、合并方式（只填空字段/全部覆盖/保留现有）
- 批量自动匹配：逐文件取首条候选按"只填空字段"合并，带进度状态
- 网络层：共享连接池、每源并发上限、429/503 自动重试；源顺序与启用由设置里的三份源列表控制
- 源配置界面：设置 →「在线源 → 启用与顺序」中有标签 / 封面 / 歌词三个页签，逐项勾选启用、用 ↑↓ 调整优先级（越靠上越先查询），可一键恢复默认

### 歌词
- LRCLIB 搜索（同步歌词优先）
- LRC 处理：重排多时间戳、移除时间标签、删除空行、删除头部标签、整体偏移
- 导入 `.lrc`（自动识别 UTF-8/BOM/UTF-16/GB18030/Big5）
- 保存 `.lrc`（编码可选）
- 应用歌词到当前文件的标签

### 封面
- 在线搜索（iTunes / Cover Art Archive）、本地选择、导出到文件、移除
- 限制：体积上限、最大宽高、格式白名单；自动等比缩放与 JPEG 质量递减
- 保存时写回标签（ID3 APIC / Vorbis PICTURE / MP4 covr / ASF Picture）

### 重命名 / 文件名相关
- 模板 `@0–@8`（`@0` 不使用，`@1` 标题 … `@8` 专辑艺术家）+ 6 个预制模式
- 校验：空模板、无占位、只用 `@0`、重复占位、未知占位；"按文件名反写标签"时额外禁止相邻占位
- 正则模式：捕获组映射到字段，实时预览
- 冲突自动加 ` (2)`；可选连带重命名 `.lrc` 与封面文件
- 反向模式：按文件名/正则写回标签

### 简繁转换
- 标签简→繁 / 繁→简（OpenCC，tw2 规则）
- 文件名简→繁 / 繁→简（连带 `.lrc` 与封面）

### 历史与撤销
- 每次保存/重命名写入 SQLite 快照（`root_id + rel_path` 键，为 Android SAF 预留）
- 历史面板：查看、恢复选中快照（写回文件后重新读取）、清空全部
- 序列号自增，清空后 `VACUUM`

### 设置
- 搜索：并发线程、每源结果上限、自动匹配线程
- 在线源：Last.fm Key、iTunes 国家码、User-Agent
- 歌词：默认操作、默认编码、保存目录、文件名格式、跳过伴奏
- 封面：体积/宽/高上限
- 写入：ID3v2 版本、保留修改时间、歌词写入
- 界面：紧凑行高；主题深/浅持久化

### 工程
- 52 个单元测试：模板/正则/合并/LRC/编码/SQLite/改名/封面/简繁/TagLib 往返
- `tools/Melotag.Probe`：不依赖 UI 的在线源诊断
- 分层：Core（无 UI 依赖）/ Tags / Storage / UI / Desktop

## 尚未实现（可选或有意不做的）

| 项 | 原因 |
|---|---|
| Android head | 按你的要求本次不做；需 `dotnet workload install android` + SAF 版 `IFileHandle` |
| 界面多语言（英/繁） | 需要完整翻译与 resx 重构，属纯文案工程 |
| 托盘最小化 | 需要应用图标资源与 `TrayIcon` 集成 |
| 版本检查 | 没有更新服务端可指向；接入后约半天 |
| 自定义列/列序持久化 | DataGrid 目前固定列；可用 TreeDataGrid 重做 |
| 全局查找替换 | 过滤 + 面板直接编辑已覆盖主要场景 |
| 网易云/QQ/酷我/酷狗 私有接口 | 已按你的要求接入（只读查询，接口可能随平台调整失效；虾米已于 2021 年停服，未接入） |
| 作词字段写入 | TagLib# 2.3 未暴露 Lyricist，需按容器写 `TEXT`/`LYRICIST` |
| DSF/DFF 写入 | 能识别但未用真实 DSD 样本验证 |
| 多音轨音频 | TagLib 不支持按轨编辑，文件会被跳过 |

## 技术备注

- .NET 10 / Avalonia 12.1.4 / FluentAvaloniaUI 3.1.0 / TagLibSharp 2.3.0 / SkiaSharp 3.119.4 / Microsoft.Data.Sqlite 10 / OpenCC 0.1.2
- 图像处理用 SkiaSharp（MIT）：ImageSharp 4.x 的许可门禁会在 Release 构建失败，不适合随包分发
- 国内平台适配器只做只读搜索/歌词/封面查询，不内置任何版权内容；歌词与封面在用户请求时按需下载
- Avalonia 12：Fluent 主题拆分为独立包；`TextBox` 无 `Header`；`DataGrid` 列模板与默认编译绑定冲突（已关闭编译绑定）；拖放使用 `DragEventArgs.DataTransfer` + `DataFormat.File`
- MusicBrainz 要求可识别 User-Agent，请把设置里的 UA 改成含真实联系方式的字符串
