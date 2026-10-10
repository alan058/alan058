"""Turn a Windows laptop on and off with a Google Nest Mini.

Google Home talks to Sinric Pro (a free smart home cloud), and Sinric Pro talks to
this program. The laptop shows up in the Google Home app as a switch, so you can say
"Hey Google, turn off the laptop" or "Hey Google, turn on the laptop".

Run one of:
    python nest_laptop.py laptop    on the Windows laptop (shuts it down, sleeps or hibernates)
    python nest_laptop.py bridge    on an always-on device (wakes the laptop with Wake-on-LAN)
    python nest_laptop.py wake      send one Wake-on-LAN packet now, to test waking
    python nest_laptop.py check-laptop / check-bridge    check config.json for that device

Settings are read from config.json next to this file. See README.md.
"""

import argparse
import asyncio
import hmac
import json
import logging
import logging.handlers
import pathlib
import re
import socket
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

HERE = pathlib.Path(__file__).resolve().parent
CONFIG_PATH = HERE / "config.json"
LOG_PATH = HERE / "nest_laptop.log"
# The installers put the Sinric Pro library here, so it works under any Windows account.
sys.path.insert(0, str(HERE / "lib"))

SETUPS = ("laptop_only", "with_bridge")
OFF_ACTIONS = ("shutdown", "sleep", "hibernate")
DEFAULTS = {
    "setup": "laptop_only",
    "off_action": "shutdown",
    "off_delay_seconds": 5,
    "laptop_mac": "",
    "laptop_ip": "",
    "broadcast_ip": "255.255.255.255",
    "agent_port": 8765,
    "agent_token": "",
    "status_check_seconds": 30,
}

log = logging.getLogger("nest_laptop")


# ---------------------------------------------------------------- configuration

class ConfigError(Exception):
    pass


def load_config(path=CONFIG_PATH):
    if not path.exists():
        raise ConfigError(f"{path.name} not found. Copy config.example.json to config.json "
                          "and fill it in.")
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except ValueError as e:
        raise ConfigError(f"{path.name} is not valid JSON: {e}") from None
    cfg = {**DEFAULTS, **{k: v for k, v in data.items() if not k.startswith("_")}}
    if cfg["setup"] not in SETUPS:
        raise ConfigError(f'"setup" must be one of {", ".join(SETUPS)}')
    if cfg["off_action"] not in OFF_ACTIONS:
        raise ConfigError(f'"off_action" must be one of {", ".join(OFF_ACTIONS)}')
    if cfg["laptop_mac"]:
        cfg["laptop_mac"] = normalize_mac(cfg["laptop_mac"])
    return cfg


def require(cfg, *keys):
    missing = [k for k in keys if not str(cfg.get(k, "")).strip()]
    if missing:
        raise ConfigError(f'Fill in {", ".join(missing)} in config.json')


def check_sinric(cfg):
    require(cfg, "sinric_app_key", "sinric_app_secret", "sinric_device_id")
    if not re.fullmatch(r"[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}",
                        cfg["sinric_app_key"].strip()):
        raise ConfigError('"sinric_app_key" should look like xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx '
                          "(Sinric Pro website > Credentials)")
    if len(cfg["sinric_app_secret"].strip()) < 32:
        raise ConfigError('"sinric_app_secret" looks too short. Copy the whole APP SECRET '
                          "from Sinric Pro > Credentials.")
    if not re.fullmatch(r"[0-9a-fA-F]{24}", cfg["sinric_device_id"].strip()):
        raise ConfigError('"sinric_device_id" should be 24 letters and digits '
                          "(Sinric Pro > Devices > your laptop > Device ID)")


def check_token(cfg):
    require(cfg, "agent_token")
    if len(cfg["agent_token"]) < 16:
        raise ConfigError('"agent_token" must be at least 16 characters (any random text, '
                          "the same on the laptop and the bridge)")


def check_laptop(cfg):
    if cfg["setup"] == "with_bridge":
        check_token(cfg)
    else:
        check_sinric(cfg)


def check_bridge(cfg):
    if cfg["setup"] != "with_bridge":
        raise ConfigError('Set "setup": "with_bridge" in config.json on both the bridge '
                          "and the laptop.")
    require(cfg, "laptop_mac", "laptop_ip")
    check_token(cfg)
    check_sinric(cfg)


def normalize_mac(mac):
    digits = re.sub(r"[^0-9A-Fa-f]", "", mac)
    if len(digits) != 12:
        raise ConfigError(f'"{mac}" is not a MAC address. It looks like AA-BB-CC-DD-EE-FF '
                          '(the "Physical Address" in ipconfig /all).')
    return ":".join(digits[i:i + 2] for i in range(0, 12, 2)).upper()


# ---------------------------------------------------------------- Wake-on-LAN

def magic_packet(mac):
    """6 bytes of FF followed by the MAC address 16 times."""
    return b"\xff" * 6 + bytes.fromhex(normalize_mac(mac).replace(":", "")) * 16


def send_wake(mac, broadcast_ip="255.255.255.255"):
    packet = magic_packet(mac)
    targets = {broadcast_ip, "255.255.255.255"}
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as s:
        s.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
        # Repeat on both usual ports: it's UDP, so a packet can get lost.
        for _ in range(3):
            for ip in targets:
                for port in (9, 7):
                    s.sendto(packet, (ip, port))
    log.info("Sent Wake-on-LAN packet to %s via %s", mac, ", ".join(sorted(targets)))


# ---------------------------------------------------------------- power control (Windows)

class PowerControl:
    """Shuts down, sleeps or hibernates this PC after a short delay that can be cancelled."""

    def __init__(self, action, delay, dry_run=False):
        self.action = action
        self.delay = max(0, int(delay))
        self.dry_run = dry_run
        self._timer = None
        self._lock = threading.Lock()

    def turn_off(self):
        with self._lock:
            self._cancel_timer()
            log.info("Turning off: %s in %s seconds", self.action, self.delay)
            self._timer = threading.Timer(self.delay, self._run)
            self._timer.daemon = True
            self._timer.start()

    def cancel(self):
        with self._lock:
            if self._cancel_timer():
                log.info("Cancelled the pending %s", self.action)

    def _cancel_timer(self):
        if self._timer and self._timer.is_alive():
            self._timer.cancel()
            self._timer = None
            return True
        return False

    def _run(self):
        if self.dry_run:
            log.info("[dry run] Would %s now", self.action)
            return
        if self.action == "shutdown":
            subprocess.run(["shutdown", "/s", "/t", "0"], check=False)
        elif self.action == "hibernate":
            subprocess.run(["shutdown", "/h"], check=False)
        else:
            # Called directly (not through rundll32) so it really sleeps instead of
            # hibernating when hibernation is enabled.
            import ctypes
            enable_shutdown_privilege()
            if not ctypes.windll.powrprof.SetSuspendState(False, True, False):
                log.error("Windows refused to sleep (error %s)", ctypes.GetLastError())


def enable_shutdown_privilege():
    """Sleeping needs SeShutdownPrivilege switched on, which shutdown.exe does for itself."""
    import ctypes
    from ctypes import wintypes

    class LUID_AND_ATTRIBUTES(ctypes.Structure):
        _fields_ = [("Luid", ctypes.c_int64), ("Attributes", wintypes.DWORD)]

    class TOKEN_PRIVILEGES(ctypes.Structure):
        _fields_ = [("PrivilegeCount", wintypes.DWORD), ("Privileges", LUID_AND_ATTRIBUTES * 1)]

    advapi32, kernel32 = ctypes.windll.advapi32, ctypes.windll.kernel32
    kernel32.GetCurrentProcess.restype = wintypes.HANDLE
    advapi32.OpenProcessToken.argtypes = [wintypes.HANDLE, wintypes.DWORD,
                                          ctypes.POINTER(wintypes.HANDLE)]
    advapi32.AdjustTokenPrivileges.argtypes = [wintypes.HANDLE, wintypes.BOOL, ctypes.c_void_p,
                                               wintypes.DWORD, ctypes.c_void_p, ctypes.c_void_p]
    kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
    token = wintypes.HANDLE()
    TOKEN_ADJUST_PRIVILEGES, TOKEN_QUERY, SE_PRIVILEGE_ENABLED = 0x20, 0x8, 0x2
    if not advapi32.OpenProcessToken(kernel32.GetCurrentProcess(),
                                     TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, ctypes.byref(token)):
        return
    try:
        privileges = TOKEN_PRIVILEGES(1)
        privileges.Privileges[0].Attributes = SE_PRIVILEGE_ENABLED
        if advapi32.LookupPrivilegeValueW(None, "SeShutdownPrivilege",
                                          ctypes.byref(privileges.Privileges[0], 0)):
            advapi32.AdjustTokenPrivileges(token, False, ctypes.byref(privileges), 0, None, None)
    finally:
        kernel32.CloseHandle(token)


# ---------------------------------------------------------------- LAN agent (laptop side of with_bridge)

def make_agent_server(power, token, port, host="0.0.0.0"):
    """A tiny HTTP server the bridge calls: POST /off, POST /on (cancel), GET /status."""

    class Handler(BaseHTTPRequestHandler):
        def _reply(self, code, text):
            body = text.encode()
            self.send_response(code)
            self.send_header("Content-Type", "text/plain")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self):
            if self.path == "/status":
                return self._reply(200, "on")
            self._reply(404, "not found")

        def do_POST(self):
            given = self.headers.get("X-Token", "")
            if not hmac.compare_digest(given.encode(), token.encode()):
                log.warning("Rejected request from %s: wrong token", self.client_address[0])
                return self._reply(403, "wrong token")
            if self.path == "/off":
                power.turn_off()
                return self._reply(200, "turning off")
            if self.path == "/on":
                power.cancel()
                return self._reply(200, "on")
            self._reply(404, "not found")

        def log_message(self, fmt, *args):
            log.debug("agent: %s - %s", self.client_address[0], fmt % args)

    return ThreadingHTTPServer((host, port), Handler)


def agent_request(cfg, path, method="POST", timeout=3):
    url = f"http://{cfg['laptop_ip']}:{cfg['agent_port']}{path}"
    req = urllib.request.Request(url, method=method, data=b"" if method == "POST" else None,
                                 headers={"X-Token": cfg["agent_token"]})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return r.read().decode()


def laptop_is_on(cfg):
    try:
        return agent_request(cfg, "/status", method="GET", timeout=2) == "on"
    except (OSError, urllib.error.URLError):
        return False


# ---------------------------------------------------------------- Sinric Pro (Google Home link)

async def connect_switch(cfg, on_power_state):
    """Connect to Sinric Pro and call on_power_state(True/False) when Google asks."""
    try:
        from sinricpro import SinricPro, SinricProConfig, SinricProSwitch
    except ImportError:
        raise ConfigError("The sinricpro library is missing. Run test_laptop.bat or "
                          "install_laptop.bat (or: pip install -r requirements.txt)") from None

    switch = SinricProSwitch(cfg["sinric_device_id"].strip())
    switch.on_power_state(on_power_state)
    client = SinricPro.get_instance()
    client.add(switch)
    await client.begin(SinricProConfig(
        app_key=cfg["sinric_app_key"].strip(),
        app_secret=cfg["sinric_app_secret"].strip(),
        local_control=False,  # Google always goes through the cloud; no LAN listener needed
        mdns=False,
    ))
    log.info("Started. Say \"Hey Google, turn off <device name>\".")
    return client, switch


class StateReporter:
    """Keeps the on/off state shown in the Google Home app in step with the laptop."""

    def __init__(self, read_state):
        self.read_state = read_state  # async () -> bool
        self.client = self.switch = None
        self.reported = None
        self._hold = (None, 0.0)

    def expect(self, state, seconds):
        """Google just set this state. Trust it while the laptop shuts down or boots."""
        self.reported = state
        self._hold = (state, time.time() + seconds)

    async def run(self, every):
        while True:
            held, until = self._hold
            state = held if time.time() < until else await self.read_state()
            if not self.client.is_connected():
                self.reported = None  # resend once reconnected (for example after sleep)
            elif state != self.reported and await self.switch.send_power_state_event(state):
                log.info("Told Google Home the laptop is %s", "on" if state else "off")
                self.reported = state
            await asyncio.sleep(every)


async def run_laptop(cfg, dry_run):
    check_laptop(cfg)
    power = PowerControl(cfg["off_action"], cfg["off_delay_seconds"], dry_run)

    if cfg["setup"] == "with_bridge":
        server = make_agent_server(power, cfg["agent_token"], int(cfg["agent_port"]))
        log.info("Waiting for the bridge on port %s", cfg["agent_port"])
        await asyncio.to_thread(server.serve_forever)
        return

    async def is_on():
        return True  # this code is running, so the laptop is on

    reporter = StateReporter(is_on)

    async def on_power_state(state):
        log.info("Google Home asked to turn the laptop %s", "on" if state else "off")
        if state:
            power.cancel()  # it's already on; just stop a shutdown that hasn't happened yet
            reporter.expect(True, 0)
        else:
            power.turn_off()  # the delay lets the reply reach Google before we go down
            reporter.expect(False, power.delay + 60)
        return True

    reporter.client, reporter.switch = await connect_switch(cfg, on_power_state)
    await reporter.run(every=15)


async def run_bridge(cfg):
    check_bridge(cfg)

    async def is_on():
        return await asyncio.to_thread(laptop_is_on, cfg)

    reporter = StateReporter(is_on)

    async def on_power_state(state):
        log.info("Google Home asked to turn the laptop %s", "on" if state else "off")
        if state:
            await asyncio.to_thread(send_wake, cfg["laptop_mac"], cfg["broadcast_ip"])
            try:  # also cancels a pending shutdown if the laptop is still up
                await asyncio.to_thread(agent_request, cfg, "/on")
            except (OSError, urllib.error.URLError):
                pass
            reporter.expect(True, 180)  # give it time to boot
            return True
        try:
            await asyncio.to_thread(agent_request, cfg, "/off")
        except urllib.error.HTTPError as e:
            log.error("The laptop refused to turn off (HTTP %s). Is agent_token the same "
                      "in both config.json files?", e.code)
            return False
        except (OSError, urllib.error.URLError):
            log.info("Laptop is not reachable, so it is already off")
        reporter.expect(False, 120)  # give it time to shut down
        return True

    reporter.client, reporter.switch = await connect_switch(cfg, on_power_state)
    await reporter.run(every=max(5, int(cfg["status_check_seconds"])))


# ---------------------------------------------------------------- entry point

def setup_logging(verbose):
    log_file = logging.handlers.RotatingFileHandler(LOG_PATH, maxBytes=1_000_000,
                                                    backupCount=2, encoding="utf-8")
    log_file.setFormatter(logging.Formatter("%(asctime)s %(levelname)s %(message)s"))
    handlers = [log_file]
    if sys.stderr is not None:  # None under pythonw / a background task
        handlers.append(logging.StreamHandler())
    logging.basicConfig(level=logging.DEBUG if verbose else logging.INFO, handlers=handlers,
                        format="%(asctime)s %(levelname)s %(message)s")
    # The Sinric Pro library prints to the console only; keep its messages in the log too.
    logging.getLogger("sinricpro").addHandler(log_file)


def main(argv=None):
    parser = argparse.ArgumentParser(description="Control a Windows laptop with Google Home.")
    parser.add_argument("role", choices=("laptop", "bridge", "wake", "check-laptop",
                                         "check-bridge"))
    parser.add_argument("--dry-run", action="store_true",
                        help="laptop: log what would happen instead of shutting down")
    parser.add_argument("--config", type=pathlib.Path, default=CONFIG_PATH)
    parser.add_argument("-v", "--verbose", action="store_true")
    args = parser.parse_args(argv)
    setup_logging(args.verbose)

    try:
        cfg = load_config(args.config)
        if args.role.startswith("check-"):
            (check_laptop if args.role == "check-laptop" else check_bridge)(cfg)
            log.info("config.json looks good (setup: %s)", cfg["setup"])
        elif args.role == "wake":
            require(cfg, "laptop_mac")
            send_wake(cfg["laptop_mac"], cfg["broadcast_ip"])
        elif args.role == "laptop":
            asyncio.run(run_laptop(cfg, args.dry_run))
        else:
            asyncio.run(run_bridge(cfg))
    except ConfigError as e:
        log.error("%s", e)
        return 2
    except KeyboardInterrupt:
        pass
    return 0


if __name__ == "__main__":
    sys.exit(main())
