using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;


[System.Serializable]
public struct SerializableVector3
{
    public float x;
    public float y;
    public float z;

    public SerializableVector3(float x, float y, float z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }

    public static implicit operator Vector3(SerializableVector3 v)
    {
        return new Vector3(v.x, v.y, v.z);
    }

    public static implicit operator SerializableVector3(Vector3 v)
    {
        return new SerializableVector3(v.x, v.y, v.z);
    }

    public override string ToString()
    {
        return $"({x}, {y}, {z})";
    }
}

[System.Serializable]
public struct SerializableQuaternion
{
    public float x;
    public float y;
    public float z;
    public float w;

    public SerializableQuaternion(float x, float y, float z, float w)
    {
        this.x = x;
        this.y = y;
        this.z = z;
        this.w = w;
    }

    public static implicit operator Quaternion(SerializableQuaternion q)
    {
        return new Quaternion(q.x, q.y, q.z, q.w);
    }

    public static implicit operator SerializableQuaternion(Quaternion q)
    {
        return new SerializableQuaternion(q.x, q.y, q.z, q.w);
    }

    public override string ToString()
    {
        return $"({x}, {y}, {z}, {w})";
    }
}

// Network message types
public enum MessageType
{
    Connect,
    Disconnect,
    GameObjectUpdate,
    GameObjectSpawn,
    GameObjectDestroy,
    PlayerInput,
    Heartbeat,
    Discovery,
    DiscoveryResponse,
    ServerAnnouncement
}



// Network message structure
[System.Serializable]
public class NetworkMessage
{
    public MessageType Type;
    public uint SenderId;
    public uint Timestamp;
    public string Data;

    public NetworkMessage()
    {
        Timestamp = (uint)Environment.TickCount;
    }
}

// server's info
[System.Serializable]
public class ServerInfo
{
    public string ServerName;
    public string IPAddress;
    public int Port;
    public int PlayerCount;
    public int MaxPlayers;
    public string GameVersion;
    public uint Timestamp;

    public ServerInfo()
    {
        Timestamp = (uint)Environment.TickCount;
        GameVersion = Application.version;
        MaxPlayers = 8;
    }
}

// GameObject network data
[System.Serializable]
public class NetworkData
{
    public uint NetworkId;
    public SerializableVector3 Position;
    public SerializableVector3 Rotation; 
    public string ObjectType;
    public uint OwnerId;
    public Dictionary<string, object> CustomData;

    public NetworkData()
    {
        CustomData = new Dictionary<string, object>();
    }
}

public class UnityNetworkClient
{
    private UdpClient udpClient;
    private IPEndPoint serverEndPoint;
    private bool isConnected = false;
    private uint clientId;
    private Thread receiveThread;
    private CancellationTokenSource cancellationToken;

    public event Action<NetworkMessage> OnMessageReceived;
    public event Action OnConnected;
    public event Action OnDisconnected;

    public bool IsConnected => isConnected;
    public uint ClientId => clientId;

    public async Task<bool> ConnectToServer(string serverIP, int port)
    {
        try
        {
            serverEndPoint = new IPEndPoint(IPAddress.Parse(serverIP), port);
            udpClient = new UdpClient();
            cancellationToken = new CancellationTokenSource();

            var connectMsg = new NetworkMessage
            {
                Type = MessageType.Connect,
                SenderId = 0,
                Data = SystemInfo.deviceName
            };

            await SendMessage(connectMsg);

            receiveThread = new Thread(ReceiveMessages) { IsBackground = true };
            receiveThread.Start();

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Connection failed: {ex.Message}");
            return false;
        }
    }

    public async Task SendMessage(NetworkMessage message)
    {
        if (udpClient == null) return;

        try
        {
            message.SenderId = clientId;
            string jsonData = JsonConvert.SerializeObject(message);
            byte[] data = Encoding.UTF8.GetBytes(jsonData);
            await udpClient.SendAsync(data, data.Length, serverEndPoint);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Send error: {ex.Message}");
        }
    }

    private void ReceiveMessages()
    {
        while (!cancellationToken.Token.IsCancellationRequested)
        {
            try
            {
                var result = udpClient.Receive(ref serverEndPoint);
                string jsonData = Encoding.UTF8.GetString(result);
                var message = JsonConvert.DeserializeObject<NetworkMessage>(jsonData);

                if (message.Type == MessageType.Connect && !isConnected)
                {
                    clientId = uint.Parse(message.Data);
                    isConnected = true;
                    UnityMainThreadDispatcher.Enqueue(() => OnConnected?.Invoke());
                }

                UnityMainThreadDispatcher.Enqueue(() => OnMessageReceived?.Invoke(message));
            }
            catch (Exception ex)
            {
                if (!cancellationToken.Token.IsCancellationRequested)
                    Debug.LogError($"Receive error: {ex.Message}");
            }
        }
    }

    public void Disconnect()
    {
        isConnected = false;
        cancellationToken?.Cancel();
        receiveThread?.Join(1000);
        udpClient?.Close();
        UnityMainThreadDispatcher.Enqueue(() => OnDisconnected?.Invoke());
    }

    // Enhanced server discovery with server info
    public async Task<List<ServerInfo>> DiscoverServersWithInfo(int discoveryPort = 9999, int timeout = 5000)
    {
        var servers = new List<ServerInfo>();
        var discoveryClient = new UdpClient();
        discoveryClient.EnableBroadcast = true;

        try
        {
            var broadcastEndPoint = new IPEndPoint(IPAddress.Broadcast, discoveryPort);
            var discoveryMsg = new NetworkMessage
            {
                Type = MessageType.Discovery,
                Data = "UNITY_GAME_DISCOVERY"
            };

            string jsonData = JsonConvert.SerializeObject(discoveryMsg);
            byte[] data = Encoding.UTF8.GetBytes(jsonData);
            await discoveryClient.SendAsync(data, data.Length, broadcastEndPoint);

            discoveryClient.Client.ReceiveTimeout = timeout;
            var startTime = Environment.TickCount;

            while (Environment.TickCount - startTime < timeout)
            {
                try
                {
                    IPEndPoint responseEndPoint = null;
                    var response = discoveryClient.Receive(ref responseEndPoint);
                    var responseMsg = JsonConvert.DeserializeObject<NetworkMessage>(
                        Encoding.UTF8.GetString(response));

                    if (responseMsg.Type == MessageType.DiscoveryResponse)
                    {
                        var serverInfo = JsonConvert.DeserializeObject<ServerInfo>(responseMsg.Data);
                        if (serverInfo != null)
                        {
                            serverInfo.IPAddress = responseEndPoint.Address.ToString();
                            servers.Add(serverInfo);
                            Debug.Log($"Discovered server: {serverInfo.ServerName} at {serverInfo.IPAddress}:{serverInfo.Port}");
                        }
                    }
                }
                catch (SocketException) { }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Discovery error: {ex.Message}");
        }
        finally
        {
            discoveryClient?.Close();
        }

        return servers;
    }

    public async Task<List<IPEndPoint>> DiscoverServers(int discoveryPort = 9999, int timeout = 5000)
    {
        var serverInfos = await DiscoverServersWithInfo(discoveryPort, timeout);
        var endpoints = new List<IPEndPoint>();

        foreach (var serverInfo in serverInfos)
        {
            try
            {
                endpoints.Add(new IPEndPoint(IPAddress.Parse(serverInfo.IPAddress), serverInfo.Port));
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to parse server endpoint: {ex.Message}");
            }
        }

        return endpoints;
    }
}


public class UnityNetworkServer
{
    private UdpClient udpServer;
    private UdpClient discoveryServer;
    private UdpClient announcementClient;

    private Dictionary<uint, IPEndPoint> clients;
    private uint nextClientId = 1;
    private bool isRunning = false;
    private Thread serverThread;
    private Thread discoveryThread;
    private Thread announcementThread;

    private CancellationTokenSource cancellationToken;

    private ServerInfo serverInfo;

    public event Action<uint, NetworkMessage> OnMessageReceived;
    public event Action<uint> OnClientConnected;
    public event Action<uint> OnClientDisconnected;

    public int Port { get; private set; }
    public int ConnectedClients => clients.Count;
    public ServerInfo ServerInfo => serverInfo;

    public UnityNetworkServer()
    {
        clients = new Dictionary<uint, IPEndPoint>();
        serverInfo = new ServerInfo();
    }

    public bool StartServer(int port, int discoveryPort = 9999, string serverName = null)
    {
        try
        {
            Port = port;
            udpServer = new UdpClient(port);
            discoveryServer = new UdpClient(discoveryPort);

            announcementClient = new UdpClient();
            announcementClient.EnableBroadcast = true;

            cancellationToken = new CancellationTokenSource();
            isRunning = true;

            // Setup server info
            serverInfo.ServerName = string.IsNullOrEmpty(serverName) ? $"Unity Game Server ({SystemInfo.deviceName})" : serverName;
            serverInfo.Port = port;
            serverInfo.IPAddress = GetLocalIPv4();

            serverThread = new Thread(ServerLoop) { IsBackground = true };
            serverThread.Start();

            discoveryThread = new Thread(DiscoveryLoop) { IsBackground = true };
            discoveryThread.Start();

            announcementThread = new Thread(AnnouncementLoop) { IsBackground = true };
            announcementThread.Start();

            Debug.Log($"Server '{serverInfo.ServerName}' started on {serverInfo.IPAddress}:{port}");
            Debug.Log($"Broadcasting server info every 5 seconds...");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to start server: {ex.Message}");
            return false;
        }
    }
    private string GetLocalIPv4()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                {
                    return ip.ToString();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to get local IP: {ex.Message}");
        }
        return "127.0.0.1";
    }


    private void AnnouncementLoop()
    {
        while (isRunning && !cancellationToken.Token.IsCancellationRequested)
        {
            try
            {
                BroadcastServerAnnouncement();
                Thread.Sleep(5000); // Broadcast every 5 seconds
            }
            catch (Exception ex)
            {
                if (isRunning)
                    Debug.LogError($"Announcement error: {ex.Message}");
            }
        }
    }
    private void BroadcastServerAnnouncement()
    {
        try
        {
            serverInfo.PlayerCount = clients.Count;
            serverInfo.Timestamp = (uint)Environment.TickCount;

            var announcement = new NetworkMessage
            {
                Type = MessageType.ServerAnnouncement,
                Data = JsonConvert.SerializeObject(serverInfo)
            };

            string jsonData = JsonConvert.SerializeObject(announcement);
            byte[] data = Encoding.UTF8.GetBytes(jsonData);
            var broadcastEndPoint = new IPEndPoint(IPAddress.Broadcast, 9998); // Different port for announcements

            announcementClient.Send(data, data.Length, broadcastEndPoint);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to broadcast server announcement: {ex.Message}");
        }
    }
    private void ServerLoop()
    {
        while (isRunning && !cancellationToken.Token.IsCancellationRequested)
        {
            try
            {
                IPEndPoint clientEndPoint = null;
                var data = udpServer.Receive(ref clientEndPoint);
                string jsonData = Encoding.UTF8.GetString(data);
                var message = JsonConvert.DeserializeObject<NetworkMessage>(jsonData);

                UnityMainThreadDispatcher.Enqueue(() => HandleClientMessage(message, clientEndPoint));
            }
            catch (Exception ex)
            {
                if (isRunning)
                    Debug.LogError($"Server error: {ex.Message}");
            }
        }
    }

    private void DiscoveryLoop()
    {
        while (isRunning && !cancellationToken.Token.IsCancellationRequested)
        {
            try
            {
                IPEndPoint clientEndPoint = null;
                var data = discoveryServer.Receive(ref clientEndPoint);
                string jsonData = Encoding.UTF8.GetString(data);
                var message = JsonConvert.DeserializeObject<NetworkMessage>(jsonData);

                if (message.Type == MessageType.Discovery)
                {
                    serverInfo.PlayerCount = clients.Count;
                    serverInfo.Timestamp = (uint)Environment.TickCount;

                    var response = new NetworkMessage
                    {
                        Type = MessageType.DiscoveryResponse,
                        Data = $"UNITY_GAME_SERVER:{Port}"
                    };

                    string responseJson = JsonConvert.SerializeObject(response);
                    byte[] responseData = Encoding.UTF8.GetBytes(responseJson);
                    discoveryServer.Send(responseData, responseData.Length, clientEndPoint);
                   
                    Debug.Log($"Responded to discovery from {clientEndPoint}");
                }
            }
            catch (Exception ex)
            {
                if (isRunning)
                    Debug.LogError($"Discovery error: {ex.Message}");
            }
        }
    }

    private void HandleClientMessage(NetworkMessage message, IPEndPoint clientEndPoint)
    {
        if (message.Type == MessageType.Connect)
        {
            uint clientId = nextClientId++;
            clients[clientId] = clientEndPoint;

            var response = new NetworkMessage
            {
                Type = MessageType.Connect,
                SenderId = 0,
                Data = clientId.ToString()
            };

            SendMessageToClient(clientId, response);
            OnClientConnected?.Invoke(clientId);
            Debug.Log($"Client {clientId} connected from {clientEndPoint}");
        }
        else
        {
            uint senderId = 0;
            foreach (var kvp in clients)
            {
                if (kvp.Value.Equals(clientEndPoint))
                {
                    senderId = kvp.Key;
                    break;
                }
            }

            if (senderId > 0)
            {
                message.SenderId = senderId;
                OnMessageReceived?.Invoke(senderId, message);

                if (message.Type == MessageType.GameObjectUpdate ||
                    message.Type == MessageType.GameObjectSpawn ||
                    message.Type == MessageType.GameObjectDestroy)
                {
                    BroadcastMessage(message, senderId);
                }
            }
        }
    }

    public async Task SendMessageToClient(uint clientId, NetworkMessage message)
    {
        if (clients.ContainsKey(clientId))
        {
            try
            {
                string jsonData = JsonConvert.SerializeObject(message);
                byte[] data = Encoding.UTF8.GetBytes(jsonData);
                await udpServer.SendAsync(data, data.Length, clients[clientId]);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Send error to client {clientId}: {ex.Message}");
            }
        }
    }

    public async Task BroadcastMessage(NetworkMessage message, uint excludeClientId = 0)
    {
        var tasks = new List<Task>();
        foreach (var clientId in clients.Keys)
        {
            if (clientId != excludeClientId)
            {
                tasks.Add(SendMessageToClient(clientId, message));
            }
        }
        await Task.WhenAll(tasks);
    }

    public void StopServer()
    {
        isRunning = false;
        cancellationToken?.Cancel();
        serverThread?.Join(1000);
        discoveryThread?.Join(1000);
        announcementThread?.Join(1000);
        udpServer?.Close();
        discoveryServer?.Close();
        announcementClient?.Close();
        Debug.Log("Server stopped");
    }
}

