"""
Generate HonorBuddy transport profiles from the transport database.
Creates standalone profiles for each transport route that can be called as sub-profiles.
"""

import os
from transport_data import (
    HORDE_ZEPPELINS, 
    ALLIANCE_SHIPS, 
    NEUTRAL_TRANSPORTS,
    TransportRoute,
    get_continent_name
)


def generate_transport_profile(route: TransportRoute, route_id: str) -> str:
    """Generate a complete HonorBuddy transport profile XML."""
    
    from_continent = get_continent_name(route.from_continent_id)
    to_continent = get_continent_name(route.to_continent_id)
    
    # Generate profile name
    profile_name = f"Transport_{route.from_subzone.replace(' ', '_')}_to_{route.to_subzone.replace(' ', '_')}"
    
    # Continent check condition
    if route.from_continent_id != route.to_continent_id:
        quest_complete_condition = f'Me.MapId != {route.from_continent_id}'
    else:
        # Same continent - check zone
        quest_complete_condition = f'Me.ZoneId == X'  # Would need zone IDs
    
    xml = f'''<?xml version="1.0" encoding="utf-8"?>
<HBProfile xsi:noNamespaceSchemaLocation="../Schemas/EchoSchema.xsd" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <!-- 
    Transport Profile: {route.from_subzone} to {route.to_subzone}
    Transport: {route.transport_name} (ID: {route.transport_id})
    Faction: {route.faction}
    Route: {from_continent} -> {to_continent}
    
    Generated from WRobot coordinates
  -->
  
  <Name>{profile_name}</Name>
  <MinLevel>1</MinLevel>
  <MaxLevel>80</MaxLevel>
  
  <AvoidMobs>true</AvoidMobs>
  <LootMobs>false</LootMobs>
  <SellGrey>false</SellGrey>
  
  <QuestOrder>
    <!-- Run to departure dock -->
    <RunTo X="{route.from_wait_pos[0]}" Y="{route.from_wait_pos[1]}" Z="{route.from_wait_pos[2]}" />
    
    <!-- Take transport -->
    <CustomBehavior File="UseTransport" TransportId="{route.transport_id}"
        WaitAtX="{route.from_wait_pos[0]}" WaitAtY="{route.from_wait_pos[1]}" WaitAtZ="{route.from_wait_pos[2]}"
        TransportStartX="{route.from_transport_pos[0]}" TransportStartY="{route.from_transport_pos[1]}" TransportStartZ="{route.from_transport_pos[2]}"
        StandOnX="{route.from_stand_pos[0]}" StandOnY="{route.from_stand_pos[1]}" StandOnZ="{route.from_stand_pos[2]}"
        TransportEndX="{route.to_transport_pos[0]}" TransportEndY="{route.to_transport_pos[1]}" TransportEndZ="{route.to_transport_pos[2]}"
        GetOffX="{route.to_exit_pos[0]}" GetOffY="{route.to_exit_pos[1]}" GetOffZ="{route.to_exit_pos[2]}" />
    
    <!-- Move away from dock -->
    <RunTo X="{route.to_exit_pos[0]}" Y="{route.to_exit_pos[1]}" Z="{route.to_exit_pos[2]}" />
  </QuestOrder>
  
</HBProfile>
'''
    return xml


def generate_all_transport_profiles(output_dir: str):
    """Generate all transport profile XMLs."""
    
    os.makedirs(output_dir, exist_ok=True)
    os.makedirs(os.path.join(output_dir, "Horde"), exist_ok=True)
    os.makedirs(os.path.join(output_dir, "Alliance"), exist_ok=True)
    os.makedirs(os.path.join(output_dir, "Neutral"), exist_ok=True)
    
    generated = 0
    
    # Horde Zeppelins
    for route_id, route in HORDE_ZEPPELINS.items():
        xml = generate_transport_profile(route, route_id)
        safe_from = route.from_subzone.replace(' ', '_').replace("'", '')
        safe_to = route.to_subzone.replace(' ', '_').replace("'", '')
        filename = f"Transport_{safe_from}_to_{safe_to}.xml"
        filepath = os.path.join(output_dir, "Horde", filename)
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(xml)
        generated += 1
        print(f"[Horde] {route.from_subzone} -> {route.to_subzone}")
    
    # Alliance Ships
    for route_id, route in ALLIANCE_SHIPS.items():
        xml = generate_transport_profile(route, route_id)
        safe_from = route.from_subzone.replace(' ', '_').replace("'", '')
        safe_to = route.to_subzone.replace(' ', '_').replace("'", '')
        filename = f"Transport_{safe_from}_to_{safe_to}.xml"
        filepath = os.path.join(output_dir, "Alliance", filename)
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(xml)
        generated += 1
        print(f"[Alliance] {route.from_subzone} -> {route.to_subzone}")
    
    # Neutral
    for route_id, route in NEUTRAL_TRANSPORTS.items():
        xml = generate_transport_profile(route, route_id)
        safe_from = route.from_subzone.replace(' ', '_').replace("'", '')
        safe_to = route.to_subzone.replace(' ', '_').replace("'", '')
        filename = f"Transport_{safe_from}_to_{safe_to}.xml"
        filepath = os.path.join(output_dir, "Neutral", filename)
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(xml)
        generated += 1
        print(f"[Neutral] {route.from_subzone} -> {route.to_subzone}")
    
    print(f"\nGenerated {generated} transport profiles in {output_dir}")
    return generated


if __name__ == "__main__":
    output_dir = "output_v2/Transport"
    generate_all_transport_profiles(output_dir)
