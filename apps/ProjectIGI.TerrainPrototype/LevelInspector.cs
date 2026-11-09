using ProjectIGI.Core.Numerics;
using ProjectIGI.Engine.Flow;
using ProjectIGI.Engine.Level;
using ProjectIGI.Engine.Tasks;
using ProjectIGI.Engine.Titles;
using ProjectIGI.Game.Igi2;
using ProjectIGI.Game.Igi2.Interaction;
using ProjectIGI.Game.Igi2.World;

namespace ProjectIGI.TerrainPrototype;

/// <summary>
/// Lists how an installed level places interactive objects around its buildings, without copying anything from it.
/// </summary>
internal static class LevelInspector
{
    private const float UnitsPerMetre = 4096f;

    public static TaskTree Load(string objectsScript)
    {
        string full = Path.GetFullPath(objectsScript);
        string gameRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(full)!, "..", "..", ".."));
        GuestTitle title = GuestTitle.Start(new Igi2Runtime(), gameRoot);
        GameLoop loop = title.Loop;
        LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        string relative = "LOCAL:" + Path.GetRelativePath(gameRoot, full).Replace('\\', '/');
        new LevelLoader(levelRoot, loop.TaskCommands, loop.Natives, title.Resources).LoadScript(relative);
        return loop.Tasks;
    }

    public static void Print(string objectsScript, TextWriter output)
    {
        TaskTree tree = Load(objectsScript);
        Igi2BuildingTask[] buildings = [.. tree.Root.Descendants().OfType<Igi2BuildingTask>()];
        (GameTask Task, Vector3d Position)[] placed = [.. tree.Root.Descendants()
            .Where(task => task is not Igi2BuildingTask)
            .Select(task => (Task: task, Position: PositionOf(task)))
            .Where(item => item.Position is not null)
            .Select(item => (item.Task, item.Position!.Value))];
        output.WriteLine($"{buildings.Length} buildings, {placed.Length} other placed tasks");
        foreach (var group in tree.Root.Descendants().GroupBy(task => task.Type.Name).OrderByDescending(g => g.Count()).Take(40))
        {
            output.WriteLine($"  {group.Key}: {group.Count()}");
        }

        foreach (Igi2BuildingTask building in buildings)
        {
            Vector3d origin = building.Position;
            (GameTask Task, Vector3d Offset)[] near = [.. placed
                .Select(item => (item.Task, Offset: item.Position - origin))
                .Where(item => Math.Abs(item.Offset.X) < 60 * UnitsPerMetre && Math.Abs(item.Offset.Y) < 60 * UnitsPerMetre
                    && Math.Abs(item.Offset.Z) < 30 * UnitsPerMetre)
                .Where(item => item.Task is Igi2DoorTask or Igi2LadderTask or Igi2SecurityCameraTask or Igi2TerminalTask
                    or Igi2AlarmControlTask or Igi2CabinetTask)];
            output.WriteLine(FormattableString.Invariant(
                $"Building {building.Id} '{building.Name}' {building.Model} orientation {building.Orientation} at {origin / UnitsPerMetre}: {near.Length} interactive nearby"));
            foreach (var (task, offset) in near.Take(30))
            {
                string model = task switch
                {
                    Igi2DoorTask door => FormattableString.Invariant(
                        $"{door.Model} orient {door.Orientation} max {door.MaxAngle} time {door.OpenTime} both {door.OpenBothWays} portal {door.LinkUpToPortal} delta {door.DeltaScaleFactor} slide {door.StopX},{door.StopY} locked '{door.LockedScript}' pick {door.Pickable} sounds {door.OpenSound}/{door.CloseSound}/{door.MoveSound}/{door.BeginOpenSound}/{door.BeginCloseSound}"),
                    _ => string.Empty,
                };
                output.WriteLine(FormattableString.Invariant(
                    $"    {task.Type.Name} {task.Id} '{task.Name}' {model} at {offset.X / UnitsPerMetre:F2},{offset.Y / UnitsPerMetre:F2},{offset.Z / UnitsPerMetre:F2} m"));
            }
        }
    }

    public static void PrintTypes(string objectsScript, IEnumerable<string> types, TextWriter output)
    {
        TaskTree tree = Load(objectsScript);
        foreach (string type in types)
        {
            foreach (GameTask task in tree.Root.Descendants().Where(task => task.Type.Name == type).Take(4))
            {
                IEnumerable<string> names = task.GetType()
                    .GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .SelectMany(member => member.GetCustomAttributes(typeof(ScriptParamAttribute), true).Cast<ScriptParamAttribute>())
                    .Select(attribute => attribute.Name ?? string.Empty);
                output.WriteLine($"{type} {task.Id} '{task.Name}' parent {task.Parent?.Type.Name}");
                foreach (string name in names)
                {
                    object? value = task.TryGetScriptValue(name, out object? found) ? found : null;
                    output.WriteLine(FormattableString.Invariant($"    {name} = {value}"));
                }
            }
        }
    }

    /// <summary>
    /// Lists, for every retail level that places one of the given building models, the props (<c>EditRigidObj</c>) it puts
    /// inside that building, in the building's own frame: how the original designers furnished each interior.
    /// </summary>
    public static void PrintFurnishings(string gameDirectory, IEnumerable<string> models, TextWriter output)
    {
        HashSet<string> wanted = new(models, StringComparer.OrdinalIgnoreCase);
        string root = Path.GetFullPath(gameDirectory);
        foreach (string script in Directory.EnumerateFiles(Path.Combine(root, "MISSIONS"), "objects.qvm", SearchOption.AllDirectories))
        {
            string levelDirectory = Path.GetDirectoryName(script)!;
            string level = Path.GetFileName(levelDirectory);
            string location = Path.GetFileName(Path.GetDirectoryName(levelDirectory)!);
            TaskTree tree;
            Func<string, ProjectIGI.Formats.World.MefModel?> resolve;
            try
            {
                tree = Load(script);
                resolve = BuildingInspector.CreateResolver(root, BuildingInspector.LevelPacks(location, level));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException)
            {
                output.WriteLine($"{location}/{level}: skipped ({exception.Message})");
                continue;
            }

            Igi2EditRigidObjTask[] props = [.. tree.Root.Descendants().OfType<Igi2EditRigidObjTask>()];
            foreach (Igi2BuildingTask building in tree.Root.Descendants().OfType<Igi2BuildingTask>().Where(b => wanted.Contains(b.Model)))
            {
                if (resolve(building.Model) is not { } mef)
                {
                    continue;
                }

                var vertices = mef.Vertices.ToArray();
                Vector3f min = new(vertices.Min(v => v.Position.X), vertices.Min(v => v.Position.Y), vertices.Min(v => v.Position.Z));
                Vector3f max = new(vertices.Max(v => v.Position.X), vertices.Max(v => v.Position.Y), vertices.Max(v => v.Position.Z));
                double heading = building.Orientation.Z;
                double cos = Math.Cos(heading), sin = Math.Sin(heading);
                List<string> inside = [];
                foreach (Igi2EditRigidObjTask prop in props)
                {
                    Vector3d d = prop.Position - building.Position;
                    double lx = (d.X * cos) + (d.Y * sin), ly = (-d.X * sin) + (d.Y * cos);
                    if (lx < min.X - 2048 || lx > max.X + 2048 || ly < min.Y - 2048 || ly > max.Y + 2048
                        || d.Z < min.Z - 2048 || d.Z > max.Z + 2048)
                    {
                        continue;
                    }

                    inside.Add(FormattableString.Invariant(
                        $"    {prop.Model} {lx / UnitsPerMetre:F2} {ly / UnitsPerMetre:F2} {d.Z / UnitsPerMetre:F2} {prop.Orientation.Z - heading:F4} ({prop.Orientation.X:F3},{prop.Orientation.Y:F3})"));
                }

                output.WriteLine(FormattableString.Invariant(
                    $"{location}/{level} Building {building.Id} {building.Model} heading {heading:F4} bounds {min / UnitsPerMetre} to {max / UnitsPerMetre}: {inside.Count} props"));
                foreach (string line in inside)
                {
                    output.WriteLine(line);
                }
            }
        }
    }

    public static void CheckDoors(string objectsScript, string gameDirectory, TextWriter output)
    {
        TaskTree tree = Load(objectsScript);
        Func<string, ProjectIGI.Formats.World.MefModel?> resolve = BuildingInspector.CreateResolver(gameDirectory);
        BuildingInspector.Placed[] buildings = [.. tree.Root.Descendants().OfType<Igi2BuildingTask>()
            .Where(building => !string.IsNullOrEmpty(building.Model))
            .Select(building => new BuildingInspector.Placed(building.Model, building.Position, building.Orientation))];
        int attached = 0, total = 0;
        foreach (Igi2DoorTask door in tree.Root.Descendants().OfType<Igi2DoorTask>())
        {
            int hits = BuildingInspector.DoorPortalHits(resolve, buildings, new BuildingInspector.Placed(door.Model, door.Position, door.Orientation));
            total++;
            attached += hits > 0 ? 1 : 0;
            output.WriteLine($"  Door {door.Id} {door.Model} portal faces crossed: {hits}");
        }

        output.WriteLine($"{attached} of {total} doors cross a portal face");
    }

    private static Vector3d? PositionOf(GameTask task)
    {
        foreach (string name in (string[])["Position", "Position start"])
        {
            if (task.TryGetScriptValue(name, out object? value) && value is Vector3d position)
            {
                return position;
            }
        }

        return null;
    }
}
