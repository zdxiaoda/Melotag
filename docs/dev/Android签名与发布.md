# Android 固定签名与发布

Android 要求同一个应用的后续版本必须使用同一份签名密钥。只要第一次发布时用固定密钥签名，后续 `android.yml` 会自动复用这份密钥，不需要每次改配置。

## 1. 生成一次密钥（只做一次）

需要 JDK 17+（`keytool` 随 JDK 一起安装）。

```bash
./tools/create-android-keystore.sh
# 或自定义文件名与别名
./tools/create-android-keystore.sh melotag-release.keystore melotag
```

脚本会生成 `melotag-release.keystore`（PKCS12），并提示输入：

- 密钥库密码（store password）
- 密钥密码（key password，PKCS12 通常与密钥库密码相同）

脚本最后会输出 `ANDROID_KEYSTORE_BASE64` 的 Base64 内容。

> **务必离线备份 `melotag-release.keystore` 和两个密码。**
> 丢失后无法再用同一签名发布新版本；已经安装旧版本的用户也无法直接覆盖升级。

## 2. 写入 GitHub Secrets

在 GitHub 仓库：

`Settings → Secrets and variables → Actions → New repository secret`

添加以下 4 个 Secrets：

| Secret | 内容 |
| --- | --- |
| `ANDROID_KEYSTORE_BASE64` | `base64 -w0 melotag-release.keystore` 的完整内容 |
| `ANDROID_KEYSTORE_PASSWORD` | 密钥库密码 |
| `ANDROID_KEY_ALIAS` | 生成时使用的别名，例如 `melotag` |
| `ANDROID_KEY_PASSWORD` | 密钥密码 |

> 不要把这些值写进仓库、workflow 文件或 README。CI 只在运行时从 Secrets 读取。

## 3. CI 如何签名

`.github/workflows/android.yml` 会：

1. 如果存在 `ANDROID_KEYSTORE_BASE64`，解码到 `$RUNNER_TEMP/android-signing/melotag.keystore`；
2. 调用 `dotnet publish` 时传入：
   - `-p:AndroidKeyStore=true`
   - `-p:AndroidSigningKeyStore=<临时密钥库路径>`
   - `-p:AndroidSigningStorePass=<密钥库密码>`
   - `-p:AndroidSigningKeyAlias=<别名>`
   - `-p:AndroidSigningKeyPass=<密钥密码>`
3. 产出的 APK 就是固定签名的 `*-Signed.apk`。

如果没有配置这些 Secrets（例如 fork 的 PR），CI 会自动回退到调试签名，只适合侧载测试，不能用于正式发布。

## 4. 发布 1.2.1

```bash
git tag v1.2.1
git push origin v1.2.1
```

`release.yml` 会复用 `android.yml` 构建 `arm64` 与 `x64` 两个 APK，并把它们放进 GitHub Release：

- `Melotag-1.2.1-android-arm64.apk`（真机）
- `Melotag-1.2.1-android-x64.apk`（模拟器）

## 5. 验证签名

```bash
apksigner verify --print-certs Melotag-1.2.1-android-arm64.apk
```

应看到与首次发布一致的证书指纹（SHA-256）。

## 6. 本地手动签名（可选）

如果不走 CI，可以本地执行：

```bash
dotnet publish src/Melotag.Android/Melotag.Android.csproj \
  -c Release \
  -p:AndroidPackageFormat=apk \
  -p:RuntimeIdentifier=android-arm64 \
  -p:AndroidKeyStore=true \
  -p:AndroidSigningKeyStore=/absolute/path/melotag-release.keystore \
  -p:AndroidSigningStorePass='你的密钥库密码' \
  -p:AndroidSigningKeyAlias=melotag \
  -p:AndroidSigningKeyPass='你的密钥密码'
```

产物在 `src/Melotag.Android/bin/Release/net10.0-android/android-arm64/publish/`。

## 7. 更换签名（不推荐）

如果确实需要换签名，Android 会把它视为另一个应用：旧版本无法直接覆盖升级，用户需要卸载重装。正式发布后不要更换密钥。
