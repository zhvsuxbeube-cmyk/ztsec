from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
FORM = (ROOT / "Form1.cs").read_text(encoding="utf-8")
MANAGER = (ROOT / "PluginManager.cs").read_text(encoding="utf-8")
CONTRACTS = (ROOT / "PluginContracts.cs").read_text(encoding="utf-8")
AGENT = (ROOT.parent / "agent" / "src" / "net.rs").read_text(encoding="utf-8")
HEADER = (ROOT.parent / "agent" / "plugin" / "ztsec_plugin.h").read_text(encoding="utf-8")


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
