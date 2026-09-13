#!/usr/bin/env python3
"""
ZeroTrace Security - standard-library telemetry connector.

This client is intentionally telemetry-only:
- opens a persistent TCP connection to the configured panel,
- sends one newline-delimited DATA record,
- sends harmless heartbeats while connected,
- receives `REQ:DATA` refresh requests from the panel and answers with `PONG` + a fresh `DATA` record,
- reconnects with exponential backoff after a network interruption,
- supports multiple simultaneous telemetry connections with --n N or --N N.

No third-party Python packages are required.
"""

import argparse
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
MAX_BACKOFF_SECONDS = 30


_STATIC_TELEMETRY_LOCK = threading.Lock()
_STATIC_TELEMETRY = None


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


def _hwid():
    """Read the Windows MachineGuid; fall back to the host UUID on other systems."""
    if os.name == "nt":
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
    try:
        return _clean(str(uuid.getnode()), "Unknown")
    except Exception:
        return "Unknown"


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
            }
    return _STATIC_TELEMETRY


def collect_telemetry(index, target_ip, target_port, ping_ms=None):
    """
    Build the panel's 15-field DATA record.

    Field order:
    Country|Nickname|Tag|UserName|Version|Privileges|OS|GPU|CPU|RAM|AntiVirus|Uptime|AFKTime|Ping
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
    ]
    return "|".join(_clean(v) for v in values)



async def run_connection_async(index, host, port, stop_event):
    """Maintain one connection without allocating a dedicated OS thread."""
    backoff = 1.0
    while not stop_event.is_set():
        reader = None
        writer = None
        try:
            connect_start = time.perf_counter()
            reader, writer = await asyncio.open_connection(host, port)
            ping_ms = int(round((time.perf_counter() - connect_start) * 1000))
            record = collect_telemetry(index, host, port, ping_ms=ping_ms)
            writer.write(f"DATA:{record}\n".encode("utf-8"))
            await writer.drain()

            backoff = 1.0
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
        except (ConnectionError, OSError, asyncio.TimeoutError) as exc:
            if not stop_event.is_set():
                print(f"[{index}] connection error: {exc}; retrying in {backoff:.0f}s", file=sys.stderr)
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
            await asyncio.wait_for(stop_event.wait(), timeout=backoff)
        except asyncio.TimeoutError:
            pass
        backoff = min(backoff * 2.0, MAX_BACKOFF_SECONDS)


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
