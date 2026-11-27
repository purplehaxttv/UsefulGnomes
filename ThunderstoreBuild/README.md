# Useful Gnomes

A mod for [R.E.P.O.](https://store.steampowered.com/app/3241660/REPO/) that turns the gnomes from a nuisance into helpers. They no longer attack, and they fetch valuables and carry them to you.

## What it does

- **Gnomes are passive.** They never notice, chase or attack players or valuables.
- **Gnomes fetch.** An idle gnome picks the nearest valuable, walks to it, lifts it over its head and carries it to the closest living player, then sets it down at their feet.
- **Carried valuables are safe.** A valuable cannot break while a gnome is holding it, and you can take it straight out of a gnome's hands.
- **They stay out of your way.** Gnomes leave alone anything that is in a cart, in the truck, in an extraction point, held by a player, or already close to a player.

## Which valuables get fetched

A gnome only goes for a valuable when all of these are true:

| Rule | Default |
| --- | --- |
| Within fetch range of the gnome | 30 m |
| No bigger than the largest allowed size | Medium |
| Not within the keep-away distance of any player | 4 m |
| Not in a cart, the truck or an extraction point | always |
| Not held by a player, and not already delivered this level | always |
| Close to a walkable spot | always |

A valuable a gnome could not reach is skipped for a minute and then tried again.

## Settings

Change these in the in-game mod settings (with REPOConfig installed) or in `BepInEx/config/com.purplehax.usefulgnomes.cfg`.

| Section | Setting | Default | What it does |
| --- | --- | --- | --- |
| Fetch | Gnomes fetch valuables | true | Turn fetching off to keep gnomes passive only |
| Fetch | Fetch range | 30 | How far a gnome looks for something to fetch, in metres |
| Fetch | Largest valuable | Medium | The biggest size of valuable a gnome will carry |
| Fetch | Gnome speed | 3 | How fast a gnome moves while fetching |
| Fetch | Leave alone near players | 4 | Valuables this close to a player are left where they are |
| Debug | Verbose log | false | Log every pick up and delivery |

## Multiplayer

The host runs the gnomes, and the host's settings apply. Carried valuables are moved by the host and synced to everyone the same way any other moving valuable is.

## License

MIT