using System.Collections.Generic;
using TheArtifacts.Core.Combat;
using UnityEngine;

namespace TheArtifacts.PC.Combat
{
    /// <summary>Builds flat floor meshes matching Core telegraph areas (graybox visuals).</summary>
    public static class TelegraphMeshes
    {
        const int SegmentsPerCircle = 64;

        public static Vector3 ToWorld(Vec2 p, float y = 0f) => new Vector3(p.X, y, p.Y);

        /// <summary>A mesh in world space for the area, or null if the area has no shape.</summary>
        public static Mesh Build(Area area, float y)
        {
            switch (area)
            {
                case CircleArea c:
                    return AnnularSector(c.Center, 0f, c.Radius, 0f, 360f, Vector2.up, y);
                case RingArea r:
                    return AnnularSector(r.Center, r.InnerRadius, r.OuterRadius, 0f, 360f, Vector2.up, y);
                case ConeArea cone:
                {
                    var facing = new Vector2(cone.Facing.X, cone.Facing.Y);
                    return AnnularSector(cone.Origin, 0f, cone.Range, -cone.AngleDegrees * 0.5f, cone.AngleDegrees * 0.5f, facing, y);
                }
                case LineArea line:
                    return Quad(line, y);
                default:
                    return null;
            }
        }

        static Mesh AnnularSector(Vec2 center, float inner, float outer, float fromDeg, float toDeg, Vector2 facing, float y)
        {
            var span = toDeg - fromDeg;
            var segments = Mathf.Max(4, Mathf.CeilToInt(SegmentsPerCircle * span / 360f));
            var baseAngle = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg;
            var c = ToWorld(center, y);

            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (var i = 0; i <= segments; i++)
            {
                var a = (baseAngle + fromDeg + span * i / segments) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                verts.Add(c + dir * inner);
                verts.Add(c + dir * outer);
                if (i == 0) continue;
                var k = i * 2;
                // Two triangles per segment, wound to face up (+Y).
                tris.AddRange(new[] { k - 2, k, k - 1, k - 1, k, k + 1 });
            }

            return Finish(verts, tris);
        }

        static Mesh Quad(LineArea line, float y)
        {
            var dir = new Vector3(line.Direction.X, 0f, line.Direction.Y);
            var side = new Vector3(-dir.z, 0f, dir.x) * (line.Width * 0.5f);
            var start = ToWorld(line.Origin, y);
            var end = start + dir * line.Length;
            var verts = new List<Vector3> { start - side, start + side, end - side, end + side };
            var tris = new List<int> { 0, 1, 2, 2, 1, 3 };
            return Finish(verts, tris);
        }

        static Mesh Finish(List<Vector3> verts, List<int> tris)
        {
            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            // Make both windings visible regardless of the math above.
            var back = new List<int>(tris.Count);
            for (var i = 0; i < tris.Count; i += 3) back.AddRange(new[] { tris[i], tris[i + 2], tris[i + 1] });
            mesh.subMeshCount = 2;
            mesh.SetTriangles(back, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
