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

生成后建议把密钥库移出仓库，例如放到 `~/.config/melotag/signing/melotag-release.keystore`，并执行 `chmod 600`。

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

## 4. 发布 1.3.0

```bash
git tag v1.3.0
git push origin v1.3.0
```

`release.yml` 会复用 `android.yml` 构建 `arm64` 与 `x64` 两个 APK，并把它们放进 GitHub Release：

- `Melotag-1.3.0-android-arm64.apk`（真机）
- `Melotag-1.3.0-android-x64.apk`（模拟器）

## 5. 验证签名

```bash
apksigner verify --print-certs Melotag-1.3.0-android-arm64.apk
```

应看到与首次发布一致的证书指纹（SHA-256）。

## 6. 本地出签名包

CI 之外也可以在本机直接产出固定签名的 APK。密钥和密码只放在用户目录，**不要提交进仓库**。

### 6.1 本地文件布局

```
~/.config/melotag/signing/
├── melotag-release.keystore   # PKCS12 密钥库（chmod 600）
├── signing.env                # 密码与别名（chmod 600）
└── publish-android.sh         # 可选：本机快捷脚本
```

`signing.env` 的内容（值换成自己的，不要提交）：

```bash
export ANDROID_KEYSTORE_PATH="$HOME/.config/melotag/signing/melotag-release.keystore"
export ANDROID_KEYSTORE_PASSWORD='你的密钥库密码'
export ANDROID_KEY_ALIAS='melotag'
export ANDROID_KEY_PASSWORD='你的密钥密码'   # PKCS12 通常与密钥库密码相同
export ANDROID_SDK_DIR="$HOME/android-sdk"
export JAVA_SDK_DIR="/usr/lib/jvm/java-21-openjdk-amd64"
```

```bash
chmod 600 ~/.config/melotag/signing/melotag-release.keystore \
          ~/.config/melotag/signing/signing.env
```

> `.gitignore` 已忽略 `*.keystore`、`*.jks` 和 `android-signing/`，但仍建议把密钥放在仓库目录之外。

### 6.2 打包

```bash
./tools/publish-android-signed.sh /path/to/Melotag android-arm64
# 模拟器改用 android-x64
```

脚本默认读取 `$HOME/.config/melotag/signing/signing.env`；也可以用
`MELOTAG_ANDROID_SIGNING_ENV=/path/to/signing.env` 指向其他文件。
`ANDROID_SDK_DIR` / `JAVA_SDK_DIR` 已配置时会传给 MSBuild，否则使用环境变量或默认值。

不想用脚本时，等价的手动命令：

```bash
set -a; . ~/.config/melotag/signing/signing.env; set +a

dotnet publish src/Melotag.Android/Melotag.Android.csproj \
  -c Release \
  -p:AndroidPackageFormat=apk \
  -p:RuntimeIdentifier=android-arm64 \
  -p:AndroidKeyStore=true \
  -p:AndroidSigningKeyStore="$ANDROID_KEYSTORE_PATH" \
  -p:AndroidSigningStorePass="$ANDROID_KEYSTORE_PASSWORD" \
  -p:AndroidSigningKeyAlias="$ANDROID_KEY_ALIAS" \
  -p:AndroidSigningKeyPass="$ANDROID_KEY_PASSWORD"
```

产物在 `src/Melotag.Android/bin/Release/net10.0-android/android-arm64/publish/`，
文件名形如 `io.melotag.app-Signed.apk`。

### 6.3 验证本地包

```bash
apksigner verify --print-certs \
  src/Melotag.Android/bin/Release/net10.0-android/android-arm64/publish/io.melotag.app-Signed.apk
```

证书 SHA-256 应与 CI 产物一致。

## 7. 更换签名（不推荐）

如果确实需要换签名，Android 会把它视为另一个应用：旧版本无法直接覆盖升级，用户需要卸载重装。正式发布后不要更换密钥。
