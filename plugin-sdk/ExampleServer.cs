using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

using ZeroTrace_Security_Official;

// Example plugin server demonstrating that plugins own their UI and application
// protocol.  It implements a small remote file-browser over the generic plugin
// message/event API; the panel itself does not interpret the explorer protocol.
public sealed class ExampleServer : IZtsecPluginServer
{
    private IZtsecPluginContext context;
    private ExplorerForm form;
    private Thread uiThread;

    public string Name { get { return "ExampleFileExplorer"; } }
    public string Version { get { return "1.0"; } }

    public void Initialize(IZtsecPluginContext host)
    {
        context = host;
        using (ManualResetEvent ready = new ManualResetEvent(false))
        {
            uiThread = new Thread(delegate()
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                form = new ExplorerForm(host, SendToSelectedAgent);
                form.FormClosed += delegate { Application.ExitThread(); };
                ready.Set();
                Application.Run(form);
            });
            uiThread.IsBackground = true;
            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.Start();
            ready.WaitOne(5000);
        }
        host.Log("ExampleFileExplorer initialized.");
    }

    public void Shutdown()
    {
        ExplorerForm local = form;
        if (local != null)
        {
            try
            {
                if (local.IsHandleCreated && !local.IsDisposed)
                    local.BeginInvoke((Action)delegate { local.Close(); });
            }
            catch { }
        }
        if (context != null) context.Log("ExampleFileExplorer shutting down.");
    }

    public void OnAgentMessage(string connectionId, string eventName, byte[] payload)
    {
        ExplorerForm local = form;
        if (local == null || local.IsDisposed) return;
        string text = Encoding.UTF8.GetString(payload ?? new byte[0]);
        try
        {
            local.BeginInvoke((Action)delegate { local.HandleAgentMessage(connectionId, eventName, text, payload ?? new byte[0]); });
        }
        catch { }
    }

    private bool SendToSelectedAgent(string connectionId, string command)
    {
        if (context == null) return false;
        return context.SendText(connectionId, "explorer", command);
    }

    private sealed class ExplorerForm : Form
    {
        private readonly IZtsecPluginContext context;
        private readonly Func<string, string, bool> send;
        private readonly ComboBox connections = new ComboBox();
        private readonly ListView entries = new ListView();
        private readonly TextBox path = new TextBox();
        private readonly Label status = new Label();
        private readonly Button refreshConnections = new Button();
        private readonly Button up = new Button();
        private readonly Button open = new Button();
        private readonly Button drives = new Button();
        private readonly Dictionary<string, string> currentPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public ExplorerForm(IZtsecPluginContext host, Func<string, string, bool> sender)
        {
            context = host;
            send = sender;
            Text = "Example File Explorer";
            Width = 1050;
            Height = 650;
            BackColor = Color.FromArgb(38, 38, 38);
            ForeColor = Color.White;
            StartPosition = FormStartPosition.CenterScreen;

            connections.DropDownStyle = ComboBoxStyle.DropDownList;
            refreshConnections.Text = "Refresh";
            up.Text = "Up";
            drives.Text = "Drives";
            open.Text = "Open";
            path.Dock = DockStyle.Fill;
            path.BackColor = Color.FromArgb(28, 28, 28);
            path.ForeColor = Color.White;
            path.BorderStyle = BorderStyle.FixedSingle;
            status.AutoSize = true;
            status.Text = "Select a connection.";

            entries.Dock = DockStyle.Fill;
            entries.View = View.Details;
            entries.FullRowSelect = true;
            entries.MultiSelect = false;
            entries.Columns.Add("Name", 420);
            entries.Columns.Add("Type", 110);
            entries.Columns.Add("Size", 130);
            entries.Columns.Add("Modified", 180);
            entries.DoubleClick += delegate { OpenSelected(); };

            FlowLayoutPanel top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(6), BackColor = Color.FromArgb(31, 31, 31) };
            connections.Width = 300;
            top.Controls.Add(connections);
            top.Controls.Add(refreshConnections);
            top.Controls.Add(drives);
            top.Controls.Add(up);

            Panel location = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(6, 4, 6, 4) };
            location.Controls.Add(path);
            open.Dock = DockStyle.Right;
            open.Width = 80;
            location.Controls.Add(open);

            status.Dock = DockStyle.Bottom;
            status.Height = 25;
            status.Padding = new Padding(6, 4, 0, 0);

            Controls.Add(entries);
            Controls.Add(location);
            Controls.Add(top);
            Controls.Add(status);

            refreshConnections.Click += delegate { ReloadConnections(); };
            drives.Click += delegate { Request("list"); };
            up.Click += delegate { Request("list|UP"); };
            open.Click += delegate { OpenSelected(); };
            connections.SelectedIndexChanged += delegate
            {
                string id = SelectedConnectionId();
                if (!string.IsNullOrEmpty(id) && !currentPaths.ContainsKey(id)) currentPaths[id] = "DRIVES";
                path.Text = string.IsNullOrEmpty(id) ? string.Empty : currentPaths[id];
            };

            ReloadConnections();
        }

        private string SelectedConnectionId()
        {
            ZtsecPluginConnection item = connections.SelectedItem as ZtsecPluginConnection;
            return item == null ? null : item.ConnectionId;
        }

        private void ReloadConnections()
        {
            string previous = SelectedConnectionId();
            connections.Items.Clear();
            foreach (ZtsecPluginConnection item in context.GetConnections()) connections.Items.Add(item);
            for (int i = 0; i < connections.Items.Count; i++)
            {
                ZtsecPluginConnection item = connections.Items[i] as ZtsecPluginConnection;
                if (item != null && string.Equals(item.ConnectionId, previous, StringComparison.OrdinalIgnoreCase)) { connections.SelectedIndex = i; break; }
            }
            if (connections.SelectedIndex < 0 && connections.Items.Count > 0) connections.SelectedIndex = 0;
            status.Text = connections.Items.Count == 0 ? "No live connections." : "Ready.";
        }

        private void Request(string command)
        {
            string id = SelectedConnectionId();
            if (string.IsNullOrEmpty(id)) { status.Text = "No connection selected."; return; }
            if (!send(id, command)) status.Text = "Request could not be sent.";
            else status.Text = "Request sent.";
        }

        private void OpenSelected()
        {
            ListViewItem item = entries.SelectedItems.Count == 0 ? null : entries.SelectedItems[0];
            if (item == null) return;
            string kind = item.SubItems[1].Text;
            if (!string.Equals(kind, "Directory", StringComparison.OrdinalIgnoreCase)) return;
            Request("list|" + item.Tag);
        }

        public void HandleAgentMessage(string connectionId, string eventName, string text, byte[] payload)
        {
            if (string.Equals(eventName, "explorer.drives", StringComparison.Ordinal))
            {
                entries.Items.Clear();
                foreach (string raw in text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] parts = raw.Split('|');
                    if (parts.Length >= 2)
                    {
                        ListViewItem row = new ListViewItem(parts[0]);
                        row.SubItems.Add(parts[1]);
                        row.SubItems.Add(parts.Length > 2 ? parts[2] : "");
                        row.SubItems.Add(parts.Length > 3 ? parts[3] : "");
                        row.Tag = parts[0];
                        entries.Items.Add(row);
                    }
                }
                currentPaths[connectionId] = "DRIVES";
                path.Text = "DRIVES";
                status.Text = "Drive list received.";
                return;
            }

            if (string.Equals(eventName, "explorer.entries", StringComparison.Ordinal))
            {
                entries.Items.Clear();
                foreach (string raw in text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] parts = raw.Split('|');
                    if (parts.Length >= 2)
                    {
                        ListViewItem row = new ListViewItem(parts[0]);
                        row.SubItems.Add(parts[1]);
                        row.SubItems.Add(parts.Length > 2 ? parts[2] : "");
                        row.SubItems.Add(parts.Length > 3 ? parts[3] : "");
                        row.Tag = parts.Length > 4 ? parts[4] : parts[0];
                        entries.Items.Add(row);
                    }
                }
                currentPaths[connectionId] = path.Text;
                status.Text = "Directory listing received.";
                return;
            }

            if (string.Equals(eventName, "explorer.error", StringComparison.Ordinal))
            {
                status.Text = "Agent error: " + text;
                return;
            }

            if (eventName.StartsWith("explorer.file.", StringComparison.Ordinal))
                status.Text = eventName + " received (" + (payload == null ? 0 : payload.Length) + " bytes).";
        }
    }
}
