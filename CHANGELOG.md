# Changelog

## 1.3.1

### 修复

- 刷新列表期间删除行后，重新读取的标签会错位显示到其他文件；现在按行身份回填。
- 拖入或打开包含无权限子目录的文件夹（系统目录、联接点等）时程序可能崩溃；现在跳过无法访问的子目录继续导入。
- 批量改名会把名称以音频文件名开头的无关文件（如 `Love Song.lrc` 之于 `Love.mp3`）一起改名；现在只连带 `.lrc`、封面及 `-cover` / ` (n)` 后缀的同名文件。
- 封面编码失败时会把 0 字节封面写入音频文件；现在编码失败即放弃写入。
- 历史记录只保存写入后的标签，首次保存出错无法回到原始值；现在记录保存前的标签，历史可回退到保存前状态。

### 工程

- 新增连带改名后缀白名单测试；单元测试增加到 66 个。
- 版本号升级到 `1.3.1`，Android versionCode 递增到 7。

## 1.3.0

### 新增

- Android 支持按文件夹导入：通过 SAF 目录树递归读取整个文件夹，不再受系统文件选择器 100 个文件的上限。
- Android 支持批量重命名：模板改名、文件名简繁转换都能在手机上完成，`.lrc` 与封面会随音频一起改名。
- 移动端顶栏新增「添加文件夹」按钮。

### 修复

- Android 通过 SAF 选中单个文件时补齐写权限，保存标签不再因只读授权失败。
- Android 读取音频时优先直连可 seek 的文件描述符，避免把整个曲库复制进应用私有目录。
- 重命名 / 简繁转换改为按文件能力判断；单文件授权或云盘 provider 不支持时逐行提示，而不是整批禁用。

### 工程

- `IFileHandle` 增加 `CanRename` / `DirectoryKey` / `GetSiblings` / `Rename`，`RenameService` 改为句柄驱动。
- 新增本地签名脚本 `tools/publish-android-signed.sh`，并在 `docs/dev/Android签名与发布.md` 补充本地打包流程。
- 单元测试增加到 65 个；版本号升级到 `1.3.0`，Android versionCode 递增到 6。

## 1.2.2

### 改进

- 默认 User-Agent 改为随程序集版本自动生成（`Melotag/<版本> (+仓库地址)`），升级版本后无需再手改；
  已有配置中的内置 UA 会在启动时自动刷新，自定义 UA 不受影响。
- 版本号收敛到 `Directory.Build.props` 的 `Version`，桌面端、Android 与发布流水线共用同一来源。

### 工程

- 版本号升级到 `1.2.2`，Android versionCode 递增到 5。

## 1.2.1

### 新增

- Microsoft Store MSIX 打包：GitHub Actions 生成 `Melotag-1.2.1.msix`，可直接上传 Partner Center。
- MSIX 标识支持 GitHub Variables 或 Secrets（兼容 `PUBLISHERDISPLAYNAME`）。

### 修复

- 设置页在 Android 竖屏下的横向溢出：设置项改为标签在上、控件自适应宽度。
- 在线源排序列表移除冗余能力列，窄屏显示更完整。
- Linux RPM 不再被默认 `brp-strip` 剥离 .NET 单文件包。

### 工程

- GitHub Actions 更新到 Node 24 版本：checkout v7、setup-dotnet v6、cache v6、upload-artifact v7、download-artifact v8、setup-java v6、setup-android v4。
- 版本号同步到 `1.2.1`，Android versionCode 递增到 4。

## 1.2.0

### 新增

- 中文 / 日本語 / English 三语界面，默认跟随系统，设置里切换后立即生效。
- 列表支持多选（Ctrl/Shift）+ `Delete` 批量移出；`Ctrl+C` 复制选中行为 Tab 分隔文本。
- 批量自动匹配先弹窗选择导入字段、在线源与覆盖方式（默认全部覆盖）。
- 在线标签匹配会同时带回歌词，保存后写入文件。
- GitHub Releases 版本检查：启动静默检查，帮助菜单可手动检查。
- 发布包新增 Windows 安装包、MSIX（Microsoft Store）、Linux RPM、AppImage、Flatpak。
- Android 正式发布支持固定签名（GitHub Secrets）。

### 修复

- 设置、歌词、标签匹配等对话框的取消/应用/确定按钮现在会真正关闭窗口。
- 歌词搜索结果改为显示歌名，而不是专辑名。
- 重命名默认模板改为“歌手 - 歌名”（`@2 - @1`）。
- 音轨、年份、时长列按数值排序，`10` 不再排在 `2` 前面。
- iTunes 国家从文本框改为可选下拉列表。
- 设置页按分类重新组织；移除界面里的 SAF、TagLib#、SkiaSharp 等实现细节文案。
- 修复对话框窗口生命周期与本地化切换后的界面刷新。
- 默认 User-Agent / 仓库地址统一为 `https://github.com/zdxiaoda/Melotag`。

### 工程

- 新增 MIT 许可证。
- 版本号同步到 `1.2.0`。
- 单元测试增加到 59 个。

## 1.1.0

- 在线标签匹配、歌词、封面、历史、重命名、简繁转换等基础功能。
