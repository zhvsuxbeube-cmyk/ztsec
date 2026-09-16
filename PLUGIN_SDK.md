# ZeroTrace Security plugin server API

A server plugin is a managed .NET Framework assembly named exactly:

`pluginname.server.dll`

Its paired agent client must be beside it and named:

`pluginname.client.dll`

`pluginname` is one or more ASCII letters/digits. The base name is matched case-insensitively; the `.server.dll` and `.client.dll` suffixes are case-sensitive.

The server plugin exposes a public concrete type with a public parameterless constructor implementing `IZtsecPluginServer` from `PluginContracts.cs`.

```csharp
public sealed class ExampleServer : IZtsecPluginServer
{
    public string Name => "Example";
    public string Version => "1.0";
    public void Initialize(IZtsecPluginContext context) { }
    public void Shutdown() { }
    public void OnAgentMessage(string connectionId, string eventName, byte[] payload) { }
}
```

The panel validates the assembly and paired client before displaying it. Starting the row creates an isolated .NET Framework `AppDomain`; stopping/unloading the plugin unloads that domain.

The context provides a generic command/data plane rather than feature-specific plugin commands:

- enumerate live connections;
- load/unload the paired agent client;
- send arbitrary UTF-8 text or arbitrary bytes to an agent plugin;
- send a local file in 128 KiB chunks;
- receive arbitrary plugin output from the agent;
- log diagnostics.

### Agent plugin client wire plane

The agent accepts these transport commands:

`CMD:PLUGIN_BEGIN:<plugin>:<transferId>:<size>:<sha256>:<name>`

`CMD:PLUGIN_CHUNK:<transferId>:<offset>:<base64>`

`CMD:PLUGIN_END:<transferId>`

`CMD:PLUGIN_RESUME:<transferId>`

`CMD:PLUGIN_MSG:<plugin>:<base64>`

Transfers are bounded to 256 MiB and use 128 KiB chunks. The agent keeps an incomplete transfer in a temporary file and accepts already-committed duplicate chunks, so a caller can resend from the beginning after reconnecting and the agent resumes at its committed offset. Completion is accepted only after the final SHA-256 matches the declared hash.

Agent-to-server plugin output is emitted as:

`PLUGIN_OUT:<event>:<base64-payload>`

Plugins can therefore carry arbitrary binary application messages. The bundled header defines conventions for file uploads:

- `file.send.begin`: `transferId|fileName|size|sha256`
- `file.send.chunk`: `transferId|offset|` + raw bytes
- `file.send.end`: `transferId`

The panel does not interpret application-specific payloads; it forwards them to loaded server plugins through `OnAgentMessage`, leaving protocol semantics above the orchestration layer under plugin control.
