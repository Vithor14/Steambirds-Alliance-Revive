# Playing online with Radmin VPN — step by step

This guide shows how to play Steambirds Alliance Revive with friends over the internet **without opening any port on
your router**, using the free program **Radmin VPN**. Radmin VPN makes every player's PC look like it is on the same
local network, so the launcher finds the server by itself.

There are two roles:

- **Host** — the person whose PC runs the server (Part 2).
- **Player** — everyone else (Part 3).

Both start with Part 1. The community network is already created, so **nobody has to create it**: the host and
the players all *join* the same network.

| | |
|---|---|
| **Network name** | `Steambirds_Revive` |
| **Network password** | `Steambirds_Revive` |

> The network is public on purpose, so anyone from the community can join. Don't keep personal files shared on
> your PC while you are in it (Windows shares nothing by default). If someone abuses the network, ask on the
> community Discord for a new name and password.

---

## Part 1 — Download, install and join the network (everyone)

### 1. Download Radmin VPN

1. Download the installer: <https://download.radmin-vpn.com/download/files/Radmin_VPN_2.1.4952.2.exe>
   (the file is called `Radmin_VPN_2.1.4952.2.exe`).
2. Wait for the download to finish. It usually lands in your **Downloads** folder.

### 2. Install it

1. Double-click `Radmin_VPN_2.1.4952.2.exe`.
2. If Windows asks *"Do you want to allow this app to make changes to your device?"*, click **Yes**
   (the program installs a virtual network adapter, so it needs administrator permission).
3. Choose your language, click **Next**, accept the license agreement and keep the default folder.
4. Click **Install** and wait. If Windows asks to install the **Radmin VPN Ethernet Adapter**, click **Install**.
5. Click **Finish**. Radmin VPN starts by itself and stays running next to the clock.

### 3. Open Radmin VPN

1. Look at the **taskbar**, at the bottom right, next to the clock. If you don't see the Radmin VPN icon, click the
   small arrow **^** (*Show hidden icons*).
2. Click the **Radmin VPN icon** to open its window (or search for *Radmin VPN* in the Windows Start menu).

### 4. Join the network

1. In the Radmin VPN window, click **Network** in the top menu.
2. Click **Join an existing network...**
3. Fill in:
   - **Network name:** `Steambirds_Revive`
   - **Password:** `Steambirds_Revive`
4. Click **Join**.

The network now appears in the window with the computers that are online. Your own PC shows a **green** dot when it
is connected. To leave later, right-click the network and choose **Leave network**.

### 5. Get the Steambirds Revive launcher

Download the latest `SteambirdsLauncher-<version>-win-x64.zip` from the GitHub Releases page of the project, extract
it to a folder **you own** (for example `C:\Games\SteambirdsRevive`, not inside `Program Files`) and run
`SteambirdsLauncher.exe`. On the first run it helps you get the game (the Steam depot download) and installs the
plugin.

---

## Part 2 — The host: create the server

Only **one** person in the network does this part.

1. Do Part 1 first, and keep Radmin VPN open while you host.
2. Open the launcher and go to the **Server** tab.
3. Open **Network and multiplayer** (in the side navigation).
4. Make sure **Players of other PCs can join** is checked.
5. Click **Allow in the Windows Firewall** (you only need to do this once). Windows asks for administrator permission:
   click **Yes**. This is needed so the other players can reach your server through Radmin VPN.
6. *(Optional)* Choose the **Server name** and the **maximum players**. If you change the ports, restart the server
   afterwards.
7. Click **Start server** and wait until the state says it is running.
8. Under **Addresses to share** you will see one address per network of your PC. Find the one that starts with
   **26.** — that is your **Radmin VPN address**. (You can also open Radmin VPN, right-click your own PC name and choose
   **Copy IP address**.)
9. Tell the players your address, or just tell them to open the Client tab: Radmin servers appear in the list by
   themselves.
10. To play yourself, go to the **Client** tab, choose the first row (**This PC**) and click **PLAY**. Keep the
    launcher open: closing it stops the server.

**Good to know**

- Everyone's account is saved on **the host's PC**. Back up the `server\accounts` folder if the progress matters.
- In the game chat, `/players` lists who is online.
- If the host's PC goes to sleep or Radmin VPN is closed, everyone is disconnected.

---

## Part 3 — The player: connect to a server

1. Do Part 1 first (install Radmin VPN and join `Steambirds_Revive`) and get the launcher.
2. Make sure the **host is online** in the Radmin VPN window (a green dot next to their name) and that they started
   the server.
3. Open the launcher and go to the **Client** tab.
4. *(Optional)* Type your **Player** name. Empty means the name of your Steam account. Your account always follows
   your Steam account, so changing the name never loses your pilot.
5. In the **Servers** list, wait a few seconds (or click **Search again**). The host's server appears with its name.
   Click its row to choose it.
6. **If the server does not appear:** type the host's Radmin address (the `26.x.x.x` number) in
   **Connect by IP** and add it. The launcher checks the server and saves it in your list.
7. Click **PLAY**. The game opens already pointing to the host's server.

---

## Troubleshooting

| Problem | What to do |
|---|---|
| The server doesn't show in the list | Check that Radmin VPN shows the host with a **green** dot, then click **Search again**. If it still doesn't show, use **Connect by IP** with the host's `26.x.x.x` address. |
| *Connect by IP* says the server can't be reached | Ask the host to click **Allow in the Windows Firewall** and to check the server is **running**. Ask them to confirm the address on *Network and multiplayer*. |
| Radmin shows a **yellow** or **red** dot for the host | The VPN link itself is down. Both sides: close and reopen Radmin VPN, check the internet, and try again. Some very strict networks (university, work) block VPNs. |
| High ping or lag | Pick a host that is closest to most players and has a wired connection. Ask other people on the host's home network not to download or stream while playing. |
| "Wrong password" when joining the network | Type it again: `Steambirds_Revive` (capital S and R, underscore in the middle). |
| The game won't open | Check the Steam depot download and the game folder in the launcher's Client tab; the game needs Steam open and logged in. |
| Firewall question on another antivirus | Allow `server\SteambirdsServer.exe` (inbound) on the Radmin VPN adapter. |

You can also ask on the community Discord. **Don't post your home IP in public channels**; the `26.x.x.x` Radmin
address is fine to share with the people in the network.
