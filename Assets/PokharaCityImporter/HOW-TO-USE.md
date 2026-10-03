# Pokhara City Importer: how to use it

No Blender needed. Everything happens inside Unity.

## What's in this zip

| File | What it is |
|---|---|
| `PokharaCity/` | The importer. Drag this whole folder into your Unity project's **Assets** folder |
| `map.osm` | Lakeside + Damside map data from OpenStreetMap (about 2.0 × 2.2 km) |
| `route.geojson` | Your Damside → Lakeside route (Mission 1) |
| `what-you-should-see.png` | Top view of the city the importer builds, for comparison |

## Step by step (about 5 minutes)

1. **Create the project.** Unity Hub → **New project** → **Universal 3D (URP)** template → name it `PokharaCityDrivingSim` → Create.
2. **Add the importer.** Unzip this file, then drag the `PokharaCity` folder into the **Project** window, onto **Assets**. Wait for Unity to finish compiling (the spinner at the bottom right).
3. **Open the importer.** Top menu: **Tools → Pokhara City → Import OSM Map**. A small window opens.
4. **Choose the map.** Click **Choose .osm file** and pick `map.osm` from the unzipped folder.
5. **Build.** Leave all the boxes ticked and click **Build Pokhara City**. After a few seconds a box shows how many roads, buildings and lanes were made (expect about 465 roads, 4,853 buildings, 1,196 lanes).
6. **Look around.** In the **Hierarchy**, double-click **Pokhara City (generated)** so the Scene view zooms to it. The lake is on the left (west); Lakeside Marg runs along it.
7. **See the traffic lanes.** Expand the city object and click **Road Network**. Cyan lines with small arrows appear on the left side of every road, which is where AI cars will drive later.
8. **Import the mission route.** **Tools → Pokhara City → Import Route (GeoJSON)** → pick `route.geojson`. A red line appears along your route, with a green ball at the start (Damside) and a black ball at the finish.
9. **Drive it.** Copy your car prefab from the trial simulator into this project (see "Bringing your car over" below), drag it into the scene, keep it selected, then click **Tools → Pokhara City → Put Selected Car At Route Start**. Press **Play**.

## Bringing your car over from Nepal-Driving-Test

1. In the old project, right-click your car prefab → **Export Package…** → keep "Include dependencies" ticked → Export.
2. In the new project: **Assets → Import Package → Custom Package…** → pick that file → Import.

This brings the car together with its scripts (ProManualCarController, CarIndicators, cameras and so on).

## Things to know

- **Rebuilding is safe.** Click Build again after changing settings and the old city is replaced. Materials in `Assets/PokharaCity/Generated/Materials` are kept, so you can recolour them or give them textures and they survive rebuilds.
- **Building heights.** Most Pokhara buildings in OpenStreetMap have no height, so the importer gives them 2–4 floors, always the same for the same building. Change the range in the window.
- **The lake has no collider yet**, so the car can drive onto the water. We will add a shore barrier or a "fell in the lake" fail in Week 3.
- **The north end of the route** (near Lakeside's northern edge) is just outside this map. To include it, export a slightly bigger area (Top: `28.2220`) and rebuild.
- **Credit.** Map data © OpenStreetMap contributors (ODbL). Keep this line in your game's credits and README.
