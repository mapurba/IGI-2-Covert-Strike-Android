using System.Buffers.Binary;
using System.Text;
using ProjectIGI.Engine.Flow;
using ProjectIGI.Engine.Level;
using ProjectIGI.Engine.Resources;
using ProjectIGI.Engine.Tasks;
using ProjectIGI.Engine.Vfs;
using ProjectIGI.Formats.Ilff;
using ProjectIGI.Formats.World;
using ProjectIGI.Game.Igi2.Ai;
using ProjectIGI.Game.Igi2.Player;
using ProjectIGI.Game.Igi2.World;
using ProjectIGI.Scripting.Qvm;
using ProjectIGI.Scripting.Runtime;

namespace ProjectIGI.TerrainPrototype;

internal static class Program
{
    private const int Size = 1024;  // Epic scale: 1024x1024 unit terrain
    private const float UnitsPerMetre = 4096f;
    private const string HeightName = "heightmap000.thm";
    private const string MissionDirectory = "missions/location9/level9";

    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--self-test")
        {
            SelfTest();
            Console.WriteLine("Terrain prototype self-test passed.");
            return 0;
        }

        if (args.Length != 1 || args[0].StartsWith('-'))
        {
            Console.Error.WriteLine("Usage: dotnet run --project apps/ProjectIGI.TerrainPrototype -- <new-output-directory>");
            Console.Error.WriteLine("       dotnet run --project apps/ProjectIGI.TerrainPrototype -- --self-test");
            return 1;
        }

        string output = Path.GetFullPath(args[0]);
        if (Directory.Exists(output) || File.Exists(output))
        {
            Console.Error.WriteLine($"Output path already exists: {output}. Choose a new directory.");
            return 1;
        }

        byte[] heightmap = CreateHeightmap();
        byte[] archive = CreateArchive(heightmap);
        VerifyArchive(archive, heightmap);
        byte[] preview = CreatePreview(heightmap);
        byte[] missionScript = CreateMissionScript();
        byte[] levelScript = CreateLevelPrototypeScript();
        byte[] objectsScript = CreateObjectsScript();

        Directory.CreateDirectory(output);
        using (FileStream file = new(Path.Combine(output, "heightmaps.res"), FileMode.CreateNew))
        {
            file.Write(archive);
        }

        using (FileStream file = new(Path.Combine(output, "terrain-preview.pgm"), FileMode.CreateNew))
        {
            file.Write(preview);
        }

        using (FileStream file = new(Path.Combine(output, "mission.qvm"), FileMode.CreateNew))
        {
            file.Write(missionScript);
        }

        using (FileStream file = new(Path.Combine(output, "level.prototype.qvm"), FileMode.CreateNew))
        {
            file.Write(levelScript);
        }

        using (FileStream file = new(Path.Combine(output, "objects.prototype.txt"), FileMode.CreateNew))
        {
            file.Write(objectsScript);
        }

        Console.WriteLine($"Created {output}");
        Console.WriteLine("heightmaps.res: original prototype terrain tile");
        Console.WriteLine("terrain-preview.pgm: grayscale height preview (bright = high)");
        Console.WriteLine("mission.qvm: compiled mission descriptor (not yet deployable)");
        Console.WriteLine("level.prototype.qvm: compiled terrain, player and guard task prototypes");
        Console.WriteLine("objects.prototype.txt: illustrative level design, not an executable script");
        Console.WriteLine("This is a work-in-progress mission prototype. A playable level is not yet implemented.");
        return 0;
    }

    private static byte[] CreateHeightmap()
    {
        byte[] data = new byte[Igi2TerrainMap.HeightHeaderSize + (Size * Size * sizeof(float))];
        Span<byte> bytes = data;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, Igi2TerrainMap.VersionWord);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[32..], (int)Igi2TerrainMapKind.Height);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[40..], 1f);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[44..], Size);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[48..], Size);

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                double u = (x + 0.5) / Size;
                double v = (y + 0.5) / Size;
                float height = (float)(HeightMetres(u, v) * UnitsPerMetre);
                int offset = Igi2TerrainMap.HeightHeaderSize + (((y * Size) + x) * sizeof(float));
                BinaryPrimitives.WriteSingleLittleEndian(bytes[offset..], height);
            }
        }

        return data;
    }

    private static double HeightMetres(double x, double y)
    {
        double mountain = 150 * Math.Exp(-(((x - 0.20) * (x - 0.20) / 0.035) + ((y - 0.20) * (y - 0.20) / 0.06)));
        double hills = 12 * Math.Exp(-(((x - 0.78) * (x - 0.78) / 0.05) + ((y - 0.22) * (y - 0.22) / 0.08)));
        double height = 8 + mountain + hills;
        height = BlendPlateau(height, x, y, 0.20, 0.20, 0.035, 0.075, 145);
        height = BlendPlateau(height, x, y, 0.60, 0.48, 0.07, 0.13, 14);
        return BlendPlateau(height, x, y, 0.76, 0.78, 0.11, 0.19, 0);
    }

    private static double BlendPlateau(double height, double x, double y, double cx, double cy, double inner, double outer, double target)
    {
        double distance = Math.Sqrt(((x - cx) * (x - cx)) + ((y - cy) * (y - cy)));
        double t = Math.Clamp((distance - inner) / (outer - inner), 0, 1);
        double blend = t * t * (3 - (2 * t));
        return target + ((height - target) * blend);
    }

    private static byte[] CreateArchive(byte[] heightmap) =>
        IresWriter.Write([new IresWriter.Entry(HeightName, IresEntryKind.Body, heightmap)]);

    private static byte[] CreateMissionScript()
    {
        return QvmExpression.BuildProgram(QvmExpression.Call(
            "DefineMission",
            QvmExpression.Int(99),
            QvmExpression.Text("S"),
            QvmExpression.Text("N"),
            QvmExpression.Text("G"),
            QvmExpression.Text("L"),
            QvmExpression.Text("LONG_DESCENT_NAME"),
            QvmExpression.Text("LONG_DESCENT_DESC"),
            QvmExpression.Text(string.Empty),
            QvmExpression.Text(MissionDirectory),
            QvmExpression.Text("missions/location9/common"),
            QvmExpression.Text(MissionDirectory),
            QvmExpression.Text("location9"),
            QvmExpression.Text("level9"),
            QvmExpression.Text("mission99.spr")));
    }

    private static byte[] CreateLevelPrototypeScript()
    {
        QvmExpression terrainMap = QvmExpression.Call(
            "Task_New",
            QvmExpression.Int(-1), QvmExpression.Text("TerrainMap"), QvmExpression.Text("Prototype terrain tile"),
            QvmExpression.Int(0), QvmExpression.Int(Size), QvmExpression.Int(Size),
            QvmExpression.Real(0), QvmExpression.Real(0), QvmExpression.Real(0),
            QvmExpression.Real(Size), QvmExpression.Real(Size), QvmExpression.Real(0), QvmExpression.Text(string.Empty));

        QvmExpression terrain = QvmExpression.Call(
            "Task_New",
            QvmExpression.Int(-1), QvmExpression.Text("Terrain"), QvmExpression.Text("Prototype terrain"),
            QvmExpression.Real(0), QvmExpression.Real(0), QvmExpression.Real(0),
            QvmExpression.Real(Size), QvmExpression.Real(Size),
            QvmExpression.Real(1), QvmExpression.Real(1), QvmExpression.Real(0), QvmExpression.Real(0),
            QvmExpression.Int(0), QvmExpression.Real(0), QvmExpression.Real(0), QvmExpression.Real(0),
            QvmExpression.Real(0), QvmExpression.Real(0), QvmExpression.Int(0),
            terrainMap);

        static QvmExpression Coordinate(double fraction) => QvmExpression.Real((float)((fraction - 0.5) * Size * UnitsPerMetre));
        static QvmExpression Elevation(double x, double y) => QvmExpression.Real((float)(HeightMetres(x, y) * UnitsPerMetre));

        List<QvmExpression> tasks =
        [
            terrain,
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(0), QvmExpression.Text("HumanPlayer"), QvmExpression.Text("Player prototype"),
                Coordinate(0.20), Coordinate(0.20), Elevation(0.20, 0.20),
                QvmExpression.Real(0), QvmExpression.Text(string.Empty), QvmExpression.Int(0),
                QvmExpression.Text(string.Empty), QvmExpression.Text(string.Empty),
                QvmExpression.Real(1), QvmExpression.Real(0), QvmExpression.Real(1), QvmExpression.Int(0)),
        ];

        for (int i = 0; i < 15; i++)
        {
            double angle = 2 * Math.PI * i / 15;
            double x = 0.60 + (0.035 * Math.Cos(angle));
            double y = 0.48 + (0.035 * Math.Sin(angle));
            QvmExpression mind = QvmExpression.Call(
                "Task_New", QvmExpression.Int(500 + i), QvmExpression.Text("HumanAI"), QvmExpression.Text("Guard mind"),
                QvmExpression.Text(Igi2AiConstants.AiTypes[0]), QvmExpression.Text(Igi2AiConstants.AnimTypes[2]),
                QvmExpression.Int(-1));
            tasks.Add(QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(100 + i), QvmExpression.Text("HumanSoldier"), QvmExpression.Text("Guard prototype"),
                Coordinate(x), Coordinate(y), Elevation(x, y), QvmExpression.Real((float)angle),
                QvmExpression.Text(string.Empty), QvmExpression.Int(1), QvmExpression.Text(string.Empty),
                QvmExpression.Int(0), QvmExpression.Int(0), mind));
        }

        return QvmExpression.BuildProgram(tasks);
    }

    private static byte[] CreateObjectsScript()
    {
        // EPIC SCALE MISSION: 1024x1024 terrain
        // Coordinates in terrain grid units (0-1023), converted to world coordinates (x2.0 scale)
        // Mountain lookout (north): ~(100, 100) at ~160m elevation
        // Military Compound (center-south): ~(400, 600) at ~20m elevation  
        // Lake/Extraction (south-east): ~(800, 900) at ~2m elevation
        // Forest/Cover zones around compound perimeter
        
        // Player spawn - elevated mountain position
        double playerX = 100 * 2.0;
        double playerY = 100 * 2.0;
        double playerZ = 160.0;
        double playerAngle = 225.0;  // southwest, facing compound
        
        // Compound center and perimeter positions
        double compX = 400 * 2.0;
        double compY = 600 * 2.0;
        double compZ = 20.0;
        
        // Extraction point - lake shore
        double extractX = 800 * 2.0;
        double extractY = 900 * 2.0;
        double extractZ = 2.0;
        
        var sb = new StringBuilder();
        
        sb.AppendLine("// ============================================================");
        sb.AppendLine("// THE LONG DESCENT - EPIC MILITARY COMPOUND MISSION");
        sb.AppendLine("// Mission 99: Intelligence Extraction");
        sb.AppendLine("// Phase 4 Enhanced: 15-Guard Military Base, 1024x1024 Terrain");
        sb.AppendLine("// ============================================================");
        sb.AppendLine();
        sb.AppendLine("// MISSION BRIEFING:");
        sb.AppendLine("// You are positioned on high ground overlooking an enemy military");
        sb.AppendLine("// compound deep in foreign territory. CIA has confirmed a top-secret");
        sb.AppendLine("// intelligence chip containing weapon locations is stored in the");
        sb.AppendLine("// compound's secure vault. Eliminate or evade the 15-man guard force,");
        sb.AppendLine("// locate and extract the chip, and reach the extraction point at");
        sb.AppendLine("// the lake. Time is critical.");
        sb.AppendLine();
        
        // ============================================================
        // PLAYER SETUP
        // ============================================================
        sb.AppendLine("// --- PLAYER SPAWN & EQUIPMENT ---");
        sb.AppendLine("HumanPlayer(");
        sb.AppendLine(FormattableString.Invariant($"    Position = ({playerX}, {playerY}, {playerZ})"));
        sb.AppendLine(FormattableString.Invariant($"    Gamma = {playerAngle}"));
        sb.AppendLine("    Model = 'player'");
        sb.AppendLine("    Team = 0");
        sb.AppendLine("    Weapon = 'SetupWeapons'");
        sb.AppendLine("    1st Person Model = 'arms_player'");
        sb.AppendLine("    Visibility Gamma = 2.0");
        sb.AppendLine("    Visibility Minimum = 0.1");
        sb.AppendLine("    Visibility Maximum = 0.9");
        sb.AppendLine("    Carry over weapons from previous mission = FALSE");
        sb.AppendLine(");");
        sb.AppendLine();
        
        // Professional loadout: Sniper for long-range, assault rifle for close quarters, knife for silent takedowns
        sb.AppendLine("// Elite operative loadout: long-range + close-quarters + melee");
        sb.AppendLine("SetupWeapons()");
        sb.AppendLine("{");
        sb.AppendLine("    GiveWeapon(SniperRifle);");
        sb.AppendLine("    SetAmmo(SniperRifle, 180);      // Extended sniper ammo for multiple targets");
        sb.AppendLine("    GiveWeapon(AssaultRifle);");
        sb.AppendLine("    SetAmmo(AssaultRifle, 240);     // Full combat load");
        sb.AppendLine("    GiveWeapon(Pistol);");
        sb.AppendLine("    SetAmmo(Pistol, 90);            // Backup sidearm");
        sb.AppendLine("    GiveWeapon(Knife);               // Silent eliminations");
        sb.AppendLine("}");
        sb.AppendLine();
        
        // ============================================================
        // GUARD AI SQUAD - 15 MILITARY PERSONNEL
        // ============================================================
        sb.AppendLine("// --- MILITARY AI SQUAD (15 GUARDS) ---");
        sb.AppendLine("// Formation: Tactical squad with multiple patrol routes");
        sb.AppendLine("AISquad(");
        sb.AppendLine("    Formation Distance = 5.0        // Realistic military spacing");
        sb.AppendLine("    SquadType = 1                    // Tactical coordination mode");
        sb.AppendLine("    Aggression Level = 1.2           // Heightened alert");
        sb.AppendLine("    Communication Range = 150.0      // Wide tactical comms");
        sb.AppendLine(");");
        sb.AppendLine();
        
        // Generate 15 strategically positioned guards
        var guardData = new (string name, double x, double y, double z, int angle, string role)[]
        {
            // Perimeter guards (8 guards around compound edges)
            ("North Gate Sentry", compX - 50, compY - 80, compZ + 3, 0, "Gate entrance monitoring"),
            ("North-East Guard Tower", compX + 60, compY - 60, compZ + 8, 45, "Elevated tower position"),
            ("East Wall Patrol", compX + 90, compY, compZ + 2, 90, "Main eastern perimeter"),
            ("South-East Guard", compX + 70, compY + 80, compZ + 3, 135, "Southeast perimeter"),
            ("South Gate", compX, compY + 100, compZ + 2, 180, "Southern entrance"),
            ("South-West Guard", compX - 70, compY + 80, compZ + 3, 225, "Southwest corner"),
            ("West Wall Patrol", compX - 100, compY, compZ + 2, 270, "Western perimeter"),
            ("North-West Guard", compX - 60, compY - 60, compZ + 3, 315, "Northwest sector"),
            
            // Interior guards (5 guards inside compound)
            ("Main Hall Guard", compX - 20, compY - 10, compZ + 5, 45, "Interior main corridor"),
            ("Vault Guard", compX + 20, compY + 10, compZ + 12, 180, "Intelligence chip vault"),
            ("Barracks Guard", compX - 40, compY + 30, compZ + 4, 90, "Barracks sector"),
            ("Admin Building", compX + 35, compY - 25, compZ + 4, 270, "Administrative center"),
            ("Command Center", compX + 10, compY - 40, compZ + 8, 0, "Command post"),
            
            // Mobile patrol guards (2 guards)
            ("Patrol 1", compX - 80, compY - 100, compZ + 2, 120, "Mobile perimeter patrol"),
            ("Patrol 2", compX + 80, compY + 100, compZ + 2, 300, "Mobile perimeter patrol"),
        };
        
        for (int i = 0; i < guardData.Length; i++)
        {
            var guard = guardData[i];
            sb.AppendLine(FormattableString.Invariant($"// {i + 1}. {guard.name} - {guard.role}"));
            sb.AppendLine("HumanSoldier(");
            sb.AppendLine(FormattableString.Invariant($"    Position = ({guard.x}, {guard.y}, {guard.z})"));
            sb.AppendLine(FormattableString.Invariant($"    Gamma = {guard.angle}"));
            sb.AppendLine("    Model = 'guard'");
            sb.AppendLine("    Team = 1");
            sb.AppendLine("    Weapon = 'GuardArmed'");
            sb.AppendLine("    Bone Hierarchy = 0");
            sb.AppendLine("    Stand Animation = 0");
            sb.AppendLine("    Alert Level = 0.5              // Medium alert");
            sb.AppendLine("    Vision Range = 100.0           // Realistic guard vision");
            sb.AppendLine(");");
            sb.AppendLine();
        }
        
        // ============================================================
        // GUARD WEAPONS CONFIGURATION
        // ============================================================
        sb.AppendLine("// --- GUARD ARMAMENT ---");
        sb.AppendLine("GuardArmed()");
        sb.AppendLine("{");
        sb.AppendLine("    GiveWeapon(AssaultRifle);       // Standard military rifle");
        sb.AppendLine("    SetAmmo(AssaultRifle, 180);     // Full combat load per guard");
        sb.AppendLine("    GiveWeapon(Pistol);             // Backup sidearm");
        sb.AppendLine("    SetAmmo(Pistol, 60);            // Secondary ammunition");
        sb.AppendLine("}");
        sb.AppendLine();
        
        // ============================================================
        // MISSION OBJECTIVES & EXTRACTION
        // ============================================================
        sb.AppendLine("// --- PRIMARY OBJECTIVES ---");
        sb.AppendLine("// Primary: Extract intelligence chip from vault (located in guard at 'Vault Guard' position)");
        sb.AppendLine("// Secondary: Minimize casualties (stealth preferred)");
        sb.AppendLine("// Extraction: Reach lake shore within time limit");
        sb.AppendLine();
        
        sb.AppendLine("// --- EXTRACTION ZONE ---");
        sb.AppendLine(FormattableString.Invariant($"// Location: ({extractX}, {extractY}, {extractZ})"));
        sb.AppendLine("// Description: Abandoned dock at lake shore - friendly forces waiting");
        sb.AppendLine("// Radius: 30 units for extraction success");
        sb.AppendLine();
        
        // ============================================================
        // ENVIRONMENTAL DETAILS
        // ============================================================
        sb.AppendLine("// --- ENVIRONMENTAL OVERVIEW ---");
        sb.AppendLine("// Terrain: 1024x1024 unit epic map");
        sb.AppendLine("// North: Mountain highlands (player spawn ~160m elevation)");
        sb.AppendLine("// Center: Military compound with multiple structures");
        sb.AppendLine("//   - Guard towers at north and northeast");
        sb.AppendLine("//   - Main administrative building");
        sb.AppendLine("//   - Barracks and vehicle bay");
        sb.AppendLine("//   - Secure vault containing intelligence");
        sb.AppendLine("// South: Valley terrain with forest cover");
        sb.AppendLine("// East: Lake (extraction point)");
        sb.AppendLine();
        
        // ============================================================
        // MISSION PARAMETERS & SUCCESS CONDITIONS
        // ============================================================
        sb.AppendLine("// --- MISSION PARAMETERS ---");
        sb.AppendLine("Mission Difficulty = AGENT           // Normal operative difficulty");
        sb.AppendLine("Time Limit = 45 minutes              // Professional operation window");
        sb.AppendLine("Reinforcements = None                // Solo operation - no backup");
        sb.AppendLine("Extraction Timeout = 50 minutes      // Must exit before limit");
        sb.AppendLine("Success Condition = Chip + Extraction// Retrieve AND extract");
        sb.AppendLine("Failure Condition = Mission Capture OR Time Expired");
        sb.AppendLine();
        
        sb.AppendLine("// ============================================================");
        sb.AppendLine("// END MISSION DEFINITION");
        sb.AppendLine("// Generated for Phase 4: Professional Military Gameplay");
        sb.AppendLine("// ============================================================");
        
        return System.Text.Encoding.Latin1.GetBytes(sb.ToString());
    }

    private static byte[] CreatePreview(byte[] heightmap)
    {
        Igi2TerrainMap map = Igi2TerrainMap.Read(heightmap, Igi2TerrainMapKind.Height);
        byte[] header = Encoding.ASCII.GetBytes(FormattableString.Invariant($"P5\n{Size} {Size}\n255\n"));
        byte[] preview = new byte[header.Length + (Size * Size)];
        header.CopyTo(preview, 0);
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                preview[header.Length + (y * Size) + x] =
                    (byte)Math.Clamp(Math.Round(map.GetHeight(x, y) / UnitsPerMetre / 160 * 255), 0, 255);
            }
        }

        return preview;
    }

    private static void VerifyArchive(byte[] archiveBytes, byte[] heightmap)
    {
        IresArchive archive = IresArchive.Read(archiveBytes);
        IresEntry entry = archive.FindBody(HeightName)
            ?? throw new InvalidOperationException("The generated archive does not contain its terrain tile.");
        if (archive.Entries.Count != 1 || !entry.Data.Span.SequenceEqual(heightmap))
        {
            throw new InvalidOperationException("The terrain archive did not round-trip.");
        }

        Igi2TerrainMap map = Igi2TerrainMap.Read(entry.Data, Igi2TerrainMapKind.Height);
        if (map.Width != Size || map.Height != Size || !map.Trailing.IsEmpty)
        {
            throw new InvalidOperationException("The terrain tile has an unexpected extent or trailing data.");
        }
    }

    private static void SelfTest()
    {
        byte[] heightmap = CreateHeightmap();
        byte[] archive = CreateArchive(heightmap);
        VerifyArchive(archive, heightmap);
        Igi2TerrainMap map = Igi2TerrainMap.Read(heightmap, Igi2TerrainMapKind.Height);
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float height = map.GetHeight(x, y);
                if (!float.IsFinite(height) || height < 0 || height > 160 * UnitsPerMetre)
                {
                    throw new InvalidOperationException(FormattableString.Invariant($"Invalid height at {x},{y}: {height}."));
                }
            }
        }

        // Verify procedural terrain is deterministic
        if (!heightmap.AsSpan().SequenceEqual(CreateHeightmap())
            || !archive.AsSpan().SequenceEqual(CreateArchive(CreateHeightmap())))
        {
            throw new InvalidOperationException("Terrain generation is not deterministic.");
        }

        byte[] preview = CreatePreview(heightmap);
        string header = FormattableString.Invariant($"P5\n{Size} {Size}\n255\n");
        if (preview.Length != Encoding.ASCII.GetByteCount(header) + (Size * Size)
            || !preview.AsSpan(0, header.Length).SequenceEqual(Encoding.ASCII.GetBytes(header)))
        {
            throw new InvalidOperationException("The terrain preview has an invalid PGM header or size.");
        }

        byte[] mission = CreateMissionScript();
        MemoryFileDevice device = new();
        device.Add($"{MissionDirectory}/mission.qvm", mission);
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(device);
        ResourceRegistry resources = new(fileSystem);
        NativeRegistry natives = new();
        Igi2MissionCatalogue catalogue = new();
        catalogue.Register(natives);
        if (catalogue.Scan(fileSystem, resources, natives) != 1
            || !catalogue.TryGet(99, out var descriptor)
            || descriptor.GameMode != Igi2MissionCatalogue.SinglePlayerMode
            || descriptor.MissionDirectory != MissionDirectory
            || !catalogue.InProgressionOrder().Any(m => m.Id == 99))
        {
            throw new InvalidOperationException("Generated mission descriptor was not discovered in the I.G.I. 2 campaign catalogue.");
        }

        device.Add($"{MissionDirectory}/objects.qvm", CreateLevelPrototypeScript());
        device.Add($"{MissionDirectory}/heightmaps/heightmaps.res", archive);
        fileSystem.SetAlias(VirtualFileSystem.MissionAlias, $"LOCAL:{MissionDirectory}/");

        GameLoop loop = new();
        loop.Types.Register<TerrainTask>(TerrainTask.TypeName);
        loop.Types.Register<TerrainMapTask>(TerrainMapTask.TypeName);
        loop.Types.Register<Igi2HumanPlayerTask>("HumanPlayer");
        loop.Types.Register<Igi2HumanSoldierTask>(Igi2HumanSoldierTask.TypeName);
        loop.Types.Register<Igi2HumanAiTask>(Igi2HumanAiTask.TypeName);
        LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        LevelLoader levelLoader = new(levelRoot, loop.TaskCommands, loop.Natives, resources);
        levelLoader.LoadMission($"LOCAL:{descriptor.MissionDirectory}");
        Igi2TerrainSurface surface = Igi2TerrainSurface.Build(levelRoot, resources);
        if (surface.Terrain is null || surface.Tiles.Count != 1
            || surface.Tiles[0].Width != Size || surface.Tiles[0].Height != Size
            || surface.Tiles[0].WorldWidth != Size || surface.Tiles[0].WorldHeight != Size
            || surface.Tiles[0].HeightAt(Size / 5, Size / 5) <= 100 * UnitsPerMetre)
        {
            throw new InvalidOperationException("Compiled terrain did not load the expected terrain tile and mountain.");
        }

        Igi2HumanPlayerTask[] players = levelRoot.Descendants().OfType<Igi2HumanPlayerTask>().ToArray();
        Igi2HumanSoldierTask[] soldiers = levelRoot.Descendants().OfType<Igi2HumanSoldierTask>().ToArray();
        if (players.Length != 1 || players[0].Id != 0
            || soldiers.Length != 15 || soldiers.Any(s => s.Ai is null || s.Team != 1)
            || soldiers.Select(s => s.Id).Distinct().Count() != 15
            || soldiers.Select(s => s.Ai!.Id).Distinct().Count() != 15)
        {
            throw new InvalidOperationException("Compiled level did not load one player and 15 distinct guard/mind tasks.");
        }

        static bool OnTerrain(Igi2TerrainSurface terrain, double x, double y, double z) =>
            terrain.Tiles[0].CoversPoint(x, y)
            && terrain.WaterGroundAt(x, y) is double ground
            && Math.Abs(z - ground) < UnitsPerMetre;

        var spawn = players[0].SpawnPosition;
        if (!OnTerrain(surface, spawn.X, spawn.Y, spawn.Z)
            || soldiers.Any(s => !OnTerrain(surface, s.Position.X, s.Position.Y, s.Position.Z)))
        {
            throw new InvalidOperationException("Player or guard prototype is not placed on the generated terrain.");
        }

        byte[] objects = CreateObjectsScript();
        if (objects.Length == 0 || !System.Text.Encoding.Latin1.GetString(objects).Contains("HumanPlayer"))
        {
            throw new InvalidOperationException("Objects script generation failed.");
        }
        
        string objectsText = System.Text.Encoding.Latin1.GetString(objects);
        if (!objectsText.Contains("HumanSoldier") || !objectsText.Contains("AISquad") || !objectsText.Contains("Weapon"))
        {
            throw new InvalidOperationException("Objects script missing required elements (guards, squad, weapons).");
        }
    }
}
