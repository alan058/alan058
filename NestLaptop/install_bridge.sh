#!/bin/sh
# Sets up the NestLaptop bridge on a Raspberry Pi or another always-on Linux computer.
# It runs as a service that starts at boot. Run: sh install_bridge.sh
set -e
cd "$(dirname "$0")"
DIR="$(pwd)"

if [ ! -f config.json ]; then
    echo "config.json not found. Copy config.example.json to config.json and fill it in first."
    exit 1
fi
if ! python3 -c 'import sys; sys.exit(sys.version_info < (3, 10))'; then
    echo "Python 3.10 or newer is needed. On Raspberry Pi, use Raspberry Pi OS Bookworm or newer."
    exit 1
fi
if ! python3 -m venv .venv; then
    echo "Could not create a virtual environment. Run: sudo apt install python3-venv"
    exit 1
fi
.venv/bin/pip install -r requirements.txt
.venv/bin/python nest_laptop.py check-bridge

sudo tee /etc/systemd/system/nest-laptop-bridge.service >/dev/null <<UNIT
[Unit]
Description=NestLaptop bridge (Google Home turns the laptop on and off)
After=network-online.target
Wants=network-online.target

[Service]
User=$(id -un)
WorkingDirectory=$DIR
ExecStart=$DIR/.venv/bin/python $DIR/nest_laptop.py bridge
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
UNIT
sudo systemctl daemon-reload
sudo systemctl enable nest-laptop-bridge
sudo systemctl restart nest-laptop-bridge
echo "Done. The bridge is running and starts at boot."
echo "See its log with: journalctl -u nest-laptop-bridge -f"
