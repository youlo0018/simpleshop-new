#!/usr/bin/env bash
# 启动后台前端（vite preview，读构建产物）。
#
#   ./start-web.sh            # 起后台 5173
#   ./start-web.sh --admin    # 同上（显式写明）
#
# 低内存：Node 堆压到 384M（preview 是静态服务，本来也不需要大堆）。
# 为什么必须 build + preview、不能用 dev server：项目路径含 # 时 Vite dev-server 会白屏
# （AI_HANDOFF 2.4 记录过）。
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

MODE="all"
case "${1:-}" in
  --admin) MODE="admin" ;;
  "")      MODE="all" ;;
  *) fail "未知参数：$1（可选 --admin）"; exit 1 ;;
esac

require_node
export NODE_OPTIONS="--max-old-space-size=384"

start_front() {
  local name="$1" dir="$2" port="$3" out="$4"
  local pidf="$LOG_DIR/$name.pid" log="$LOG_DIR/$name.log"

  if [ ! -d "$dir/$out" ]; then
    fail "$name 缺少构建产物 $dir/$out（先跑 ./build.sh --web）"
    return 1
  fi

  if curl -fsS --max-time 2 "http://127.0.0.1:$port/" >/dev/null 2>&1; then
    gray "$name 已在运行（端口 $port），跳过"
    return 0
  fi

  info "启动 $name（http://127.0.0.1:$port）"
  (
    cd "$dir"
    nohup npx vite preview --host 0.0.0.0 --port "$port" --outDir "$out" --strictPort >"$log" 2>&1 &
    echo $! >"$pidf"
  )

  for _ in $(seq 1 20); do
    sleep 1
    if curl -fsS --max-time 2 "http://127.0.0.1:$port/" >/dev/null 2>&1; then
      ok "$name 就绪（pid $(cat "$pidf" 2>/dev/null)）"
      return 0
    fi
    pid_alive "$pidf" || break
  done

  fail "$name 启动失败——日志尾部："
  tail -n 5 "$log" 2>/dev/null | sed 's/^/      /' || true
  return 1
}

rc=0
if [ "$MODE" = "all" ] || [ "$MODE" = "admin" ]; then
  start_front "web-admin" "$ROOT_DIR/apps/admin-vue" 5173 "dist" || rc=1
fi

exit "$rc"
