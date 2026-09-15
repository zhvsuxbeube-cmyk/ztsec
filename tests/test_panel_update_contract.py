import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FORM = ROOT / "Form1.cs"


def main():
    source = FORM.read_text(encoding="utf-8")

    # The failed CI build came from a recursively self-referencing local lambda.
    assert "updateRow(statusText, statusColor, pct)" not in source
    assert "UpdateFileCommandRow(" in source

    # All worker-thread UI updates must be marshalled asynchronously and safely.
    helper_start = source.index("private static void UpdateFileCommandRow(")
    helper_end = source.index("private void LogFinalCommandResult", helper_start)
    helper = source[helper_start:helper_end]
    assert "dlg.InvokeRequired" in helper
    assert "dlg.BeginInvoke(updateOnUiThread)" in helper
    assert "InvalidOperationException" in helper
    assert "ObjectDisposedException" in helper

    # A response can arrive as soon as the last request byte is written, so the
    # file-command result must be registered before NetworkStream.Write is reached.
    file_block_start = source.index("string resultKey = TrackPendingFileCommand(cid, cmdKey)")
    file_block_end = source.index("// Wait up to 30 s for ACK or ERR.", file_block_start)
    file_block = source[file_block_start:file_block_end]
    assert file_block.index("TrackPendingFileCommand") < file_block.index("NetworkStream ns = tcpClient.GetStream()")
    assert file_block.index("NetworkStream ns = tcpClient.GetStream()") < file_block.index("ns.Write")

    # The same ordering must hold for normal command sends.
    send_start = source.index("string pendingKey = TrackPendingCommand(connectionId, command);")
    send_end = source.index("catch (IOException)", send_start)
    send_block = source[send_start:send_end]
    assert send_block.index("TrackPendingCommand") < send_block.index("NetworkStream stream = client.GetStream()")
    assert send_block.index("NetworkStream stream = client.GetStream()") < send_block.index("stream.Write")

    # Watchdog expiry must identify the exact pending instance so an old watchdog
    # cannot remove a newer request using the same connection/command key.
    track_start = source.index("private string RegisterPendingCommand(")
    track_end = source.index("private string TrackPendingCommand(", track_start)
    tracker = source[track_start:track_end]
    assert "ReferenceEquals(current, pending)" in tracker

    # Successful results are retained only for file dialogs that explicitly poll them.
    complete_start = source.index("private void CompletePendingCommand(")
    complete_end = source.index("private void ReportPendingCommandsOnDisconnect", complete_start)
    complete = source[complete_start:complete_end]
    assert "if (pending.StoreResult)" in complete

    print("PASS: panel update concurrency/source contracts")


if __name__ == "__main__":
    main()
