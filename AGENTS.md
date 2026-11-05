# AGENTS.md

面向开发者和自动化代理的项目说明。用户文档请回到 [README](README.md)。

## 项目结构

```
src/
  Melotag.Core/       标签模型、在线源、合并、重命名、LRC、更新检查（无 UI 依赖）
  Melotag.Tags/       TagLib# 读写实现
  Melotag.Storage/    settings.json、SQLite 历史、桌面平台路径
  Melotag.UI/         Avalonia 共享界面、三语资源、对话框、ViewModel
  Melotag.Desktop/    桌面入口（net10.0）
  Melotag.Android/    Android 头项目（net10.0-android，不在 Melotag.slnx 中）
tests/
  Melotag.Core.Tests/ 92 个单元测试
tools/
  Melotag.Probe/      在线源诊断
  Melotag.Branding/   LOGO / .ico / MSIX 图标生成
packaging/
  linux/              desktop、rpm spec
  windows/            Inno Setup、MSIX 清单与脚本
  flatpak/            Flatpak 清单与 desktop
docs/dev/             签名、上架等开发文档
```

## 构建与测试

```bash
dotnet build Melotag.slnx
dotnet test  Melotag.slnx
dotnet build Melotag.slnx -c Release

dotnet run --project src/Melotag.Desktop -- "D:\music"
dotnet run --project tools/Melotag.Probe -- "晴天" "周杰伦"
dotnet run --project tools/Melotag.Branding -- emit .
dotnet run --project tools/Melotag.Branding -- msix packaging/windows/msix/Assets
```

国内网络还原 NuGet 时使用镜像（不要把镜像配置提交成 `NuGet.Config`）：

```bash
dotnet restore Melotag.slnx \
  --source https://mirrors.huaweicloud.com/repository/nuget/v3/index.json
```

数据目录：`%APPDATA%\Melotag`（设置 `settings.json`、历史 `history.db`），可用 `MELOTAG_DATA_DIR` 覆盖。
Android 数据目录是应用私有空间 `files/data`。

## 代码约定

- .NET 10 / Avalonia 12 / CommunityToolkit.Mvvm / TagLib# 2.3 / SkiaSharp / Microsoft.Data.Sqlite。
- 可空引用类型已开启；新增代码尽量不引入警告。
- 用户可见文案统一放 `src/Melotag.UI/Localization/Strings*.resx`（中/日/英），不要在中途硬编码中文。
- 逻辑判断不要依赖本地化后的状态文本；使用 `TrackStatus` 等枚举。
- 平台差异集中在 `IFileHandle`、`IPlatformHost`、`IFilePickerPlatform`、`ModalHost`。
- 修改 XAML 后至少在本地跑一次 `dotnet build`；UI 文案改动同时检查三种语言的 resx 键是否齐全。
- 生成资源：`tools/Melotag.Branding`（`emit` 生成 LOGO 与 `.ico`，`msix` 生成 Store 图标）。

## 发布与打包

### 版本规则

- 应用版本只在 `Directory.Build.props` 的 `Version` 维护（当前 `1.5.0`）；桌面端、
  Android 的 `ApplicationDisplayVersion` 与默认 User-Agent 都由程序集版本自动派生，
  发布流水线也可用 `-p:Version=...` 覆盖。
- Android `ApplicationVersion`（versionCode）每次发布递增。
- MSIX 的 `Version` 必须是四段数字（例如 `1.2.1.0`），且高于商店已发布版本。

### 工作流

- `ci.yml`：Ubuntu + Windows 上 Release 构建并跑全部单元测试。
- `android.yml`：Android arm64/x64 APK；配置 Secrets 后使用固定签名。
- `release.yml`：
  - `publish` 矩阵构建 Windows x64/arm64 与 Linux x64；Windows 两个架构各出便携 zip 与安装包；
  - 独立 `msix` 作业合并两个架构的 MSIX 为 `.msixbundle`（未配置 `MSIX_*` 时跳过）；
  - 推 `v*` 标签或手动运行都会生成完整包集，并上传 `packages-<tag>` artifact；
  - 推标签或手动勾选 `publish` 时才创建/更新 GitHub Release；
  - 手动运行不勾选 `publish` 时只产出 artifact，不发布。

### 产物

| 产物 | 类型 | 说明 |
| --- | --- | --- |
| `Melotag-<ver>-win-{x64,arm64}.zip` | 框架依赖 | 单文件 exe，需要 .NET 10 运行时 |
| `Melotag-<ver>-win-{x64,arm64}-setup.exe` | 自带运行时 | Inno Setup 安装包（arm64 需 Inno 6.3+） |
| `Melotag-<ver>.msixbundle` | 自带运行时 | Microsoft Store 用，内含 x64 + arm64，需配置 `MSIX_*` Variables/Secrets |
| `Melotag-<ver>-linux-x64.tar.gz` | 框架依赖 | 单文件 + `.desktop` + `install.sh` |
| `melotag_<ver>_amd64.deb` | 框架依赖 | Debian / Ubuntu |
| `melotag-<ver>-1.x86_64.rpm` | 框架依赖 | Fedora / RHEL 系 |
| `Melotag-<ver>-x86_64.AppImage` | 自带运行时 | 免安装 |
| `Melotag-<ver>.flatpak` | 自带运行时 | 依赖 Freedesktop 运行时 |
| `Melotag-<ver>-android-{arm64,x64}.apk` | Android | 固定签名后可直接升级 |
| `SHA256SUMS.txt` | 校验和 | 所有发布包 |

### 打包实现

- **Windows 安装包**：`packaging/windows/melotag.iss`，Inno Setup 6；CI 在 `windows-latest` 上生成。
- **MSIX**：`packaging/windows/msix/Package.appxmanifest` + `build-msix.ps1`；需要 Partner Center 的三个产品标识，见
  [Microsoft Store 上架指南](docs/dev/MicrosoftStore上架指南.md)。
- **Linux RPM**：`packaging/linux/melotag.spec`，CI 用 `rpmbuild`。
- **AppImage**：self-contained Linux 发布 + `appimagetool`。
- **Flatpak**：`packaging/flatpak/io.melotag.app.yml` + Freedesktop 24.08 运行时。
- **Android 签名**：`tools/create-android-keystore.sh` + 4 个 GitHub Secrets，见
  [Android 签名与发布](docs/dev/Android签名与发布.md)。

### Android 签名 Secrets

| Secret | 说明 |
| --- | --- |
| `ANDROID_KEYSTORE_BASE64` | 密钥库文件的 Base64 |
| `ANDROID_KEYSTORE_PASSWORD` | 密钥库密码 |
| `ANDROID_KEY_ALIAS` | 密钥别名 |
| `ANDROID_KEY_PASSWORD` | 密钥密码 |

密钥库和密码不要提交到仓库；丢失后同签名升级会失效。

### Microsoft Store 标识（Variables 或 Secrets）

| 名称 | 说明 |
| --- | --- |
| `MSIX_IDENTITY_NAME` | Partner Center 的 `Package/Identity/Name` |
| `MSIX_PUBLISHER` | Partner Center 的 `Package/Identity/Publisher` |
| `MSIX_PUBLISHER_DISPLAY_NAME` / `PUBLISHERDISPLAYNAME` | Partner Center 的 `PublisherDisplayName` |

工作流优先读取 Variables，其次读取 Secrets；当前仓库使用的是 Secrets，显示名 Secret 名为 `PUBLISHERDISPLAYNAME`。
未配置时 CI 跳过 MSIX，不影响其他包。

## 已知限制与技术备注

| 项 | 原因 |
|---|---|
| Android 单文件授权的连带改名 | 通过 `ACTION_OPEN_DOCUMENT` 选中的单个文件无法枚举同目录，`.lrc`/封面不会随音频一起改名；用「添加文件夹」导入即可 |
| Android 云盘改名 | 是否支持由 provider 的 `FLAG_SUPPORTS_RENAME` 决定，不支持时按文件失败并提示 |
| Android 改名后的历史记录 | 重命名会改变 `content://` URI，旧历史记录键不再匹配（与桌面改路径一致） |
| 繁体中文界面 | 目前提供简中 / 日文 / 英文 |
| 托盘最小化 | 需要 `TrayIcon` 集成 |
| 全局查找替换 | 过滤 + 面板直接编辑已覆盖主要场景 |
| 作词字段写入 | TagLib# 2.3 未暴露 Lyricist |
| DSF/DFF 写入 | 能识别但未用真实 DSD 样本验证 |
| 多音轨音频 | TagLib 不支持按轨编辑，文件会被跳过 |

- 图像处理用 SkiaSharp（MIT）；ImageSharp 4.x 的许可门禁会在 Release 构建失败。
- 国内平台适配器只做只读搜索/歌词/封面查询，不内置任何版权内容。
- Avalonia 12：Fluent 主题拆分为独立包；`TextBox` 无 `Header`；DataGrid 列模板与默认编译绑定冲突（已关闭编译绑定）；拖放使用 `DragEventArgs.DataTransfer` + `DataFormat.File`。
- MusicBrainz 要求可识别 User-Agent；默认 UA 已指向 GitHub 仓库。

## 文档索引

- [Android 签名与发布](docs/dev/Android签名与发布.md)
- [Microsoft Store 上架指南](docs/dev/MicrosoftStore上架指南.md)
- [功能清单与复刻方案](docs/功能清单与复刻方案.md)
- [跨平台重写方案](docs/跨平台重写方案.md)
- [项目图表](docs/charts.html)
- [品牌资源](docs/branding/)
