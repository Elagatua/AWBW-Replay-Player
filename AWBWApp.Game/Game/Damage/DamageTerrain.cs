using System;
using AWBWApp.Game.Game.Building;
using AWBWApp.Game.Game.Tile;

namespace AWBWApp.Game.Game.Damage
{
    /// <summary>
    /// Names the damage charts use for terrain, and how to work out which one a tile is.
    /// </summary>
    /// <remarks>
    /// The charts draw finer distinctions than <see cref="TerrainType"/> does - a bridge is not a
    /// road and a reef is not open sea - so this cannot simply be the terrain type. Only the four
    /// COs with a terrain bonus ever consult it.
    /// </remarks>
    public static class DamageTerrain
    {
        public const string Plains = "Plains";
        public const string Mountain = "Mountain";
        public const string Forest = "Forest";
        public const string River = "River";
        public const string Road = "Road";
        public const string Bridge = "Bridge";
        public const string Sea = "Sea";
        public const string Shoal = "Shoal";
        public const string Reef = "Reef";
        public const string City = "City";
        public const string Base = "Base";
        public const string Airport = "Airport";
        public const string Port = "Port";
        public const string HQ = "HQ";
        public const string Pipe = "Pipe";
        public const string Silo = "Silo";
        public const string ComTower = "Com Tower";
        public const string Lab = "Lab";

        /// <summary>
        /// The chart name for the tile a unit is standing on, or null where the tile is something
        /// the charts have no opinion about. A building always wins over the tile underneath it.
        /// </summary>
        public static string For(TerrainTile tile, BuildingTile building)
        {
            if (building != null)
            {
                switch (building.BuildingType)
                {
                    case "City": return City;
                    case "Base": return Base;
                    case "Airport": return Airport;
                    case "Port": return Port;
                    case "HQ": return HQ;
                    case "Lab": return Lab;
                    case "ComTower": return ComTower;
                    case "Missile": return Silo;
                    case "PipeSeam": return Pipe;
                }
            }

            if (tile == null)
                return null;

            switch (tile.TerrainType)
            {
                case TerrainType.Mountain:
                    return Mountain;

                case TerrainType.Forest:
                    return Forest;

                case TerrainType.River:
                    return River;

                case TerrainType.Shoal:
                    return Shoal;

                //A reef is the only stretch of sea that defends anything, which is the one thing
                //in the tile data that tells the two apart.
                case TerrainType.Sea:
                    return tile.BaseDefence > 0 ? Reef : Sea;

                //Bridges and roads are identical in every field but their name, and Koal's bonus
                //applies to one and not the other.
                case TerrainType.Road:
                    return tile.Name != null && tile.Name.EndsWith("Bridge", StringComparison.Ordinal) ? Bridge : Road;

                //Pipes are modelled as a building type that nothing can ever capture.
                case TerrainType.Building:
                    return Pipe;

                //A spent missile silo keeps its three defence stars, which is what separates it
                //from the plains it otherwise looks like.
                case TerrainType.Plain:
                    return tile.BaseDefence >= 3 ? Silo : Plains;
            }

            return null;
        }
    }
}
