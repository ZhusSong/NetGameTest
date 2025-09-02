using UnityEngine;
using UnityEngine.UI;

public class NetworkUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button startServerButton;
    [SerializeField] private Button startClientButton;
    [SerializeField] private Button discoverButton;
    [SerializeField] private Button spawnPlayerButton;
    [SerializeField] private InputField serverIPInput;
    [SerializeField] private Text statusText;

    private NetworkGameManager networkManager;

    private void Start()
    {
        networkManager = FindObjectOfType<NetworkGameManager>();

        if (startServerButton != null)
            startServerButton.onClick.AddListener(StartServer);
        if (startClientButton != null)
            startClientButton.onClick.AddListener(StartClientFromUI);
        if (discoverButton != null)
            discoverButton.onClick.AddListener(DiscoverServers);
        if (spawnPlayerButton != null)
            spawnPlayerButton.onClick.AddListener(SpawnPlayer);

        UpdateStatus("Ready");
    }

    private void StartServer()
    {
        networkManager.StartServer();
        startServerButton.interactable = false; 
        startClientButton.interactable = false; 
        spawnPlayerButton.interactable = false;
        UpdateStatus("Starting server...");
        networkManager.SpawnPlayer();
    }

    private async void StartClientFromUI()
    {
        startServerButton.interactable = false;
        startClientButton.interactable = false;

        string ip = serverIPInput != null ? serverIPInput.text : "127.0.0.1";
        if (string.IsNullOrEmpty(ip))
            ip = "127.0.0.1";

        UpdateStatus($"Connecting to {ip}...");

        bool connected = await networkManager.StartClient(ip);
        if (connected)
        {
            UpdateStatus($"Connected to {ip}");
        }
        else
        {
            UpdateStatus($"Failed to connect to {ip}");
        }
    }

    private async void DiscoverServers()
    {
        UpdateStatus("Discovering servers...");
        await networkManager.DiscoverAndConnectToServerAsync();
    }

    private void SpawnPlayer()
    {
        networkManager.SpawnPlayer();
        UpdateStatus("Player spawned");
    }

    private void UpdateStatus(string message)
    {
        if (statusText != null)
            statusText.text = $"Status: {message}";
        Debug.Log(message);
    }

    private void Update()
    {
        if (networkManager != null)
        {
            if (spawnPlayerButton != null)
                spawnPlayerButton.interactable = networkManager.IsConnected;

            if (networkManager.IsServer)
                spawnPlayerButton.interactable = false;
        }

    }
}