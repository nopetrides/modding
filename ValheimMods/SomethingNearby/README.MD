This is my first mod! It is a simple mod that scrapes the console logs and looks for messages like "Deer spawned" or "Dungeon Loaded" and then places a message within the in-game UI for you to be notified.
Uses BepInEx and Jotunn.

Configurables:
Turn messages on / off
Message color
Message timings
Message positions

Limitations:
- Only English messaging for now (localization can be added in a future version)
- It won't inform you of creatures that already exist in an area.
- It only scrapes the logs, so if the logging changes it may break
- Text is always right justified for now, I may add this to the config in future.
