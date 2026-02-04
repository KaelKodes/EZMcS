# EzMinecraftServer v0.0.9

EzMinecraftServer is a powerful, streamlined tool designed to take the frustration out of managing Minecraft servers.

> [!NOTE]
> This is a very early version and has been primarily tested on the following hardware:
> - **CPU**: AMD Ryzen 7 2700X / Intel i9-14900K
> - **RAM**: 32 GB

## 🧪 Tested Environment
- **Minecraft**: 1.20.1 / 1.21.1
- **Vanilla**: ✅ Supported
- **Fabric**: ✅ Supported (installer-based)
- **Forge**: ✅ Supported (installer-based)
- **NeoForge**: ✅ Supported (installer-based)
- **Java**: Adoptium JDK 17 / 21
- **Mod Manager**: CurseForge

---

## 🚀 Server Setup Wizard
Create new servers with the guided wizard:
- **Vanilla** - Direct JAR download from Mojang
- **Fabric** - Automatic installer download and execution
- **Forge** - Automatic installer download and execution
- **NeoForge** - Automatic installer download and execution

The wizard handles EULA acceptance, Java version detection, and initial server configuration automatically.

---

## 🛠️ Components
EzMcS is divided into three functional areas:

### 1. The Console
The heart of your server management.
- **Commands**: Run server commands directly. The `/` prefix is optional.
- **Command History**: Use the **UP** arrow key to cycle through previously sent commands.
- **Remote Visibility**: Console logs are stored and shared, allowing remote clients to access past logs when connected to a host.

### 2. Setup
Best managed by the host user.
- **Server Profiles**: Save and persist your settings (path, JAR name, RAM, etc.) across updates.
- **CurseForge Mod Sync**: Point to your CurseForge mods folder and sync automatically on server start.
- **Mod Conflict Detection**: Automatically detects client-only mods that crash the server.
- **Interactive Conflict Resolution**: Choose which problematic mods to remove with a single click.
- **Mod Blacklist**: Removed mods won't be re-synced from CurseForge.
- **Optimize CPU Affinity**: 
  > [!WARNING]
  > **Experimental Feature.** Scans CPU topology to pin Minecraft threads to physical/performance cores. 
  > Validated on **Ryzen 2700X** and **Intel i9-14900K**. Use with caution on other architectures.
- **Manual Affinity**: Granting precise control over CPU thread assignment. Features an "Auto-Optimize" button for quick setup.
- **Player Management**: View online players with the ability to **Kick**, **Ban**, or **Make OP** via right-click.

### 3. Monitor & Management
#### Management Tools
- **Config Editor**: Quickly edit `server.properties` through a dedicated UI.
- **RAM Allocation**: Set Min/Max RAM (e.g., `4G`, `2G`).
- **JVM Flags**: Toggle common optimization flags or add your own custom arguments.
- **Intelligent Java Detection**: Automatically finds installed JDKs and selects the correct version for your server.

#### Remote Management Modes
- **Host Account**: Set a port and click "Initialize Network" twice (once to clean up/check, once to start). Leave IP blank.
- **Connect to Host**: Enter the host's public IP and Port, then click "Initialize Network" twice to connect.
- **Local Only**: For LAN-based management only.

#### System Monitor
Real-time tracking of server resources:
- **CPU**: Total machine CPU usage.
- **RAM**: Total machine RAM usage.
- **Svr**: Allocated RAM currently used by the Minecraft process.

---

## 🎨 Themes
Customize your experience with the **Theme Selection** dropdown:
- **Minetrix**: Classic green matrix aesthetic.
- **Godot**: Modern dark theme.
- **Fantasy**: Retro blue-box RPG style.
- **Cyberpunk**: High-contrast neon interface.

---

## 📋 Changelog (v0.0.9)
- Added Server Setup Wizard for Vanilla, Fabric, Forge, and NeoForge
- Added CurseForge mod sync with automatic conflict detection
- Added interactive mod conflict resolution dialog
- Added mod blacklist to prevent re-syncing removed mods
- Added intelligent Java version detection and auto-selection
- Improved CPU affinity detection for Intel hybrid architectures

---

Enjoy your EZ server experience!
