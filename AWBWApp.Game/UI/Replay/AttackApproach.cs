using System;
using System.Collections.Generic;
using osu.Framework.Graphics.Primitives;

namespace AWBWApp.Game.UI.Replay
{
    /// <summary>
    /// Works out which tile a direct attack is launched from.
    /// </summary>
    /// <remarks>
    /// A direct unit hardly ever fires from where it is standing: it walks up to its target first,
    /// and the tile it stops on is the ground that defends it when the counterattack lands. So the
    /// gesture lets the cursor choose that tile - whichever tile beside the target it last passed
    /// over is the one the attack is measured from.
    /// <para>
    /// Nothing here asks whether the attacker could really get there. The tooltip answers a
    /// what-if, and refusing to answer it because of a mountain in the way is not the point.
    /// </para>
    /// </remarks>
    public static class AttackApproach
    {
        private static readonly Vector2I[] neighbour_offsets =
        {
            new Vector2I(0, -1),
            new Vector2I(-1, 0),
            new Vector2I(1, 0),
            new Vector2I(0, 1)
        };

        /// <param name="attacker">Where the attacking unit is standing.</param>
        /// <param name="target">The tile being attacked.</param>
        /// <param name="mapSize">Used only to keep the fallback on the board.</param>
        /// <param name="trail">Tiles the cursor has crossed since the gesture began, oldest first.</param>
        public static Vector2I ChooseAttackFrom(Vector2I attacker, Vector2I target, Vector2I mapSize, IReadOnlyList<Vector2I> trail)
        {
            if (trail != null)
            {
                for (var i = trail.Count - 1; i >= 0; i--)
                {
                    if (IsAdjacent(trail[i], target))
                        return trail[i];
                }
            }

            //A cursor can reach a unit without ever crossing a tile beside it - coming in from off
            //the board, or simply moving further in one frame than this is sampled. Walking up to
            //it from the nearest side is the reading least likely to surprise.
            var best = attacker;
            var bestDistance = int.MaxValue;

            foreach (var offset in neighbour_offsets)
            {
                var candidate = target + offset;

                if (candidate.X < 0 || candidate.Y < 0 || candidate.X >= mapSize.X || candidate.Y >= mapSize.Y)
                    continue;

                var distance = Math.Abs(candidate.X - attacker.X) + Math.Abs(candidate.Y - attacker.Y);

                if (distance >= bestDistance)
                    continue;

                best = candidate;
                bestDistance = distance;
            }

            return best;
        }

        /// <summary>
        /// Orthogonally adjacent, which is the only kind of adjacency this game has.
        /// </summary>
        public static bool IsAdjacent(Vector2I first, Vector2I second) =>
            Math.Abs(first.X - second.X) + Math.Abs(first.Y - second.Y) == 1;
    }
}
