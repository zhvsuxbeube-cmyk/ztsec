from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FORM = (ROOT / "Form1.cs").read_text(encoding="utf-8")
WORKFLOW = (ROOT / ".github/workflows/windows-build.yml").read_text(encoding="utf-8")


def test_ci_automation_does_not_delete_live_trigger_from_worker():
    # Both filesystem trigger consumers must avoid a Windows-sharing-violation race.
    assert FORM.count('File.Delete(triggerPath)') == 0
    assert 'ci-open-download-one-consumed.flag' in FORM
    assert 'ci-open-notifications-consumed.flag' in FORM


def test_file_command_registers_pending_before_socket_write():
    register = FORM.index('pendingCommands[resultKey] = new PendingCommand')
    write = FORM.index('ns.Write(rawBytes, sent, toSend);')
    assert register < write


def test_ci_cleanup_owns_trigger_lifecycle_between_runs():
    cleanup = WORKFLOW.index("(Join-Path $projectRoot 'ci-open-download-one.flag')")
    start_admin = WORKFLOW.index("$triggerPath = Join-Path $output 'ci-open-download-one.flag'")
    assert cleanup < start_admin


def test_update_row_has_no_self_recursive_delegate_and_handles_disposal_race():
    block_start = FORM.index('Action<string, Color, int> updateRow =')
    block_end = FORM.index('updateRow("Sending…"', block_start)
    block = FORM[block_start:block_end]
    assert 'updateRow(statusText, statusColor, pct)' not in block
    assert 'BeginInvoke(updateUi)' in block
    assert 'IsHandleCreated' in block
    assert 'catch (InvalidOperationException)' in block
    assert 'catch (ObjectDisposedException)' in block
    assert block.index('catch (ObjectDisposedException)') < block.index('catch (InvalidOperationException)')


def test_ci_administration_marker_matches_real_menu_action_and_dialog():
    assert 'ciAutomationMarker: "Administration|Download [ One ]"' in FORM
    assert '"|Dialog=" + title' in FORM
    assert 'Administration\|Download \[ One \]\|Dialog=Remote Execution' in WORKFLOW
