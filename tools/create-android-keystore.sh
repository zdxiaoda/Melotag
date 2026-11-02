#!/usr/bin/env bash
# 生成一次即可的 Android 发布签名密钥。生成后请离线备份，切勿提交到仓库。
set -euo pipefail

out="${1:-melotag-release.keystore}"
alias="${2:-melotag}"

if [ -e "$out" ]; then
  echo "拒绝覆盖已存在的文件：$out" >&2
  echo "如果确实要重新生成，请先自行备份并移走旧文件。" >&2
  exit 1
fi

if ! command -v keytool >/dev/null 2>&1; then
  echo "未找到 keytool，请先安装 JDK 17 或更高版本。" >&2
  exit 1
fi

echo "即将生成发布签名：$out"
echo "别名：$alias"
echo "请设置一个强密码，并把它和密钥库一起妥善保管。"
echo

keytool -genkeypair -v \
  -keystore "$out" \
  -alias "$alias" \
  -keyalg RSA \
  -keysize 4096 \
  -validity 10000 \
  -storetype PKCS12

echo
echo "==== GitHub Secrets ===="
echo "ANDROID_KEYSTORE_BASE64（复制下面整行内容）："
if base64 --help >/dev/null 2>&1; then
  base64 -w0 "$out"
else
  base64 "$out" | tr -d '\n'
fi
echo
echo
echo "还需要在 GitHub 仓库的 Settings → Secrets and variables → Actions 中添加："
echo "  ANDROID_KEYSTORE_BASE64   = 上面的 Base64"
echo "  ANDROID_KEYSTORE_PASSWORD = 密钥库密码"
echo "  ANDROID_KEY_ALIAS         = $alias"
echo "  ANDROID_KEY_PASSWORD      = 密钥密码（PKCS12 通常与密钥库密码相同）"
echo
echo "警告：丢失密钥库或密码后，新版本无法覆盖安装旧版本，也无法更新已上架的应用。"
echo "请把 $out 和密码保存到安全的地方，不要提交到 Git。"
