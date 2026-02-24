using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;

#if UNITY_WSA && !UNITY_EDITOR
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.Security.Cryptography;
#else
using System.Net.WebSockets;
#endif

/// <summary>
/// WebSocket client for real-time building energy data updates on HoloLens 2.
///
/// ARCHITECTURE (Hybrid WebSocket + Polling Fallback):
///   Primary: WebSocket connection to the Django Channels backend for instant push notifications
///   Fallback: If WebSocket fails or disconnects, BuildingEnergyManager's polling resumes automatically
///
/// PROTOCOL:
///   1. Client connects to  wss://backend.gisworld-tech.com/ws/buildings/{community_id}/
///   2. Client sends auth message:  {"type": "authenticate", "token": "<JWT>"}
///   3. Server sends building updates: {"type": "building_updated", "data": {...}}
///   4. Client ACKs each update:        {"type": "ack", "gml_id": "..."}
///
/// MESSAGE TYPES FROM SERVER:
///   • building_updated  — a single building's energy data changed
///   • building_deleted  — a building was removed
///   • bulk_update       — multiple buildings changed (e.g., batch simulation)
///   • auth_ok           — authentication successful
///   • auth_fail         — authentication failed, fall back to polling
///   • ping              — keepalive from server
///
/// THREAD SAFETY:
///   WebSocket recv runs on a background thread. Incoming messages are queued in a
///   ConcurrentQueue and dispatched on the Unity main thread in Update().
///   No Unity API calls happen off the main thread.
///
/// HoloLens 2 (UWP):
///   Uses Windows.Networking.Sockets.MessageWebSocket (System.Net.WebSockets is not
///   available on UWP/IL2CPP). The same public API is exposed to BuildingEnergyManager.
///
/// Attach to the same GameObject as BuildingEnergyManager (auto-added by integration code).
/// </summary>
public class BuildingWebSocketClient : MonoBehaviour
{
    // ─── Configuration ───────────────────────────────────────────────
    [Header("WebSocket Configuration")]
    [Tooltip("WebSocket server URL (wss:// for production)")]
    public string wsBaseUrl = "wss://backend.gisworld-tech.com";

    [Tooltip("Community ID — must match BuildingEnergyManager.communityId")]
    public string communityId = "08417008";

    [Tooltip("Reconnect delay after disconnect (seconds)")]
    [Range(2f, 60f)]
    public float reconnectDelay = 5f;

    [Tooltip("Maximum reconnect attempts before falling back to polling")]
    [Range(1, 20)]
    public int maxReconnectAttempts = 5;

    [Tooltip("Ping interval to keep connection alive (seconds)")]
    [Range(10f, 120f)]
    public float pingInterval = 30f;

    // ─── State ───────────────────────────────────────────────────────
    public bool IsConnected { get; private set; }
    public bool IsAuthenticated { get; private set; }
    public int ReconnectAttempts { get; private set; }

    // ─── Events (subscribe from BuildingEnergyManager) ───────────────
    /// <summary>Fired on main thread when a single building update arrives.</summary>
    public event Action<string, JObject> OnBuildingUpdated;   // (gmlId, fullBuildingJson)

    /// <summary>Fired on main thread when a building is deleted.</summary>
    public event Action<string> OnBuildingDeleted;             // (gmlId)

    /// <summary>Fired on main thread when multiple buildings update at once.</summary>
    public event Action<JArray> OnBulkUpdate;                  // (array of building objects)

    /// <summary>Fired when WebSocket connects/disconnects (true=connected).</summary>
    public event Action<bool> OnConnectionChanged;

    // ─── Internal ────────────────────────────────────────────────────
    private string authToken;
    private readonly Queue<string> incomingMessages = new Queue<string>();
    private readonly object msgLock = new object();
    private float pingTimer;
    private Coroutine connectionCoroutine;
    private bool intentionalDisconnect;

#if UNITY_WSA && !UNITY_EDITOR
    // UWP: MessageWebSocket
    private MessageWebSocket uwpSocket;
    private DataWriter uwpWriter;
#else
    // Standalone / Editor: ClientWebSocket
    private ClientWebSocket wsClient;
    private CancellationTokenSource cts;
    private Thread recvThread;
#endif

    // ─── Public API ──────────────────────────────────────────────────

    /// <summary>
    /// Connect to the WebSocket server with JWT authentication.
    /// Call this after BuildingEnergyManager has an access token.
    /// </summary>
    public void Connect(string jwtToken)
    {
        if (IsConnected)
        {
            Debug.Log("[WS] Already connected.");
            return;
        }

        authToken = jwtToken;
        intentionalDisconnect = false;
        ReconnectAttempts = 0;

        if (connectionCoroutine != null) StopCoroutine(connectionCoroutine);
        connectionCoroutine = StartCoroutine(ConnectLoop());
    }

    /// <summary>
    /// Disconnect gracefully. Stops reconnect attempts.
    /// </summary>
    public void Disconnect()
    {
        intentionalDisconnect = true;
        if (connectionCoroutine != null)
        {
            StopCoroutine(connectionCoroutine);
            connectionCoroutine = null;
        }
        CloseSocket();
    }

    /// <summary>
    /// Send a JSON message to the server (main thread safe).
    /// </summary>
    public void SendMessage(string json)
    {
        if (!IsConnected) return;
        StartCoroutine(SendAsync(json));
    }

    // ─── Unity Lifecycle ─────────────────────────────────────────────

    void Update()
    {
        // Dispatch queued messages on the main thread
        lock (msgLock)
        {
            while (incomingMessages.Count > 0)
            {
                string raw = incomingMessages.Dequeue();
                ProcessMessage(raw);
            }
        }

        // Keepalive ping
        if (IsConnected && IsAuthenticated)
        {
            pingTimer += Time.deltaTime;
            if (pingTimer >= pingInterval)
            {
                pingTimer = 0f;
                SendMessage("{\"type\":\"ping\"}");
            }
        }
    }

    void OnDestroy()
    {
        Disconnect();
    }

    // ─── Connection Loop with Reconnect ──────────────────────────────

    private IEnumerator ConnectLoop()
    {
        while (!intentionalDisconnect && ReconnectAttempts <= maxReconnectAttempts)
        {
            if (ReconnectAttempts > 0)
            {
                float delay = Mathf.Min(reconnectDelay * ReconnectAttempts, 60f); // Exponential backoff capped at 60s
                Debug.Log($"[WS] Reconnect attempt {ReconnectAttempts}/{maxReconnectAttempts} in {delay:F0}s...");
                yield return new WaitForSeconds(delay);
            }

            string url = $"{wsBaseUrl}/ws/buildings/{communityId}/";
            Debug.Log($"<color=cyan>[WS] Connecting to {url}</color>");

            yield return StartCoroutine(ConnectPlatform(url));

            if (IsConnected)
            {
                // Authenticate
                string authMsg = $"{{\"type\":\"authenticate\",\"token\":\"{authToken}\"}}";
                yield return StartCoroutine(SendAsync(authMsg));

                // Wait for auth response (up to 10s)
                float authWait = 0f;
                while (!IsAuthenticated && IsConnected && authWait < 10f)
                {
                    authWait += Time.deltaTime;
                    yield return null;
                }

                if (IsAuthenticated)
                {
                    Debug.Log("<color=green>[WS] ✅ Authenticated — real-time updates active</color>");
                    ReconnectAttempts = 0;
                    OnConnectionChanged?.Invoke(true);

                    // Stay connected until socket closes
                    while (IsConnected && !intentionalDisconnect)
                    {
                        yield return new WaitForSeconds(1f);
                    }
                }
                else
                {
                    Debug.LogWarning("[WS] Authentication timeout — closing socket");
                    CloseSocket();
                }
            }

            if (!intentionalDisconnect)
            {
                ReconnectAttempts++;
                IsConnected = false;
                IsAuthenticated = false;
                OnConnectionChanged?.Invoke(false);
            }
        }

        if (ReconnectAttempts > maxReconnectAttempts)
        {
            Debug.LogWarning($"<color=yellow>[WS] Max reconnect attempts ({maxReconnectAttempts}) reached — falling back to polling</color>");
            OnConnectionChanged?.Invoke(false);
        }
    }

    // ─── Message Processing (Main Thread) ────────────────────────────

    private void ProcessMessage(string raw)
    {
        try
        {
            JObject msg = JObject.Parse(raw);
            string type = msg["type"]?.ToString();

            switch (type)
            {
                case "auth_ok":
                    IsAuthenticated = true;
                    Debug.Log("<color=green>[WS] Server confirmed authentication</color>");
                    break;

                case "auth_fail":
                    Debug.LogWarning($"[WS] Authentication failed: {msg["reason"]}");
                    IsAuthenticated = false;
                    CloseSocket();
                    break;

                case "building_updated":
                    {
                        JObject data = msg["data"] as JObject;
                        string gmlId = data?["modified_gml_id"]?.ToString();
                        if (!string.IsNullOrEmpty(gmlId) && data != null)
                        {
                            Debug.Log($"<color=green>[WS] 📡 Building updated: {gmlId}</color>");
                            OnBuildingUpdated?.Invoke(gmlId, data);

                            // ACK
                            SendMessage($"{{\"type\":\"ack\",\"gml_id\":\"{gmlId}\"}}");
                        }
                    }
                    break;

                case "building_deleted":
                    {
                        string gmlId = msg["gml_id"]?.ToString();
                        if (!string.IsNullOrEmpty(gmlId))
                        {
                            Debug.Log($"<color=yellow>[WS] 🗑 Building deleted: {gmlId}</color>");
                            OnBuildingDeleted?.Invoke(gmlId);
                        }
                    }
                    break;

                case "bulk_update":
                    {
                        JArray buildings = msg["data"] as JArray;
                        if (buildings != null && buildings.Count > 0)
                        {
                            Debug.Log($"<color=green>[WS] 📦 Bulk update: {buildings.Count} buildings</color>");
                            OnBulkUpdate?.Invoke(buildings);
                        }
                    }
                    break;

                case "pong":
                    // Server responded to our ping — connection is alive
                    break;

                default:
                    Debug.Log($"[WS] Unknown message type: {type}");
                    break;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[WS] Failed to parse message: {e.Message}\nRaw: {raw}");
        }
    }

    // ─── Platform-Specific Socket Implementation ─────────────────────

#if UNITY_WSA && !UNITY_EDITOR
    // ═══════════════════════════════════════════════════════════════════
    // UWP (HoloLens 2): Windows.Networking.Sockets.MessageWebSocket
    // ═══════════════════════════════════════════════════════════════════

    private IEnumerator ConnectPlatform(string url)
    {
        bool done = false;
        bool success = false;

        UnityEngine.WSA.Application.InvokeOnUIThread(async () =>
        {
            try
            {
                uwpSocket = new MessageWebSocket();
                uwpSocket.Control.MessageType = SocketMessageType.Utf8;
                uwpSocket.MessageReceived += UwpSocket_MessageReceived;
                uwpSocket.Closed += UwpSocket_Closed;

                await uwpSocket.ConnectAsync(new System.Uri(url));
                uwpWriter = new DataWriter(uwpSocket.OutputStream);
                success = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[WS-UWP] Connect failed: {e.Message}");
                success = false;
            }
            done = true;
        }, false);

        while (!done) yield return null;
        IsConnected = success;
    }

    private void UwpSocket_MessageReceived(MessageWebSocket sender, MessageWebSocketMessageReceivedEventArgs args)
    {
        try
        {
            using (var reader = args.GetDataReader())
            {
                reader.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;
                string msg = reader.ReadString(reader.UnconsumedBufferLength);
                lock (msgLock)
                {
                    incomingMessages.Enqueue(msg);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[WS-UWP] Receive error: {e.Message}");
        }
    }

    private void UwpSocket_Closed(IWebSocket sender, WebSocketClosedEventArgs args)
    {
        Debug.Log($"[WS-UWP] Closed: {args.Code} {args.Reason}");
        IsConnected = false;
        IsAuthenticated = false;
    }

    private IEnumerator SendAsync(string json)
    {
        if (uwpWriter == null) yield break;

        bool done = false;
        UnityEngine.WSA.Application.InvokeOnUIThread(async () =>
        {
            try
            {
                uwpWriter.WriteString(json);
                await uwpWriter.StoreAsync();
            }
            catch (Exception e)
            {
                Debug.LogError($"[WS-UWP] Send failed: {e.Message}");
                IsConnected = false;
            }
            done = true;
        }, false);

        while (!done) yield return null;
    }

    private void CloseSocket()
    {
        try
        {
            uwpWriter?.Dispose();
            uwpWriter = null;
            uwpSocket?.Dispose();
            uwpSocket = null;
        }
        catch { }
        IsConnected = false;
        IsAuthenticated = false;
    }

#else
    // ═══════════════════════════════════════════════════════════════════
    // Standalone / Editor: System.Net.WebSockets.ClientWebSocket
    // ═══════════════════════════════════════════════════════════════════

    private IEnumerator ConnectPlatform(string url)
    {
        wsClient = new ClientWebSocket();
        cts = new CancellationTokenSource();

        bool done = false;
        bool success = false;
        Exception connectException = null;

        // Connect on a thread pool thread to avoid blocking Update()
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                wsClient.ConnectAsync(new Uri(url), cts.Token).GetAwaiter().GetResult();
                success = true;
            }
            catch (Exception e)
            {
                connectException = e;
                success = false;
            }
            done = true;
        });

        while (!done) yield return null;

        if (success)
        {
            IsConnected = true;
            // Start background receive loop
            cts = new CancellationTokenSource();
            recvThread = new Thread(ReceiveLoop) { IsBackground = true };
            recvThread.Start();
        }
        else
        {
            string errMsg = connectException?.InnerException?.Message ?? connectException?.Message ?? "Unknown";
            Debug.LogError($"[WS] Connect failed: {errMsg}");
            IsConnected = false;
        }
    }

    private void ReceiveLoop()
    {
        byte[] buffer = new byte[8192];
        var sb = new StringBuilder();

        try
        {
            while (wsClient.State == WebSocketState.Open && !cts.IsCancellationRequested)
            {
                var segment = new ArraySegment<byte>(buffer);
                var result = wsClient.ReceiveAsync(segment, cts.Token).GetAwaiter().GetResult();

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    IsConnected = false;
                    break;
                }

                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                if (result.EndOfMessage)
                {
                    string msg = sb.ToString();
                    sb.Clear();
                    lock (msgLock)
                    {
                        incomingMessages.Enqueue(msg);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (!intentionalDisconnect)
                Debug.LogError($"[WS] Receive error: {e.Message}");
        }

        IsConnected = false;
    }

    private IEnumerator SendAsync(string json)
    {
        if (wsClient == null || wsClient.State != WebSocketState.Open) yield break;

        byte[] bytes = Encoding.UTF8.GetBytes(json);
        bool done = false;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                wsClient.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token)
                    .GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                Debug.LogError($"[WS] Send failed: {e.Message}");
                IsConnected = false;
            }
            done = true;
        });

        while (!done) yield return null;
    }

    private void CloseSocket()
    {
        try
        {
            cts?.Cancel();
            if (wsClient != null && wsClient.State == WebSocketState.Open)
            {
                wsClient.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
            wsClient?.Dispose();
        }
        catch { }

        wsClient = null;
        cts = null;
        IsConnected = false;
        IsAuthenticated = false;
    }

#endif
}
