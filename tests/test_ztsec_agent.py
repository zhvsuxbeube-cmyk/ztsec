import asyncio
import importlib.util
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("ztsec_agent", ROOT / "ztsec_agent.py")
agent = importlib.util.module_from_spec(spec)
spec.loader.exec_module(agent)


class FakeWriter:
    def __init__(self):
        self.data = bytearray()

    def write(self, data):
        self.data.extend(data)


class StopEvent:
    def __init__(self):
        self.value = False

    def set(self):
        self.value = True



def test_command_ack_contract():
    for command in ("CLOSE", "RECONNECT"):
        writer = FakeWriter()
        stop = StopEvent()
        handled, disconnect = agent._handle_command(command, writer, stop)
        assert handled is True
        assert bytes(writer.data) == (f"ACK:{command}\n").encode()
        if command == "CLOSE":
            assert stop.value is True
            assert disconnect is True
        else:
            assert stop.value is False
            assert disconnect is True


def test_block_command_is_not_agent_supported():
    writer = FakeWriter()
    stop = StopEvent()
    handled, disconnect = agent._handle_command("BLOCK", writer, stop)
    assert handled is False
    assert disconnect is False
    assert bytes(writer.data) == b""
    assert stop.value is False


def test_agent_does_not_register_block_command_source():
    source = (ROOT / "ztsec_agent.py").read_text(encoding="utf-8")
    assert "\"BLOCK\"" not in source.split("COMMANDS =", 1)[1].split("\n", 1)[0]
    assert "CMD:BLOCK" not in source


def test_retry_delay_is_fixed_five_seconds():
    assert agent.RETRY_DELAY_SECONDS == 5.0


def test_power_command_returns_clean_error_off_windows(monkeypatch):
    monkeypatch.setattr(agent.os, "name", "posix")
    writer = FakeWriter()
    stop = StopEvent()
    handled, disconnect = agent._handle_command("RESTART", writer, stop)
    assert handled is True
    assert disconnect is False
    assert bytes(writer.data) == b"ERR:RESTART\n"


def test_power_command_ack_without_invoking_windows():
    original = agent._execute_power_command
    agent._execute_power_command = lambda command: True
    try:
        writer = FakeWriter()
        stop = StopEvent()
        handled, disconnect = agent._handle_command("RESTART", writer, stop)
        assert handled is True
        assert disconnect is False
        assert bytes(writer.data) == b"ACK:RESTART\n"
    finally:
        agent._execute_power_command = original


async def integration_reconnect_and_close():
    # Keep this integration test focused on socket/retry semantics.
    # The real Windows telemetry collectors can legitimately take several
    # seconds and are covered by the application/agent runtime itself.
    original_static = agent._get_static_telemetry
    original_collect = agent.collect_telemetry
    agent._get_static_telemetry = lambda: {
        "fingerprint": "a" * 64,
    }
    agent.collect_telemetry = lambda index, host, port, ping_ms=-1: "Test|ZTSecurity|127.0.0.1|Local|Unknown|Unknown|Unknown|Unknown|Unknown|Unknown|Unknown|Unknown|Unknown|Unknown"

    received = []
    connection_count = 0
    connection_times = []
    second_connection = asyncio.Event()
    closed = asyncio.Event()

    async def handle(reader, writer):
        nonlocal connection_count
        connection_count += 1
        connection_times.append(__import__("time").monotonic())
        local_count = connection_count
        try:
            hello = await asyncio.wait_for(reader.readline(), 3)
            received.append(hello.decode(errors="replace").strip())
            data = await asyncio.wait_for(reader.readline(), 3)
            received.append(data.decode(errors="replace").strip())
            if local_count == 1:
                writer.write(b"CMD:RECONNECT\n")
                await writer.drain()
                ack = await asyncio.wait_for(reader.readline(), 3)
                received.append(ack.decode(errors="replace").strip())
            elif local_count == 2:
                second_connection.set()
                writer.write(b"CMD:CLOSE\n")
                await writer.drain()
                ack = await asyncio.wait_for(reader.readline(), 3)
                received.append(ack.decode(errors="replace").strip())
                closed.set()
        finally:
            writer.close()
            try:
                await writer.wait_closed()
            except (ConnectionResetError, BrokenPipeError, OSError):
                pass

    server = await asyncio.start_server(handle, "127.0.0.1", 0)
    host, port = server.sockets[0].getsockname()[:2]
    stop = asyncio.Event()
    task = asyncio.create_task(agent.run_connection_async(1, host, port, stop))
    try:
        await asyncio.wait_for(second_connection.wait(), 9)
        await asyncio.wait_for(closed.wait(), 2)
        assert received[0].startswith("HELLO:FINGERPRINT:")
        assert received[1].startswith("DATA:")
        assert received.count("ACK:RECONNECT") == 1
        assert received.count("ACK:CLOSE") == 1
        assert connection_count >= 2
        assert connection_times[1] - connection_times[0] >= 4.5
    finally:
        stop.set()
        task.cancel()
        await asyncio.gather(task, return_exceptions=True)
        server.close()
        await server.wait_closed()
        agent._get_static_telemetry = original_static
        agent.collect_telemetry = original_collect


if __name__ == "__main__":
    test_command_ack_contract()
    test_power_command_ack_without_invoking_windows()
    asyncio.run(integration_reconnect_and_close())
    print("PASS: agent protocol tests")


def test_ed25519_public_key_matches_rfc8032_vector():
    seed = bytes.fromhex(
        "9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60"
    )
    expected = bytes.fromhex(
        "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a"
    )
    assert agent._ed25519_public_key_from_seed(seed) == expected


def test_windows_fingerprint_shape_from_known_machine_value(monkeypatch):
    monkeypatch.setattr(agent, "os", type("OsShim", (), {"name": "nt"})())
    monkeypatch.setattr(agent, "_windows_machine_guid", lambda: "TEST-MACHINE-GUID")
    fingerprint = agent._fingerprint()
    assert len(fingerprint) == 64
    assert all(character in "0123456789abcdef" for character in fingerprint)
