from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FORM = (ROOT / "Form1.cs").read_text(encoding="utf-8")


def test_popup_rows_are_visible_and_use_compact_identity():
    assert "const int headerRowH = 30;" in FORM
    assert "const int dataRowH = 32;" in FORM
    assert "int tableH = headerRowH + (visibleRows * dataRowH) + 2;" in FORM
    assert 'DataTable commandStatusTable = new DataTable("RemoteCommandStatus");' in FORM
    assert 'Name = "remoteCommandStatusGrid"' in FORM
    assert 'return userName + "@" + computerName;' in FORM
    assert 'connectionColumn.Caption = "Client";' in FORM
    assert 'statusColumn.Caption = "Status";' in FORM
    assert 'progressColumn.Caption = "Progress";' in FORM
    assert 'RepositoryItemProgressBar' in FORM


def test_update_candidate_probe_is_server_authorized():
    assert 'line.StartsWith("HELLO:UPDATE-PROBE:", StringComparison.Ordinal)' in FORM
    assert 'HandleUpdateProbeConnection(stream, reader, line);' in FORM
    assert 'pendingUpdateProbes[expectedFingerprint + "|" + cachedUpdateHash]' in FORM
    assert 'SendProtocolLine(stream, "ACK:UPDATE-PROBE:" + fingerprint + ":" + token);' in FORM
    assert 'SendProtocolLine(stream, "ERR:UPDATE_PROBE");' in FORM


def test_update_waiter_registered_before_network_write():
    pending = FORM.index('pendingCommands[resultKey] = new PendingCommand')
    first_write = FORM.index('ns.Write(rawBytes, sent, toSend);')
    assert pending < first_write


def test_stop_listener_has_single_normal_completion_log():
    assert FORM.count('AppendServerSettingsLog("Port listener stopped", LogType.Success);') == 1
    stop_handler = FORM.index('private void simpleButton2_Click')
    stop_handler_end = FORM.index('private void panelControl13_Paint', stop_handler)
    section = FORM[stop_handler:stop_handler_end]
    assert 'AppendServerSettingsLog("Port listener stopped", LogType.Success);' not in section


def test_update_probe_reads_telemetry_from_existing_reader():
    assert 'HandleUpdateProbeConnection(NetworkStream stream, StreamReader reader, string helloLine)' in FORM
    assert 'string dataLine = reader.ReadLine();' in FORM
    assert 'new StreamReader(stream, Encoding.UTF8, false, 4096, true).ReadLine()' not in FORM


def test_update_probe_requires_live_original_connection_and_valid_telemetry():
    assert 'connectedClients.ContainsKey(probe.ConnectionId)' in FORM
    assert 'IsValidUpdateProbeData(dataLine.Substring(5), fingerprint)' in FORM
    assert 'pendingUpdateProbes.Remove(fingerprint + "|" + candidateHash.ToLowerInvariant())' in FORM


def test_popup_has_real_rows_and_compact_identity():
    assert 'const int headerRowH = 30;' in FORM
    assert 'const int dataRowH = 32;' in FORM
    assert 'int tableH = headerRowH + (visibleRows * dataRowH) + 2;' in FORM
    assert 'Name = "remoteCommandStatusGrid"' in FORM
    assert 'return userName + "@" + computerName;' in FORM


def test_normal_command_waiter_is_registered_before_send_and_has_generation_id():
    start = FORM.index('private void SendSelectedConnectionCommand')
    end = FORM.index('private static string CommandLabel', start)
    section = FORM[start:end]
    pending = section.index('TrackPendingCommand(connectionId, command, pendingId, false);')
    write = section.index('stream.Write(request, 0, request.Length);')
    assert pending < write
    assert 'PendingCommand current;\n                    if (pendingCommands.TryGetValue(key, out current) && current.Id == pendingId)' in FORM


def test_file_command_waiter_has_generation_id_and_update_probe_cleanup():
    assert 'Guid pendingId = Guid.NewGuid();' in FORM
    assert 'PersistResult = true' in FORM
    assert 'RemovePendingUpdateProbeForConnection(cid);' in FORM


def test_ci_trigger_is_observed_without_racy_application_side_delete():
    assert "File.Delete(triggerPath)" not in FORM
    assert "ci-open-download-one-consumed.flag" in FORM
    assert "ci-open-notifications-consumed.flag" in FORM


def test_popup_uses_devexpress_progress_editor_and_fixed_row_height():
    assert 'commandGridView.RowHeight = 32;' in FORM
    assert 'commandGridView.ColumnPanelRowHeight = 30;' in FORM
    assert 'progressColumn.ColumnEdit = progressEditor;' in FORM
    assert 'commandGridView.HorzScrollVisibility = DevExpress.XtraGrid.Views.Base.ScrollVisibility.Never;' in FORM


def test_update_payload_is_cached_once_for_multiple_connections():
    start = FORM.index('private void ShowRemoteUpdateDialog')
    end = FORM.index('// ── Shared File-Command Dialog', start)
    section = FORM[start:end]
    assert 'byte[] cachedUpdateBytes = null;' in section
    assert 'string cachedUpdateBase64 = null;' in section
    assert 'Convert.ToBase64String(bytes)' in section
    assert section.count('File.ReadAllBytes(filePath)') == 1


def test_ci_administration_marker_matches_workflow_contract():
    assert '|DownloadOneChecked=True|Rendered=True"' in FORM
    workflow = (ROOT / '.github' / 'workflows' / 'windows-build.yml').read_text(encoding='utf-8')
    assert r"Administration\|Download \[ One \]" in workflow


def test_ci_timeout_diagnostic_reflects_retained_trigger():
    workflow = (ROOT / '.github' / 'workflows' / 'windows-build.yml').read_text(encoding='utf-8')
    assert 'trigger consumed (application-side watcher observed it)' in workflow
    assert 'trigger file absent but no consume marker was observed' in workflow
    assert '$triggerStatus = if (Test-Path -LiteralPath $notificationsTrigger -PathType Leaf)' in workflow


def test_popup_uses_supported_devexpress_dispose_api_and_scoped_async_state():
    assert 'commandGridView.IsDisposing' in FORM
    assert 'commandGrid.IsDisposed' in FORM
    assert 'Guid pendingId = Guid.Empty;' in FORM
    assert 'string resultKey = cid + "|" + cmdKey.ToUpperInvariant();' in FORM
    assert 'bool isUpdateCommand = string.Equals(cmdKey, "UPDATE:", StringComparison.OrdinalIgnoreCase);' in FORM
    # The catch block must be able to see the state even when GetStream()/setup throws.
    task_start = FORM.index('Task.Run(() =>', FORM.index('private void ShowFileCommandDialog'))
    catch_start = FORM.index('catch (Exception ex)', task_start)
    section = FORM[task_start:catch_start]
    assert section.index('string resultKey =') < section.index('try')
    assert section.index('Guid pendingId =') < section.index('try')


def test_popup_does_not_use_unsupported_gridview_isdisposed_property():
    assert 'commandGridView.IsDisposed' not in FORM
    assert 'commandGridView.IsDisposing' in FORM
    assert 'commandGrid.IsDisposed' in FORM


def test_ci_screenshot_marker_waits_for_two_ui_turns():
    marker = FORM.index('ci-connections-menu-opened.flag')
    section_start = FORM.rfind('private void connectionsContextMenu_Opening', 0, marker)
    section = FORM[section_start:marker]
    assert section.count('BeginInvoke(new Action(delegate') >= 1
    assert 'BeginInvoke(new Action(MarkCiConnectionsPopupIfOpen))' in section


def test_administration_render_marker_is_after_second_ui_turn():
    start = FORM.index('ci-administration-dialog-opened.flag')
    section_start = FORM.rfind('dlg.Shown +=', 0, start)
    section = FORM[section_start:start]
    assert section.count('dlg.BeginInvoke(new Action(delegate') >= 2
    assert 'Rendered=True' in FORM[start - 400:start + 500]
