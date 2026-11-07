using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GTH6
{
    /// <summary>
    /// Builds one flat road-surface mesh straight from the road graph, so the drivable road and the
    /// graph that traffic and the GPS use can never drift apart. Bends get mitred corners (no gaps or
    /// overlaps), intersections get a round pad, dead ends get a square end.
    /// Blockout quality on purpose: in the art pass you can replace it with modular road pieces.
    /// </summary>
    public static class RoadMeshBuilder
    {
        // Sharp bends would make a mitre corner shoot out to infinity; this caps it.
        const float MaxMiterScale = 3f;

        public static Mesh Build(RoadNetwork network)
        {
            network.Rebuild();

            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            var builtPairs = new HashSet<(RoadNode, RoadNode)>();
            Vector3 lift = Vector3.up * network.surfaceHeightOffset;

            foreach (RoadNode a in network.Nodes)
            {
                if (a == null) continue;
                foreach (RoadNode b in network.NeighboursOf(a))
                {
                    if (b == null || builtPairs.Contains((b, a))) continue;
                    builtPairs.Add((a, b));

                    float halfWidth = network.HalfWidth(a, b);
                    EndCorners(network, a, b, halfWidth, out Vector3 aLeft, out Vector3 aRight);
                    EndCorners(network, b, a, halfWidth, out Vector3 bRight, out Vector3 bLeft); // seen from b, left and right swap

                    float length = Vector3.Distance(a.Position, b.Position);
                    float vTiles = length / (network.laneWidth * 2f);
                    int start = vertices.Count;

                    vertices.Add(aLeft + lift);
                    vertices.Add(aRight + lift);
                    vertices.Add(bLeft + lift);
                    vertices.Add(bRight + lift);
                    uvs.Add(new Vector2(0f, 0f));
                    uvs.Add(new Vector2(1f, 0f));
                    uvs.Add(new Vector2(0f, vTiles));
                    uvs.Add(new Vector2(1f, vTiles));

                    // Clockwise seen from above = faces up in Unity.
                    triangles.Add(start);
                    triangles.Add(start + 2);
                    triangles.Add(start + 3);
                    triangles.Add(start);
                    triangles.Add(start + 3);
                    triangles.Add(start + 1);
                }
            }

            foreach (RoadNode node in network.Nodes)
            {
                if (node == null || network.Degree(node) < 3) continue;
                AddPad(network, node, vertices, uvs, triangles, lift);
            }

            // Everything above is in world space; the mesh lives under the RoadNetwork object.
            Transform root = network.transform;
            for (int i = 0; i < vertices.Count; i++) vertices[i] = root.InverseTransformPoint(vertices[i]);

            var mesh = new Mesh { name = "RoadSurface" };
            if (vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Radius of the round pad at an intersection.</summary>
        public static float PadRadius(RoadNetwork network, RoadNode node)
        {
            float widest = 0f;
            foreach (RoadNode other in network.NeighboursOf(node))
                widest = Mathf.Max(widest, network.HalfWidth(node, other));
            return widest * 1.2f;
        }

        /// <summary>
        /// The two road-edge corners where the strip from "node" toward "other" begins.
        /// Left/right are relative to looking from node toward other.
        /// </summary>
        static void EndCorners(RoadNetwork network, RoadNode node, RoadNode other, float halfWidth, out Vector3 left, out Vector3 right)
        {
            Vector3 p = node.Position;
            Vector3 toOther = other.Position - p;
            Vector3 sideA = RoadNetwork.RightOf(p, other.Position);
            int degree = network.Degree(node);

            if (degree >= 3)
            {
                // Start the strip at the edge of the intersection pad, but never past the middle of a short road.
                float flatLength = new Vector3(toOther.x, 0f, toOther.z).magnitude;
                float inset = Mathf.Min(PadRadius(network, node), flatLength * 0.45f);
                float t = flatLength > 0.001f ? inset / flatLength : 0f;
                Vector3 centre = p + toOther * t; // follows the slope
                left = centre - sideA * halfWidth;
                right = centre + sideA * halfWidth;
                return;
            }

            if (degree == 2)
            {
                RoadNode third = null;
                foreach (RoadNode n in network.NeighboursOf(node))
                    if (n != other) third = n;

                if (third != null)
                {
                    // Mitre: the right edge of this road continues as the left edge of the next one.
                    Vector3 sideB = RoadNetwork.RightOf(p, third.Position);
                    Vector3 miter = sideA - sideB;
                    if (miter.sqrMagnitude > 0.0001f)
                    {
                        miter.Normalize();
                        float dot = Vector3.Dot(miter, sideA);
                        float scale = Mathf.Min(1f / Mathf.Max(dot, 0.0001f), MaxMiterScale);
                        float averageHalf = (halfWidth + network.HalfWidth(node, third)) * 0.5f;
                        left = p - miter * (averageHalf * scale);
                        right = p + miter * (averageHalf * scale);
                        return;
                    }
                }
            }

            // Dead end, or a U-turn that cannot be mitred: square end.
            left = p - sideA * halfWidth;
            right = p + sideA * halfWidth;
        }

        static void AddPad(RoadNetwork network, RoadNode node, List<Vector3> vertices, List<Vector2> uvs, List<int> triangles, Vector3 offset)
        {
            int segments = network.intersectionSegments;
            float radius = PadRadius(network, node);
            int centre = vertices.Count;

            vertices.Add(node.Position + offset);
            uvs.Add(new Vector2(0.5f, 0.5f));

            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector3 dir = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                vertices.Add(node.Position + dir * radius + offset);
                uvs.Add(new Vector2(0.5f + dir.x * 0.5f, 0.5f + dir.z * 0.5f));
            }

            // Increasing angle turns clockwise seen from above, so (centre, i, i+1) faces up.
            for (int i = 0; i < segments; i++)
            {
                triangles.Add(centre);
                triangles.Add(centre + 1 + i);
                triangles.Add(centre + 1 + (i + 1) % segments);
            }
        }
    }
}
