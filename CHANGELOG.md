# CHANGELOG

## 1.1.0
NEW:
- Moving turrets: Entity attachment logic to enable turrets on entiry cargo slots
    - 2x2 placing on vanilla boats
    - Mounting Functionality: seat creation with orbiting configuration
    - Aiming Functionality: mandatory controls and renderer configurations
    - Inventory Functionality: ammo rendering and loading interactions
    - Weapon Functionality: animation and projectile firing logic

FIX:
- relog state desync bug for loaded turrets animation

## 1.0.1
NEW:
- More robust attribute extraction and fallback default logic and logging
- Exposed more weapon properties: firing sound params, ProjectilePropulsionForce

## 1.0.0
NEW:
- Static Turrets: BlockEntity logic for static turrets-like devices.
    - Place Behaviour: holding SHIFT + RIGHT CLICK for 1 sec
    - Pickup Behaviour: holding SHIFT + RIGHT CLICK for 1 sec
    - Mounting Behaviour: seat creation with orbiting configuration
    - Aiming Behaviour: mandatory controls and renderer configurations
    - Inventory Behaviour: ammo rendering and loading interactions
    - Weapon Behaviour: animation and projectile firing logic
    
