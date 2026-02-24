# ============================================================================
# Django Channels WebSocket Setup Guide
# Building Energy Real-Time Updates
# ============================================================================
#
# This guide explains how to add WebSocket support to your existing Django
# REST Framework backend at https://backend.gisworld-tech.com
#
# The Unity client (BuildingWebSocketClient.cs) connects to:
#   wss://backend.gisworld-tech.com/ws/buildings/{community_id}/
#
# ============================================================================

## 1. Install Dependencies

```bash
pip install channels channels-redis daphne
```

- **channels** — Django Channels (WebSocket support for Django)
- **channels-redis** — Redis channel layer backend (for multi-process message passing)
- **daphne** — ASGI server that handles both HTTP and WebSocket


## 2. Redis Setup

Redis is used as the message broker between Django processes and WebSocket consumers.

```bash
# Ubuntu/Debian
sudo apt install redis-server
sudo systemctl enable redis-server

# Or use Docker
docker run -d --name redis -p 6379:6379 redis:7-alpine
```


## 3. Django Settings (settings.py)

```python
# Add to INSTALLED_APPS
INSTALLED_APPS = [
    "daphne",                    # Must be BEFORE django.contrib.staticfiles
    "channels",
    # ... your existing apps ...
    "websocket_backend",         # Copy the websocket_backend/ folder into your project
]

# ASGI application (replaces WSGI for WebSocket support)
ASGI_APPLICATION = "your_project.asgi.application"

# Channel Layer — uses Redis for cross-process communication
CHANNEL_LAYERS = {
    "default": {
        "BACKEND": "channels_redis.core.RedisChannelLayer",
        "CONFIG": {
            "hosts": [("127.0.0.1", 6379)],
            # Optional: connection pool for high-concurrency
            # "capacity": 1500,
            # "expiry": 60,
        },
    },
}
```


## 4. ASGI Configuration (asgi.py)

Replace your existing `asgi.py` with:

```python
"""
ASGI config — serves both HTTP (REST API) and WebSocket (real-time updates).
"""
import os
import django

os.environ.setdefault("DJANGO_SETTINGS_MODULE", "your_project.settings")
django.setup()

from channels.routing import ProtocolTypeRouter, URLRouter
from channels.auth import AuthMiddlewareStack
from django.core.asgi import get_asgi_application
from websocket_backend.routing import websocket_urlpatterns

application = ProtocolTypeRouter({
    # HTTP — your existing REST API (unchanged)
    "http": get_asgi_application(),

    # WebSocket — real-time building updates
    "websocket": AuthMiddlewareStack(
        URLRouter(websocket_urlpatterns)
    ),
})
```


## 5. Wire Up Signals (in your geospatial app)

In your geospatial app's `apps.py`:

```python
from django.apps import AppConfig

class GeospatialConfig(AppConfig):
    name = "geospatial"

    def ready(self):
        from django.db.models.signals import post_save, post_delete
        from websocket_backend.signals import (
            notify_building_updated,
            notify_building_deleted,
        )
        from .models import BuildingEnergy  # Your actual model

        post_save.connect(notify_building_updated, sender=BuildingEnergy)
        post_delete.connect(notify_building_deleted, sender=BuildingEnergy)
```


## 6. Adapt Signal Serialization

Edit `websocket_backend/signals.py` → `_serialize_building()` function to match
your actual Django model fields. The output must match the JSON structure that
`BuildingEnergyManager.ParseSingleBuilding()` expects in Unity.


## 7. Push Updates from REST API Views

When your API view handles a PUT/PATCH request to update a building, the
`post_save` signal fires automatically, which triggers `notify_building_updated()`,
which pushes to all connected WebSocket clients. **No changes needed to your views.**

For batch operations (e.g., simulation results), call `notify_bulk_update()` directly:

```python
from websocket_backend.signals import notify_bulk_update

class SimulationResultsView(APIView):
    def post(self, request):
        # ... process simulation results ...
        # Push all results to connected clients
        notify_bulk_update("08417008", serialized_buildings_list)
        return Response({"status": "ok"})
```


## 8. Deployment (Production)

### Option A: Daphne (simplest)
```bash
daphne -b 0.0.0.0 -p 8000 your_project.asgi:application
```

### Option B: Daphne behind Nginx (recommended)
```nginx
# nginx.conf
upstream channels-backend {
    server 127.0.0.1:8000;
}

server {
    listen 443 ssl;
    server_name backend.gisworld-tech.com;

    # SSL certificates
    ssl_certificate /etc/letsencrypt/live/backend.gisworld-tech.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/backend.gisworld-tech.com/privkey.pem;

    # HTTP (REST API) — proxy to Daphne
    location / {
        proxy_pass http://channels-backend;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    # WebSocket — proxy to Daphne with upgrade headers
    location /ws/ {
        proxy_pass http://channels-backend;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 86400;  # 24h — keeps WebSocket alive
    }
}
```


## 9. Testing

### Test with wscat (command line):
```bash
npm install -g wscat
wscat -c "wss://backend.gisworld-tech.com/ws/buildings/08417008/"
> {"type": "authenticate", "token": "YOUR_JWT_TOKEN_HERE"}
< {"type": "auth_ok"}
# Now update a building via REST API and watch for push notifications
```

### Test in Unity Editor:
1. Enable `enableWebSocket` on BuildingEnergyManager Inspector
2. Play the scene
3. Check Console for `[WS] ✅ Authenticated — real-time updates active`
4. Update a building via REST API or web UI
5. Unity should log `[WS] 📡 Building updated: DEBW_...` immediately


## 10. Architecture Summary

```
┌───────────────────────────────────────────────────────────────────┐
│                      Unity / HoloLens 2                          │
│                                                                   │
│  BuildingEnergyManager                                           │
│    ├── REST API (initial data load + auth)                       │
│    ├── BuildingWebSocketClient (primary real-time)               │
│    │     ├── Connects to wss://.../ws/buildings/{community_id}/  │
│    │     ├── Authenticates with JWT token                        │
│    │     └── Receives instant push on building change            │
│    └── Polling Fallback (only when WebSocket disconnected)       │
│          └── GET /buildings-energy/ every 120s                   │
└───────────────────────┬───────────────────────────────────────────┘
                        │ wss:// + https://
                        ▼
┌───────────────────────────────────────────────────────────────────┐
│              Django Backend (Daphne ASGI)                         │
│                                                                   │
│  HTTP Routes (REST API — unchanged)                              │
│    ├── POST /auth/token/        → JWT auth                       │
│    ├── GET  /buildings-energy/  → building data                  │
│    └── PUT  /buildings/{id}/    → update building                │
│                                        │                         │
│  WebSocket Routes (NEW)                │ post_save signal        │
│    └── /ws/buildings/{community_id}/   │                         │
│          ↑                             ▼                         │
│    BuildingEnergyConsumer ← Redis Channel Layer ← signals.py     │
└───────────────────────────────────────────────────────────────────┘
```


## WebSocket-Only Mode

If you want to use ONLY WebSocket (no polling at all), set in Unity Inspector:
- `enableWebSocket = true`
- `enableChangeDetection = false`

The system will rely entirely on WebSocket push. If the WebSocket disconnects,
no polling fallback will occur — the client simply waits for reconnection.

**Recommended: Keep polling enabled as a safety net** (`enableChangeDetection = true`).
The polling only fires when WebSocket is disconnected, so there's no overhead
when WebSocket is working correctly.
