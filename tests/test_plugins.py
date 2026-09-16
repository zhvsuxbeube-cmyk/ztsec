from pathlib import Path
import re
import os

ROOT = Path(__file__).resolve().parents[1]
FORM = (ROOT / "Form1.cs").read_text(encoding="utf-8")
MANAGER = (ROOT / "PluginManager.cs").read_text(encoding="utf-8")
CONTRACTS = (ROOT / "PluginContracts.cs").read_text(encoding="utf-8")
AGENT_ROOT = Path(os.environ.get("ZTSEC_AGENT_ROOT", str(ROOT.parent / "agent")))
AGENT = (AGENT_ROOT / "src" / "net.rs").read_text(encoding="utf-8")
HEADER = (AGENT_ROOT / "plugin" / "ztsec_plugin.h").read_text(encoding="utf-8")


def test_plugin_name_rules_are_strict():
    assert "[A-Za-z0-9]+" in MANAGER
    assert r"\\.server\\.dll$" in MANAGER
    assert r"\\.client\\.dll$" in MANAGER
    assert 'StringComparison.OrdinalIgnoreCase' in MANAGER


def test_panel_requires_matching_client_before_load():
    assert 'Matching pluginname.client.dll was not found in the same directory.' in MANAGER
    assert 'AssemblyName.GetAssemblyName(serverPath)' in MANAGER
    assert 'Plugin API validation failed' in MANAGER


def test_plugin_page_has_requested_context_actions_and_grid():
    assert 'Load Plugin' in FORM
    assert 'Unload Plugin' in FORM
    assert 'Name = "pluginManagerGrid"' in FORM
    assert 'RowHeight = 32' in FORM
    assert 'pluginManagerGrid.MouseDoubleClick' in FORM


def test_dummy_plugins_removed_from_connection_menu():
    assert 'CreateConnectionMenuItem("ExPlugin 1"' not in FORM
    assert 'CreateConnectionMenuItem("ExPlugin 2"' not in FORM
    assert 'CreateConnectionMenuItem("ExPlugin 3"' not in FORM
    assert 'ConfigureDynamicPluginMenu();' in FORM


def test_plugin_server_contract_is_explicit():
    for symbol in ['IZtsecPluginServer', 'IZtsecPluginContext', 'Initialize', 'Shutdown', 'OnAgentMessage']:
        assert symbol in CONTRACTS
    assert 'Assembly.CreateInstance' not in MANAGER
    assert 'AppDomain.CreateDomain' in MANAGER


def test_agent_plugin_transport_supports_generic_messages_and_chunked_files():
    TEXT = (ROOT.parent / "agent" / "src" / "text.rs").read_text(encoding="utf-8")
    for token in ["PLUGIN_MSG:", "PLUGIN_BEGIN:", "PLUGIN_CHUNK:", "PLUGIN_END:", "PLUGIN_RESUME:"]:
        assert token in TEXT
    for token in ["text::PMSG", "text::PBEGIN", "text::PCHUNK", "text::PEND", "text::PRESUME"]:
        assert token in AGENT
    for token in ['PluginOutput', 'set_emit_sender', 'drain_outputs']:
        assert token in (ROOT.parent / 'agent' / 'src' / 'plugin.rs').read_text(encoding='utf-8')
    assert '256 * 1024 * 1024' in AGENT
    assert '128 * 1024' in AGENT
    assert 'ZT_EVENT_FILE_SEND_CHUNK' in HEADER


def test_example_file_explorer_plugin_is_present_and_generic_api_based():
    server = (ROOT / "plugin-sdk" / "ExampleServer.cs").read_text(encoding="utf-8")
    client = (AGENT_ROOT / "plugin" / "example_explorer.cpp").read_text(encoding="utf-8")
    assert "ExampleFileExplorer" in server
    assert "Application.Run(form)" in server
    assert 'context.SendText' in server
    for token in ["PluginOnLoad", "PluginOnEvent", "PluginOnUnload", "explorer.drives", "explorer.entries"]:
        assert token in client


def test_panel_compile_hazards_are_corrected():
    assert 'LogType.Data);' not in FORM
    assert 'using (AppDomain inspector' not in MANAGER
    assert 'string validatedPluginName' in MANAGER
    assert 'SendFileChunk(string connectionId, string transferId, long offset, byte[] chunk, long totalLength, string sha256)' in MANAGER
    assert 'PrefixEvent("file.send.chunk", framed)' in MANAGER
