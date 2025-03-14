# Unity Gaming Services Setup for Global Matchmaking

This document explains how to set up Unity Gaming Services (UGS) for global matchmaking in your game. This allows players to see and join games from anywhere in the world.

## Required Packages

You need to install the following packages via the Package Manager:

1. **Unity Services Core** (com.unity.services.core)
2. **Authentication** (com.unity.services.authentication)
3. **Lobby** (com.unity.services.lobby)
4. **Relay** (com.unity.services.relay)

## Setup Steps

### 1. Install Required Packages

1. Open Unity Package Manager (Window > Package Manager)
2. Click the "+" button in the top-left corner and select "Add package by name..."
3. Add each of these packages:
   - `com.unity.services.core`
   - `com.unity.services.authentication`
   - `com.unity.services.lobby`
   - `com.unity.services.relay`

### 2. Link Your Project to Unity Services

1. Open the Unity Dashboard (Window > Unity Services)
2. Sign in with your Unity account
3. Link your project to Unity Services by clicking "Create" or "Link"
4. Select your organization or create a new one
5. Wait for the linking process to complete

### 3. Enable Required Services

1. In the Unity Dashboard, go to "Dashboard"
2. Enable the following services:
   - Authentication
   - Lobby
   - Relay

### 4. Configure the Project

1. Make sure the `MatchmakingManager` script has `useUnityServices` set to `true` in the Inspector
2. If you want to use a specific region for Relay servers, set the `relayRegionIndex` to one of:
   - 0: Auto (best region)
   - 1: US East
   - 2: US West
   - 3: EU West
   - 4: AP South

### 5. Configure Unity Transport

1. Make sure your NetworkManager GameObject has the UnityTransport component attached
2. No need to configure host/port as it will be set automatically

## Usage

- When a player creates a server, it will now be registered globally
- When a player refreshes the server list, they will see both local and global servers
- Global servers are marked with a 🌐 icon in the server browser

## Troubleshooting

### Authentication Failures

If you see authentication errors:
- Make sure your project is properly linked to Unity Services
- Check if you're connected to the internet
- Verify the services are enabled in your Unity Dashboard

### Cannot See Global Servers

If you can't see global servers:
- Ensure your internet connection is working
- Check if the Unity Services initialization was successful (look for logs)
- Make sure Relay and Lobby services are enabled in your project

### Cannot Join Global Servers

If you can join local but not global servers:
- Verify that the Unity Transport is configured correctly
- Check for any firewall blocking the connection
- Make sure the services quotas haven't been exceeded (check Dashboard) 