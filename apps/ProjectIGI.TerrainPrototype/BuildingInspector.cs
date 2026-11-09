using ProjectIGI.Core.Numerics;
using ProjectIGI.Formats;
using ProjectIGI.Formats.Ilff;
using ProjectIGI.Formats.World;
using ProjectIGI.Game.Igi2.World;

namespace ProjectIGI.TerrainPrototype;

internal static class BuildingInspector
{
    private static readonly string[] PackPaths =
    [
        Path.Combine("COMMON", "common"),
        Path.Combine("COMMON", "new"),
        Path.Combine("MISSIONS", "location1", "common", "location1"),
        Path.Combine("MISSIONS", "location1", "level1", "level1"),
        Path.Combine("MISSIONS", "location2", "LEVEL6", "level6"),
        Path.Combine("MISSIONS", "location2", "LEVEL4", "level4"),
        Path.Combine("MISSIONS", "location1", "LEVEL7", "level7"),
        Path.Combine("MISSIONS", "location1", "LEVEL5", "level5"),
    ];

    /// <summary>The mission's packs in load order (paths without extension).</summary>
    public static IReadOnlyList<string> MissionPackPaths => PackPaths;

    /// <summary>The packs a retail level loads: the shared ones, its location's, and its own (paths without extension).</summary>
    public static string[] LevelPacks(string location, string level) =>
    [
        Path.Combine("COMMON", "common"),
        Path.Combine("COMMON", "new"),
        Path.Combine("MISSIONS", location, "common", location == "multiplayer" ? "multiplayer" : location),
        Path.Combine("MISSIONS", location, level, level.ToLowerInvariant()),
    ];

    public readonly record struct Placed(string Model, Vector3d Position, Vector3f Orientation);

    /// <summary>Every model instance name the given packs declare (by default the mission's).</summary>
    public static IReadOnlyList<string> ModelNames(string gameDirectory, string[]? packPaths = null)
    {
        string root = Path.GetFullPath(gameDirectory);
        return [.. (packPaths ?? PackPaths).Select(path => MtpPack.Read(File.ReadAllBytes(Path.Combine(root, path + ".mtp"))))
            .SelectMany(pack => pack.Instances).Select(instance => instance.Name).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    public static Func<string, MefModel?> CreateResolver(string gameDirectory, string[]? packPaths = null)
    {
        string root = Path.GetFullPath(gameDirectory);
        packPaths ??= PackPaths;
        MtpPack[] packs = [.. packPaths.Select(path => MtpPack.Read(File.ReadAllBytes(Path.Combine(root, path + ".mtp"))))];
        IresArchive[] archives = [.. packPaths.Select(path => IresArchive.Read(File.ReadAllBytes(
            Path.Combine(root, Path.GetDirectoryName(path)!, "models", Path.GetFileName(path) + ".res"))))];
        Igi2ModelTable table = Igi2ModelTable.Create(packs);
        Dictionary<string, MefModel?> cache = new(StringComparer.OrdinalIgnoreCase);
        return name =>
        {
            if (cache.TryGetValue(name, out MefModel? found))
            {
                return found;
            }

            MefModel? model = null;
            if (table.TryResolveMesh(name, out string? mesh))
            {
                foreach (IresArchive archive in archives)
                {
                    if (archive.FindBody(mesh + ".mef") is { } entry)
                    {
                        model = MefModel.Read(entry.Data, AssetFormatProfile.Igi2);
                        break;
                    }
                }
            }

            cache[name] = model;
            return model;
        };
    }

    /// <summary>
    /// Counts the portal faces a door's own portal trace crosses among the given buildings and their attached sectors:
    /// the segment <c>Door_AttachToPortal</c> traces, from its render centre half a metre either way along its own Y.
    /// A door that is to shut a doorway must cross at least one.
    /// </summary>
    public static int DoorPortalHits(Func<string, MefModel?> resolve, IReadOnlyList<Placed> buildings, Placed door)
    {
        if (resolve(door.Model) is not { } doorModel)
        {
            return -1;
        }

        Igi2Pose doorPose = Igi2Pose.FromAngles(door.Position, door.Orientation);
        Vector3f c = doorModel.RenderSphereCentre;
        Vector3d start = Transform(doorPose, new Vector3f(c.X, c.Y - 2048f, c.Z));
        Vector3d end = Transform(doorPose, new Vector3f(c.X, c.Y + 2048f, c.Z));
        int hits = 0;
        foreach (Placed building in buildings)
        {
            if (Math.Abs(building.Position.X - start.X) > 150 * 4096 || Math.Abs(building.Position.Y - start.Y) > 150 * 4096)
            {
                continue;
            }

            void Walk(string name, Igi2Pose pose, int depth)
            {
                if (depth > 4 || resolve(name) is not { } model)
                {
                    return;
                }

                if (MefCollisionMesh.TryRead(model.Container, 0, out MefCollisionMesh? mesh, AssetFormatProfile.Igi2.Collision)
                    && mesh is not null)
                {
                    foreach (MefCollisionFace face in mesh.Faces)
                    {
                        if (mesh.IsPortal(face.Material)
                            && SegmentHits(start, end, Transform(pose, mesh.Vertices[face.I0]),
                                Transform(pose, mesh.Vertices[face.I1]), Transform(pose, mesh.Vertices[face.I2])))
                        {
                            hits++;
                        }
                    }
                }

                foreach (MefAttachment attachment in model.Attachments)
                {
                    Walk(attachment.Name, pose.Attach(attachment), depth + 1);
                }
            }

            Walk(building.Model, Igi2Pose.FromAngles(building.Position, building.Orientation), 0);
        }

        return hits;
    }

    private static Vector3d Transform(Igi2Pose pose, Vector3f v)
    {
        IReadOnlyList<float> o = pose.Orientation;
        return new Vector3d(
            pose.Position.X + (o[0] * v.X) + (o[1] * v.Y) + (o[2] * v.Z),
            pose.Position.Y + (o[3] * v.X) + (o[4] * v.Y) + (o[5] * v.Z),
            pose.Position.Z + (o[6] * v.X) + (o[7] * v.Y) + (o[8] * v.Z));
    }

    private static bool SegmentHits(Vector3d from, Vector3d to, Vector3d a, Vector3d b, Vector3d c)
    {
        Vector3d direction = to - from;
        Vector3d e1 = b - a;
        Vector3d e2 = c - a;
        Vector3d p = Vector3d.Cross(direction, e2);
        double det = Vector3d.Dot(e1, p);
        if (Math.Abs(det) < 1e-9)
        {
            return false;
        }

        Vector3d s = from - a;
        double u = Vector3d.Dot(s, p) / det;
        if (u < 0 || u > 1)
        {
            return false;
        }

        Vector3d q = Vector3d.Cross(s, e1);
        double v = Vector3d.Dot(direction, q) / det;
        if (v < 0 || u + v > 1)
        {
            return false;
        }

        double t = Vector3d.Dot(e2, q) / det;
        return t >= 0 && t <= 1;
    }

    public static void PrintCandidates(string gameDirectory, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        string root = Path.GetFullPath(gameDirectory);
        MtpPack[] packs = [.. PackPaths.Select(path => MtpPack.Read(File.ReadAllBytes(Path.Combine(root, path + ".mtp"))))];
        IresArchive[] archives = [.. PackPaths.Select(path => IresArchive.Read(File.ReadAllBytes(
            Path.Combine(root, Path.GetDirectoryName(path)!, "models", Path.GetFileName(path) + ".res"))))];
        Igi2ModelTable table = Igi2ModelTable.Create(packs);
        Dictionary<string, MefModel?> models = new(StringComparer.OrdinalIgnoreCase);

        MefModel? Resolve(string name)
        {
            if (models.TryGetValue(name, out MefModel? model))
            {
                return model;
            }

            if (table.TryResolveMesh(name, out string? mesh))
            {
                foreach (IresArchive archive in archives)
                {
                    if (archive.FindBody(mesh + ".mef") is { } entry)
                    {
                        model = MefModel.Read(entry.Data, AssetFormatProfile.Igi2);
                        break;
                    }
                }
            }

            models.Add(name, model);
            return model;
        }

        output.WriteLine("Candidate models with attached portal sectors (not proof of navigability):");
        foreach (string name in packs.SelectMany(pack => pack.Instances)
            .Select(instance => instance.Name).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Resolve(name) is not { Attachments.Count: > 0 } shell)
            {
                continue;
            }

            string[] rooms = [.. shell.Attachments
                .Where(attachment => Resolve(attachment.Name)?.IsSector == true)
                .Select(attachment => attachment.Name)];
            if (rooms.Length == 0)
            {
                continue;
            }

            MefVertex[] vertices = shell.Vertices.ToArray();
            if (vertices.Length == 0)
            {
                throw new InvalidDataException($"Candidate model '{name}' has no render vertices.");
            }

            const float unitsPerMetre = 4096f;
            float width = (vertices.Max(v => v.Position.X) - vertices.Min(v => v.Position.X)) / unitsPerMetre;
            float length = (vertices.Max(v => v.Position.Y) - vertices.Min(v => v.Position.Y)) / unitsPerMetre;
            float height = (vertices.Max(v => v.Position.Z) - vertices.Min(v => v.Position.Z)) / unitsPerMetre;
            output.WriteLine(FormattableString.Invariant(
                $"{name}: {width:F1} x {length:F1} x {height:F1} m; {shell.Portals.Portals.Count} shell portals; attached sectors: {string.Join(", ", rooms)}"));
        }
    }

    /// <summary>
    /// Prints the walkable floor area of a model and everything attached to it by height, which separates storeys from
    /// stair flights: a storey is a wide band, a flight many narrow bands a step apart.
    /// </summary>
    public static void PrintFloors(string gameDirectory, IEnumerable<string> names, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        string root = Path.GetFullPath(gameDirectory);
        MtpPack[] packs = [.. PackPaths.Select(path => MtpPack.Read(File.ReadAllBytes(Path.Combine(root, path + ".mtp"))))];
        IresArchive[] archives = [.. PackPaths.Select(path => IresArchive.Read(File.ReadAllBytes(
            Path.Combine(root, Path.GetDirectoryName(path)!, "models", Path.GetFileName(path) + ".res"))))];
        Igi2ModelTable table = Igi2ModelTable.Create(packs);
        MefModel? Resolve(string name)
        {
            if (!table.TryResolveMesh(name, out string? mesh))
            {
                return null;
            }

            foreach (IresArchive archive in archives)
            {
                if (archive.FindBody(mesh + ".mef") is { } entry)
                {
                    return MefModel.Read(entry.Data, AssetFormatProfile.Igi2);
                }
            }

            return null;
        }

        const float unitsPerMetre = 4096f;
        foreach (string name in names)
        {
            SortedDictionary<int, double> floors = [];
            Dictionary<int, (double X, double Y)> centres = [];
            double sloped = 0;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            List<string> parts = [];
            void Walk(string model, Igi2Pose pose, int depth)
            {
                if (depth > 4 || Resolve(model) is not { } mef)
                {
                    return;
                }

                parts.Add(model);
                if (MefCollisionMesh.TryRead(mef.Container, 0, out MefCollisionMesh? mesh, AssetFormatProfile.Igi2.Collision)
                    && mesh is not null)
                {
                    IReadOnlyList<float> o = pose.Orientation;
                    Vector3d World(Vector3f v) => new(
                        pose.Position.X + (o[0] * v.X) + (o[1] * v.Y) + (o[2] * v.Z),
                        pose.Position.Y + (o[3] * v.X) + (o[4] * v.Y) + (o[5] * v.Z),
                        pose.Position.Z + (o[6] * v.X) + (o[7] * v.Y) + (o[8] * v.Z));
                    foreach (MefCollisionFace face in mesh.Faces)
                    {
                        if (mesh.IsPortal(face.Material))
                        {
                            continue;
                        }

                        Vector3d a = World(mesh.Vertices[face.I0]) / unitsPerMetre;
                        Vector3d b = World(mesh.Vertices[face.I1]) / unitsPerMetre;
                        Vector3d c = World(mesh.Vertices[face.I2]) / unitsPerMetre;
                        foreach (Vector3d corner in (Vector3d[])[a, b, c])
                        {
                            minX = Math.Min(minX, corner.X); maxX = Math.Max(maxX, corner.X);
                            minY = Math.Min(minY, corner.Y); maxY = Math.Max(maxY, corner.Y);
                        }

                        Vector3d n = Vector3d.Cross(b - a, c - a);
                        double area = n.Length / 2;
                        if (area <= 0)
                        {
                            continue;
                        }

                        double up = Math.Abs(n.Z) / n.Length;
                        if (up > 0.95)
                        {
                            int band = (int)Math.Round((a.Z + b.Z + c.Z) / 3 * 4);
                            floors[band] = floors.GetValueOrDefault(band) + area;
                            (double cx, double cy) = centres.GetValueOrDefault(band);
                            centres[band] = (cx + (area * (a.X + b.X + c.X) / 3), cy + (area * (a.Y + b.Y + c.Y) / 3));
                        }
                        else if (up > 0.5)
                        {
                            sloped += area;
                        }
                    }
                }

                foreach (MefAttachment attachment in mef.Attachments)
                {
                    Walk(attachment.Name, pose.Attach(attachment), depth + 1);
                }
            }

            Walk(name, Igi2Pose.FromAngles(default, default), 0);
            output.WriteLine(FormattableString.Invariant($"{name}: x {minX:F1}..{maxX:F1} y {minY:F1}..{maxY:F1}; parts {string.Join(",", parts.Distinct())}; sloped {sloped:F0} m2"));

            output.WriteLine("  floor bands (height m: horizontal area m2, >= 1 m2): " + string.Join("  ", floors
                .Where(pair => pair.Value >= 1)
                .Select(pair => FormattableString.Invariant($"{pair.Key / 4.0:F2}:{pair.Value:F0}@({centres[pair.Key].X / pair.Value:F1},{centres[pair.Key].Y / pair.Value:F1})"))));
        }
    }

    /// <summary>
    /// The solid (non-portal) collision triangles of placed models and everything they attach, in world units. People walk
    /// against mesh level 1 (<c>Igi2Human</c>'s footing and wall sweeps); rounds and sight use level 0.
    /// </summary>
    public static List<(Vector3d A, Vector3d B, Vector3d C)> SolidTriangles(Func<string, MefModel?> resolve, IEnumerable<Placed> placed,
        int level = 1, List<string>? parts = null)
    {
        List<(Vector3d, Vector3d, Vector3d)> triangles = [];
        void Walk(string name, Igi2Pose pose, int depth)
        {
            if (depth > 4 || resolve(name) is not { } model)
            {
                return;
            }

            if (MefCollisionMesh.TryRead(model.Container, level, out MefCollisionMesh? mesh, AssetFormatProfile.Igi2.Collision)
                && mesh is not null)
            {
                foreach (MefCollisionFace face in mesh.Faces)
                {
                    if (!mesh.IsPortal(face.Material))
                    {
                        triangles.Add((Transform(pose, mesh.Vertices[face.I0]), Transform(pose, mesh.Vertices[face.I1]),
                            Transform(pose, mesh.Vertices[face.I2])));
                        parts?.Add($"{name}@{depth}");
                    }
                }
            }

            foreach (MefAttachment attachment in model.Attachments)
            {
                Walk(attachment.Name, pose.Attach(attachment), depth + 1);
            }
        }

        foreach (Placed item in placed)
        {
            Walk(item.Model, Igi2Pose.FromAngles(item.Position, item.Orientation), 0);
        }

        return triangles;
    }

    /// <summary>The fraction along the segment of the nearest solid triangle it meets, or <see langword="null"/>.</summary>
    public static double? FirstHit(IReadOnlyList<(Vector3d A, Vector3d B, Vector3d C)> triangles, Vector3d from, Vector3d to)
    {
        double? nearest = null;
        Vector3d direction = to - from;
        foreach ((Vector3d a, Vector3d b, Vector3d c) in triangles)
        {
            Vector3d e1 = b - a;
            Vector3d e2 = c - a;
            Vector3d p = Vector3d.Cross(direction, e2);
            double det = Vector3d.Dot(e1, p);
            if (Math.Abs(det) < 1e-9)
            {
                continue;
            }

            Vector3d s = from - a;
            double u = Vector3d.Dot(s, p) / det;
            if (u < 0 || u > 1)
            {
                continue;
            }

            Vector3d q = Vector3d.Cross(s, e1);
            double v = Vector3d.Dot(direction, q) / det;
            if (v < 0 || u + v > 1)
            {
                continue;
            }

            double t = Vector3d.Dot(e2, q) / det;
            if (t >= 0 && t <= 1 && (nearest is null || t < nearest))
            {
                nearest = t;
            }
        }

        return nearest;
    }

    /// <summary>The portal faces of a model and its attachments, grouped by plane into openings, in the model's own frame.</summary>
    public static List<(string Part, int Depth, Vector3d Centre, Vector3d Normal, double Width, double Height)> Openings(
        Func<string, MefModel?> resolve, string name)
    {
        List<(string, int, Vector3d, Vector3d, double, double)> openings = [];
        void Walk(string model, Igi2Pose pose, int depth)
        {
            if (depth > 4 || resolve(model) is not { } mef)
            {
                return;
            }

            if (MefCollisionMesh.TryRead(mef.Container, 0, out MefCollisionMesh? mesh, AssetFormatProfile.Igi2.Collision)
                && mesh is not null)
            {
                List<(Vector3d A, Vector3d B, Vector3d C, Vector3d N)> portal = [];
                foreach (MefCollisionFace face in mesh.Faces)
                {
                    if (mesh.IsPortal(face.Material))
                    {
                        Vector3d a = Transform(pose, mesh.Vertices[face.I0]) / 4096.0;
                        Vector3d b = Transform(pose, mesh.Vertices[face.I1]) / 4096.0;
                        Vector3d c = Transform(pose, mesh.Vertices[face.I2]) / 4096.0;
                        Vector3d n = Vector3d.Cross(b - a, c - a);
                        if (n.Length > 0)
                        {
                            portal.Add((a, b, c, n / n.Length));
                        }
                    }
                }

                // Faces sharing a plane (normal and offset) and lying within 1 m of each other form one opening.
                List<List<(Vector3d A, Vector3d B, Vector3d C, Vector3d N)>> groups = [];
                foreach (var face in portal)
                {
                    var group = groups.FirstOrDefault(g => Math.Abs(Vector3d.Dot(g[0].N, face.N)) > 0.99
                        && Math.Abs(Vector3d.Dot(g[0].N, face.A - g[0].A)) < 0.05
                        && g.Any(o => (o.A - face.A).Length < 4 || (o.B - face.B).Length < 4 || (o.C - face.C).Length < 4));
                    if (group is null)
                    {
                        groups.Add([face]);
                    }
                    else
                    {
                        group.Add(face);
                    }
                }

                foreach (var group in groups)
                {
                    Vector3d[] corners = [.. group.SelectMany(f => (Vector3d[])[f.A, f.B, f.C])];
                    double minZ = corners.Min(p => p.Z), maxZ = corners.Max(p => p.Z);
                    Vector3d side = Vector3d.Cross(group[0].N, new Vector3d(0, 0, 1));
                    double width = side.Length < 0.1 ? 0 : corners.Max(p => Vector3d.Dot(p, side / side.Length)) - corners.Min(p => Vector3d.Dot(p, side / side.Length));
                    Vector3d centre = new(corners.Average(p => p.X), corners.Average(p => p.Y), corners.Average(p => p.Z));
                    openings.Add((model, depth, centre, group[0].N, width, maxZ - minZ));
                }
            }

            foreach (MefAttachment attachment in mef.Attachments)
            {
                Walk(attachment.Name, pose.Attach(attachment), depth + 1);
            }
        }

        Walk(name, Igi2Pose.FromAngles(default, default), 0);
        return openings;
    }

    /// <summary>A door's walk-through axis: its render centre and its own Y, the line its portal trace follows.</summary>
    public static (Vector3d Centre, Vector3d Through)? DoorAxis(Func<string, MefModel?> resolve, Placed door)
    {
        if (resolve(door.Model) is not { } model)
        {
            return null;
        }

        Igi2Pose pose = Igi2Pose.FromAngles(door.Position, door.Orientation);
        Vector3f c = model.RenderSphereCentre;
        Vector3d centre = Transform(pose, c);
        Vector3d ahead = Transform(pose, new Vector3f(c.X, c.Y + 4096f, c.Z));
        return (centre, (ahead - centre) / 4096.0);
    }
}
