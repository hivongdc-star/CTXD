CTXD REMAKE - UNITY CLIENT

Target major: Unity 6.
Open this folder in Unity Hub. Unity may upgrade ProjectVersion.txt to your installed Unity 6 patch; that is expected.
The editor script CTXDProjectSetup automatically:
- imports legacy textures as Sprites
- creates Assets/Game/Scenes/FirstPlayable.unity
- puts it in Build Settings
- configures Windows/Android identifiers and Android ARM64

First playable flow:
Login/Register -> Choose force -> Main City -> Building tutorial -> Task 8 name/picture.

Default DEV server URL: http://127.0.0.1:5080
For Android/LAN, set ctxd.server.url through PlayerPrefs/dev tooling to the Windows server LAN IP.
