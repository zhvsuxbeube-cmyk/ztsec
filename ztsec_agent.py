#!/usr/bin/env python3
"""
ZeroTrace Security - standard-library telemetry connector.

This client maintains one or more persistent telemetry connections and accepts
small newline-delimited control commands from the panel. Commands are acknowledged
with a compact `ACK:<COMMAND>` response before connection-management or power actions.

No third-party Python packages are required.
"""

import argparse
import hashlib
import hmac
import ctypes
import getpass
import os
import platform
import shutil
import subprocess
import uuid
import sys
import asyncio
import threading
import time
from ctypes import wintypes


PROTOCOL_VERSION = "Python-StdLib/1"
DEFAULT_IP = "127.0.0.1"
DEFAULT_PORT = 4793
HEARTBEAT_SECONDS = 3
RETRY_DELAY_SECONDS = 5.0
COMMANDS = {"SLEEP", "HIBERNATE", "RESTART", "SHUTDOWN", "CLOSE", "RECONNECT"}


_STATIC_TELEMETRY_LOCK = threading.Lock()
_STATIC_TELEMETRY = None




def _send_command_ack(writer, command, ok=True):
    status = "ACK" if ok else "ERR"
    writer.write((f"{status}:{command}\n").encode("utf-8"))


def _execute_power_command(command):
    if os.name != "nt":
        return False

    try:
        if command == "SLEEP":
            ctypes.WinDLL("powrprof").SetSuspendState(False, False, False)
            return True
        if command == "HIBERNATE":
            ctypes.WinDLL("powrprof").SetSuspendState(True, False, False)
            return True
        shutdown = os.path.join(
            os.environ.get("SystemRoot", r"C:\Windows"),
            "System32",
            "shutdown.exe",
        )
        if command == "RESTART":
            subprocess.Popen([shutdown, "/r", "/t", "0", "/f"],
                             creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
            return True
        if command == "SHUTDOWN":
            subprocess.Popen([shutdown, "/s", "/t", "0", "/f"],
                             creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
            return True
    except (OSError, ctypes.ArgumentError):
        return False
    return False


def _handle_command(command, writer, stop_event):
    command = command.strip().upper()
    if command not in COMMANDS:
        return False, False

    if command == "CLOSE":
        _send_command_ack(writer, command)
        stop_event.set()
        return True, True

    if command == "RECONNECT":
        _send_command_ack(writer, command)
        return True, True

    if command in {"SLEEP", "HIBERNATE", "RESTART", "SHUTDOWN"}:
        ok = _execute_power_command(command)
        _send_command_ack(writer, command, ok=ok)
        return True, False

    return False, False


def _clean(value, fallback="Unknown"):
    """Make a value safe for the panel's pipe-delimited DATA record."""
    if value is None:
        return fallback
    value = str(value).replace("|", "/").replace("\r", " ").replace("\n", " ").strip()
    return value or fallback


def _format_duration(seconds):
    seconds = max(0, int(seconds))
    days, seconds = divmod(seconds, 86400)
    hours, seconds = divmod(seconds, 3600)
    minutes, seconds = divmod(seconds, 60)
    if days:
        return f"{days}d {hours}h {minutes}m"
    if hours:
        return f"{hours}h {minutes}m"
    if minutes:
        return f"{minutes}m {seconds}s"
    return f"{seconds}s"


def _windows_user_name():
    """Get the interactive Windows user using GetUserNameW when available."""
    if os.name != "nt":
        return getpass.getuser()

    advapi32 = ctypes.WinDLL("advapi32", use_last_error=True)
    func = advapi32.GetUserNameW
    func.argtypes = [wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD)]
    func.restype = wintypes.BOOL

    size = wintypes.DWORD(256)
    while size.value <= 32768:
        buf = ctypes.create_unicode_buffer(size.value)
        if func(buf, ctypes.byref(size)):
            return buf.value
        error = ctypes.get_last_error()
        # ERROR_INSUFFICIENT_BUFFER
        if error != 122:
            break
        size = wintypes.DWORD(size.value * 2)
    return getpass.getuser()


class _MEMORYSTATUSEX(ctypes.Structure):
    _fields_ = [
        ("dwLength", wintypes.DWORD),
        ("dwMemoryLoad", wintypes.DWORD),
        ("ullTotalPhys", ctypes.c_ulonglong),
        ("ullAvailPhys", ctypes.c_ulonglong),
        ("ullTotalPageFile", ctypes.c_ulonglong),
        ("ullAvailPageFile", ctypes.c_ulonglong),
        ("ullTotalVirtual", ctypes.c_ulonglong),
        ("ullAvailVirtual", ctypes.c_ulonglong),
        ("ullAvailExtendedVirtual", ctypes.c_ulonglong),
    ]


def _memory_text():
    """Return used/total physical RAM, preferring GlobalMemoryStatusEx."""
    try:
        if os.name == "nt":
            kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
            func = kernel32.GlobalMemoryStatusEx
            func.argtypes = [ctypes.POINTER(_MEMORYSTATUSEX)]
            func.restype = wintypes.BOOL
            state = _MEMORYSTATUSEX()
            state.dwLength = ctypes.sizeof(_MEMORYSTATUSEX)
            if func(ctypes.byref(state)):
                total_gb = state.ullTotalPhys / (1024 ** 3)
                used_gb = (state.ullTotalPhys - state.ullAvailPhys) / (1024 ** 3)
                return f"{used_gb:.1f}/{total_gb:.1f} GB ({state.dwMemoryLoad}%)"
    except (OSError, AttributeError):
        pass

    # Portable fallback for environments where the Windows API is unavailable.
    total = getattr(os, "sysconf", lambda *_: 0)("SC_PAGE_SIZE") if hasattr(os, "sysconf") else 0
    if total:
        try:
            pages = os.sysconf("SC_PHYS_PAGES")
            total_bytes = pages * total
            return f"{total_bytes / (1024 ** 3):.1f} GB"
        except (ValueError, OSError):
            pass
    return "Unknown"


class _LASTINPUTINFO(ctypes.Structure):
    _fields_ = [
        ("cbSize", wintypes.UINT),
        ("dwTime", wintypes.DWORD),
    ]


def _afk_seconds():
    """Return interactive idle time in seconds using User32.GetLastInputInfo."""
    if os.name != "nt":
        return 0

    user32 = ctypes.WinDLL("user32", use_last_error=True)
    func = user32.GetLastInputInfo
    func.argtypes = [ctypes.POINTER(_LASTINPUTINFO)]
    func.restype = wintypes.BOOL

    info = _LASTINPUTINFO()
    info.cbSize = ctypes.sizeof(_LASTINPUTINFO)
    if not func(ctypes.byref(info)):
        return 0

    # GetLastInputInfo.dwTime is a 32-bit tick count, so calculate elapsed
    # time with the same 32-bit counter and modulo arithmetic. This remains
    # correct across the ~49.7-day DWORD tick wrap.
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    ticks = kernel32.GetTickCount
    ticks.argtypes = []
    ticks.restype = wintypes.DWORD
    current_ms = int(ticks())
    last_ms = int(info.dwTime)
    elapsed_ms = (current_ms - last_ms) & 0xFFFFFFFF
    return int(elapsed_ms / 1000)


def _uptime_seconds():
    if os.name == "nt":
        try:
            kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
            func = kernel32.GetTickCount64
            func.argtypes = []
            func.restype = ctypes.c_ulonglong
            return int(func() / 1000)
        except (OSError, AttributeError):
            pass

    # Portable fallback (Linux/macOS test environments).
    try:
        return max(0, int(time.time() - os.stat("/proc").st_ctime))
    except (OSError, ValueError):
        return 0


def _is_admin():
    if os.name != "nt":
        return False
    try:
        shell32 = ctypes.WinDLL("shell32", use_last_error=True)
        func = shell32.IsUserAnAdmin
        func.argtypes = []
        func.restype = wintypes.BOOL
        return bool(func())
    except (OSError, AttributeError):
        return False


def _cpu_model():
    value = platform.processor().strip()
    if value:
        return value
    value = os.environ.get("PROCESSOR_IDENTIFIER", "").strip()
    return value or "Unknown"


def _disk_text():
    drive = os.environ.get("SystemDrive", "")
    if not drive:
        drive = os.path.abspath(os.sep)
    try:
        usage = shutil.disk_usage(drive)
        percent = (usage.used / usage.total * 100.0) if usage.total else 0.0
        return f"{usage.used / (1024 ** 3):.1f}/{usage.total / (1024 ** 3):.1f} GB ({percent:.0f}%)"
    except OSError:
        return "Unknown"


def _powershell(command, timeout=8):
    """Run a read-only Windows query using the built-in Windows PowerShell."""
    if os.name != "nt":
        return ""
    powershell = os.path.join(
        os.environ.get("SystemRoot", r"C:\Windows"),
        "System32", "WindowsPowerShell", "v1.0", "powershell.exe",
    )
    if not os.path.exists(powershell):
        return ""
    try:
        completed = subprocess.run(
            [powershell, "-NoProfile", "-NonInteractive", "-Command", command],
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=timeout,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
            check=False,
        )
        return completed.stdout.strip()
    except (OSError, subprocess.SubprocessError):
        return ""


def _gpu_model():
    """Read GPU names from the documented Win32_VideoController WMI class."""
    if os.name != "nt":
        return "Unknown"
    output = _powershell(
        "(Get-CimInstance Win32_VideoController | "
        "Where-Object { $_.Name } | "
        "Select-Object -ExpandProperty Name) -join '; '"
    )
    if not output:
        output = _powershell(
            "(Get-WmiObject Win32_VideoController | "
            "Where-Object { $_.Name } | "
            "Select-Object -ExpandProperty Name) -join '; '"
        )
    return _clean(output, "Unknown")


def _antivirus_status():
    """Read registered antivirus product names from Windows Security Center."""
    if os.name != "nt":
        return "Unknown"
    output = _powershell(
        "(Get-CimInstance -Namespace root/SecurityCenter2 "
        "-ClassName AntiVirusProduct | "
        "Where-Object { $_.displayName } | "
        "Select-Object -ExpandProperty displayName) -join '; '"
    )
    if not output:
        output = _powershell(
            "(Get-WmiObject -Namespace root/SecurityCenter2 "
            "-Class AntiVirusProduct | "
            "Where-Object { $_.displayName } | "
            "Select-Object -ExpandProperty displayName) -join '; '"
        )
    if not output:
        defender = _powershell(
            "try { Get-MpComputerStatus -ErrorAction Stop | "
            "Select-Object -First 1 | ForEach-Object { 'Microsoft Defender Antivirus' } "
            "} catch {}"
        )
        output = defender
    return _clean(output, "Unknown")


def _windows_machine_guid():
    if os.name != "nt":
        return ""
    try:
        import winreg
        for path in (
            r"SOFTWARE\Microsoft\Cryptography",
            r"SOFTWARE\Wow6432Node\Microsoft\Cryptography",
        ):
            try:
                with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, path) as key:
                    value, _ = winreg.QueryValueEx(key, "MachineGuid")
                    value = _clean(value, "")
                    if value:
                        return value
            except OSError:
                continue
    except ImportError:
        pass
    return ""


def _hwid():
    """Keep the existing Windows HWID telemetry value stable (MachineGuid)."""
    machine_id = _windows_machine_guid()
    if machine_id:
        return machine_id
    try:
        return _clean(str(uuid.getnode()), "Unknown")
    except Exception:
        return "Unknown"


def _derived_hwid_for_identity(machine_id):
    """Match the supplied Go config's deriveHWID() for fingerprint derivation."""
    if machine_id:
        return hashlib.sha256((machine_id + "|windows").encode("utf-8")).hexdigest()
    return hashlib.sha256((platform.node() + "|" + getpass.getuser() + "|windows").encode("utf-8")).hexdigest()


def _hkdf_sha256(secret, salt, info, length):
    """RFC5869 HKDF-SHA256 matching golang.org/x/crypto/hkdf.New."""
    if not salt:
        salt = b"\x00" * hashlib.sha256().digest_size
    prk = hmac.new(salt, secret, hashlib.sha256).digest()
    output = bytearray()
    previous = b""
    counter = 1
    while len(output) < length:
        previous = hmac.new(prk, previous + info + bytes([counter]), hashlib.sha256).digest()
        output.extend(previous)
        counter += 1
    return bytes(output[:length])


# Minimal Ed25519 public-key derivation, used only to reproduce the identity
# fingerprint generated by the Go agent's ed25519.NewKeyFromSeed path.
_ED25519_Q = 2 ** 255 - 19
_ED25519_L = 2 ** 252 + 27742317777372353535851937790883648493
_ED25519_D = (-121665 * pow(121666, _ED25519_Q - 2, _ED25519_Q)) % _ED25519_Q
_ED25519_I = pow(2, (_ED25519_Q - 1) // 4, _ED25519_Q)
_ED25519_B_Y = (4 * pow(5, _ED25519_Q - 2, _ED25519_Q)) % _ED25519_Q
_ED25519_B_X = None


def _xrecover(y):
    xx = (y * y - 1) * pow(_ED25519_D * y * y + 1, _ED25519_Q - 2, _ED25519_Q)
    x = pow(xx, (_ED25519_Q + 3) // 8, _ED25519_Q)
    if (x * x - xx) % _ED25519_Q:
        x = (x * _ED25519_I) % _ED25519_Q
    if x & 1:
        x = _ED25519_Q - x
    return x


_ED25519_B_X = _xrecover(_ED25519_B_Y)
_ED25519_B = (_ED25519_B_X, _ED25519_B_Y)


def _edwards_add(p, q):
    x1, y1 = p
    x2, y2 = q
    denom_x = pow(1 + _ED25519_D * x1 * x2 * y1 * y2, _ED25519_Q - 2, _ED25519_Q)
    denom_y = pow(1 - _ED25519_D * x1 * x2 * y1 * y2, _ED25519_Q - 2, _ED25519_Q)
    return (
        ((x1 * y2 + x2 * y1) * denom_x) % _ED25519_Q,
        ((y1 * y2 + x1 * x2) * denom_y) % _ED25519_Q,
    )


def _edwards_mul(point, scalar):
    result = (0, 1)
    addend = point
    while scalar:
        if scalar & 1:
            result = _edwards_add(result, addend)
        addend = _edwards_add(addend, addend)
        scalar >>= 1
    return result


def _ed25519_public_key_from_seed(seed):
    h = hashlib.sha512(seed).digest()
    scalar_bytes = bytearray(h[:32])
    scalar_bytes[0] &= 248
    scalar_bytes[31] &= 63
    scalar_bytes[31] |= 64
    scalar = int.from_bytes(scalar_bytes, "little")
    x, y = _edwards_mul(_ED25519_B, scalar)
    encoded = bytearray(int(y).to_bytes(32, "little"))
    encoded[31] |= (x & 1) << 7
    return bytes(encoded)


def _fingerprint():
    if os.name != "nt":
        return ""
    machine_id = _windows_machine_guid()
    if not machine_id:
        return ""
    hwid = _derived_hwid_for_identity(machine_id)
    seed = _hkdf_sha256(
        machine_id.encode("utf-8"),
        hwid.encode("utf-8"),
        b"mirage-identity",
        32,
    )
    public_key = _ed25519_public_key_from_seed(seed)
    return hashlib.sha256(public_key).hexdigest()


def _get_static_telemetry():
    """Collect expensive/read-mostly host facts once for all simulated clients."""
    global _STATIC_TELEMETRY
    if _STATIC_TELEMETRY is not None:
        return _STATIC_TELEMETRY

    with _STATIC_TELEMETRY_LOCK:
        if _STATIC_TELEMETRY is None:
            _STATIC_TELEMETRY = {
                "nickname": _clean(platform.node(), "Windows"),
                "user_name": _clean(_windows_user_name(), "Unknown"),
                "privileges": "Administrator" if _is_admin() else "User",
                "os_name": _clean(platform.platform(), "Windows"),
                "cpu": _clean(_cpu_model()),
                # GPU and antivirus previously ran PowerShell once per connection.
                # Concurrent PowerShell/WMI launches caused timeouts and therefore
                # "Unknown" values. These values are host-level and are safely cached.
                "gpu": _clean(_gpu_model()),
                "antivirus": _clean(_antivirus_status()),
                "hwid": _clean(_hwid()),
                "fingerprint": _clean(_fingerprint(), "Unknown"),
            }
    return _STATIC_TELEMETRY


def collect_telemetry(index, target_ip, target_port, ping_ms=None):
    """
    Build the panel's 15-field DATA record.

    Field order:
    Country|Nickname|Tag|UserName|Version|Privileges|OS|GPU|CPU|RAM|AntiVirus|Uptime|AFKTime|Ping|HWID|Fingerprint
    """
    static = _get_static_telemetry()
    tag = f"PY-{index}"
    nickname = static["nickname"]
    user_name = static["user_name"]
    privileges = static["privileges"]
    os_name = static["os_name"]
    cpu = static["cpu"]
    ram = _clean(_memory_text())
    uptime = _format_duration(_uptime_seconds())
    afk = _format_duration(_afk_seconds())
    gpu = static["gpu"]
    antivirus = static["antivirus"]
    hwid = static["hwid"]
    fingerprint = static["fingerprint"]

    # The connection RTT is measured by run_connection so the telemetry
    # record does not open an unnecessary second TCP connection.
    if ping_ms is None:
        ping_ms = -1

    values = [
        "Local" if target_ip in {"127.0.0.1", "::1", "localhost"} else "Unknown",
        nickname,
        tag,
        user_name,
        PROTOCOL_VERSION,
        privileges,
        os_name,
        gpu,
        cpu,
        ram,
        antivirus,
        uptime,
        afk,
        "Unknown" if ping_ms < 0 else f"{ping_ms} ms",
        hwid,
        fingerprint,
    ]
    return "|".join(_clean(v) for v in values)



async def run_connection_async(index, host, port, stop_event):
    """Maintain one connection without allocating a dedicated OS thread."""
    while not stop_event.is_set():
        reader = None
        writer = None
        try:
            connect_start = time.perf_counter()
            reader, writer = await asyncio.open_connection(host, port)
            fingerprint = _get_static_telemetry()["fingerprint"]
            writer.write(f"HELLO:FINGERPRINT:{fingerprint}\n".encode("utf-8"))
            await writer.drain()
            ping_ms = int(round((time.perf_counter() - connect_start) * 1000))
            record = collect_telemetry(index, host, port, ping_ms=ping_ms)
            writer.write(f"DATA:{record}\n".encode("utf-8"))
            await writer.drain()

            while not stop_event.is_set():
                try:
                    raw_line = await asyncio.wait_for(reader.readline(), timeout=HEARTBEAT_SECONDS)
                except asyncio.TimeoutError:
                    writer.write(b"HB\n")
                    await writer.drain()
                    continue

                if not raw_line:
                    raise ConnectionError("panel closed the connection")

                command = raw_line.decode("utf-8", errors="replace").strip()
                if command == "REQ:DATA":
                    writer.write(b"PONG\n")
                    writer.write(f"DATA:{collect_telemetry(index, host, port)}\n".encode("utf-8"))
                    await writer.drain()
                    continue

                if command.startswith("CMD:"):
                    command_name = command[4:].strip().upper()
                    handled, disconnect = _handle_command(command_name, writer, stop_event)
                    if handled:
                        await writer.drain()
                        if command_name in {"SLEEP", "HIBERNATE", "RESTART", "SHUTDOWN"}:
                            break
                        if disconnect:
                            break
                        continue
        except (ConnectionError, OSError, asyncio.TimeoutError) as exc:
            if not stop_event.is_set():
                print(f"[{index}] connection error: {exc}; retrying in {RETRY_DELAY_SECONDS:.0f}s", file=sys.stderr)
        finally:
            if writer is not None:
                writer.close()
                try:
                    await writer.wait_closed()
                except (OSError, RuntimeError):
                    pass

        if stop_event.is_set():
            break
        try:
            await asyncio.wait_for(stop_event.wait(), timeout=RETRY_DELAY_SECONDS)
        except asyncio.TimeoutError:
            pass


def parse_args():
    parser = argparse.ArgumentParser(
        description="Connect to the ZTSecurity panel and send standard-library system telemetry."
    )
    parser.add_argument("--ip", default=DEFAULT_IP, help=f"Panel IP/hostname (default: {DEFAULT_IP})")
    parser.add_argument("--port", default=DEFAULT_PORT, type=int, help=f"Panel TCP port (default: {DEFAULT_PORT})")
    parser.add_argument(
        "--n", "--N",
        dest="connections",
        default=1,
        type=int,
        metavar="N",
        help="Number of simultaneous telemetry connections (default: 1)",
    )
    args = parser.parse_args()

    if not 1 <= args.port <= 65535:
        parser.error("--port must be between 1 and 65535")
    if args.connections < 1:
        parser.error("--n/--N must be at least 1")
    if not args.ip.strip():
        parser.error("--ip cannot be empty")
    return args


async def _async_main(args):
    # Warm expensive, read-mostly telemetry before opening any sockets. This makes
    # connection fan-out bounded by network/OS capacity rather than one WMI/
    # PowerShell launch per simulated client.
    _get_static_telemetry()

    stop_event = asyncio.Event()
    tasks = [
        asyncio.create_task(run_connection_async(index, args.ip, args.port, stop_event))
        for index in range(1, args.connections + 1)
    ]

    print(f"connecting to {args.ip}:{args.port} with {args.connections} telemetry connection(s)")

    try:
        await asyncio.gather(*tasks)
    finally:
        stop_event.set()
        for task in tasks:
            task.cancel()
        await asyncio.gather(*tasks, return_exceptions=True)


def main():
    args = parse_args()
    try:
        asyncio.run(_async_main(args))
    except KeyboardInterrupt:
        print("\nstopping...")


if __name__ == "__main__":
    main()
