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
    for command in ("CLOSE", "RECONNECT", "BLOCK"):
        writer = FakeWriter()
        stop = StopEvent()
        handled, disconnect = agent._handle_command(command, writer, stop)
        assert handled is True
        assert bytes(writer.data) == (f"ACK:{command}\n").encode()
        if command in {"CLOSE", "BLOCK"}:
            assert stop.value is True
            assert disconnect is True
        else:
            assert stop.value is False
            assert disconnect is True


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
    received = []
    connection_count = 0
    closed = asyncio.Event()

    async def handle(reader, writer):
        nonlocal connection_count
        connection_count += 1
        hello = await reader.readline()
        received.append(hello.decode().strip())
        data = await reader.readline()
        received.append(data.decode().strip())
        if connection_count == 1:
            writer.write(b"CMD:RECONNECT\n")
            await writer.drain()
            ack = await reader.readline()
            received.append(ack.decode().strip())
        else:
            writer.write(b"CMD:CLOSE\n")
            await writer.drain()
            ack = await reader.readline()
            received.append(ack.decode().strip())
            closed.set()
        writer.close()
        await writer.wait_closed()

    server = await asyncio.start_server(handle, "127.0.0.1", 0)
    host, port = server.sockets[0].getsockname()[:2]
    stop = asyncio.Event()
    task = asyncio.create_task(agent.run_connection_async(1, host, port, stop))
    try:
        await asyncio.wait_for(closed.wait(), 5)
        assert received[0].startswith("HELLO:FINGERPRINT:")
        assert received[1].startswith("DATA:")
        assert "ACK:RECONNECT" in received
        assert "ACK:CLOSE" in received
    finally:
        stop.set()
        task.cancel()
        await asyncio.gather(task, return_exceptions=True)
        server.close()
        await server.wait_closed()


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
