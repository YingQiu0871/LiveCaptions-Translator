#!/bin/sh
# Installs 课堂同传助手 for the current user: no root needed.
set -e

here="$(cd "$(dirname "$0")" && pwd)"
target="$HOME/.local/opt/LiveCaptionsTranslator"
apps="$HOME/.local/share/applications"

mkdir -p "$target" "$apps"
cp "$here/LiveCaptionsTranslator" "$here/icon.png" "$target/"
chmod +x "$target/LiveCaptionsTranslator"
sed "s|@INSTALL_DIR@|$target|g" "$here/课堂同传助手.desktop" > "$apps/课堂同传助手.desktop"
chmod +x "$apps/课堂同传助手.desktop"
update-desktop-database "$apps" >/dev/null 2>&1 || true

echo "已安装到 $target，应用菜单里可以找到“课堂同传助手”。"
echo
echo "还需要这些系统组件（Debian / Ubuntu 的包名）："
echo "  pulseaudio-utils    采集声音（parec、pactl；PipeWire 也可用）"
echo "  fonts-noto-cjk      中文字体"
echo "  speech-dispatcher 或 espeak-ng    语音播报（可选）"
echo "  poppler-utils tesseract-ocr       扫描版课件 OCR（可选，语言包如 tesseract-ocr-eng）"
