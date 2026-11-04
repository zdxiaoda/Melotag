# Microsoft Store 提交清单

适用版本：应用 `1.4.1`；MSIX `1.4.1.0`；核对日期：2026-11-04。

> 本清单只区分“仓库内物料已备妥”和“需要发布者在 Partner Center 或发布流程中完成”的事项。勾选 `[x]` 不代表应用已经提交、通过认证、上架或获得评级；提交、认证、评级和上架状态均以 Microsoft Partner Center、IARC 和 Microsoft Store 的实际结果为准。

图例：

- `[x]`：仓库内已有对应文件，可按路径核对；不代表 Partner Center 已接收。
- `[ ]`：需要发布者在 Partner Center、GitHub 或发布流程中操作。

## 1. 仓库内已备妥

以下项目只代表当前工作区里有对应文件，并且内容可按路径核对。

- [x] 简体中文 listing：`listing/zh-CN/listing.md`。
- [x] 日文 listing：`listing/ja-JP/listing.md`。
- [x] 英文 listing：`listing/en-US/listing.md`。
- [x] 简体中文截图 4 张：`images/screenshots/zh-CN/01-main.png`、`02-lyrics.png`、`03-rename.png`、`04-settings.png`。
- [x] 日文截图 4 张：`images/screenshots/ja-JP/01-main.png`、`02-lyrics.png`、`03-rename.png`、`04-settings.png`。
- [x] 英文截图 4 张：`images/screenshots/en-US/01-main.png`、`02-lyrics.png`、`03-rename.png`、`04-settings.png`。
- [x] 300×300 商店图标：`images/logo/app-tile-300x300.png`。
- [x] 三语 poster：`images/poster/zh-CN/poster-720x1080.png`、`images/poster/ja-JP/poster-720x1080.png`、`images/poster/en-US/poster-720x1080.png`。
- [x] 通用 box art：`images/logo/box-art-1080x1080.png`。
- [x] 通用 hero art：`images/hero/super-hero-1920x1080.png`。
- [x] 三语隐私政策源文件：`privacy/zh-CN/privacy.md`、`privacy/ja-JP/privacy.md`、`privacy/en-US/privacy.md`。
- [x] 提交辅助文档：`submission/upload-checklist.md`、`submission/age-rating-notes.md`、`submission/certification-notes.md`、`submission/package-and-release.md`。
- [x] 版本一致性可核对：版本只在 `Directory.Build.props` 维护；桌面端与 Android 显示版本由程序集版本派生。
- [x] 系统要求可核对：`packaging/windows/msix/Package.appxmanifest` 的 `TargetDeviceFamily` 为 `Windows.Desktop`，`MinVersion` 为 `10.0.19041.0`，`ProcessorArchitecture` 由构建参数注入（x64 / arm64）。
- [x] MSIX 清单和构建脚本存在：`packaging/windows/msix/Package.appxmanifest`、`build-msix.ps1`、`build-msix-bundle.ps1`。
- [x] 截图和商店图片复现脚本存在：`tools/Generate-StoreArt.py`、`tools/Capture-StoreScreenshots.ps1`。

注意：仓库中没有已生成、已签名、可直接上传的 `.msixbundle` 成品；商店包属于构建产物，需要在 Windows 上生成后再上传。

## 2. 待发布者操作：预留名称、创建产品与记录标识

- [ ] 在 Partner Center 预留或确认产品名称：中文 listing 使用“音签 Melotag”，日文和英文 listing 使用“Melotag”。如名称已被占用，先解决名称问题，不要为了绕过名称占用而修改包标识。
- [ ] 创建 Microsoft Store 应用，产品类型选择 Windows PC/桌面应用。
- [ ] 记录 Partner Center 的 `Package/Identity/Name`、`Package/Identity/Publisher`、`PublisherDisplayName`。这三个值必须与最终 MSIX 完全一致。
- [ ] 将三个产品标识配置为 GitHub Actions 的 Variables 或 Secrets：`MSIX_IDENTITY_NAME`、`MSIX_PUBLISHER`、`MSIX_PUBLISHER_DISPLAY_NAME`（兼容 Secret 名 `PUBLISHERDISPLAYNAME`）。
- [ ] 确认仓库工作流能读取上述值；任一为空时，`release.yml` 会跳过 MSIX 构建和 `msix-bundle` 上传。
- [ ] 不要手工修改 `packaging/windows/msix/Package.appxmanifest` 中的占位符；由构建脚本注入。详见 [package-and-release.md](package-and-release.md)。
- [ ] 不要把 Publisher 证书、密码或 Partner Center 凭据提交到仓库。

## 3. 待发布者操作：Properties 与定价

- [ ] 价格选择 Free（免费）。
- [ ] 类别选择 Music。若 Partner Center 还要求子类别，按实际可用选项选择最接近“音乐工具/音乐”的类别。
- [ ] 市场/国家或地区按发布计划选择；不要在未确认的情况下宣称全球可用。
- [ ] 系统要求填写：Windows 10 版本 2004（内部版本 19041）或更高；x64 或 arm64 处理器。
- [ ] 说明 Store 包已包含运行所需组件，不要求用户另装 .NET 运行时。
- [ ] 支持 URL：`https://github.com/zdxiaoda/Melotag/issues`。
- [ ] 网站：`https://github.com/zdxiaoda/Melotag`。
- [ ] 版权信息：`Copyright (c) 2026 zdxiaoda`，MIT License。
- [ ] 开发者显示名：`zdxiaoda`。
- [ ] 配置应用是否面向儿童或家庭；如无专门儿童内容，按实际定位回答，不要为了过审而错误声明。
- [ ] 如 Partner Center 要求隐私政策 URL，按第 4 节填写；不要在推送前填写尚未公开的链接。

## 4. 待发布者操作：隐私政策公开 URL

三份隐私政策的建议 URL 使用 GitHub `main` 分支的公开仓库路径：

| 语言 | 建议 URL |
| --- | --- |
| zh-CN | https://github.com/zdxiaoda/Melotag/blob/main/packaging/store/privacy/zh-CN/privacy.md |
| ja-JP | https://github.com/zdxiaoda/Melotag/blob/main/packaging/store/privacy/ja-JP/privacy.md |
| en-US | https://github.com/zdxiaoda/Melotag/blob/main/packaging/store/privacy/en-US/privacy.md |

- [ ] 先将 `packaging/store/privacy/` 提交并推送到公开仓库的 `main` 分支。推送前这些链接不可访问，不能当作已公开隐私政策。
- [ ] 当前工作区中 `packaging/store/` 尚未被 Git 跟踪（以 `git status` 为准）；不要把上面的 URL 当作已经上线，也不要在 Partner Center 中宣称隐私政策已发布。
- [ ] 推送后用无痕窗口或未登录浏览器逐个打开三个 URL，确认无需登录即可访问、渲染正常、没有 404。如果仓库不是公开仓库，GitHub 链接不能作为公开隐私政策 URL。
- [ ] 在 Partner Center 的 Privacy policy URL 字段填入对应语言的公开 URL；如果 Partner Center 只接受一个通用 URL，使用英文页面或与商店主要语言一致的页面，并确认另外两种语言仍可从应用/网站访问。
- [ ] 确认隐私政策内容与提交包一致：本地设置/历史、在线查询发送的搜索文本、第三方服务、Last.fm API key、GitHub 更新检查、无账号/广告/遥测、数据删除方式。
- [ ] 如最终使用的不是 GitHub 链接，更新本表和 `submission/certification-notes.md` 中的隐私政策引用，确保认证备注中的 URL 可公开访问。

## 5. 待发布者操作：IARC 年龄分级

- [ ] 在 Partner Center 打开 IARC 问卷。
- [ ] 按 [age-rating-notes.md](age-rating-notes.md) 的事实表回答；具体题目措辞以 IARC 页面为准。
- [ ] 对“用户生成内容/用户可查看或编辑内容”“在线获取的第三方歌词/封面”“网络请求和数据收集”等题目如实披露，不要简单回答“没有用户内容”或“没有网络功能”。
- [ ] 提交问卷并取得 IARC 证书/评级结果。
- [ ] 将评级结果填入 listing；在证书签发前，不要声称已获得任何评级。
- [ ] 如果评级与预期不同，先复核问卷答案和 listing 描述，再提交认证。
- [ ] 保留 IARC 返回的评级/证书信息，供后续更新和商店页面核对。

## 6. 待发布者操作：MSIX 包

- [ ] 确认版本：应用版本取 `Directory.Build.props`，MSIX `Identity/@Version` 为对应四段数字；MSIX 版本必须高于 Store 已发布版本。
- [ ] 在 Windows 上生成商店包，或在 GitHub Actions 的 `msix` 任务中生成；CI 产物为 `dist/msix-bundle/Melotag-<版本>.msixbundle`（x64 + arm64），artifact 名为 `msix-bundle`。
- [ ] 单架构包位于 `dist/msix/win-<arch>/Melotag-<版本>-win-<arch>.msix`；本地省略 `-Output` 时默认输出到 `dist/installer-win-<arch>/`。
- [ ] 检查 `AppxManifest.xml` 中不再残留 `__IDENTITY_NAME__`、`__PUBLISHER__`、`__PUBLISHER_DISPLAY_NAME__`、`__VERSION__`、`__ARCH__`。
- [ ] 检查 `Identity Name`、`Publisher`、`PublisherDisplayName` 与 Partner Center 产品标识完全一致。
- [ ] 检查 x64 包架构为 `x64`、arm64 包架构为 `arm64`，`TargetDeviceFamily` 为 `Windows.Desktop`，最低版本为 `10.0.19041.0`。
- [ ] 在干净 Windows 10 2004+ x64 与 arm64 环境各安装并启动一次；确认不需要管理员权限、无需账号、无缺失运行时。
- [ ] 检查 `.mp4` 文件关联是否符合预期；应用是标签编辑器，不是媒体播放器。关联范围的决定见 [package-and-release.md](package-and-release.md)。
- [ ] 检查包内图标与 listing 的 300×300 图标不是同一个文件；两者都要保留，不要互相替换。
- [ ] 不要将 `tools/`、`listing/*.md` 或 `privacy/*.md` 放入 MSIX 包或作为 Store 图片上传。
- [ ] 在 Partner Center 上传 `.msixbundle`，等待包验证完成；如果验证失败，先修复包，并按 Partner Center 的要求决定是否递增版本，不要只改文件名。
- [ ] 上传前复核 `package-and-release.md` 中的 `.mp4` 关联、硬编码中文、GitHub 更新检查和酷我 HTTP 风险。

## 7. 待发布者操作：三语 listing

每完成一种语言，勾选对应列。字段正文来自对应 `listing.md`，截图与 caption 的逐张映射见 [README.md](../README.md)。

| 检查项 | zh-CN | ja-JP | en-US | 来源/备注 |
| --- | --- | --- | --- | --- |
| 产品名称 | [ ] | [ ] | [ ] | `listing/*/listing.md` 对应字段；中文“音签 Melotag”，日/英“Melotag” |
| 简短标题 | [ ] | [ ] | [ ] | 与产品名称一致 |
| 简短说明 | [ ] | [ ] | [ ] | 英文保持单段，不超过 270 字符 |
| 详细说明 | [ ] | [ ] | [ ] | 必需字段；保留限制说明 |
| 产品功能（10 条） | [ ] | [ ] | [ ] | 每条一行 |
| 搜索关键词（7 条） | [ ] | [ ] | [ ] | 逗号分隔，不带编号 |
| 版权 | [ ] | [ ] | [ ] | MIT / 2026 zdxiaoda |
| 开发者 | [ ] | [ ] | [ ] | `zdxiaoda` |
| 4 张截图已上传 | [ ] | [ ] | [ ] | `images/screenshots/<lang>/`，按 01→04 顺序 |
| 4 条截图 caption 已填写 | [ ] | [ ] | [ ] | 见 README 的逐张映射 |
| 300×300 商店图标 | [ ] | [ ] | [ ] | 同一张 `images/logo/app-tile-300x300.png` |
| 三语 poster（PC 可选） | [ ] | [ ] | [ ] | `images/poster/<lang>/poster-720x1080.png`；不上传不影响必需项 |
| 通用 box art（PC 可选） | [ ] | [ ] | [ ] | `images/logo/box-art-1080x1080.png`；不能替代截图 |
| 通用 hero art（PC 可选） | [ ] | [ ] | [ ] | `images/hero/super-hero-1920x1080.png`；不能替代截图 |
| 更新说明 | [ ] | [ ] | [ ] | 首次提交可留空；备用文本见 listing |
| 附加系统要求 | [ ] | [ ] | [ ] | Windows 10 2004+ x64/arm64；Store 包已包含运行时 |
| 支持 URL | [ ] | [ ] | [ ] | `https://github.com/zdxiaoda/Melotag/issues` |
| 网站 | [ ] | [ ] | [ ] | `https://github.com/zdxiaoda/Melotag` |
| 隐私政策 URL | [ ] | [ ] | [ ] | 见第 4 节；推送并验证后才可填写 |

补充检查：

- [ ] 三语 listing 均只描述 Windows 桌面版，不暗示 Linux/Android 可通过 Microsoft Store 获取。
- [ ] 三语 listing 均保留 DSF/DFF 写入未充分验证、多音轨文件受限、在线结果依赖第三方的说明。
- [ ] 英文简短说明不超过 270 字符；三语字段的实时字符数仍以 Partner Center 页面为准。
- [ ] 截图 caption 与实际上传顺序一致；不要复用其他语言的截图或 caption。
- [ ] 只复制字段正文，不把 Markdown 标题、列表编号或“首次提交可留空”等编辑说明粘贴到商店字段。
- [ ] 如上传可选的 poster，先复核其营销文案与已实现功能一致；海报不是 listing 描述，也不能替代截图。
- [ ] 不上传 `tools/` 中的脚本。

## 8. 待发布者操作：最终认证与发布

- [ ] 运行 `dotnet test Melotag.slnx`，确认单元测试通过。
- [ ] 运行 `dotnet build Melotag.slnx -c Release`，确认 Release 构建通过。
- [ ] 用最终 MSIX 做一次人工冒烟测试：启动、添加文件、编辑标签、保存、重命名、歌词/封面工具、设置语言、离线可用。
- [ ] 确认测试不需要账号；认证备注中不要填写虚构的测试账号或密码。
- [ ] 在断网环境验证核心编辑/保存仍可用；在线匹配、封面和歌词查询失败时应给出可理解的提示。
- [ ] 确认没有内置受版权保护的音乐、歌词或封面；截图中的演示素材为虚构内容。
- [ ] 在 Notes for certification 中粘贴 [certification-notes.md](certification-notes.md) 的英文草稿，并按实际包内容核对版本号、包标识和限制说明。
- [ ] 确认隐私政策 URL 在推送后可以公开访问；支持 URL 和网站可访问。
- [ ] 提交认证前再核对一次 [package-and-release.md](package-and-release.md) 中的发布前代码/包检查项。
- [ ] 提交认证并等待结果；在获得明确通过前，不要对外宣称已上架或已通过认证。
- [ ] 认证通过后检查 Store listing 实际显示：产品名称、价格、类别、三语截图、300×300 图标、系统要求、隐私政策链接。
- [ ] IARC 评级签发后再引用评级；不要把本文档中的答题建议当作已获得评级。

## 9. 状态说明

- `[x]` 仅表示仓库内物料可核对，不表示 Partner Center 已接收、包已上传或认证已开始。
- `[ ]` 表示需要发布者在 Partner Center 或发布流程中完成；完成后仍应以 Partner Center 的实际状态为准。
- 在 Partner Center 明确显示认证通过和发布完成前，不要对外宣称应用“已提交”“已通过认证”“已上架”或“已获得某年龄评级”。
