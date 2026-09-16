using System;
using ZeroTrace_Security_Official;

public sealed class ExampleServer : IZtsecPluginServer
{
    private IZtsecPluginContext context;
    public string Name { get { return "Example"; } }
    public string Version { get { return "1.0"; } }

    public void Initialize(IZtsecPluginContext host)
    {
        context = host;
        host.Log("Example plugin initialized.");
    }

    public void Shutdown()
    {
        if (context != null) context.Log("Example plugin shutting down.");
    }

    public void OnAgentMessage(string connectionId, string eventName, byte[] payload)
    {
        if (string.Equals(eventName, "file.send.end", StringComparison.Ordinal))
            context.Log("File transfer completed: " + connectionId);
    }
}
