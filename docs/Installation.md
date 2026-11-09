# Installation

## Add the Plugin Repository

1. Open Jellyfin and go to **Dashboard → Plugins → Repositories**.
2. Click **Add Repository**.
3. Enter this repository URL and click **Save**:

```
https://raw.githubusercontent.com/JPKribs/jellyfin-plugin-artworkgenerator/master/manifest.json
```

## Install the Plugin

1. Open the **Catalog** tab in the Plugins section.
2. Find **Artwork Generator** and click **Install**.
3. Wait for the installation to complete.

## Restart Jellyfin

Restart your Jellyfin server completely and wait for it to fully start up.

## Check It Works

After the restart, go to **Dashboard → Plugins → Artwork Generator** and confirm the configuration page loads.

## Set a Published Server URI

Recommended if you pick artwork by hand in the **Edit Images** dialog.

The choices the plugin offers there are addressed by URL, and your browser loads their previews from that URL. When the server has no published address, the plugin falls back to `127.0.0.1`, which only points at the server from the server itself. Any browser on another device then shows broken previews, even though the images were generated.

To fix it, go to **Dashboard → Networking**, fill in **Published Server URIs** with an address your clients use to reach the server, then save and restart Jellyfin. For example:

```
all=http://192.168.1.10:8096
```

Use an address the server can also reach itself, such as its LAN address, since Jellyfin downloads the choice you pick from the same URL. If Jellyfin sits behind a reverse proxy, use the address clients use, including any base URL.

Leave the field empty if you only ever open Jellyfin on the server machine itself; the loopback fallback works there.

Next: [Poster Styles](Styles.md).
