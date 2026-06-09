#!/usr/bin/env bash
set -euo pipefail

COMMAND="${1:-foreground}"
HOST="${ERATRANSLATOR_HOST:-0.0.0.0}"
PORT="${ERATRANSLATOR_PORT:-8000}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SERVER_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
PID_DIR="${SERVER_DIR}/run"
LOG_DIR="${SERVER_DIR}/logs"
PID_FILE="${PID_DIR}/eratranslator-team-server.pid"
LOG_FILE="${LOG_DIR}/eratranslator-team-server.log"

export ERATRANSLATOR_DATABASE_URL="${ERATRANSLATOR_DATABASE_URL:-postgresql+psycopg://eratran:eratrandb!23@localhost:5432/eratrandb}"

cd "${SERVER_DIR}"

ensure_uv() {
    if ! command -v uv >/dev/null 2>&1; then
        echo "uv is not installed or not available on PATH." >&2
        exit 1
    fi
}

run_migration() {
    if [[ "${ERATRANSLATOR_SKIP_MIGRATION:-0}" != "1" ]]; then
        uv run alembic upgrade head
    fi
}

is_running() {
    [[ -f "${PID_FILE}" ]] && kill -0 "$(cat "${PID_FILE}")" >/dev/null 2>&1
}

start_server() {
    ensure_uv
    mkdir -p "${PID_DIR}" "${LOG_DIR}"
    if is_running; then
        echo "EraTranslator team server is already running: pid $(cat "${PID_FILE}")"
        return 0
    fi

    run_migration
    nohup uv run uvicorn app.main:app --host "${HOST}" --port "${PORT}" >>"${LOG_FILE}" 2>&1 &
    echo $! >"${PID_FILE}"
    echo "Started EraTranslator team server: pid $(cat "${PID_FILE}")"
    echo "Log: ${LOG_FILE}"
}

stop_server() {
    if ! is_running; then
        echo "EraTranslator team server is not running."
        rm -f "${PID_FILE}"
        return 0
    fi

    local pid
    pid="$(cat "${PID_FILE}")"
    kill "${pid}"
    for _ in {1..30}; do
        if ! kill -0 "${pid}" >/dev/null 2>&1; then
            rm -f "${PID_FILE}"
            echo "Stopped EraTranslator team server."
            return 0
        fi
        sleep 1
    done

    echo "Process did not stop gracefully; sending SIGKILL." >&2
    kill -9 "${pid}" || true
    rm -f "${PID_FILE}"
}

status_server() {
    if is_running; then
        echo "EraTranslator team server is running: pid $(cat "${PID_FILE}")"
    else
        echo "EraTranslator team server is not running."
        return 1
    fi
}

foreground_server() {
    ensure_uv
    run_migration
    uv run uvicorn app.main:app --host "${HOST}" --port "${PORT}"
}

case "${COMMAND}" in
    start)
        start_server
        ;;
    stop)
        stop_server
        ;;
    restart)
        stop_server
        start_server
        ;;
    status)
        status_server
        ;;
    foreground|run)
        foreground_server
        ;;
    *)
        echo "Usage: $0 {foreground|start|stop|restart|status}" >&2
        exit 2
        ;;
esac
