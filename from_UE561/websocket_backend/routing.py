"""
WebSocket URL routing for Django Channels.

Maps WebSocket URLs to consumers. Include this in your project's
main ASGI routing configuration.

URL Pattern:
  ws(s)://backend.gisworld-tech.com/ws/buildings/{community_id}/

Example ASGI config (in your project's asgi.py):

    from channels.routing import ProtocolTypeRouter, URLRouter
    from channels.auth import AuthMiddlewareStack
    from websocket_backend.routing import websocket_urlpatterns

    application = ProtocolTypeRouter({
        "http": get_asgi_application(),
        "websocket": AuthMiddlewareStack(
            URLRouter(websocket_urlpatterns)
        ),
    })
"""

from django.urls import re_path
from . import consumers

websocket_urlpatterns = [
    re_path(
        r"ws/buildings/(?P<community_id>\w+)/$",
        consumers.BuildingEnergyConsumer.as_asgi()
    ),
]
