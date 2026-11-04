#!/usr/bin/env bash
# SimpleShop 端到端回归的本地终端入口（Linux / macOS）。
#
# 用法：
#   ./run.sh                        # 跑全部 *-regression.ps1（等价 run-all.ps1）
#   ./run.sh gateway                # 只跑文件名含 gateway 的那个脚本
#   ./run.sh --filter payment       # 透传给 run-all.ps1 的 -Filter
#   ./run.sh --stop-on-first-fail   # 任一脚本失败即停
#   ./run.sh --list                 # 列出可跑的脚本
#
# 为什么是「bash 壳 + pwsh 跑原脚本」而不是把 12k 行 PowerShell 手翻成 bash：
# 这些脚本是回归口径的唯一来源，翻译一份必然与原脚本漂移（改了这边忘了那边）。
# 本脚本只负责找到 pwsh 运行时并把参数原样透传，测试逻辑一行都不复制。
#
# pwsh 查找顺序：$PWSH → ~/.local/pwsh/pwsh → PATH 里的 pwsh。
# 本机 pwsh 7 是用官方 linux-x64 tar 包解到 ~/.local/pwsh 的（不需要 sudo）。
set -euo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

find_pwsh() {
  if [ -n "${PWSH:-}" ] && [ -x "$PWSH" ]; then
    echo "$PWSH"; return 0
  fi
  if [ -x "$HOME/.local/pwsh/pwsh" ]; then
    echo "$HOME/.local/pwsh/pwsh"; return 0
  fi
  if command -v pwsh >/dev/null 2>&1; then
    command -v pwsh; return 0
  fi
  return 1
}

PWSH_BIN="$(find_pwsh || true)"
if [ -z "$PWSH_BIN" ]; then
  echo "找不到 pwsh 运行时。安装方式（无需 sudo）：" >&2
  echo "  curl -sL -o /tmp/pwsh.tar.gz https://mirrors.tuna.tsinghua.edu.cn/github-release/PowerShell/PowerShell/LatestRelease/powershell-7.6.6-linux-x64.tar.gz" >&2
  echo "  mkdir -p ~/.local/pwsh && tar -xzf /tmp/pwsh.tar.gz -C ~/.local/pwsh && chmod +x ~/.local/pwsh/pwsh" >&2
  exit 1
fi

if [ "${1:-}" = "--list" ]; then
  cd "$DIR"
  for f in *-regression.ps1 seckill-concurrency.ps1; do
    [ -f "$f" ] && echo "$f"
  done
  exit 0
fi

# 单脚本模式：./run.sh <名字片段> [额外参数...]
if [ $# -gt 0 ] && [ "${1:0:1}" != "-" ]; then
  pattern="$1"; shift
  # 匹配优先级：精确 <名字>.ps1 / <名字>-regression.ps1 → <名字> 开头 → 含子串。
  # 只做子串匹配会踩坑：`point` 会先命中 admin-endpoints-regression.ps1（含 "point"）。
  script=""
  if [ -f "$DIR/$pattern.ps1" ]; then
    script="$pattern.ps1"
  elif [ -f "$DIR/$pattern-regression.ps1" ]; then
    script="$pattern-regression.ps1"
  elif [ -n "$(cd "$DIR" && ls "$pattern"*.ps1 2>/dev/null | head -1)" ]; then
    script="$(cd "$DIR" && ls "$pattern"*.ps1 | head -1)"
  else
    script="$(cd "$DIR" && ls *"$pattern"*.ps1 2>/dev/null | head -1)"
  fi
  if [ -z "$script" ]; then
    echo "没有文件名含「$pattern」的脚本。用 ./run.sh --list 看清单。" >&2
    exit 1
  fi
  echo "==> 运行 tests/e2e/$script"
  exec "$PWSH_BIN" -NoLogo -NoProfile -File "$DIR/$script" "$@"
fi

echo "==> 运行 run-all.ps1（pwsh：$PWSH_BIN）"
exec "$PWSH_BIN" -NoLogo -NoProfile -File "$DIR/run-all.ps1" "$@"
