# Turret-lib

A C# library mod for **Vintage Story** that exposes general functionalities for turret-like devices, weapons, and mountable entity systems.

---

## Features

* **Static Turret Base**: Core block entity logic for static, turret-like devices.
* **Custom Placement**: Requires holding **SHIFT + Right Click** for 1 second to place.
* **Mounting & Seats**: Player mounting with built-in seat creation and orbiting camera controls.
* **Aiming System**: Smooth pitch and yaw bone movement with JSON-configured min/max angles and model offset.
* **Inventory & Ammo**: Handles ammo storage, loading interactions, and rendering held projectiles on the turret.
* **Weapon Logic**: Controls weapon states, firing animations, and projectile logic.
* **State Persistence**: Keeps aim angles, inventory, and weapon state saved across server syncs, chunk reloads, and relogs.

---

## How To Use

1. Add `TurretLib` as a dependency in your mod's `modinfo.json`.
```json
"dependencies": {
    "turretlib": "1.0.0"
}
```

2. Attach the required turret behaviors to your block definition:

```json
"entityBehaviors": [
  {
    "name": "turretlib:TurretAim",
    "properties": {
      "YawBoneName": "YawJoint",
      "PitchBoneName": "PitchJoint",
      "DefaultYaw": 0.0,
      "DefaultPitch": 0.0,
      "MinPitchDeg": -45.0,
      "MaxPitchDeg": 30.0,
      "ModelYawOffsetDeg": 90.0
    }
  },
  {
    "name": "turretlib:TurretWeapon",
    "properties": {
      "ProjectileAPName": "ProjectileAP",
      "ProjectileTransform": {
        "translation": { "x": 0.03125, "y": 0, "z": -0.5 },
        "origin": { "x": 0.5, "y": 0.5, "z": 0.5 }
      },
      "ProjectileModelYOffsetDeg": -90.0
    }
  }
]

```

- [see mod page](https://mods.vintagestory.at/show/mod/64406)

---

## Contribution & Development

Want to contribute code or compile this mod locally? Please review the central [Contributing Guidelines](https://github.com/Furio-s-Mods/.github/blob/main/CONTRIBUTING.md) for environment setup and path management instructions.

## Acknowledgements
* **[Anego Studios](https://anegostudios.com)** - Vintage Story Devs


## License

This project is licensed under the [MIT License](LICENSE).

---