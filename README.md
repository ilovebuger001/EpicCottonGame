# EpicCottonGame

**EpicCottonGame** is a clean pixel-art cotton tycoon built with **C#**, **.NET 8**, and **Blazor WebAssembly**.

The game is designed around a simple idea: grow valuable cotton, physically collect it, build wealth, upgrade your equipment, discover rare mutations, and eventually own cotton worth absurd amounts of money.

## Core gameplay

`Buy Seed → Inventory → Use → Choose a World Position → Confirm → Grow → Drag Cotton to Bag → Sell / Collect / Trade → Upgrade`

The world is an effectively infinite flat space. Planting uses a subtle world grid only to keep stems separated; there is no fixed farm size, field tier, plot limit, or garden boundary.

Each stem is persistent. When ready cotton is dragged into the Bag, the stem stays in place and begins its next growth cycle.

## Main systems

- Infinite world-space stem placement
- Physical hold-and-drag cotton collection
- Persistent stems with automatic regrowth
- One combined in-world Shop using the Global Market artwork
- Global Market and Upgrade Shop are switchable views inside the combined Shop
- Medic is permanently docked at the bottom of the combined Shop
- Inventory-first purchases with finite Cotton Seeds
- Space centers the camera on the starting point
- Targeted fertilizer and seed use
- Space-to-confirm and Escape-to-cancel interactions
- Infinite Glove and Bag progression
- Glove-tier-dependent cursor artwork
- Rare cotton and mutation collection
- Favorites and selling
- Huge-number money formatting (`K`, `M`, `B`, `T`, `Qa`, ...)
- Browser-local single-account autosave
- Lightweight save-integrity validation for classroom demonstrations

## Technology

- C#
- .NET 8
- Blazor WebAssembly
- Static JSON configuration
- Browser `localStorage` for the local account/save
- GitHub Pages for the presentation build

No server is required for the presentation version.

## Run locally

Install the **.NET 8 SDK**, then from the repository root:

```powershell
dotnet restore
dotnet build
dotnet run
```

The terminal will print the local URL. Open it in a browser.

## Test checklist

1. Wait for the starter stem to produce cotton.
2. Hold the ready cotton and drag it into the physical Bag.
3. Verify that the same stem remains and begins growing again.
4. Open the Market and buy a Cotton Seed.
5. Open Inventory, select the seed, and press **USE**.
6. Select a highlighted empty world position and confirm with **Space**.
7. Buy a fertilizer, select it in Inventory, then choose a growing stem.
8. Verify the fertilizer is consumed and affects the next harvest.
9. Upgrade the Glove and Bag and verify the artwork/cursor changes.
10. Open the combined Shop and switch to Upgrade Shop to verify Glove and Bag upgrades.
11. Use the Medic dock at the bottom of the combined Shop to verify full-heal confirmation and price.
12. Favorite a valuable cotton and test **SELL ALL UNFAVORITED**.
13. Reload the browser and verify the local save returns.

## GitHub Pages

The repository includes a GitHub Actions workflow at:

`.github/workflows/deploy-pages.yml`

Push the project to a GitHub repository using the `main` branch. In the repository settings, enable **Pages** and choose **GitHub Actions** as the source. Each push to `main` will build the Blazor WebAssembly project and publish the generated `wwwroot` site.

## Project layout

```text
EpicCottonGame/
├─ .github/
│  └─ workflows/deploy-pages.yml
├─ App.razor
├─ EpicCottonGame.csproj
├─ Program.cs
├─ Models/
│  └─ GameModels.cs
├─ Pages/
│  └─ Game.razor
├─ Services/
│  ├─ BigIntegerJsonConverter.cs
│  ├─ GameEngine.cs
│  ├─ GameEngineSaveModels.cs
│  ├─ LocalSaveService.cs
│  └─ MoneyFormatter.cs
└─ wwwroot/
   ├─ assets/
   │  ├─ cotton/
   │  ├─ globalmarket.json
   │  ├─ mutations.json
   │  └─ shop.json
   ├─ css/app.css
   └─ index.html
```

## Design philosophy

EpicCottonGame intentionally keeps important interactions inside the world instead of covering the screen with permanent HUD panels. The Shop is a world object, the Bag is a physical draggable object, and the health hearts follow the pointer as part of the game's original clean-world style.

The infinite world is the stage. The economy, equipment, rare cotton, mutations, and collection are what give the player a reason to keep expanding it.

## Additional documentation

- `README_TH.md` — technical project and code-flow documentation in Thai.
- `GITHUB_GUIDE_TH.md` — step-by-step GitHub Pages setup in Thai.
- `ART_ASSET_PROMPT.txt` — complete asset-generation specification.

## Presentation

This project is intended to be presented directly from a browser using the GitHub Pages link. The game does not require installation on the presentation machine once the static site has been deployed.


## Health rules
- Passive regeneration restores 25 HP every 3 seconds, capped at Max HP.
- The first-ever harvest has a one-time safety rule: damage greater than Max HP leaves the player at 1 HP instead of immediately collapsing them.
- The player starts with no free seeds. A single permanent starter stem provides the first harvest; every additional stem requires a Cotton Seed purchased from the Global Market. Seed cost scales as BaseCost^OwnedSeedUnits, where OwnedSeedUnits = planted stems + seeds currently in inventory. The Cotton Seed is always in stock.

## Current Gameplay Systems

- 10 cotton grades: Common, Fine, Premium, Rare, Royal, Epic, Mythic, Celestial, Divine, Golden.
- 10 mutations: Frozen, Burning, Electrified, Toxic, Shadowed, Glitched, Crystallized, Starborn, Voidtouched, Gilded.
- Harvest damage scales strongly with cotton grade and mutation.
- Glove upgrades add +25 Max HP and 3% damage reduction per tier, capped at 90% (capped at 90%).
