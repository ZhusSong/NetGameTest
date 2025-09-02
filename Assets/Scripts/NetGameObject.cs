using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using System;

public class NetworkGameObject : MonoBehaviour
{
    [SerializeField] private uint networkId;
    [SerializeField] private bool isNetworkOwned;
    [SerializeField] private uint ownerId;
    [SerializeField] private string objectType;
    [SerializeField] private float sendRate = 20f; 

    private float lastSendTime;
    private Vector3 lastSentPosition;
    private Vector3 lastSentRotation;
    private NetworkGameManager networkManager;

    // Interpolation for smooth movement
    private Vector3 networkPosition;
    private Vector3 networkRotation;
    private float lerpRate = 15f;

    public uint NetworkId
    {
        get => networkId;
        set => networkId = value;
    }

    public bool IsNetworkOwned
    {
        get => isNetworkOwned;
        set => isNetworkOwned = value;
    }

    public uint OwnerId
    {
        get => ownerId;
        set => ownerId = value;
    }

    public string ObjectType
    {
        get => objectType;
        set => objectType = value;
    }

    protected virtual void Start()
    {
        networkManager = FindObjectOfType<NetworkGameManager>();
        networkPosition = transform.position;
        networkRotation = transform.eulerAngles;

        if (string.IsNullOrEmpty(objectType))
            objectType = gameObject.name;
    }

    protected virtual void Update()
    {
        if (isNetworkOwned && networkManager != null && networkManager.IsConnected)
        {
            // Send updates if we own this object
            if (Time.time - lastSendTime > 1f / sendRate)
            {
                if (HasChanged())
                {
                    SendUpdate();
                    lastSendTime = Time.time;
                }
            }
        }
        else
        {
            // Interpolate to network position if we don't own it
            transform.position = Vector3.Lerp(transform.position, networkPosition, lerpRate * Time.deltaTime);
            transform.eulerAngles = Vector3.Lerp(transform.eulerAngles, networkRotation, lerpRate * Time.deltaTime);
        }
    }

    private bool HasChanged()
    {
        return Vector3.Distance(transform.position, lastSentPosition) > 0.1f ||
               Vector3.Distance(transform.eulerAngles, lastSentRotation) > 1f;
    }

    private void SendUpdate()
    {
        lastSentPosition = transform.position;
        lastSentRotation = transform.eulerAngles;
        networkManager?.UpdateNetworkObject(this);
    }

    public virtual NetworkData Serialize()
    {
        var data = new NetworkData
        {
            NetworkId = networkId,
            Position = transform.position,
            Rotation = transform.eulerAngles,
            ObjectType = objectType,
            OwnerId = ownerId
        };

        // Add custom component data
        SerializeCustomData(data);
        return data;
    }

    public virtual void Deserialize(NetworkData data)
    {
        networkId = data.NetworkId;
        networkPosition = data.Position;
        networkRotation = data.Rotation;
        objectType = data.ObjectType;
        ownerId = data.OwnerId;

        // Apply custom component data
        DeserializeCustomData(data);
    }

    protected virtual void SerializeCustomData(NetworkData data)
    {
        // Override in derived classes to add custom data
    }

    protected virtual void DeserializeCustomData(NetworkData data)
    {
        // Override in derived classes to handle custom data
    }
}

// Player specific network component
public class NetworkPlayer : NetworkGameObject
{
    [SerializeField] private float health = 100f;
    [SerializeField] private float speed = 5f;
    [SerializeField] private string playerName = "Player";

    private CharacterController characterController;
    private Camera playerCamera;

    public float Health => health;
    public string PlayerName => playerName;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        playerCamera = GetComponentInChildren<Camera>();
    }

  
    protected override void Start()
    {
        base.Start();
        ObjectType = "Player";
        playerName = $"Player_{NetworkId}";
        Debug.Log($"Player created with NetworkId: {NetworkId}, ObjectType: {ObjectType}");
    }

    protected override void Update()
    {
        base.Update();

        if (IsNetworkOwned)
        {
            HandleInput();
        }
        else
        {
            // Disable camera for non-owned players
            if (playerCamera != null)
                playerCamera.gameObject.SetActive(false);
        }
    }

    private void HandleInput()
    {
        if (characterController == null) return;

        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");

        Vector3 direction = new Vector3(horizontal, 0f, vertical).normalized;

        if (direction.magnitude >= 0.1f)
        {
            Vector3 moveDir = direction * speed;
            moveDir.y = -9.81f; // Simple gravity
            characterController.Move(moveDir * Time.deltaTime);
        }
    }

    protected override void SerializeCustomData(NetworkData data)
    {
        data.CustomData["Health"] = health;
        data.CustomData["PlayerName"] = playerName;
        data.CustomData["Speed"] = speed;
    }

    protected override void DeserializeCustomData(NetworkData data)
    {
        if (data.CustomData.ContainsKey("Health"))
            health = Convert.ToSingle(data.CustomData["Health"]);
        if (data.CustomData.ContainsKey("PlayerName"))
            playerName = data.CustomData["PlayerName"].ToString();
        if (data.CustomData.ContainsKey("Speed"))
            speed = Convert.ToSingle(data.CustomData["Speed"]);
    }

    public void TakeDamage(float damage)
    {
        if (IsNetworkOwned)
        {
            health -= damage;
            health = Mathf.Max(0f, health);
        }
    }
}