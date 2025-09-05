using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;
using UnityEditor;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using System.Linq;
using System.Text;

public class NetworkGameManager : MonoBehaviour
{
    [Header("Network Settings")]
    [SerializeField] private int serverPort = 8888;
    [SerializeField] private int discoveryPort = 9999;
    [SerializeField] public bool startAsServer = false;
    [SerializeField] private bool autoDiscoverOnStart = false;
    [SerializeField] private string serverName = "My Game Server";
    [SerializeField] private string manualServerIP = "192.64.58.157";

    [Header("Prefabs")]
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private GameObject[] networkPrefabs;

    [Header("Auto Discovery Settings")]
    [SerializeField] private float discoveryTimeout = 5f;
    [SerializeField] private bool listenForServerAnnouncements = true;

    private Dictionary<uint, NetworkGameObject> networkObjects;
    private UnityNetworkClient client;
    private UnityNetworkServer server;
    private uint nextNetworkId = 1;
    private bool isServer = false;

    // Auto discovery components
    private UdpClient announcementListener;
    private Thread announcementThread;
    private CancellationTokenSource announcementCancellationToken;
    private List<ServerInfo> discoveredServers;

    public bool IsConnected => isServer || (client != null && client.IsConnected);
    public bool IsServer => isServer;
    public List<ServerInfo> DiscoveredServers => discoveredServers?.ToList() ?? new List<ServerInfo>();

    public event System.Action<NetworkGameObject> OnGameObjectSpawned;
    public event System.Action<uint> OnGameObjectDestroyed;
    public event System.Action<ServerInfo> OnServerDiscovered;
    public event System.Action<string> OnConnectionStatusChanged;

    private void Awake()
    {
        networkObjects = new Dictionary<uint, NetworkGameObject>();
        discoveredServers = new List<ServerInfo>();

        // Ensure we have a main thread dispatcher
        if (FindObjectOfType<UnityMainThreadDispatcher>() == null)
        {
            var dispatcher = new GameObject("MainThreadDispatcher");
            dispatcher.AddComponent<UnityMainThreadDispatcher>();
            DontDestroyOnLoad(dispatcher);
        }
    }

    private void Start()
    {
        if (startAsServer)
        {
            StartServer();
        }
        else if (autoDiscoverOnStart)
        {
            AutoDiscoverAndConnect();
        }

       
    }

    private string GetLocalIPv4()
    {
        string localIP = "";
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                {
                    localIP = ip.ToString();
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to get local IP: {ex.Message}");
        }
        return string.IsNullOrEmpty(localIP) ? "127.0.0.1" : localIP;
    }

    public async void StartServer()
    {
        string localIP = GetLocalIPv4();
        Debug.Log($"Starting server on IP: {localIP}:{serverPort}");

        server = new UnityNetworkServer();
        isServer = true;

        server.OnClientConnected += OnClientConnected;
        server.OnClientDisconnected += OnClientDisconnected;
        server.OnMessageReceived += OnServerMessageReceived;

        bool success = server.StartServer(serverPort, discoveryPort, serverName);
        if (success)
        {
            Debug.Log($"Server '{serverName}' started successfully on {localIP}:{serverPort}!");
            OnConnectionStatusChanged?.Invoke($"Server started on {localIP}:{serverPort}");
        }
        else
        {
            OnConnectionStatusChanged?.Invoke("Failed to start server");
        }
    }

    public async Task<bool> StartClient(string ip = null)
    {
        // Start listening for server announcements if not server
        if (!startAsServer && listenForServerAnnouncements)
        {
            Debug.Log("start as server is " + startAsServer);
            StartListeningForServerAnnouncements();
        }

        if (string.IsNullOrEmpty(ip))
            ip = manualServerIP;

        client = new UnityNetworkClient();
        isServer = false;

        client.OnMessageReceived += OnClientMessageReceived;
        client.OnConnected += OnClientConnected;
        client.OnDisconnected += OnClientDisconnected;

        OnConnectionStatusChanged?.Invoke($"Connecting to {ip}:{serverPort}...");

        bool success = await client.ConnectToServer(ip, serverPort);
        if (success)
        {
            Debug.Log($"Connected to server at {ip}:{serverPort}");
            OnConnectionStatusChanged?.Invoke($"Connected to {ip}:{serverPort}");
            return true;
        }
        else
        {
            Debug.LogError("Failed to connect to server");
            OnConnectionStatusChanged?.Invoke($"Failed to connect to {ip}:{serverPort}");
            return false;
        }
    }

    // New method: Auto discover and connect to best available server
    public async Task AutoDiscoverAndConnectAsync()
    {
        OnConnectionStatusChanged?.Invoke("Auto-discovering servers...");

        if (client == null)
            client = new UnityNetworkClient();

        client.OnMessageReceived += OnClientMessageReceived;
        client.OnConnected += OnClientConnected;
        client.OnDisconnected += OnClientDisconnected;

        var servers = await client.DiscoverServersWithInfo(discoveryPort, (int)(discoveryTimeout * 1000));

        if (servers.Count > 0)
        {
            // Sort servers by player count (prefer less crowded servers) or by response time
            servers.Sort((a, b) => a.PlayerCount.CompareTo(b.PlayerCount));

            var bestServer = servers[0];
            Debug.Log($"Found {servers.Count} server(s). Connecting to best option: {bestServer.ServerName}");

            bool connected = await StartClient(bestServer.IPAddress);
            if (connected)
            {
                Debug.Log($"Successfully auto-connected to {bestServer.ServerName} at {bestServer.IPAddress}");
                OnConnectionStatusChanged?.Invoke($"Connected to {bestServer.ServerName}");
            }
            else
            {
                OnConnectionStatusChanged?.Invoke("Failed to connect to discovered server");
            }
        }
        else
        {
            Debug.Log("No servers found during auto-discovery");
            OnConnectionStatusChanged?.Invoke("No servers found");
        }
    }

    public async void AutoDiscoverAndConnect()
    {
        await AutoDiscoverAndConnectAsync();
    }

    // New method: Start listening for server announcements
    private void StartListeningForServerAnnouncements()
    {
        try
        {
            announcementListener = new UdpClient(9998); // Listen on announcement port
            announcementCancellationToken = new CancellationTokenSource();

            announcementThread = new Thread(ListenForAnnouncements) { IsBackground = true };
            announcementThread.Start();

            Debug.Log("Started listening for server announcements on port 9998");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to start announcement listener: {ex.Message}");
        }
    }

    private void ListenForAnnouncements()
    {
        while (!announcementCancellationToken.Token.IsCancellationRequested)
        {
            try
            {
                IPEndPoint serverEndPoint = null;
                var data = announcementListener.Receive(ref serverEndPoint);
                string jsonData = Encoding.UTF8.GetString(data);
                var message = JsonConvert.DeserializeObject<NetworkMessage>(jsonData);

                if (message.Type == MessageType.ServerAnnouncement)
                {
                    var serverInfo = JsonConvert.DeserializeObject<ServerInfo>(message.Data);
                    if (serverInfo != null)
                    {
                        serverInfo.IPAddress = serverEndPoint.Address.ToString();

                        // Update or add server to discovered list
                        var existingServer = discoveredServers.Find(s => s.IPAddress == serverInfo.IPAddress && s.Port == serverInfo.Port);
                        if (existingServer != null)
                        {
                            // Update existing server info
                            int index = discoveredServers.IndexOf(existingServer);
                            discoveredServers[index] = serverInfo;
                        }
                        else
                        {
                            // Add new server
                            discoveredServers.Add(serverInfo);
                            UnityMainThreadDispatcher.Enqueue(() => {
                                Debug.Log($"Discovered server via announcement: {serverInfo.ServerName} at {serverInfo.IPAddress}:{serverInfo.Port}");
                                OnServerDiscovered?.Invoke(serverInfo);
                            });
                        }

                        // Clean up old servers (remove servers not seen for 30 seconds)
                        var currentTime = (uint)Environment.TickCount;
                        discoveredServers.RemoveAll(s => currentTime - s.Timestamp > 30000);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!announcementCancellationToken.Token.IsCancellationRequested)
                    Debug.LogError($"Announcement listener error: {ex.Message}");
            }
        }
    }

    // Enhanced method: Discover servers with more info
    public async Task<List<ServerInfo>> DiscoverServersAsync()
    {
        OnConnectionStatusChanged?.Invoke("Discovering servers...");

        if (client == null)
            client = new UnityNetworkClient();

        var servers = await client.DiscoverServersWithInfo(discoveryPort, (int)(discoveryTimeout * 1000));

        // Merge with servers discovered via announcements
        foreach (var announcedServer in discoveredServers)
        {
            if (!servers.Exists(s => s.IPAddress == announcedServer.IPAddress && s.Port == announcedServer.Port))
            {
                servers.Add(announcedServer);
            }
        }

        Debug.Log($"Total servers discovered: {servers.Count}");
        OnConnectionStatusChanged?.Invoke($"Found {servers.Count} server(s)");

        return servers;
    }

    public async void DiscoverAndConnectToServer()
    {
        await DiscoverAndConnectToServerAsync();
    }

    public NetworkGameObject SpawnNetworkObject(GameObject prefab, Vector3 position, Quaternion rotation, uint ownerId = 0)
    {
        var instance = Instantiate(prefab, position, rotation);
        var networkObj = instance.GetComponent<NetworkGameObject>();

        if (networkObj == null)
        {
            networkObj = instance.AddComponent<NetworkGameObject>();
        }
        if (!isServer)
            networkObj.NetworkId = ++nextNetworkId;
        else
            networkObj.NetworkId = nextNetworkId++;

        networkObj.IsNetworkOwned = true;
        networkObj.OwnerId = ownerId == 0 ? (isServer ? 0 : client?.ClientId ?? 0) : ownerId;

        networkObjects[networkObj.NetworkId] = networkObj;

        // Send spawn message
        var spawnMessage = new NetworkMessage
        {
            Type = MessageType.GameObjectSpawn,
            Data = JsonConvert.SerializeObject(networkObj.Serialize())
        };

        if (isServer)
            server?.BroadcastMessage(spawnMessage);
        else
            client?.SendMessage(spawnMessage);

        OnGameObjectSpawned?.Invoke(networkObj);
        return networkObj;
    }

    public void SpawnPlayer()
    {
        if (playerPrefab != null)
        {
            Vector3 spawnPos = new Vector3(UnityEngine.Random.Range(-5f, 5f), 1f, UnityEngine.Random.Range(-5f, 5f));
            SpawnNetworkObject(playerPrefab, spawnPos, Quaternion.identity);
        }
    }

    public void UpdateNetworkObject(NetworkGameObject networkObj)
    {
        var updateMessage = new NetworkMessage
        {
            Type = MessageType.GameObjectUpdate,
            Data = JsonConvert.SerializeObject(networkObj.Serialize())
        };

        if (isServer)
            server?.BroadcastMessage(updateMessage);
        else
            client?.SendMessage(updateMessage);
    }

    private void OnClientConnected(uint clientId)
    {
        Debug.Log($"Client connected: {clientId}");

        if (isServer)
        {
            // Send all existing objects to new client
            foreach (var obj in networkObjects.Values)
            {
                var spawnMessage = new NetworkMessage
                {
                    Type = MessageType.GameObjectSpawn,
                    Data = JsonConvert.SerializeObject(obj.Serialize())
                };
                server.SendMessageToClient(clientId, spawnMessage);
            }
        }
    }

    private void OnClientDisconnected(uint clientId)
    {
        Debug.Log($"Client disconnected: {clientId}");

        // Remove objects owned by disconnected client
        var objectsToRemove = new List<uint>();
        foreach (var kvp in networkObjects)
        {
            if (kvp.Value.OwnerId == clientId)
            {
                objectsToRemove.Add(kvp.Key);
            }
        }

        foreach (var id in objectsToRemove)
        {
            DestroyNetworkObject(id);
        }
    }

    private void OnClientConnected()
    {
        Debug.Log("Connected to server!");
    }

    private void OnClientDisconnected()
    {
        Debug.Log("Disconnected from server!");
        OnConnectionStatusChanged?.Invoke("Disconnected from server");
    }

    private void OnServerMessageReceived(uint senderId, NetworkMessage message)
    {
        HandleNetworkMessage(message);
    }

    private void OnClientMessageReceived(NetworkMessage message)
    {
        HandleNetworkMessage(message);
    }

    private void HandleNetworkMessage(NetworkMessage message)
    {
        Debug.Log($"Received message type: {message.Type} from sender: {message.SenderId}");

        switch (message.Type)
        {
            case MessageType.GameObjectSpawn:
                Debug.Log("Processing GameObjectSpawn message");
                var spawnData = JsonConvert.DeserializeObject<NetworkData>(message.Data);
                Debug.Log($"Spawning object: {spawnData.ObjectType} at {spawnData.Position}");
                SpawnNetworkObjectFromData(spawnData);
                break;

            case MessageType.GameObjectUpdate:
                var updateData = JsonConvert.DeserializeObject<NetworkData>(message.Data);
                Debug.Log($"Updating object: {updateData.NetworkId}");
                UpdateNetworkObjectFromData(updateData);
                break;

            case MessageType.GameObjectDestroy:
                uint destroyId = uint.Parse(message.Data);
                Debug.Log($"Destroying object: {destroyId}");
                DestroyNetworkObjectLocal(destroyId);
                break;
        }
    }

    private void SpawnNetworkObjectFromData(NetworkData data)
    {
        Debug.Log($"Spawning object: {data.ObjectType} (ID: {data.NetworkId}) at position: {data.Position}");

        if (networkObjects.ContainsKey(data.NetworkId))
        {
            Debug.LogWarning($"Network object {data.NetworkId} already exists, updating instead.");
            UpdateNetworkObjectFromData(data);
            return;
        }

        GameObject prefab = GetPrefabByType(data.ObjectType);
        if (prefab != null)
        {

            Vector3 position = new Vector3(data.Position.x, data.Position.y, data.Position.z);
            Vector3 rotation = new Vector3(data.Rotation.x, data.Rotation.y, data.Rotation.z);

            var instance = Instantiate(prefab, position, Quaternion.Euler(rotation));
            var networkObj = instance.GetComponent<NetworkGameObject>();

            if (networkObj == null)
            {
                networkObj = instance.AddComponent<NetworkGameObject>();
                Debug.Log("Added NetworkGameObject component to instance");
            }

            networkObj.NetworkId = data.NetworkId;
            networkObj.ObjectType = data.ObjectType;
            networkObj.OwnerId = data.OwnerId;

            if (isServer)
            {
                networkObj.IsNetworkOwned = (data.OwnerId == 0);
            }
            else
            {
                networkObj.IsNetworkOwned = (client != null && data.OwnerId == client.ClientId);
            }

            Debug.Log($"Instantiated {data.ObjectType} with ID {data.NetworkId}, Owner: {data.OwnerId}, IsOwned: {networkObj.IsNetworkOwned}");

            networkObjects[data.NetworkId] = networkObj;

            OnGameObjectSpawned?.Invoke(networkObj);

            instance.name = $"{data.ObjectType}_{data.NetworkId}_{(networkObj.IsNetworkOwned ? "Local" : "Remote")}";
        }
        else
        {
            Debug.LogError($"Failed to find prefab for object type: {data.ObjectType}");
        }
    }

    private void UpdateNetworkObjectFromData(NetworkData data)
    {
        if (networkObjects.ContainsKey(data.NetworkId))
        {
            var networkObj = networkObjects[data.NetworkId];
            if (!networkObj.IsNetworkOwned)
            {
                Vector3 position = new Vector3(data.Position.x, data.Position.y, data.Position.z);
                Vector3 rotation = new Vector3(data.Rotation.x, data.Rotation.y, data.Rotation.z);

                networkObj.Deserialize(data);
                Debug.Log($"Updated object {data.NetworkId} to position: {position}");
            }
        }
        else
        {
            Debug.LogWarning($"Received update for unknown object ID: {data.NetworkId}, attempting to spawn...");
            SpawnNetworkObjectFromData(data);
        }
    }

    private void DestroyNetworkObject(uint networkId)
    {
        if (networkObjects.ContainsKey(networkId))
        {
            DestroyNetworkObjectLocal(networkId);

            var destroyMessage = new NetworkMessage
            {
                Type = MessageType.GameObjectDestroy,
                Data = networkId.ToString()
            };

            if (isServer)
                server?.BroadcastMessage(destroyMessage);
            else
                client?.SendMessage(destroyMessage);
        }
    }

    private void DestroyNetworkObjectLocal(uint networkId)
    {
        if (networkObjects.ContainsKey(networkId))
        {
            var obj = networkObjects[networkId];
            networkObjects.Remove(networkId);

            if (obj != null && obj.gameObject != null)
                Destroy(obj.gameObject);

            OnGameObjectDestroyed?.Invoke(networkId);
        }
    }

    private GameObject GetPrefabByType(string objectType)
    {
        if (objectType == "Player" && playerPrefab != null)
            return playerPrefab;

        foreach (var prefab in networkPrefabs)
        {
            if (prefab.name == objectType)
                return prefab;
        }

        return null;
    }

    private void OnDestroy()
    {
        server?.StopServer();
        client?.Disconnect();

        // Stop announcement listener
        announcementCancellationToken?.Cancel();
        announcementThread?.Join(1000);
        announcementListener?.Close();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            server?.StopServer();
            client?.Disconnect();
        }
    }

    public async Task DiscoverAndConnectToServerAsync()
    {
        if (client == null)
            client = new UnityNetworkClient();

        var servers = await client.DiscoverServers(discoveryPort);
        if (servers.Count > 0)
        {
            var serverEndpoint = servers[0];
            bool connected = await StartClient(serverEndpoint.Address.ToString());
            if (connected)
            {
                Debug.Log($"Successfully connected to server at {serverEndpoint.Address}");
            }
            else
            {
                Debug.LogError("Failed to connect to discovered server");
            }
        }
        else
        {
            Debug.Log("No servers found on LAN");
        }
    }

    // New utility methods for UI integration
    public async Task<bool> ConnectToServer(ServerInfo serverInfo)
    {
        return await StartClient(serverInfo.IPAddress);
    }

    public void StopConnection()
    {
        if (isServer)
        {
            server?.StopServer();
            server = null;
            isServer = false;
        }
        else
        {
            client?.Disconnect();
            client = null;
        }
        OnConnectionStatusChanged?.Invoke("Disconnected");
    }

    // Get current connection info
    public string GetConnectionStatus()
    {
        if (isServer)
            return $"Server: {GetLocalIPv4()}:{serverPort} ({server?.ConnectedClients ?? 0} clients)";
        else if (client != null && client.IsConnected)
            return $"Client: Connected (ID: {client.ClientId})";
        else
            return "Disconnected";
    }
}


public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static readonly Queue<Action> _executionQueue = new();

    private void Update()
    {
        lock (_executionQueue)
        {
            while (_executionQueue.Count > 0)
                _executionQueue.Dequeue().Invoke();
        }
    }

    public static void Enqueue(Action action)
    {
        lock (_executionQueue)
        {
            _executionQueue.Enqueue(action);
        }
    }

    private void OnDestroy()
    {
        lock (_executionQueue)
        {
            _executionQueue.Clear();
        }
    }
}