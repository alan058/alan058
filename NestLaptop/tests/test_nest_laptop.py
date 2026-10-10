"""Tests for NestLaptop that need no Sinric Pro account and never shut anything down."""

import asyncio
import json
import pathlib
import sys
import threading

import pytest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))
import nest_laptop as nl  # noqa: E402

GOOD = {
    "sinric_app_key": "12345678-abcd-ef01-2345-6789abcdef01",
    "sinric_app_secret": "s" * 40,
    "sinric_device_id": "0123456789abcdef01234567",
}
TOKEN = "a-long-random-token-123"


def write_config(tmp_path, **values):
    path = tmp_path / "config.json"
    path.write_text(json.dumps(values), encoding="utf-8")
    return path


def test_magic_packet_is_ff_then_mac_16_times():
    packet = nl.magic_packet("aa-bb-cc-dd-ee-ff")
    assert len(packet) == 102
    assert packet[:6] == b"\xff" * 6
    assert packet[6:] == bytes.fromhex("aabbccddeeff") * 16


@pytest.mark.parametrize("mac", ["AA-BB-CC-DD-EE-FF", "aa:bb:cc:dd:ee:ff", "aabb.ccdd.eeff"])
def test_mac_formats(mac):
    assert nl.normalize_mac(mac) == "AA:BB:CC:DD:EE:FF"


def test_bad_mac_is_explained():
    with pytest.raises(nl.ConfigError, match="ipconfig"):
        nl.normalize_mac("192.168.1.5")


def test_config_defaults_and_help_keys_ignored(tmp_path):
    cfg = nl.load_config(write_config(tmp_path, _help="x", laptop_mac="aa-bb-cc-dd-ee-ff"))
    assert cfg["setup"] == "laptop_only"
    assert cfg["off_action"] == "shutdown"
    assert cfg["laptop_mac"] == "AA:BB:CC:DD:EE:FF"
    assert "_help" not in cfg


def test_example_config_loads():
    cfg = nl.load_config(nl.HERE / "config.example.json")
    assert cfg["setup"] in nl.SETUPS


def test_missing_config(tmp_path):
    with pytest.raises(nl.ConfigError, match="config.example.json"):
        nl.load_config(tmp_path / "config.json")


def test_bad_off_action(tmp_path):
    with pytest.raises(nl.ConfigError, match="off_action"):
        nl.load_config(write_config(tmp_path, off_action="reboot"))


def test_laptop_only_needs_sinric(tmp_path):
    cfg = nl.load_config(write_config(tmp_path))
    with pytest.raises(nl.ConfigError, match="sinric_app_key"):
        nl.check_laptop(cfg)
    nl.check_laptop(nl.load_config(write_config(tmp_path, **GOOD)))


def test_bad_sinric_values_are_explained(tmp_path):
    with pytest.raises(nl.ConfigError, match="sinric_device_id"):
        nl.check_sinric({**GOOD, "sinric_device_id": "Laptop"})
    with pytest.raises(nl.ConfigError, match="too short"):
        nl.check_sinric({**GOOD, "sinric_app_secret": "abc"})


def test_with_bridge_laptop_needs_only_token(tmp_path):
    cfg = nl.load_config(write_config(tmp_path, setup="with_bridge", agent_token="short"))
    with pytest.raises(nl.ConfigError, match="16 characters"):
        nl.check_laptop(cfg)
    cfg["agent_token"] = TOKEN
    nl.check_laptop(cfg)


def test_bridge_needs_with_bridge_setup_and_laptop_address(tmp_path):
    with pytest.raises(nl.ConfigError, match="with_bridge"):
        nl.check_bridge(nl.load_config(write_config(tmp_path, **GOOD)))
    cfg = nl.load_config(write_config(tmp_path, setup="with_bridge", agent_token=TOKEN, **GOOD))
    with pytest.raises(nl.ConfigError, match="laptop_mac"):
        nl.check_bridge(cfg)


def test_power_control_delay_and_cancel():
    power = nl.PowerControl("shutdown", 30, dry_run=True)
    power.turn_off()
    assert power._timer.is_alive()
    power.cancel()
    assert power._timer is None


@pytest.fixture
def agent():
    power = nl.PowerControl("sleep", 30, dry_run=True)
    server = nl.make_agent_server(power, TOKEN, 0, host="127.0.0.1")
    threading.Thread(target=server.serve_forever, daemon=True).start()
    cfg = {"laptop_ip": "127.0.0.1", "agent_port": server.server_address[1], "agent_token": TOKEN}
    yield power, cfg
    server.shutdown()
    power.cancel()


def test_agent_off_on_and_status(agent):
    power, cfg = agent
    assert nl.laptop_is_on(cfg)
    assert nl.agent_request(cfg, "/off") == "turning off"
    assert power._timer.is_alive()
    assert nl.agent_request(cfg, "/on") == "on"
    assert power._timer is None


def test_agent_rejects_wrong_token(agent):
    power, cfg = agent
    with pytest.raises(nl.urllib.error.HTTPError) as e:
        nl.agent_request({**cfg, "agent_token": "wrong-token-but-long"}, "/off")
    assert e.value.code == 403
    assert power._timer is None


def test_laptop_off_when_nothing_answers():
    import socket
    with socket.socket() as s:  # a port nothing listens on
        s.bind(("127.0.0.1", 0))
        port = s.getsockname()[1]
    assert not nl.laptop_is_on({"laptop_ip": "127.0.0.1", "agent_port": port, "agent_token": TOKEN})


class FakeClient:
    def is_connected(self):
        return True


class FakeSwitch:
    def __init__(self):
        self.events = []

    async def send_power_state_event(self, state):
        self.events.append(state)
        return True


def test_bridge_end_to_end(agent, monkeypatch):
    """Google says off -> laptop agent schedules the shutdown; Google says on -> magic packet."""
    power, agent_cfg = agent
    cfg = {**nl.DEFAULTS, **GOOD, **agent_cfg, "setup": "with_bridge",
           "laptop_mac": "AA:BB:CC:DD:EE:FF", "status_check_seconds": 5}
    switch, captured, woken = FakeSwitch(), {}, []

    async def fake_connect(cfg, on_power_state):
        captured["cb"] = on_power_state
        return FakeClient(), switch

    monkeypatch.setattr(nl, "connect_switch", fake_connect)
    monkeypatch.setattr(nl, "send_wake", lambda mac, ip: woken.append(mac))

    async def scenario():
        task = asyncio.create_task(nl.run_bridge(cfg))
        while "cb" not in captured or not switch.events:
            await asyncio.sleep(0.01)
        assert switch.events == [True]  # it reported the laptop as on at startup

        assert await captured["cb"](False) is True
        assert power._timer.is_alive()

        assert await captured["cb"](True) is True
        assert woken == ["AA:BB:CC:DD:EE:FF"]
        assert power._timer is None
        task.cancel()

    asyncio.run(scenario())


def test_bridge_off_when_laptop_already_off(monkeypatch):
    cfg = {**nl.DEFAULTS, **GOOD, "setup": "with_bridge", "laptop_ip": "127.0.0.1",
           "agent_port": 1, "agent_token": TOKEN, "laptop_mac": "AA:BB:CC:DD:EE:FF"}
    captured = {}

    async def fake_connect(cfg, on_power_state):
        captured["cb"] = on_power_state
        return FakeClient(), FakeSwitch()

    monkeypatch.setattr(nl, "connect_switch", fake_connect)

    async def scenario():
        task = asyncio.create_task(nl.run_bridge(cfg))
        while "cb" not in captured:
            await asyncio.sleep(0.01)
        assert await captured["cb"](False) is True
        task.cancel()

    asyncio.run(scenario())


def test_laptop_only_turns_off_and_reports(monkeypatch, tmp_path):
    cfg = {**nl.DEFAULTS, **GOOD}
    switch, captured = FakeSwitch(), {}
    ran = []

    async def fake_connect(cfg, on_power_state):
        captured["cb"] = on_power_state
        return FakeClient(), switch

    monkeypatch.setattr(nl, "connect_switch", fake_connect)
    monkeypatch.setattr(nl.PowerControl, "turn_off", lambda self: ran.append("off"))

    async def scenario():
        task = asyncio.create_task(nl.run_laptop(cfg, dry_run=True))
        while not switch.events:
            await asyncio.sleep(0.01)
        assert switch.events == [True]
        assert await captured["cb"](False) is True
        assert ran == ["off"]
        task.cancel()

    asyncio.run(scenario())


def test_real_sinric_sdk_accepts_our_settings():
    """The SDK pieces connect_switch uses exist with the arguments it passes."""
    sinricpro = pytest.importorskip("sinricpro")
    switch = sinricpro.SinricProSwitch(GOOD["sinric_device_id"])
    switch.on_power_state(lambda state: None)
    sinricpro.SinricProConfig(app_key=GOOD["sinric_app_key"], app_secret=GOOD["sinric_app_secret"],
                              local_control=False, mdns=False)
