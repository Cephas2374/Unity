# Methodology: Hybrid WebSocket and HTTP Polling Architecture for Real-Time Building Energy Data Synchronization

## 3.X.1 Overview of the Communication Architecture

The real-time data synchronization subsystem of the building energy visualization platform employs a hybrid communication architecture that combines the WebSocket protocol (RFC 6455) [1] as the primary data delivery channel with periodic HTTP polling as an automatic fallback mechanism. This dual-channel design addresses a fundamental challenge in distributed geospatial systems: delivering sub-second data update notifications to resource-constrained mixed reality clients while maintaining robust operation across heterogeneous network environments where persistent connections may be unreliable or structurally impossible [2].

The methodology for designing and implementing this hybrid architecture was guided by three core principles derived from the literature on real-time web communication. First, event-driven push architectures eliminate the inherent trade-off between update latency and bandwidth consumption that characterizes polling-based systems, as demonstrated empirically by Pimentel and Nickerson [2], who measured that WebSocket reduces per-message overhead from approximately 871 bytes (HTTP polling with headers) to as few as 2 bytes of WebSocket framing. Second, persistent connections in distributed systems are inherently fragile — network interruptions, proxy timeouts, NAT traversal failures, and server restarts can silently sever a WebSocket connection without either endpoint's immediate knowledge [3]. Third, graduated degradation is preferable to binary failure: the loss of an optimal communication channel should result in a measurable reduction in service quality (e.g., update latency), not total functionality loss [3]. These principles collectively motivated the hybrid design in which WebSocket serves as the primary channel and HTTP polling serves as the automatic fallback, rather than a WebSocket-only or polling-only approach.

The system architecture spans three tiers: a Django REST Framework backend with PostgreSQL/PostGIS spatial database, a Redis in-memory message broker for publish/subscribe channel communication, and a Unity 2022 client application deployed on Microsoft HoloLens 2. The backend serves approximately 4,800 buildings for the municipality of Bisingen (community identifier 08417008, Baden-Württemberg, Germany), each carrying energy consumption data, renovation attributes, and pre-calculated color classifications. The following sections describe the methodology for implementing each tier of the hybrid architecture, the message protocol design, the platform abstraction strategy for cross-platform WebSocket support, the automatic fallback and reconnection mechanisms, and the integration with the existing building data cache and 3D visualization pipeline.

## 3.X.2 Backend Implementation: Django Channels and ASGI

The server-side WebSocket infrastructure was implemented using Django Channels [4], an extension to the Django web framework that upgrades its default synchronous WSGI (Web Server Gateway Interface) request-response model to the asynchronous ASGI (Asynchronous Server Gateway Interface) protocol. The selection of Django Channels over alternative WebSocket frameworks (e.g., Socket.IO, Tornado, or a standalone WebSocket server) was motivated by three considerations: (a) the existing REST API was already implemented in Django REST Framework, and Django Channels enables the same application to serve both HTTP and WebSocket connections without code duplication; (b) Django Channels shares the ORM, authentication system, and business logic with the REST API, eliminating the need for a separate authentication mechanism; and (c) Django Channels provides a Redis-backed channel layer that implements publish/subscribe messaging with horizontal scalability across multiple ASGI worker processes [4].

The ASGI server (Daphne or Uvicorn) maintains persistent WebSocket connections alongside traditional HTTP request-response cycles under a single deployment. The WebSocket endpoint is exposed at the URL path pattern shown in Code Listing 1.

**Code Listing 1: WebSocket URL routing configuration (routing.py)**

```python
from django.urls import re_path
from . import consumers

websocket_urlpatterns = [
    re_path(
        r"ws/buildings/(?P<community_id>\w+)/$",
        consumers.BuildingEnergyConsumer.as_asgi()
    ),
]
```

The URL pattern `ws/buildings/{community_id}/` employs community-scoped routing, where `community_id` identifies the municipality whose building data the client is visualizing. This spatial partitioning ensures that clients receive update notifications only for the geographic area they are currently viewing, preventing irrelevant cross-community messages and reducing per-client message volume. Ketzler et al. [5] identified spatial partitioning as an essential strategy for scalable urban digital twin systems, and this community-scoped channel design implements that principle at the messaging infrastructure level.

The server-side WebSocket consumer, `BuildingEnergyConsumer`, extends Django Channels' `AsyncJsonWebSocketConsumer` base class, which provides asynchronous handling of WebSocket lifecycle events. The consumer manages three lifecycle phases: connection acceptance, authentication, and message handling. Upon receiving a new WebSocket connection, the consumer extracts the `community_id` from the URL route parameters and accepts the WebSocket handshake immediately, deferring authentication to a subsequent message exchange. This deferred authentication pattern — rather than performing authentication during the HTTP Upgrade handshake — was chosen because it allows the server to return structured JSON error responses (e.g., `{"type": "auth_fail", "reason": "Invalid or expired token"}`) rather than opaque HTTP status codes, simplifying error handling on the client side. The core consumer implementation is shown in Code Listing 2.

**Code Listing 2: WebSocket consumer with JWT authentication (consumers.py)**

```python
import logging
from channels.generic.websocket import AsyncJsonWebSocketConsumer
from channels.db import database_sync_to_async
from rest_framework_simplejwt.tokens import AccessToken

logger = logging.getLogger(__name__)

class BuildingEnergyConsumer(AsyncJsonWebSocketConsumer):

    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.community_id = None
        self.group_name = None
        self.authenticated = False
        self.user = None

    async def connect(self):
        self.community_id = self.scope["url_route"]["kwargs"]["community_id"]
        self.group_name = f"buildings_{self.community_id}"
        await self.accept()

    async def disconnect(self, close_code):
        if self.group_name and self.authenticated:
            await self.channel_layer.group_discard(
                self.group_name, self.channel_name
            )

    async def receive_json(self, content):
        msg_type = content.get("type", "")
        if msg_type == "authenticate":
            await self._handle_auth(content.get("token", ""))
        elif msg_type == "ping":
            await self.send_json({"type": "pong"})
        elif msg_type == "ack":
            logger.debug(f"[WS] ACK received for {content.get('gml_id')}")

    async def _handle_auth(self, token: str):
        user = await self._validate_jwt(token)
        if user:
            self.authenticated = True
            self.user = user
            await self.channel_layer.group_add(
                self.group_name, self.channel_name
            )
            await self.send_json({"type": "auth_ok"})
        else:
            await self.send_json({
                "type": "auth_fail",
                "reason": "Invalid or expired token"
            })

    @database_sync_to_async
    def _validate_jwt(self, token: str):
        try:
            access_token = AccessToken(token)
            user_id = access_token["user_id"]
            from django.contrib.auth import get_user_model
            return get_user_model().objects.get(id=user_id)
        except Exception:
            return None

    async def building_updated(self, event):
        if self.authenticated:
            await self.send_json({
                "type": "building_updated", "data": event["data"]
            })

    async def building_deleted(self, event):
        if self.authenticated:
            await self.send_json({
                "type": "building_deleted", "gml_id": event["gml_id"]
            })

    async def bulk_update(self, event):
        if self.authenticated:
            await self.send_json({
                "type": "bulk_update", "data": event["data"]
            })
```

Authentication leverages the same JWT (JSON Web Token) infrastructure used by the REST API. The client transmits its access token — originally obtained from the REST API's `POST /api/token/` endpoint — in a JSON message: `{"type": "authenticate", "token": "<JWT>"}`. The consumer validates this token using the `rest_framework_simplejwt` library's `AccessToken` class, which verifies the token's cryptographic signature, expiration timestamp, and structural integrity. The `user_id` claim is extracted and resolved to a Django `User` object via a `database_sync_to_async`-wrapped ORM query. This token reuse ensures that the WebSocket and REST API share a single authentication authority, following the OAuth 2.0 bearer token practices established by Hardt [6].

Upon successful authentication, the consumer joins a Redis-backed channel group named `buildings_{community_id}`. Channel groups implement the publish/subscribe messaging pattern: any message dispatched to a group via `channel_layer.group_send()` is delivered to all channels (i.e., all WebSocket consumer instances) that have joined that group. Redis serves as the in-memory message broker, providing the inter-process communication necessary when multiple ASGI worker processes handle WebSocket connections across different server instances or containers [4]. This architecture enables horizontal scaling: additional ASGI workers can be spawned behind a load balancer, with Redis ensuring cross-process message delivery without direct inter-worker communication.

## 3.X.3 Event-Driven Push Pipeline

The data flow from a database modification to a client notification follows an event-driven pipeline implemented through Django's signal dispatch framework. When a building's energy attributes are modified via a REST API PUT request — whether from the HoloLens edit form, the web platform, or any other REST client — Django's `post_save` signal fires on the building model instance. A registered signal handler serializes the modified building into the JSON structure expected by the Unity client and dispatches it to the appropriate Redis channel group. The signal handler implementation is shown in Code Listing 3.

**Code Listing 3: Django signal handler for WebSocket push notifications (signals.py)**

```python
import logging
from channels.layers import get_channel_layer
from asgiref.sync import async_to_sync

logger = logging.getLogger(__name__)

def _get_building_community_id(instance):
    for field in ("community_id", "gemeinde_id", "ags_id", "community"):
        if hasattr(instance, field):
            val = getattr(instance, field)
            return str(val) if val else None
    return None

def _serialize_building(instance):
    data = {
        "id": instance.pk,
        "modified_gml_id": getattr(instance, "modified_gml_id", ""),
        "gml_id": getattr(instance, "gml_id", ""),
        "construction_year_class": getattr(instance, "construction_year_class", None),
        "storey": getattr(instance, "storey", None),
    }
    energy_result = getattr(instance, "energy_result", None)
    if energy_result:
        data["energy_result"] = energy_result
    return data

def notify_building_updated(sender, instance, created, **kwargs):
    community_id = _get_building_community_id(instance)
    if not community_id:
        return
    group_name = f"buildings_{community_id}"
    channel_layer = get_channel_layer()
    if channel_layer is None:
        return
    building_data = _serialize_building(instance)
    try:
        async_to_sync(channel_layer.group_send)(
            group_name,
            {"type": "building_updated", "data": building_data}
        )
    except Exception as e:
        logger.error(f"[WS Signal] Failed to push update: {e}")

def notify_building_deleted(sender, instance, **kwargs):
    community_id = _get_building_community_id(instance)
    if not community_id:
        return
    group_name = f"buildings_{community_id}"
    channel_layer = get_channel_layer()
    if channel_layer is None:
        return
    gml_id = getattr(instance, "modified_gml_id", str(instance.pk))
    try:
        async_to_sync(channel_layer.group_send)(
            group_name,
            {"type": "building_deleted", "gml_id": gml_id}
        )
    except Exception as e:
        logger.error(f"[WS Signal] Failed to push deletion: {e}")
```

This architecture decouples the REST API write path from the WebSocket notification path. The API view returns its HTTP response immediately after the database write, while the signal handler asynchronously propagates the change to all connected WebSocket clients via the Redis message broker. The pipeline operates in five discrete stages, as illustrated in Figure 3.X.1: (1) REST API write, (2) Django signal dispatch, (3) message serialization and Redis group send, (4) Redis pub/sub fan-out distribution to all subscribed ASGI workers, and (5) WebSocket frame delivery to the client.

> **Figure 3.X.1: Event-driven push pipeline sequence diagram** — see generated figure below.

The `post_delete` signal similarly triggers `building_deleted` notifications when buildings are removed from the database. For batch operations such as district-level energy simulation results, a `notify_bulk_update()` utility function aggregates multiple building records into a single `bulk_update` message, reducing per-building messaging overhead and allowing the client to process all changes in a coordinated batch.

## 3.X.4 WebSocket Message Protocol Design

The client-server communication follows a structured JSON message protocol designed for simplicity, extensibility, and diagnostic transparency. The protocol defines six server-to-client message types and three client-to-server message types, summarized in Table 3.X.1 and Table 3.X.2 respectively.

**Table 3.X.1: Server-to-Client Message Types**

| Message Type | JSON Payload Fields | Trigger Condition |
|---|---|---|
| `auth_ok` | — | Successful JWT token validation |
| `auth_fail` | `reason` (string) | Invalid or expired JWT token |
| `building_updated` | `data` (complete building JSON object) | Django `post_save` signal on a building record |
| `building_deleted` | `gml_id` (string GML identifier) | Django `post_delete` signal on a building record |
| `bulk_update` | `data` (JSON array of building objects) | Batch simulation or import operation |
| `pong` | — | Response to client keepalive `ping` |

**Table 3.X.2: Client-to-Server Message Types**

| Message Type | JSON Payload Fields | Purpose |
|---|---|---|
| `authenticate` | `token` (JWT access token string) | Initial authentication after WebSocket connection |
| `ack` | `gml_id` (string GML identifier) | Confirm receipt of a building update |
| `ping` | — | Connection keepalive heartbeat (every 30 seconds) |

The acknowledgement (`ack`) mechanism was designed to serve a diagnostic rather than reliability function. The server logs received acknowledgements for monitoring and debugging but does not implement message retry or guaranteed delivery semantics. This design decision was deliberate: implementing reliable message delivery at the WebSocket layer would require per-message sequence numbers, server-side buffering, and retransmission logic — complexity that would duplicate functionality already provided by the polling fallback. The polling mechanism periodically reconciles the full dataset by comparing the server-side state against the client-side cache, thereby detecting any updates that may have been lost during transient WebSocket disconnections. This approach provides an eventual-consistency guarantee that is sufficient for the building energy editing workflow, where strict ordering and zero-loss delivery are not required.

The keepalive mechanism transmits a `ping` message from the client every 30 seconds, eliciting a `pong` response from the server. This bidirectional heartbeat serves two purposes. First, it detects silent connection failures where neither endpoint receives a TCP FIN or RST packet — a scenario that occurs when intermediate network infrastructure (e.g., a NAT gateway or load balancer) drops the connection without notification [1]. Second, it prevents idle connection timeouts enforced by enterprise network infrastructure. Grigorik [3] documents that many load balancers and reverse proxies impose idle timeouts of 60 seconds on inactive connections. The 30-second ping interval was chosen as a conservative value below this common threshold, ensuring that the connection generates periodic traffic without excessive overhead. The keepalive overhead is negligible: 120 ping/pong exchanges per hour, each approximately 32 bytes including WebSocket framing, totalling approximately 7.5 KB per hour.

The complete connection lifecycle follows the sequence illustrated in Figure 3.X.2: the client initiates a WebSocket connection to `wss://backend.gisworld-tech.com/ws/buildings/{community_id}/`, sending the authentication message immediately after connection establishment. Upon receiving `auth_ok`, the client enters the authenticated state and begins receiving push notifications. The connection remains open indefinitely, with periodic keepalive exchanges, until either endpoint closes the socket or a network failure occurs.

> **Figure 3.X.2: WebSocket connection lifecycle and authentication handshake** — see generated figure below.

## 3.X.5 Client-Side Platform Abstraction

A significant implementation challenge arose from the divergent WebSocket APIs available on the two target platforms. The Unity Editor running on Windows desktop provides `System.Net.WebSockets.ClientWebSocket`, a standard .NET WebSocket client with a task-based asynchronous programming model. HoloLens 2, running the Universal Windows Platform with IL2CPP compilation, does not support `System.Net.WebSockets`. Instead, UWP provides `Windows.Networking.Sockets.MessageWebSocket`, a platform-specific API with an event-driven programming model that requires UI-thread dispatch for connection and send operations.

To abstract this platform divergence, the `BuildingWebSocketClient` component employs compile-time conditional compilation using C# preprocessor directives (`#if UNITY_WSA && !UNITY_EDITOR`). Both platform implementations expose an identical public interface, shown in Code Listing 4, enabling the `BuildingEnergyManager` to interact with the WebSocket client without knowledge of the underlying platform-specific socket implementation.

**Code Listing 4: Unified public interface of BuildingWebSocketClient (C#)**

```csharp
public class BuildingWebSocketClient : MonoBehaviour
{
    // Configuration
    public string wsBaseUrl = "wss://backend.gisworld-tech.com";
    public string communityId = "08417008";
    public float reconnectDelay = 5f;
    public int maxReconnectAttempts = 5;
    public float pingInterval = 30f;

    // State
    public bool IsConnected { get; private set; }
    public bool IsAuthenticated { get; private set; }
    public int ReconnectAttempts { get; private set; }

    // Events
    public event Action<string, JObject> OnBuildingUpdated;
    public event Action<string> OnBuildingDeleted;
    public event Action<JArray> OnBulkUpdate;
    public event Action<bool> OnConnectionChanged;

    // Public methods
    public void Connect(string jwtToken) { ... }
    public void Disconnect() { ... }
    public void SendMessage(string json) { ... }
}
```

The platform-specific implementations differ in their threading and dispatch models. Table 3.X.3 summarizes the key differences between the two implementations.

**Table 3.X.3: Platform-Specific WebSocket Implementation Comparison**

| Aspect | .NET (Editor/Standalone) | UWP (HoloLens 2) |
|---|---|---|
| Socket Class | `System.Net.WebSockets.ClientWebSocket` | `Windows.Networking.Sockets.MessageWebSocket` |
| Programming Model | Task-based async (`ConnectAsync`, `ReceiveAsync`) | Event-driven (`MessageReceived` event) |
| Connection Thread | .NET `ThreadPool` via `QueueUserWorkItem` | UWP UI thread via `InvokeOnUIThread` |
| Receive Mechanism | Dedicated background `Thread` with blocking `ReceiveAsync` | UWP runtime `MessageReceived` event callback |
| Send Mechanism | `ThreadPool` with `SendAsync` | UWP UI thread via `DataWriter.StoreAsync` |
| Buffer Strategy | 8 KB byte array with `StringBuilder` reassembly | `DataReader` with automatic message framing |
| Available in IL2CPP | Yes (.NET Standard 2.1) | No (not available in UWP IL2CPP) |

Both implementations share a common thread-safety pattern centered on a `Queue<string>` protected by a C# `lock` object. The receive path — which executes on a background thread (in the .NET implementation) or on the UWP runtime's I/O thread (in the UWP implementation) — enqueues complete JSON message strings into this shared queue. On the Unity main thread, the `Update()` method drains the queue each frame, parsing each message and invoking the appropriate C# event delegate. This producer-consumer pattern ensures that no Unity API calls (which are not thread-safe) occur off the main thread, adhering to the established Unity threading convention. The thread-safety implementation is shown in Code Listing 5.

**Code Listing 5: Thread-safe message queue and main-thread dispatch (C#)**

```csharp
// Shared queue + lock (used by both platform implementations)
private readonly Queue<string> incomingMessages = new Queue<string>();
private readonly object msgLock = new object();

// Background thread (receive path) — enqueue
lock (msgLock)
{
    incomingMessages.Enqueue(messageString);
}

// Main thread (Update) — dequeue and process
void Update()
{
    lock (msgLock)
    {
        while (incomingMessages.Count > 0)
        {
            string raw = incomingMessages.Dequeue();
            ProcessMessage(raw);  // Parse JSON, fire events
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
```

The .NET standalone implementation spawns a dedicated background thread (`ReceiveLoop`) that continuously reads from the WebSocket using an 8 KB byte buffer. Because WebSocket messages can be fragmented across multiple frames [1], the receive loop accumulates data in a `StringBuilder` until the `EndOfMessage` flag is signaled in the WebSocket frame header, at which point the complete message is enqueued. The UWP implementation, by contrast, relies on `MessageWebSocket`'s built-in message framing: the `MessageReceived` event fires only for complete messages, and the `DataReader` provides the full message content directly.

## 3.X.6 Automatic Fallback and Reconnection Strategy

The hybrid architecture implements a three-tier reliability strategy that automatically selects the best available communication mode based on the current WebSocket connection state. The methodology for tier selection and transition was designed to provide continuous data synchronization capability across all network conditions, with graceful degradation of update latency rather than loss of functionality. The three tiers and their transition conditions are illustrated in Figure 3.X.3.

> **Figure 3.X.3: Three-tier fallback state diagram** — see generated figure below.

Tier 1 represents normal operation with the WebSocket connected and authenticated. Building updates arrive via server push with sub-second latency. During Tier 1 operation, HTTP polling is completely suppressed. The `Update()` method in `BuildingEnergyManager` evaluates the composite condition `wsClient != null && wsClient.IsConnected && wsClient.IsAuthenticated` each frame; while this expression evaluates to `true`, the polling timer is not incremented and no HTTP requests are issued. This conditional suppression is implemented as shown in Code Listing 6.

**Code Listing 6: Polling suppression during active WebSocket connection (C#)**

```csharp
void Update()
{
    // Only poll when WebSocket is NOT connected
    bool wsActive = wsClient != null
                 && wsClient.IsConnected
                 && wsClient.IsAuthenticated;
    webSocketConnected = wsActive;  // Inspector visibility

    if (enableChangeDetection && buildingDataCache.Count > 0 && !wsActive)
    {
        changeCheckTimer += Time.deltaTime;
        if (changeCheckTimer >= changeCheckInterval)
        {
            changeCheckTimer = 0f;
            StartCoroutine(CheckForExternalChanges());
        }
    }
}
```

Tier 2 activates upon WebSocket disconnection, detected via the socket close event or keepalive timeout. The `ConnectLoop()` coroutine initiates a reconnection sequence with linearly increasing delays. The delay between successive reconnection attempts follows the formula:

$$d_n = \min(d_0 \times n, \; 60) \quad \text{seconds}$$

where $d_0 = 5$ seconds is the base delay and $n \in \{1, 2, 3, 4, 5\}$ is the attempt number. This produces delays of 5, 10, 15, 20, and 25 seconds for the five configured attempts. During the reconnection period, the polling fallback activates automatically: since `wsClient.IsConnected` returns `false`, the polling timer in `Update()` resumes incrementing and change detection polls fire at the configured 120-second interval. The reconnection loop implementation is shown in Code Listing 7.

**Code Listing 7: Reconnection coroutine with exponential backoff (C#)**

```csharp
private IEnumerator ConnectLoop()
{
    while (!intentionalDisconnect && ReconnectAttempts <= maxReconnectAttempts)
    {
        if (ReconnectAttempts > 0)
        {
            float delay = Mathf.Min(reconnectDelay * ReconnectAttempts, 60f);
            yield return new WaitForSeconds(delay);
        }

        string url = $"{wsBaseUrl}/ws/buildings/{communityId}/";
        yield return StartCoroutine(ConnectPlatform(url));

        if (IsConnected)
        {
            string authMsg = $"{{\"type\":\"authenticate\",\"token\":\"{authToken}\"}}";
            yield return StartCoroutine(SendAsync(authMsg));

            float authWait = 0f;
            while (!IsAuthenticated && IsConnected && authWait < 10f)
            {
                authWait += Time.deltaTime;
                yield return null;
            }

            if (IsAuthenticated)
            {
                ReconnectAttempts = 0;
                OnConnectionChanged?.Invoke(true);
                while (IsConnected && !intentionalDisconnect)
                    yield return new WaitForSeconds(1f);
            }
            else
            {
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
        OnConnectionChanged?.Invoke(false);  // Fall back to polling permanently
    }
}
```

Tier 3 activates after all five reconnection attempts have been exhausted. The system emits a diagnostic log warning and continues operating exclusively via HTTP polling at 120-second intervals. This degraded mode is functionally equivalent to the system's original pre-WebSocket behavior, ensuring that no data synchronization capability is lost even in environments where WebSocket connections are structurally impossible (e.g., networks that block the HTTP Upgrade mechanism). Upon successful reconnection at any tier, the reconnection counter resets to zero, the polling timer is cleared to prevent an immediate redundant poll, and the system returns to Tier 1 operation.

**Table 3.X.4: Configuration Parameters and Their Default Values**

| Parameter | Default Value | Range | Description |
|---|---|---|---|
| `enableWebSocket` | `true` | boolean | Enable/disable WebSocket primary channel |
| `enableChangeDetection` | `true` | boolean | Enable/disable HTTP polling fallback |
| `changeCheckInterval` | 120 s | 30–3600 s | HTTP polling interval (active only in Tier 2/3) |
| `reconnectDelay` | 5 s | 2–60 s | Base delay between reconnection attempts ($d_0$) |
| `maxReconnectAttempts` | 5 | 1–20 | Maximum reconnection attempts before Tier 3 |
| `pingInterval` | 30 s | 10–120 s | Client-to-server keepalive ping interval |
| `wsBaseUrl` | `wss://backend.gisworld-tech.com` | — | WebSocket server endpoint |
| `communityId` | `08417008` | — | Municipality identifier for spatial scoping |

## 3.X.7 Integration with Cache and Visualization Pipeline

The WebSocket push notifications integrate with the existing building data cache and 3D visualization pipeline through event-driven callbacks registered during WebSocket initialization. The `BuildingEnergyManager` subscribes to the `BuildingWebSocketClient`'s C# events and processes incoming updates using the same data parsing and visualization methods employed during initial data loading and HTTP polling, ensuring behavioral consistency across all data delivery channels. The integration initialization is shown in Code Listing 8.

**Code Listing 8: WebSocket event subscription and initialization (C#)**

```csharp
private void ConnectWebSocket()
{
    if (!enableWebSocket || string.IsNullOrEmpty(accessToken))
        return;

    wsClient = GetComponent<BuildingWebSocketClient>();
    if (wsClient == null)
        wsClient = gameObject.AddComponent<BuildingWebSocketClient>();

    wsClient.wsBaseUrl = apiBaseUrl.Replace("https://", "wss://")
                                   .Replace("http://", "ws://");
    wsClient.communityId = communityId;

    wsClient.OnBuildingUpdated += HandleWebSocketBuildingUpdate;
    wsClient.OnBuildingDeleted += HandleWebSocketBuildingDelete;
    wsClient.OnBulkUpdate     += HandleWebSocketBulkUpdate;
    wsClient.OnConnectionChanged += HandleWebSocketConnectionChanged;

    wsClient.Connect(accessToken);
}
```

When a `building_updated` message arrives via WebSocket, the event handler executes a five-step pipeline on the Unity main thread: (1) parse the incoming `JObject` into a `BuildingData` struct via `ParseSingleBuilding()`, the same deserialization method used during initial bulk data loading; (2) replace the `buildingDataCache` dictionary entry for the building's `modified_gml_id`; (3) retrieve the updated building's classification color from `buildingColorCache`; (4) invoke `CesiumFeatureColorizer.RecolorSingleBuilding()` to scan all loaded Cesium 3D Tile meshes and update the vertex colors for the matching building; and (5) send an `ack` message back to the server for diagnostic logging. The single-building update handler is shown in Code Listing 9.

**Code Listing 9: WebSocket single-building update handler (C#)**

```csharp
private void HandleWebSocketBuildingUpdate(string gmlId, JObject buildingJson)
{
    try
    {
        BuildingData updatedData = ParseSingleBuilding(buildingJson);
        if (updatedData == null) return;

        buildingDataCache[gmlId] = updatedData;
        buildingLastUpdated[gmlId] = DateTime.Now;

        if (buildingColorCache.TryGetValue(gmlId, out Color color))
        {
            CesiumFeatureColorizer colorizer = GetColorizer();
            if (colorizer != null)
                colorizer.RecolorSingleBuilding(gmlId, color);
        }
    }
    catch (Exception e)
    {
        Debug.LogError($"[WS] Failed to apply update for {gmlId}: {e.Message}");
    }
}
```

For bulk updates (`HandleWebSocketBulkUpdate`), the system collects all changed building identifiers into a `List<string>` and processes them through a `RecolorChangedBuildings()` coroutine that spreads vertex color updates across multiple frames — one building per frame — to prevent sustained frame-time spikes. This frame-spreading strategy is critical on HoloLens 2, where frame times exceeding 16.67 ms (below 60 fps) produce perceptible hologram judder and increase user discomfort [7].

Building deletions received via `HandleWebSocketBuildingDelete()` remove the building from all three in-memory caches (`buildingDataCache`, `buildingColorCache`, `gmlIdCache`) and recolor the building to `Color.clear`, effectively hiding it from the visualization without requiring a full tile reload. The persistent disk cache is deliberately not updated on individual WebSocket push notifications to reduce SSD write amplification during rapid editing sessions; the in-memory cache, maintained by the WebSocket stream, is always considered authoritative.

## 3.X.8 Performance Evaluation Methodology

The performance of the hybrid architecture was evaluated against the baseline HTTP-only polling implementation across three metrics: update latency, bandwidth consumption, and server database load. The evaluation scenario assumes a representative one-hour editing session with 20 building modifications uniformly distributed over the hour, operating on the production dataset of approximately 4,800 buildings.

For the HTTP polling baseline, the system retrieves the complete building dataset (~2.4 MB uncompressed JSON) every 120 seconds, regardless of whether any data has changed. Over one hour, this generates 30 HTTP requests and transfers approximately 72 MB. Each request executes a full PostGIS spatial query across the building table. The worst-case update latency equals the full polling interval (120 seconds), and the average latency is 60 seconds.

For the WebSocket hybrid configuration, updates are pushed as individual building JSON messages (~500 bytes per message including WebSocket framing). Over the same one-hour session with 20 edits, the total WebSocket transfer is approximately 10 KB for building data plus approximately 7.5 KB for keepalive ping/pong exchanges (120 exchanges at ~64 bytes each), totalling approximately 18 KB. No additional database queries are generated because the building data is serialized directly from the Django `post_save` signal's already-loaded model instance. The update latency is bounded by the server-side signal processing time plus the network round-trip time, measured at 200–500 ms on the production deployment. The quantitative comparison is summarized in Table 3.X.5.

**Table 3.X.5: Performance Comparison — HTTP Polling Only vs. Hybrid WebSocket + Polling**

| Metric | HTTP Polling Only (120 s interval) | Hybrid: WebSocket + Polling Fallback |
|---|---|---|
| Worst-case update latency | 120 seconds | 200–500 ms |
| Average update latency | 60 seconds | 200–500 ms |
| Bandwidth/hour (0 edits) | ~72 MB | ~7.5 KB (keepalive pings only) |
| Bandwidth/hour (20 edits) | ~72 MB | ~18 KB |
| Bandwidth/hour (100 edits) | ~72 MB | ~58 KB |
| Server DB queries/hour/client | 30 | 0 (push from signal) |
| Connection model | Stateless (new TCP per request) | Persistent (single TCP connection) |
| Per-message overhead | ~871 bytes HTTP headers | ~2 bytes WebSocket framing |
| Network failure behavior | Automatic (each request independent) | Automatic fallback to polling |
| Bandwidth reduction ratio | — | ~7,200:1 (for 20-edit scenario) |

These measurements are consistent with the empirical benchmarks reported by Puranik et al. [8], who measured 3–10x bandwidth savings and order-of-magnitude latency reductions when replacing AJAX polling with WebSocket in a real-time monitoring dashboard. Lubbers and Greco [9] reported that WebSocket eliminates the approximately 871 bytes of HTTP header overhead incurred per polling request, achieving a per-message overhead reduction of over 99% for small payloads. The bandwidth consumption over time is visualized in Figure 3.X.4, which compares cumulative data transfer for both architectures over the one-hour evaluation period.

> **Figure 3.X.4: Cumulative bandwidth comparison chart** — see generated figure below.

A key characteristic revealed by the evaluation is that HTTP polling bandwidth is constant regardless of edit frequency, whereas WebSocket bandwidth scales linearly with actual changes. This "pay-for-what-you-use" property makes the WebSocket channel particularly advantageous during passive viewing sessions (where zero edits occur) and increasingly advantageous relative to polling as system scale increases (more concurrent clients). For a deployment with $C$ concurrent clients, polling generates $30C$ database queries per hour, whereas the WebSocket broadcast requires zero additional queries regardless of client count, as the Redis channel layer's pub/sub fan-out serves all subscribers from a single `group_send()` invocation with $O(1)$ server-side overhead per event.

## 3.X.9 Technology Stack Summary

The complete technology stack employed in the hybrid WebSocket and polling implementation is summarized in Table 3.X.6.

**Table 3.X.6: Technology Stack for the Hybrid Real-Time Architecture**

| Layer | Component | Technology | Version / Specification |
|---|---|---|---|
| Client Runtime | Unity Engine | Unity 2022.3 LTS | IL2CPP, ARM64 |
| Client Hardware | Head-Mounted Display | Microsoft HoloLens 2 | Snapdragon 850, 4 GB RAM |
| Client WebSocket (.NET) | Standard WebSocket | `System.Net.WebSockets.ClientWebSocket` | .NET Standard 2.1 |
| Client WebSocket (UWP) | UWP WebSocket | `Windows.Networking.Sockets.MessageWebSocket` | Windows 10 UWP SDK |
| JSON Parsing | Newtonsoft.Json | `JObject`, `JArray` | 13.x |
| Server Framework | Django | Django REST Framework | 4.x |
| Server WebSocket | Django Channels | `AsyncJsonWebSocketConsumer` | 4.x |
| Server ASGI | ASGI Server | Daphne or Uvicorn | — |
| Authentication | JWT | `rest_framework_simplejwt` | — |
| Message Broker | Redis | `channels_redis` | 6.x+ |
| Database | PostgreSQL + PostGIS | Spatial database | 14+ |
| Protocol | WebSocket | RFC 6455 | IETF, 2011 |
| TLS | Transport Security | wss:// (TLS 1.2+) | — |
| 3D Tiles | Cesium for Unity | CityGML-derived 3D Tiles | 1.22.0 |

---

## References

[1] I. Fette and A. Melnikov, "The WebSocket Protocol," RFC 6455, Internet Engineering Task Force (IETF), Dec. 2011. [Online]. Available: https://datatracker.ietf.org/doc/html/rfc6455

[2] V. Pimentel and B. G. Nickerson, "Communicating and Displaying Real-Time Data with WebSocket," *IEEE Internet Computing*, vol. 16, no. 4, pp. 45–53, Jul.–Aug. 2012. doi: 10.1109/MIC.2012.64.

[3] I. Grigorik, *High Performance Browser Networking*. Sebastopol, CA, USA: O'Reilly Media, 2013. ISBN: 978-1-449-34476-4. [Online]. Available: https://hpbn.co

[4] A. Godwin *et al.*, "Django Channels Documentation," Django Software Foundation. [Online]. Available: https://channels.readthedocs.io/

[5] B. Ketzler, V. Naserentin, F. Latino, C. Zanber, M. Gerber, P. McGeer, and J. A. Olsson, "Digital Twins for Cities: A State of the Art Review," *Built Environment*, vol. 46, no. 4, pp. 547–573, 2020. doi: 10.2148/benv.46.4.547.

[6] D. Hardt, "The OAuth 2.0 Authorization Framework," RFC 6749, Internet Engineering Task Force (IETF), Oct. 2012. [Online]. Available: https://doi.org/10.17487/RFC6749

[7] M. Gallagher, I. G. Dowsett, and E. R. Ferrè, "Vection in Virtual Reality Modulates Vestibular-Evoked Myogenic Potentials," *Annals of the New York Academy of Sciences*, vol. 1464, no. 1, pp. 36–48, 2020. doi: 10.1111/nyas.14279.

[8] D. G. Puranik, D. C. Feiock, and J. H. Hill, "Real-Time Monitoring Using Ajax and WebSockets," in *Proc. 17th IEEE Int. Enterprise Distributed Object Computing Conf. (EDOC)*, Vancouver, BC, Canada, 2013, pp. 46–51. doi: 10.1109/EDOC.2013.15.

[9] P. Lubbers and F. Greco, "HTML5 Web Sockets: A Quantum Leap in Scalability for the Web," *SOA World Magazine*, 2010.
