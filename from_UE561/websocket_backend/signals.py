"""
Django Signals for pushing building updates to WebSocket clients.

When a building's energy data is saved via the REST API (or admin), this
signal handler automatically broadcasts the change to all connected
WebSocket clients in the same community group.

Setup:
  In your app's apps.py ready() method (or import this module in __init__.py):
      from websocket_backend import signals  # noqa: F401

  Or in your AppConfig:
      def ready(self):
          import websocket_backend.signals  # noqa: F401
"""

import logging
from django.db.models.signals import post_save, post_delete
from django.dispatch import receiver
from channels.layers import get_channel_layer
from asgiref.sync import async_to_sync

logger = logging.getLogger(__name__)


def _get_building_community_id(instance):
    """
    Extract the community_id from a building model instance.
    Adjust the field name to match your actual Django model.
    Common field names: community_id, gemeinde_id, ags_id
    """
    # Try common field names — adapt to your actual model
    for field in ("community_id", "gemeinde_id", "ags_id", "community"):
        if hasattr(instance, field):
            val = getattr(instance, field)
            return str(val) if val else None
    return None


def _serialize_building(instance):
    """
    Serialize a building model instance to the JSON format expected by Unity.
    This must match the structure returned by your REST API's buildings-energy endpoint.

    IMPORTANT: Adapt this to your actual model fields. The structure below
    matches what BuildingEnergyManager.ParseSingleBuilding() expects.
    """
    data = {
        "id": instance.pk,
        "modified_gml_id": getattr(instance, "modified_gml_id", ""),
        "gml_id": getattr(instance, "gml_id", ""),
        "construction_year_class": getattr(instance, "construction_year_class", None),
        "storey": getattr(instance, "storey", None),
        "energy_consumption": getattr(instance, "energy_consumption", None),
        "begin_heating_system_type_1": getattr(instance, "begin_heating_system_type_1", None),
        "end_heating_system_type_1": getattr(instance, "end_heating_system_type_1", None),
        "begin_window_type_1": getattr(instance, "begin_window_type_1", None),
        "end_window_type_1": getattr(instance, "end_window_type_1", None),
        "begin_wall_type_1": getattr(instance, "begin_wall_type_1", None),
        "end_wall_type_1": getattr(instance, "end_wall_type_1", None),
        "begin_roof_type_1": getattr(instance, "begin_roof_type_1", None),
        "end_roof_type_1": getattr(instance, "end_roof_type_1", None),
        "begin_ceiling_type_1": getattr(instance, "begin_ceiling_type_1", None),
        "end_ceiling_type_1": getattr(instance, "end_ceiling_type_1", None),
    }

    # Include energy_result if available (matches API structure)
    energy_result = _get_energy_result(instance)
    if energy_result:
        data["energy_result"] = energy_result

    return data


def _get_energy_result(instance):
    """
    Build the energy_result nested structure from the model.
    Adapt to your actual model/related fields.

    Expected structure (what Unity parses):
    {
        "begin": {
            "result": {
                "energy_demand_specific": {"value": 120},
                "co2_from_energy_demand": {"value": 5400}
            },
            "color": {
                "energy_demand_specific_color": "#FF8C00"
            }
        },
        "end": {
            "result": {
                "energy_demand_specific": {"value": 80},
                "co2_from_energy_demand": {"value": 3200}
            },
            "color": {
                "energy_demand_specific_color": "#4CAF50"
            }
        }
    }
    """
    # This is a placeholder — adapt to your actual model relationships.
    # If energy_result is stored as a JSONField:
    if hasattr(instance, "energy_result") and instance.energy_result:
        return instance.energy_result

    # If it's computed from related models, build it here:
    # begin_result = instance.begin_simulation_result  # related object
    # end_result = instance.end_simulation_result
    # ...
    return None


# ─── Signal Handlers ──────────────────────────────────────────────

# IMPORTANT: Replace 'YourBuildingModel' with your actual Django model class.
# Example: from geospatial.models import BuildingEnergy
# @receiver(post_save, sender=BuildingEnergy)

# For now, we use a generic approach that you wire up to your model:

def notify_building_updated(sender, instance, created, **kwargs):
    """
    Post-save signal handler: broadcasts building update to WebSocket group.
    Wire this to your building model's post_save signal.

    Usage in your app's ready():
        post_save.connect(
            signals.notify_building_updated,
            sender=BuildingEnergy
        )
    """
    community_id = _get_building_community_id(instance)
    if not community_id:
        logger.warning(f"[WS Signal] No community_id for building {instance.pk}")
        return

    group_name = f"buildings_{community_id}"
    channel_layer = get_channel_layer()

    if channel_layer is None:
        logger.warning("[WS Signal] No channel layer configured")
        return

    building_data = _serialize_building(instance)

    try:
        async_to_sync(channel_layer.group_send)(
            group_name,
            {
                "type": "building_updated",  # Maps to consumer method name
                "data": building_data,
            }
        )
        logger.info(
            f"[WS Signal] Pushed update for {building_data.get('modified_gml_id', '?')} "
            f"to group {group_name}"
        )
    except Exception as e:
        logger.error(f"[WS Signal] Failed to push update: {e}")


def notify_building_deleted(sender, instance, **kwargs):
    """
    Post-delete signal handler: broadcasts building deletion to WebSocket group.
    Wire this to your building model's post_delete signal.
    """
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
            {
                "type": "building_deleted",
                "gml_id": gml_id,
            }
        )
        logger.info(f"[WS Signal] Pushed deletion for {gml_id} to group {group_name}")
    except Exception as e:
        logger.error(f"[WS Signal] Failed to push deletion: {e}")


def notify_bulk_update(community_id: str, buildings_data: list):
    """
    Utility function: push multiple building updates at once.
    Call this from views or management commands after batch operations.

    Usage:
        from websocket_backend.signals import notify_bulk_update
        notify_bulk_update("08417008", [serialized_building_1, serialized_building_2, ...])
    """
    group_name = f"buildings_{community_id}"
    channel_layer = get_channel_layer()

    if channel_layer is None:
        logger.warning("[WS Signal] No channel layer configured")
        return

    try:
        async_to_sync(channel_layer.group_send)(
            group_name,
            {
                "type": "bulk_update",
                "data": buildings_data,
            }
        )
        logger.info(
            f"[WS Signal] Pushed bulk update ({len(buildings_data)} buildings) "
            f"to group {group_name}"
        )
    except Exception as e:
        logger.error(f"[WS Signal] Failed to push bulk update: {e}")
