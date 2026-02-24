"""
Django Channels WebSocket Consumer for Building Energy Updates.

Handles:
  - Client authentication via JWT token
  - Real-time building update push notifications
  - Bulk update broadcasts
  - Keepalive ping/pong
  - Per-community channel groups (only receive updates for your community)

Channel Groups:
  - "buildings_{community_id}" — all authenticated clients for a community

Usage:
  This consumer is routed via routing.py and auto-discovers connected clients.
  When a building is saved via the REST API, signals.py sends a channel_layer
  message to the appropriate group, which this consumer forwards to the client.
"""

import json
import logging
from channels.generic.websocket import AsyncJsonWebSocketConsumer
from channels.db import database_sync_to_async
from django.contrib.auth.models import AnonymousUser
from rest_framework_simplejwt.tokens import AccessToken
from rest_framework_simplejwt.exceptions import TokenError

logger = logging.getLogger(__name__)


class BuildingEnergyConsumer(AsyncJsonWebSocketConsumer):
    """
    WebSocket consumer for real-time building energy data updates.

    Protocol:
      → Client sends:  {"type": "authenticate", "token": "eyJ..."}
      ← Server sends:  {"type": "auth_ok"}  or  {"type": "auth_fail", "reason": "..."}

      ← Server pushes: {"type": "building_updated", "data": { full building JSON }}
      → Client sends:  {"type": "ack", "gml_id": "DEBW_..."}

      ← Server pushes: {"type": "bulk_update", "data": [ array of buildings ]}

      → Client sends:  {"type": "ping"}
      ← Server sends:  {"type": "pong"}
    """

    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.community_id = None
        self.group_name = None
        self.authenticated = False
        self.user = None

    async def connect(self):
        """Accept WebSocket connection and extract community_id from URL."""
        self.community_id = self.scope["url_route"]["kwargs"]["community_id"]
        self.group_name = f"buildings_{self.community_id}"

        # Accept the connection immediately (auth happens via message)
        await self.accept()
        logger.info(f"[WS] Client connected for community {self.community_id}")

    async def disconnect(self, close_code):
        """Leave the channel group on disconnect."""
        if self.group_name and self.authenticated:
            await self.channel_layer.group_discard(
                self.group_name, self.channel_name
            )
        logger.info(
            f"[WS] Client disconnected from community {self.community_id} "
            f"(code: {close_code})"
        )

    async def receive_json(self, content):
        """Handle incoming messages from the client."""
        msg_type = content.get("type", "")

        if msg_type == "authenticate":
            await self._handle_auth(content.get("token", ""))

        elif msg_type == "ping":
            await self.send_json({"type": "pong"})

        elif msg_type == "ack":
            # Client acknowledged a building update — log for debugging
            gml_id = content.get("gml_id", "unknown")
            logger.debug(f"[WS] ACK received for {gml_id}")

        else:
            logger.warning(f"[WS] Unknown message type: {msg_type}")

    # ─── Authentication ─────────────────────────────────────────────

    async def _handle_auth(self, token: str):
        """Validate JWT token and join the community channel group."""
        user = await self._validate_jwt(token)

        if user and not isinstance(user, AnonymousUser):
            self.authenticated = True
            self.user = user

            # Join the community-specific broadcast group
            await self.channel_layer.group_add(
                self.group_name, self.channel_name
            )

            await self.send_json({"type": "auth_ok"})
            logger.info(
                f"[WS] Authenticated user '{user.username}' "
                f"for community {self.community_id}"
            )
        else:
            await self.send_json({
                "type": "auth_fail",
                "reason": "Invalid or expired token"
            })
            logger.warning(f"[WS] Auth failed for community {self.community_id}")

    @database_sync_to_async
    def _validate_jwt(self, token: str):
        """
        Validate a JWT access token and return the associated Django User.
        Uses rest_framework_simplejwt (same as your REST API auth).
        """
        try:
            access_token = AccessToken(token)
            user_id = access_token["user_id"]
            from django.contrib.auth import get_user_model
            User = get_user_model()
            return User.objects.get(id=user_id)
        except (TokenError, Exception) as e:
            logger.error(f"[WS] JWT validation error: {e}")
            return None

    # ─── Channel Layer Handlers (called by signals.py) ──────────────

    async def building_updated(self, event):
        """
        Push a single building update to the WebSocket client.
        Called when channel_layer.group_send() is invoked from signals.py.
        """
        if not self.authenticated:
            return

        await self.send_json({
            "type": "building_updated",
            "data": event["data"]
        })

    async def building_deleted(self, event):
        """Push a building deletion notification to the client."""
        if not self.authenticated:
            return

        await self.send_json({
            "type": "building_deleted",
            "gml_id": event["gml_id"]
        })

    async def bulk_update(self, event):
        """Push a bulk update (multiple buildings) to the client."""
        if not self.authenticated:
            return

        await self.send_json({
            "type": "bulk_update",
            "data": event["data"]
        })
