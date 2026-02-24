# ============================================================================
# Django Channels WebSocket Backend for Building Energy Real-Time Updates
# ============================================================================
#
# This directory contains the server-side WebSocket code that pairs with
# BuildingWebSocketClient.cs in Unity. Deploy these files into your Django
# project alongside the existing REST API.
#
# ARCHITECTURE:
#   Client (Unity/HoloLens 2)  ←──WebSocket──→  Django Channels (ASGI)
#                                                    ↑
#                                          Django Signals / REST API
#                                          (on building save/update)
#
# FILES:
#   consumers.py   – WebSocket consumer (handles connect, auth, send)
#   routing.py     – URL routing for WebSocket connections
#   signals.py     – Post-save signal to push updates to connected clients
#   setup_guide.md – Installation and configuration instructions
#
# PROTOCOL (matches BuildingWebSocketClient.cs):
#   1. Client connects:  wss://backend.gisworld-tech.com/ws/buildings/{community_id}/
#   2. Client authenticates: {"type": "authenticate", "token": "<JWT>"}
#   3. Server pushes:        {"type": "building_updated", "data": {...}}
#   4. Client ACKs:          {"type": "ack", "gml_id": "..."}
# ============================================================================
