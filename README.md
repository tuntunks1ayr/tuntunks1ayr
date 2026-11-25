# Android 3D RPG

An original Android 3D open-world role-playing game project built with Unity 6. This project is not affiliated with or a copy of Black Russia.

## Architecture

- `client/` — Unity 6 Android game client
- `server/` — ASP.NET Core API and PostgreSQL data layer
- `docs/` — design and setup documentation

## MVP

- Third-person mobile character movement
- Touch joystick and action controls
- ASP.NET Core authentication and character persistence
- PostgreSQL database
- Network-ready session services
- Spawn, inventory, currency, and player-save foundations

## Planned stack

- Unity 6 + Universal Render Pipeline
- Android 16 KB page support
- Photon Fusion for real-time multiplayer (commercial SDK to be installed)
- ASP.NET Core .NET 9
- PostgreSQL
- Docker Compose

## Quick start

### Game client

1. Install Unity Hub and Unity 6.
2. Open the `client` directory as a Unity project.
3. Install Android Build Support and OpenJDK.
4. Switch the build target to Android.
5. Open `Assets/Scenes/Main.unity` after it is created in the editor.

### Backend

```bash
cd server
cp appsettings.Example.json appsettings.Development.json
dotnet run
```

## Development status

- [x] Repository bootstrap
- [ ] Unity client scaffold
- [ ] Mobile controls
- [ ] Authentication
- [ ] Character persistence
- [ ] Multiplayer sessions
- [ ] Android build
