# G15-LogiDiscord
A WIP Discord status display / controller application for Logitech G15 keyboards

This app is highly experimental. Since Discord RPC is not well documented, and always changing; this app is not well coded and will be changed accordingly.

~~Currently no pre-build binaries. I will upload pre-build binaries after adding "Reading Client-ID/Client-Secret from file" functionality.~~

Pre-build binaries are ready now.

## Usage
- After downloading binary from the "releases", extract it to the a good location (Such as: C:\Program Files\LogiDiscord); since there is an option to launch on boot.

- You have to create an application on https://discord.com/developers/applications. Then, from left bar click "OAuth2".

- You must be able to see your app's "Client ID". To generate "Client Secret" click "Reset Secret" button. This will generate you a "Client Secret".

- Copy "Client ID" and "Client Secret" to the "clientid.txt" and "clientsecret.txt" files in application directory (Ex: C:\Program Files\LogiDiscord\clientid.txt).

- You can then launch "LogiDiscordApplet.exe". It will open Discord app and ask you for a permission. Click authorize.
- - - -

###### Optional
You can make the LogiDiscord app launch on the boot by right clicking it's icon on notification area (right side of the taskbar), then selecting "Run on boot".

###### To make mute / deafen buttons work:

- You have to add "LControl + LShift + Numpad *" as "Mute toggle" shortcut in Discord.
- You have to add "LControl + LShift + Numpad -" as "Deafen toggle" shortcut in Discord.

Since in Discord RPC mute/deafen not working stabile. 
