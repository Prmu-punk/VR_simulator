#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

created_venv=0
if [[ ! -d .venv ]]; then
  python3 -m venv .venv
  created_venv=1
fi

if [[ ! -x .venv/bin/python ]]; then
  rm -rf .venv
  python3 -m venv .venv
  created_venv=1
fi

export MPLCONFIGDIR="$SCRIPT_DIR/.cache/matplotlib"
export QT_QPA_PLATFORM="${QT_QPA_PLATFORM:-xcb}"
export GLOG_minloglevel="${GLOG_minloglevel:-2}"
export TF_CPP_MIN_LOG_LEVEL="${TF_CPP_MIN_LOG_LEVEL:-2}"
mkdir -p "$MPLCONFIGDIR"

if [[ "$created_venv" -eq 1 ]]; then
  .venv/bin/python -m pip install --upgrade pip
fi

if [[ ! -f .venv/.requirements-installed || requirements.txt -nt .venv/.requirements-installed ]]; then
  .venv/bin/python -m pip install -r requirements.txt
  touch .venv/.requirements-installed
fi

exec .venv/bin/python webcam_tracking_service.py "$@"
