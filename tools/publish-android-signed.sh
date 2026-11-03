#!/usr/bin/env bash
# 用本地 release keystore 打出固定签名的 Android APK。
# 密钥与密码放在仓库外（默认 ~/.config/melotag/signing/signing.env），不要提交。
set -euo pipefail

env_file="${MELOTAG_ANDROID_SIGNING_ENV:-$HOME/.config/melotag/signing/signing.env}"
if [ ! -f "$env_file" ]; then
  echo "找不到签名配置：$env_file" >&2
  echo "请参考 docs/dev/Android签名与发布.md 创建。" >&2
  exit 1
fi

# shellcheck disable=SC1090
. "$env_file"

: "${ANDROID_KEYSTORE_PATH:?缺少 ANDROID_KEYSTORE_PATH}"
: "${ANDROID_KEYSTORE_PASSWORD:?缺少 ANDROID_KEYSTORE_PASSWORD}"
: "${ANDROID_KEY_ALIAS:?缺少 ANDROID_KEY_ALIAS}"
: "${ANDROID_KEY_PASSWORD:?缺少 ANDROID_KEY_PASSWORD}"

if [ ! -f "$ANDROID_KEYSTORE_PATH" ]; then
  echo "找不到密钥库：$ANDROID_KEYSTORE_PATH" >&2
  exit 1
fi

repo="${1:-$(cd "$(dirname "$0")/.." && pwd)}"
rid="${2:-android-arm64}"

sdk="${ANDROID_SDK_DIR:-${ANDROID_HOME:-$HOME/android-sdk}}"
java="${JAVA_SDK_DIR:-${JAVA_HOME:-}}"

args=(
  -c Release
  -p:AndroidPackageFormat=apk
  -p:RuntimeIdentifier="$rid"
  -p:AndroidKeyStore=true
  -p:AndroidSigningKeyStore="$ANDROID_KEYSTORE_PATH"
  -p:AndroidSigningStorePass="$ANDROID_KEYSTORE_PASSWORD"
  -p:AndroidSigningKeyAlias="$ANDROID_KEY_ALIAS"
  -p:AndroidSigningKeyPass="$ANDROID_KEY_PASSWORD"
)

if [ -n "$sdk" ]; then
  args+=(-p:AndroidSdkDirectory="$sdk")
fi

if [ -n "$java" ]; then
  args+=(-p:JavaSdkDirectory="$java")
fi

cd "$repo"
dotnet publish src/Melotag.Android/Melotag.Android.csproj "${args[@]}"

echo
echo "产物：src/Melotag.Android/bin/Release/net10.0-android/$rid/publish/*-Signed.apk"
