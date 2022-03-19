# Risk of Rain 2: The Trading Card Game

## Description

Turns item and equipment pickup models into trading cards. Complete with metal foil on Rare and Lunar items!

Provides a config option to use full description text or short pickup text in the description section of a card, or to hide card text completely.

Provides a config option to alter the spin of pickups to face the camera most of the time.

## Issues/TODO

- Void items from SotV have no special background yet.
- Color tags on text are too bright/washed-out.
- Models are situated too low in 3D Printer displays.
- May try to implement a config option for untilted models.
- See the GitHub repo for more!

## Changelog

**1.0.0**

- Initial version (migrated feature from ClassicItems). Turns all pickup models into trading cards bearing the item/equipment's icon and text.
	- Includes configs for: full/short description, hide all text, camera-favoring spin animation.
	- Changes from CI code:
		- Now affects ALL items, instead of only vanilla/CI.
		- Fixed model sizes.
		- Spin anim option now faces camera instead of player body, if possible.
		- Name text is slightly more readable (thicker outline, swapped outline and text colors).