using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

public class NetworkUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button startServerButton;
    [SerializeField] private Button startClientButton;
    [SerializeField] private Button discoverButton;
    [SerializeField] private Button spawnPlayerButton;
    [SerializeField] private Button autoDiscoverButton;
    [SerializeField] private Button refreshServersButton;
    [SerializeField] private Button disconnectButton;

    [SerializeField] private InputField serverIPInput;
    [SerializeField] private InputField serverNameInput;
    [SerializeField] private Text statusText;
    [SerializeField] private Text connectionInfoText;

    [Header("Server List UI")]
    [SerializeField] private Transform serverListContent;
    [SerializeField] private GameObject serverListItemPrefab;
    [SerializeField] private ScrollRect serverListScrollRect;

    [Header("Settings")]
    [SerializeField] private Toggle autoDiscoveryToggle;
    [SerializeField] private Toggle listenForAnnouncementsToggle;

    private NetworkGameManager networkManager;
    private List<ServerListItem> serverListItems = new List<ServerListItem>();

    private void Start()
    {
        networkManager = FindObjectOfType<NetworkGameManager>();

        if (networkManager == null)
        {
            Debug.LogError("NetworkGameManager not found!");
            return;
        }

        // Subscribe to events
        networkManager.OnServerDiscovered += OnServerDiscovered;
        networkManager.OnConnectionStatusChanged += OnConnectionStatusChanged;

        SetupUI();
        UpdateUI();
    }

    private void SetupUI()
    {
        // Button listeners
        if (startServerButton != null)
            startServerButton.onClick.AddListener(StartServer);
        if (startClientButton != null)
            startClientButton.onClick.AddListener(StartClientFromUI);
        if (discoverButton != null)
            discoverButton.onClick.AddListener(DiscoverServers);
        if (spawnPlayerButton != null)
            spawnPlayerButton.onClick.AddListener(SpawnPlayer);
        if (autoDiscoverButton != null)
            autoDiscoverButton.onClick.AddListener(AutoDiscoverAndConnect);
        if (refreshServersButton != null)
            refreshServersButton.onClick.AddListener(RefreshServerList);
        if (disconnectButton != null)
            disconnectButton.onClick.AddListener(Disconnect);

        // Set default values
        if (serverNameInput != null && string.IsNullOrEmpty(serverNameInput.text))
            serverNameInput.text = $"{SystemInfo.deviceName}'s Server";

        if (serverIPInput != null && string.IsNullOrEmpty(serverIPInput.text))
            serverIPInput.text = "192.168.1.100";

        UpdateStatus("Ready");
    }

    private void StartServer()
    {
        string serverName = serverNameInput != null ? serverNameInput.text : "Unity Game Server";
        if (string.IsNullOrEmpty(serverName))
            serverName = "Unity Game Server";

        // Set server name in NetworkGameManager if possible
        var serverNameField = typeof(NetworkGameManager).GetField("serverName",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (serverNameField != null)
            serverNameField.SetValue(networkManager, serverName);

        networkManager.startAsServer = true;
        networkManager.StartServer();
        UpdateUI();

        // Auto-spawn player for server
        Invoke(nameof(SpawnPlayer), 1f);
    }

    private async void StartClientFromUI()
    {
        startServerButton.interactable = false;

        string ip = serverIPInput != null ? serverIPInput.text : "";
        if (string.IsNullOrEmpty(ip))
        {
            UpdateStatus("Please enter server IP address");
            return;
        }

        UpdateStatus($"Connecting to {ip}...");
        bool connected = await networkManager.StartClient(ip);

        UpdateUI();
    }

    private async void DiscoverServers()
    {
        UpdateStatus("Discovering servers...");
        var servers = await networkManager.DiscoverServersAsync();

        if (servers.Count > 0)
        {
            UpdateStatus($"Found {servers.Count} server(s)");
            UpdateServerList(servers);
        }
        else
        {
            UpdateStatus("No servers found");
        }
    }

    private void AutoDiscoverAndConnect()
    {
        UpdateStatus("Auto-discovering and connecting...");
        networkManager.AutoDiscoverAndConnect();
        UpdateUI();
    }

    private void RefreshServerList()
    {
        DiscoverServers();
    }

    private void SpawnPlayer()
    {
        networkManager.SpawnPlayer();
        UpdateStatus("Player spawned");
    }

    private void Disconnect()
    {
        networkManager.StopConnection();
        UpdateUI();
        ClearServerList();
    }

    private void UpdateServerList(List<ServerInfo> servers)
    {
        ClearServerList();

        if (serverListContent == null || serverListItemPrefab == null)
            return;

        foreach (var server in servers)
        {
            GameObject itemObj = Instantiate(serverListItemPrefab, serverListContent);
            ServerListItem listItem = itemObj.GetComponent<ServerListItem>();

            if (listItem == null)
                listItem = itemObj.AddComponent<ServerListItem>();

            listItem.Setup(server, OnServerSelected);
            serverListItems.Add(listItem);
        }
    }

    private void ClearServerList()
    {
        foreach (var item in serverListItems)
        {
            if (item != null && item.gameObject != null)
                Destroy(item.gameObject);
        }
        serverListItems.Clear();
    }

    private async void OnServerSelected(ServerInfo serverInfo)
    {
        UpdateStatus($"Connecting to {serverInfo.ServerName}...");
        bool connected = await networkManager.ConnectToServer(serverInfo);

        if (connected)
        {
            UpdateStatus($"Connected to {serverInfo.ServerName}");
        }
        else
        {
            UpdateStatus($"Failed to connect to {serverInfo.ServerName}");
        }

        UpdateUI();
    }

    private void OnServerDiscovered(ServerInfo serverInfo)
    {
        UpdateStatus($"Discovered: {serverInfo.ServerName}");

        // If we're showing the server list, refresh it
        if (serverListItems.Count > 0)
        {
            DiscoverServers();
        }
    }

    private void OnConnectionStatusChanged(string status)
    {
        UpdateStatus(status);
        UpdateUI();
    }

    private void UpdateStatus(string message)
    {
        if (statusText != null)
            statusText.text = $"Status: {message}";
        Debug.Log($"UI Status: {message}");
    }

    private void UpdateUI()
    {
        if (networkManager == null) return;

        bool isConnected = networkManager.IsConnected;
        bool isServer = networkManager.IsServer;

        // Update button states
        if (startServerButton != null)
            startServerButton.interactable = !isConnected;
        if (startClientButton != null)
            startClientButton.interactable = !isConnected;
        if (autoDiscoverButton != null)
            autoDiscoverButton.interactable = !isConnected;
        if (discoverButton != null)
            discoverButton.interactable = !isConnected;
        if (disconnectButton != null)
            disconnectButton.interactable = isConnected;
        if (spawnPlayerButton != null)
            spawnPlayerButton.interactable = isConnected && !isServer;

        // Update connection info
        if (connectionInfoText != null)
            connectionInfoText.text = networkManager.GetConnectionStatus();

        // Update input field states
        if (serverIPInput != null)
            serverIPInput.interactable = !isConnected;
        if (serverNameInput != null)
            serverNameInput.interactable = !isConnected;
    }

    private void Update()
    {
        UpdateUI();
    }

    private void OnDestroy()
    {
        if (networkManager != null)
        {
            networkManager.OnServerDiscovered -= OnServerDiscovered;
            networkManager.OnConnectionStatusChanged -= OnConnectionStatusChanged;
        }
    }
}

// Server list item component
public class ServerListItem : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Text serverNameText;
    [SerializeField] private Text serverIPText;
    [SerializeField] private Text playerCountText;
    [SerializeField] private Text pingText;
    [SerializeField] private Button connectButton;

    private ServerInfo serverInfo;
    private System.Action<ServerInfo> onConnectCallback;

    public void Setup(ServerInfo info, System.Action<ServerInfo> connectCallback)
    {
        serverInfo = info;
        onConnectCallback = connectCallback;

        // Update UI elements
        if (serverNameText != null)
            serverNameText.text = info.ServerName;
        if (serverIPText != null)
            serverIPText.text = $"{info.IPAddress}:{info.Port}";
        if (playerCountText != null)
            playerCountText.text = $"{info.PlayerCount}/{info.MaxPlayers}";
        if (pingText != null)
        {
            uint timeDiff = (uint)System.Environment.TickCount - info.Timestamp;
            pingText.text = $"{timeDiff}ms";
        }

        // Setup connect button
        if (connectButton != null)
        {
            connectButton.onClick.RemoveAllListeners();
            connectButton.onClick.AddListener(() => onConnectCallback?.Invoke(serverInfo));
        }

        // Update button state based on server capacity
        if (connectButton != null)
            connectButton.interactable = info.PlayerCount < info.MaxPlayers;
    }

    // Auto-create UI elements if not assigned
    private void Awake()
    {
        if (serverNameText == null)
            serverNameText = transform.Find("ServerName")?.GetComponent<Text>();
        if (serverIPText == null)
            serverIPText = transform.Find("ServerIP")?.GetComponent<Text>();
        if (playerCountText == null)
            playerCountText = transform.Find("PlayerCount")?.GetComponent<Text>();
        if (pingText == null)
            pingText = transform.Find("Ping")?.GetComponent<Text>();
        if (connectButton == null)
            connectButton = transform.Find("ConnectButton")?.GetComponent<Button>();
    }
}