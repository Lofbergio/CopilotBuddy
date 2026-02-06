"""
Transport Database for WoW 3.3.5a (WotLK)

Contains coordinates for all zeppelins and ships.
Data sourced from WRobot profiles and game databases.

HonorBuddy UseTransport format:
    <CustomBehavior File="UseTransport" 
        TransportId="ID"
        WaitAtX="" WaitAtY="" WaitAtZ=""           -- Where player waits
        TransportStartX="" TransportStartY="" TransportStartZ=""  -- Transport docked position (from)
        StandOnX="" StandOnY="" StandOnZ=""        -- Where to stand on transport
        TransportEndX="" TransportEndY="" TransportEndZ=""  -- Transport docked position (to)
        GetOffX="" GetOffY="" GetOffZ=""           -- Where to exit
    />
"""

from dataclasses import dataclass
from typing import Dict, Tuple, Optional


@dataclass
class TransportRoute:
    """A transport route between two locations."""
    transport_id: int  # GameObject entry ID
    transport_name: str
    faction: str  # "Horde", "Alliance", or "Neutral"
    
    # From location
    from_zone: str
    from_subzone: str
    from_continent_id: int  # 0=Azeroth(EK), 1=Kalimdor, 571=Northrend
    from_wait_pos: Tuple[float, float, float]  # Where player waits for transport
    from_transport_pos: Tuple[float, float, float]  # Where transport docks
    from_stand_pos: Tuple[float, float, float]  # Where player stands on transport
    
    # To location
    to_zone: str
    to_subzone: str  
    to_continent_id: int
    to_transport_pos: Tuple[float, float, float]  # Where transport docks at destination
    to_exit_pos: Tuple[float, float, float]  # Where player exits transport


# ============================================================================
# HORDE ZEPPELINS
# ============================================================================

HORDE_ZEPPELINS: Dict[str, TransportRoute] = {
    
    # Orgrimmar <-> Undercity
    "og_to_uc": TransportRoute(
        transport_id=164871,
        transport_name="The Thundercaller",
        faction="Horde",
        from_zone="Durotar",
        from_subzone="Orgrimmar",
        from_continent_id=1,  # Kalimdor
        from_wait_pos=(1325.077, -4651.817, 53.79624),
        from_transport_pos=(1318.107, -4658.047, 71.86043),
        from_stand_pos=(1314.662, -4653.219, 54.11862),
        to_zone="Tirisfal Glades",
        to_subzone="Undercity",
        to_continent_id=0,  # Azeroth
        to_transport_pos=(2062.376, 292.998, 114.973),
        to_exit_pos=(2066.298, 286.7292, 97.03134),
    ),
    
    "uc_to_og": TransportRoute(
        transport_id=164871,
        transport_name="The Thundercaller",
        faction="Horde",
        from_zone="Tirisfal Glades",
        from_subzone="Undercity",
        from_continent_id=0,  # Azeroth
        from_wait_pos=(2066.298, 286.7292, 97.03134),
        from_transport_pos=(2062.376, 292.998, 114.973),
        from_stand_pos=(2068.04, 295.0981, 97.23881),
        to_zone="Durotar",
        to_subzone="Orgrimmar",
        to_continent_id=1,  # Kalimdor
        to_transport_pos=(1318.107, -4658.047, 71.86043),
        to_exit_pos=(1325.077, -4651.817, 53.79624),
    ),
    
    # Orgrimmar <-> Grom'gol
    "og_to_gromgol": TransportRoute(
        transport_id=20808,  # Zeppelin to Grom'gol
        transport_name="The Iron Eagle",
        faction="Horde",
        from_zone="Durotar",
        from_subzone="Orgrimmar",
        from_continent_id=1,  # Kalimdor
        from_wait_pos=(1323.69, -4631.55, 53.89),
        from_transport_pos=(1312.28, -4623.69, 72.03),
        from_stand_pos=(1315.58, -4622.63, 54.03),
        to_zone="Stranglethorn Vale",
        to_subzone="Grom'gol Base Camp",
        to_continent_id=0,  # Azeroth (Eastern Kingdoms)
        to_transport_pos=(-12457.91, 134.56, 31.51),
        to_exit_pos=(-12462.09, 145.83, 11.90),
    ),
    
    "gromgol_to_og": TransportRoute(
        transport_id=20808,
        transport_name="The Iron Eagle",
        faction="Horde",
        from_zone="Stranglethorn Vale",
        from_subzone="Grom'gol Base Camp",
        from_continent_id=0,
        from_wait_pos=(-12462.09, 145.83, 11.90),
        from_transport_pos=(-12457.91, 134.56, 31.51),
        from_stand_pos=(-12455.25, 139.63, 12.03),
        to_zone="Durotar",
        to_subzone="Orgrimmar",
        to_continent_id=1,
        to_transport_pos=(1312.28, -4623.69, 72.03),
        to_exit_pos=(1323.69, -4631.55, 53.89),
    ),
    
    # Undercity <-> Grom'gol
    "uc_to_gromgol": TransportRoute(
        transport_id=195276,  # The Purple Princess
        transport_name="The Purple Princess",
        faction="Horde",
        from_zone="Tirisfal Glades",
        from_subzone="Undercity",
        from_continent_id=0,
        from_wait_pos=(2057.04, 237.56, 97.03),
        from_transport_pos=(2047.52, 236.13, 115.00),
        from_stand_pos=(2051.58, 242.16, 97.10),
        to_zone="Stranglethorn Vale",
        to_subzone="Grom'gol Base Camp",
        to_continent_id=0,
        to_transport_pos=(-12441.14, 197.26, 31.51),
        to_exit_pos=(-12433.80, 204.32, 11.90),
    ),
    
    "gromgol_to_uc": TransportRoute(
        transport_id=195276,
        transport_name="The Purple Princess",
        faction="Horde",
        from_zone="Stranglethorn Vale",
        from_subzone="Grom'gol Base Camp",
        from_continent_id=0,
        from_wait_pos=(-12433.80, 204.32, 11.90),
        from_transport_pos=(-12441.14, 197.26, 31.51),
        from_stand_pos=(-12437.92, 204.98, 12.03),
        to_zone="Tirisfal Glades",
        to_subzone="Undercity",
        to_continent_id=0,
        to_transport_pos=(2047.52, 236.13, 115.00),
        to_exit_pos=(2057.04, 237.56, 97.03),
    ),
    
    # Orgrimmar <-> Borean Tundra (WotLK)
    "og_to_borean": TransportRoute(
        transport_id=186238,
        transport_name="Zeppelin to Borean Tundra",
        faction="Horde",
        from_zone="Durotar",
        from_subzone="Orgrimmar",
        from_continent_id=1,  # Kalimdor
        from_wait_pos=(1763.203, -4284.529, 133.1072),
        from_transport_pos=(1775.066, -4299.745, 151.0326),
        from_stand_pos=(1770.22, -4292.056, 133.1872),
        to_zone="Borean Tundra",
        to_subzone="Warsong Hold",
        to_continent_id=571,  # Northrend
        to_transport_pos=(2837.908, 6187.443, 140.1648),
        to_exit_pos=(2836.831, 6185.15, 121.9923),
    ),
    
    "borean_to_og": TransportRoute(
        transport_id=186238,
        transport_name="Zeppelin to Borean Tundra",
        faction="Horde",
        from_zone="Borean Tundra",
        from_subzone="Warsong Hold",
        from_continent_id=571,
        from_wait_pos=(2836.831, 6185.15, 121.9923),
        from_transport_pos=(2837.908, 6187.443, 140.1648),
        from_stand_pos=(2845.18, 6192.4, 121.99),
        to_zone="Durotar",
        to_subzone="Orgrimmar",
        to_continent_id=1,
        to_transport_pos=(1775.066, -4299.745, 151.0326),
        to_exit_pos=(1763.203, -4284.529, 133.1072),
    ),
    
    # Undercity <-> Howling Fjord (WotLK)
    "uc_to_vengeance": TransportRoute(
        transport_id=181689,  # The Cloudkisser / Vengeance Landing Zep
        transport_name="Zeppelin to Howling Fjord",
        faction="Horde",
        from_zone="Tirisfal Glades",
        from_subzone="Undercity",
        from_continent_id=0,
        from_wait_pos=(2058.89, 384.75, 97.03),
        from_transport_pos=(2059.44, 393.20, 115.00),
        from_stand_pos=(2065.10, 388.07, 97.10),
        to_zone="Howling Fjord",
        to_subzone="Vengeance Landing",
        to_continent_id=571,
        to_transport_pos=(1989.26, -6079.51, 90.07),
        to_exit_pos=(2000.56, -6086.11, 69.32),
    ),
    
    "vengeance_to_uc": TransportRoute(
        transport_id=181689,
        transport_name="Zeppelin to Howling Fjord",
        faction="Horde",
        from_zone="Howling Fjord",
        from_subzone="Vengeance Landing",
        from_continent_id=571,
        from_wait_pos=(2000.56, -6086.11, 69.32),
        from_transport_pos=(1989.26, -6079.51, 90.07),
        from_stand_pos=(1994.80, -6073.90, 69.32),
        to_zone="Tirisfal Glades",
        to_subzone="Undercity",
        to_continent_id=0,
        to_transport_pos=(2059.44, 393.20, 115.00),
        to_exit_pos=(2058.89, 384.75, 97.03),
    ),
}


# ============================================================================
# ALLIANCE SHIPS
# ============================================================================

ALLIANCE_SHIPS: Dict[str, TransportRoute] = {
    
    # Menethil Harbor <-> Theramore (vanilla)
    "menethil_to_theramore": TransportRoute(
        transport_id=176310,  # The Bravery or Lady Mehley
        transport_name="The Bravery",
        faction="Alliance",
        from_zone="Wetlands",
        from_subzone="Menethil Harbor",
        from_continent_id=0,  # Azeroth
        from_wait_pos=(-3733.074, -587.595, 6.316904),
        from_transport_pos=(-3727.30, -583.98, -0.05),
        from_stand_pos=(-3718.757, -577.4539, 6.099637),
        to_zone="Dustwallow Marsh",
        to_subzone="Theramore Isle",
        to_continent_id=1,  # Kalimdor
        to_transport_pos=(-3842.29, -4531.95, -0.10),
        to_exit_pos=(-3838.47, -4535.83, 5.89),
    ),
    
    "theramore_to_menethil": TransportRoute(
        transport_id=176310,
        transport_name="The Bravery",
        faction="Alliance",
        from_zone="Dustwallow Marsh",
        from_subzone="Theramore Isle",
        from_continent_id=1,
        from_wait_pos=(-3838.47, -4535.83, 5.89),
        from_transport_pos=(-3842.29, -4531.95, -0.10),
        from_stand_pos=(-3846.50, -4527.44, 6.05),
        to_zone="Wetlands",
        to_subzone="Menethil Harbor",
        to_continent_id=0,
        to_transport_pos=(-3727.30, -583.98, -0.05),
        to_exit_pos=(-3733.074, -587.595, 6.316904),
    ),
    
    # Menethil Harbor <-> Auberdine
    "menethil_to_auberdine": TransportRoute(
        transport_id=176231,  # The Lady Mehley / Feathermoon Ferry
        transport_name="The Lady Mehley",
        faction="Alliance",
        from_zone="Wetlands",
        from_subzone="Menethil Harbor",
        from_continent_id=0,
        from_wait_pos=(-3671.50, -599.50, 6.32),
        from_transport_pos=(-3664.10, -604.37, -0.04),
        from_stand_pos=(-3659.80, -608.90, 6.10),
        to_zone="Darkshore",
        to_subzone="Auberdine",
        to_continent_id=1,
        to_transport_pos=(6443.32, 823.58, -0.04),
        to_exit_pos=(6439.00, 814.70, 5.77),
    ),
    
    "auberdine_to_menethil": TransportRoute(
        transport_id=176231,
        transport_name="The Lady Mehley",
        faction="Alliance",
        from_zone="Darkshore",
        from_subzone="Auberdine",
        from_continent_id=1,
        from_wait_pos=(6439.00, 814.70, 5.77),
        from_transport_pos=(6443.32, 823.58, -0.04),
        from_stand_pos=(6447.20, 829.10, 6.05),
        to_zone="Wetlands",
        to_subzone="Menethil Harbor",
        to_continent_id=0,
        to_transport_pos=(-3664.10, -604.37, -0.04),
        to_exit_pos=(-3671.50, -599.50, 6.32),
    ),
    
    # Auberdine <-> Rut'theran Village / Teldrassil
    "auberdine_to_teldrassil": TransportRoute(
        transport_id=176365,  # The Moonspray
        transport_name="The Moonspray",
        faction="Alliance",
        from_zone="Darkshore",
        from_subzone="Auberdine",
        from_continent_id=1,
        from_wait_pos=(6576.501, 769.4759, 5.575114),
        from_transport_pos=(6583.683, 764.0164, -0.05),
        from_stand_pos=(6585.978, 764.5873, 6.099618),
        to_zone="Teldrassil",
        to_subzone="Rut'theran Village",
        to_continent_id=1,
        to_transport_pos=(8561.36, 1008.30, -0.04),
        to_exit_pos=(8555.00, 1012.55, 6.06),
    ),
    
    "teldrassil_to_auberdine": TransportRoute(
        transport_id=176365,
        transport_name="The Moonspray",
        faction="Alliance", 
        from_zone="Teldrassil",
        from_subzone="Rut'theran Village",
        from_continent_id=1,
        from_wait_pos=(8555.00, 1012.55, 6.06),
        from_transport_pos=(8561.36, 1008.30, -0.04),
        from_stand_pos=(8568.20, 1003.10, 6.05),
        to_zone="Darkshore",
        to_subzone="Auberdine",
        to_continent_id=1,
        to_transport_pos=(6583.683, 764.0164, -0.05),
        to_exit_pos=(6576.501, 769.4759, 5.575114),
    ),
    
    # Rut'theran Village <-> Stormwind (WotLK added)
    "ruttheran_to_sw": TransportRoute(
        transport_id=176310,  # The Bravery (repurposed in WotLK?)
        transport_name="Ship to Stormwind",
        faction="Alliance",
        from_zone="Teldrassil",
        from_subzone="Rut'theran Village",
        from_continent_id=1,
        from_wait_pos=(8177.832, 1002.582, 6.699792),
        from_transport_pos=(8162.587, 1005.365, -0.04972067),
        from_stand_pos=(8163.446, 1012.211, 6.029718),
        to_zone="Elwynn Forest",
        to_subzone="Stormwind Harbor",
        to_continent_id=0,
        to_transport_pos=(-8650.719, 1346.051, 0.02488805),
        to_exit_pos=(-8639.334, 1318.533, 5.536631),
    ),
    
    "sw_to_ruttheran": TransportRoute(
        transport_id=176310,
        transport_name="Ship to Stormwind",
        faction="Alliance",
        from_zone="Elwynn Forest",
        from_subzone="Stormwind Harbor",
        from_continent_id=0,
        from_wait_pos=(-8639.334, 1318.533, 5.536631),
        from_transport_pos=(-8650.719, 1346.051, 0.02488805),
        from_stand_pos=(-8656.50, 1350.00, 6.00),
        to_zone="Teldrassil",
        to_subzone="Rut'theran Village",
        to_continent_id=1,
        to_transport_pos=(8162.587, 1005.365, -0.04972067),
        to_exit_pos=(8177.832, 1002.582, 6.699792),
    ),
    
    # Stormwind Harbor <-> Valiance Keep (WotLK)
    "sw_to_valiance": TransportRoute(
        transport_id=190536,  # The Kraken
        transport_name="The Kraken",
        faction="Alliance",
        from_zone="Elwynn Forest",
        from_subzone="Stormwind Harbor",
        from_continent_id=0,
        from_wait_pos=(-8300.08, 1405.158, 4.422395),
        from_transport_pos=(-8288.816, 1424.703, 0.04),
        from_stand_pos=(-8285.50, 1430.50, 5.50),
        to_zone="Borean Tundra",
        to_subzone="Valiance Keep",
        to_continent_id=571,
        to_transport_pos=(2218.391, 5119.589, 0.04),
        to_exit_pos=(2234.375, 5132.568, 5.343217),
    ),
    
    "valiance_to_sw": TransportRoute(
        transport_id=190536,
        transport_name="The Kraken",
        faction="Alliance",
        from_zone="Borean Tundra",
        from_subzone="Valiance Keep",
        from_continent_id=571,
        from_wait_pos=(2234.375, 5132.568, 5.343217),
        from_transport_pos=(2218.391, 5119.589, 0.04),
        from_stand_pos=(2220.50, 5126.00, 5.50),
        to_zone="Elwynn Forest",
        to_subzone="Stormwind Harbor",
        to_continent_id=0,
        to_transport_pos=(-8288.816, 1424.703, 0.04),
        to_exit_pos=(-8300.08, 1405.158, 4.422395),
    ),
    
    # Menethil Harbor <-> Howling Fjord (WotLK)
    "menethil_to_valgarde": TransportRoute(
        transport_id=181688,  # The Northspear
        transport_name="The Northspear",
        faction="Alliance",
        from_zone="Wetlands",
        from_subzone="Menethil Harbor",
        from_continent_id=0,
        from_wait_pos=(-3702.60, -584.85, 6.32),
        from_transport_pos=(-3695.17, -593.88, -0.06),
        from_stand_pos=(-3690.00, -598.50, 6.05),
        to_zone="Howling Fjord",
        to_subzone="Valgarde",
        to_continent_id=571,
        to_transport_pos=(620.21, -5165.75, -0.05),
        to_exit_pos=(610.00, -5159.50, 5.86),
    ),
    
    "valgarde_to_menethil": TransportRoute(
        transport_id=181688,
        transport_name="The Northspear",
        faction="Alliance",
        from_zone="Howling Fjord",
        from_subzone="Valgarde",
        from_continent_id=571,
        from_wait_pos=(610.00, -5159.50, 5.86),
        from_transport_pos=(620.21, -5165.75, -0.05),
        from_stand_pos=(625.00, -5172.00, 6.05),
        to_zone="Wetlands",
        to_subzone="Menethil Harbor",
        to_continent_id=0,
        to_transport_pos=(-3695.17, -593.88, -0.06),
        to_exit_pos=(-3702.60, -584.85, 6.32),
    ),
}


# ============================================================================
# NEUTRAL TRANSPORTS
# ============================================================================

NEUTRAL_TRANSPORTS: Dict[str, TransportRoute] = {
    # Booty Bay <-> Ratchet
    "booty_to_ratchet": TransportRoute(
        transport_id=176495,  # The Maiden's Fancy
        transport_name="The Maiden's Fancy",
        faction="Neutral",
        from_zone="Stranglethorn Vale",
        from_subzone="Booty Bay",
        from_continent_id=0,
        from_wait_pos=(-14284.17, 555.64, 9.01),
        from_transport_pos=(-14276.35, 566.09, -0.05),
        from_stand_pos=(-14272.00, 570.50, 6.05),
        to_zone="The Barrens",
        to_subzone="Ratchet",
        to_continent_id=1,
        to_transport_pos=(-995.75, -3827.40, -0.05),
        to_exit_pos=(-1001.00, -3834.50, 5.96),
    ),
    
    "ratchet_to_booty": TransportRoute(
        transport_id=176495,
        transport_name="The Maiden's Fancy",
        faction="Neutral",
        from_zone="The Barrens",
        from_subzone="Ratchet",
        from_continent_id=1,
        from_wait_pos=(-1001.00, -3834.50, 5.96),
        from_transport_pos=(-995.75, -3827.40, -0.05),
        from_stand_pos=(-990.00, -3822.00, 6.05),
        to_zone="Stranglethorn Vale",
        to_subzone="Booty Bay",
        to_continent_id=0,
        to_transport_pos=(-14276.35, 566.09, -0.05),
        to_exit_pos=(-14284.17, 555.64, 9.01),
    ),
}


# ============================================================================
# HELPER FUNCTIONS
# ============================================================================

def get_all_transports() -> Dict[str, TransportRoute]:
    """Get all transport routes combined."""
    all_routes = {}
    all_routes.update(HORDE_ZEPPELINS)
    all_routes.update(ALLIANCE_SHIPS)
    all_routes.update(NEUTRAL_TRANSPORTS)
    return all_routes


def find_transport_by_destination(to_zone: str, to_subzone: str = None, faction: str = None) -> Optional[TransportRoute]:
    """Find a transport route by destination."""
    all_routes = get_all_transports()
    
    for route_id, route in all_routes.items():
        # Match zone
        if to_zone.lower() not in route.to_zone.lower():
            continue
        
        # Match subzone if provided
        if to_subzone and to_subzone.lower() not in route.to_subzone.lower():
            continue
            
        # Match faction if provided
        if faction and route.faction != faction and route.faction != "Neutral":
            continue
            
        return route
        
    return None


def find_transport_by_locations(from_zone: str, to_zone: str, faction: str = None) -> Optional[TransportRoute]:
    """Find a transport route by both from and to locations."""
    all_routes = get_all_transports()
    
    for route_id, route in all_routes.items():
        # Match from zone
        if from_zone.lower() not in route.from_zone.lower() and from_zone.lower() not in route.from_subzone.lower():
            continue
            
        # Match to zone
        if to_zone.lower() not in route.to_zone.lower() and to_zone.lower() not in route.to_subzone.lower():
            continue
            
        # Match faction if provided
        if faction and route.faction != faction and route.faction != "Neutral":
            continue
            
        return route
        
    return None


def get_continent_name(continent_id: int) -> str:
    """Get continent name from ID."""
    return {
        0: "Azeroth",  # Eastern Kingdoms
        1: "Kalimdor",
        571: "Northrend"
    }.get(continent_id, "Unknown")


def generate_hb_use_transport_xml(route: TransportRoute, indent: str = "    ") -> str:
    """Generate HonorBuddy UseTransport CustomBehavior XML."""
    return (
        f'{indent}<CustomBehavior File="UseTransport" TransportId="{route.transport_id}"\n'
        f'{indent}    WaitAtX="{route.from_wait_pos[0]}" WaitAtY="{route.from_wait_pos[1]}" WaitAtZ="{route.from_wait_pos[2]}"\n'
        f'{indent}    TransportStartX="{route.from_transport_pos[0]}" TransportStartY="{route.from_transport_pos[1]}" TransportStartZ="{route.from_transport_pos[2]}"\n'
        f'{indent}    StandOnX="{route.from_stand_pos[0]}" StandOnY="{route.from_stand_pos[1]}" StandOnZ="{route.from_stand_pos[2]}"\n'
        f'{indent}    TransportEndX="{route.to_transport_pos[0]}" TransportEndY="{route.to_transport_pos[1]}" TransportEndZ="{route.to_transport_pos[2]}"\n'
        f'{indent}    GetOffX="{route.to_exit_pos[0]}" GetOffY="{route.to_exit_pos[1]}" GetOffZ="{route.to_exit_pos[2]}" />'
    )


# Zone aliases for matching
ZONE_ALIASES = {
    "orgrimmar": ["og", "org", "durotar"],
    "undercity": ["uc", "tirisfal glades", "tirisfal"],
    "stormwind": ["sw", "stormwind city", "elwynn forest", "stormwind harbor"],
    "grom'gol": ["gromgol", "grom'gol base camp", "stranglethorn vale"],
    "theramore": ["theramore isle", "dustwallow marsh"],
    "menethil": ["menethil harbor", "wetlands"],
    "auberdine": ["darkshore"],
    "teldrassil": ["rut'theran", "rut'theran village", "ruttheran"],
    "borean tundra": ["warsong hold", "valiance keep"],
    "howling fjord": ["vengeance landing", "valgarde"],
    "ratchet": ["the barrens", "barrens"],
    "booty bay": ["stranglethorn vale"],
}


if __name__ == "__main__":
    # Test: Print all routes
    all_routes = get_all_transports()
    print(f"Total transport routes: {len(all_routes)}")
    print()
    
    for route_id, route in all_routes.items():
        print(f"{route_id}: {route.from_subzone} -> {route.to_subzone} [{route.faction}]")
    
    print()
    print("Sample XML output:")
    route = HORDE_ZEPPELINS["og_to_uc"]
    print(generate_hb_use_transport_xml(route))
