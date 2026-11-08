using System.Buffers.Binary;
using System.Text;
using ProjectIGI.Formats.Ilff;
using ProjectIGI.Formats.World;

namespace ProjectIGI.TerrainPrototype;

internal static class Program
{
    private const int Size = 256;
    private const float UnitsPerMetre = 4096f;
    private const string HeightName = "heightmap000.thm";

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

        using (FileStream file = new(Path.Combine(output, "objects.qvm"), FileMode.CreateNew))
        {
            file.Write(objectsScript);
        }

        Console.WriteLine($"Created {output}");
        Console.WriteLine("heightmaps.res: original prototype terrain tile");
        Console.WriteLine("terrain-preview.pgm: grayscale height preview (bright = high)");
        Console.WriteLine("mission.qvm: mission definition script");
        Console.WriteLine("objects.qvm: level objects, guards, and objectives (PHASE 4)");
        Console.WriteLine("This is a work-in-progress mission prototype. Full gameplay pending.");
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
        string script = @"// The Long Descent: original prototype mission
// Mission 99 - a custom test mission
// Parameters: id, mode(4 chars), name-key, desc-key, tag, mission-dir, common-dir, level-dir, common-pack, level-pack, briefing-sprite

DefineMission(
    99,
    'S', 'N', 'G', 'L',
    'LONG_DESCENT_NAME',
    'LONG_DESCENT_DESC',
    '',
    'missions/long_descent',
    'missions/long_descent/common',
    'missions/long_descent/level',
    'long_descent',
    'long_descent',
    'mission99.spr'
);
";
        return System.Text.Encoding.Latin1.GetBytes(script);
    }

    private static byte[] CreateObjectsScript()
    {
        // Coordinates: terrain is 256x256 units, heights defined in terrain
        // Mountain lookout: ~(51, 51) at ~145m
        // Compound: ~(153, 122) at ~14m
        // Lake: ~(194, 199) at ~0m
        
        // Convert to world coordinates (units):
        // Lookout position (player spawn)
        double lookoutX = 51 * 2.0;  // scale to world coordinates
        double lookoutY = 51 * 2.0;
        double lookoutZ = 145.0;     // height in metres
        double lookoutAngle = 180.0; // facing south toward compound
        
        // Compound center
        double compoundX = 153 * 2.0;
        double compoundY = 122 * 2.0;
        double compoundZ = 14.0;
        
        // Lake extraction point
        double lakeX = 194 * 2.0;
        double lakeY = 199 * 2.0;
        double lakeZ = 0.5;
        
        var sb = new StringBuilder();
        
        sb.AppendLine("// ============================================================");
        sb.AppendLine("// The Long Descent - Level Objects & Gameplay");
        sb.AppendLine("// Phase 4: Gameplay Implementation");
        sb.AppendLine("// ============================================================");
        sb.AppendLine();
        
        // PLAYER SPAWN
        sb.AppendLine("// --- PLAYER ---");
        sb.AppendLine("HumanPlayer(");
        sb.AppendLine(FormattableString.Invariant($"    Position = ({lookoutX}, {lookoutY}, {lookoutZ})"));
        sb.AppendLine(FormattableString.Invariant($"    Gamma = {lookoutAngle}"));
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
        
        // PLAYER WEAPON LOADOUT
        sb.AppendLine("// --- PLAYER WEAPONS ---");
        sb.AppendLine("SetupWeapons()");
        sb.AppendLine("{");
        sb.AppendLine("    GiveWeapon(SniperRifle);");
        sb.AppendLine("    SetAmmo(SniperRifle, 150);");
        sb.AppendLine("    GiveWeapon(AssaultRifle);");
        sb.AppendLine("    SetAmmo(AssaultRifle, 120);");
        sb.AppendLine("    GiveWeapon(Knife);");
        sb.AppendLine("}");
        sb.AppendLine();
        
        // GUARD SQUAD
        sb.AppendLine("// --- GUARDS & AI ---");
        sb.AppendLine("AISquad(");
        sb.AppendLine("    Formation Distance = 3.0");
        sb.AppendLine("    SquadType = 1");
        sb.AppendLine(");");
        sb.AppendLine();
        
        // GUARDS AROUND COMPOUND
        sb.AppendLine("// Guard 1: North perimeter");
        double guard1X = compoundX - 30;
        double guard1Y = compoundY - 40;
        sb.AppendLine("HumanSoldier(");
        sb.AppendLine(FormattableString.Invariant($"    Position = ({guard1X}, {guard1Y}, {compoundZ + 2})"));
        sb.AppendLine("    Gamma = 45");
        sb.AppendLine("    Model = 'guard'");
        sb.AppendLine("    Team = 1");
        sb.AppendLine("    Weapon = 'GuardArmed'");
        sb.AppendLine("    Bone Heirachy = 0");
        sb.AppendLine("    Stand Animation = 0");
        sb.AppendLine(");");
        sb.AppendLine();
        
        // Guard 2: East perimeter
        double guard2X = compoundX + 40;
        double guard2Y = compoundY;
        sb.AppendLine("HumanSoldier(");
        sb.AppendLine(FormattableString.Invariant($"    Position = ({guard2X}, {guard2Y}, {compoundZ + 2})"));
        sb.AppendLine("    Gamma = 135");
        sb.AppendLine("    Model = 'guard'");
        sb.AppendLine("    Team = 1");
        sb.AppendLine("    Weapon = 'GuardArmed'");
        sb.AppendLine("    Bone Heirachy = 0");
        sb.AppendLine("    Stand Animation = 0");
        sb.AppendLine(");");
        sb.AppendLine();
        
        // Guard 3: South perimeter
        double guard3X = compoundX;
        double guard3Y = compoundY + 40;
        sb.AppendLine("HumanSoldier(");
        sb.AppendLine(FormattableString.Invariant($"    Position = ({guard3X}, {guard3Y}, {compoundZ + 2})"));
        sb.AppendLine("    Gamma = 225");
        sb.AppendLine("    Model = 'guard'");
        sb.AppendLine("    Team = 1");
        sb.AppendLine("    Weapon = 'GuardArmed'");
        sb.AppendLine("    Bone Heirachy = 0");
        sb.AppendLine("    Stand Animation = 0");
        sb.AppendLine(");");
        sb.AppendLine();
        
        // Guard 4: Interior at locker
        double guard4X = compoundX;
        double guard4Y = compoundY;
        sb.AppendLine("HumanSoldier(");
        sb.AppendLine(FormattableString.Invariant($"    Position = ({guard4X}, {guard4Y}, {compoundZ + 15})"));
        sb.AppendLine("    Gamma = 0");
        sb.AppendLine("    Model = 'guard'");
        sb.AppendLine("    Team = 1");
        sb.AppendLine("    Weapon = 'GuardArmed'");
        sb.AppendLine("    Bone Heirachy = 0");
        sb.AppendLine("    Stand Animation = 0");
        sb.AppendLine(");");
        sb.AppendLine();
        
        // GUARD WEAPONS
        sb.AppendLine("// --- GUARD WEAPONS ---");
        sb.AppendLine("GuardArmed()");
        sb.AppendLine("{");
        sb.AppendLine("    GiveWeapon(AssaultRifle);");
        sb.AppendLine("    SetAmmo(AssaultRifle, 120);");
        sb.AppendLine("}");
        sb.AppendLine();
        
        // OBJECTIVES & TRIGGERS
        sb.AppendLine("// --- OBJECTIVES ---");
        sb.AppendLine("// Primary: Retrieve intelligence chip from compound locker");
        sb.AppendLine("// Secondary: Survive");
        sb.AppendLine("// Extraction: Reach the lake");
        sb.AppendLine();
        
        sb.AppendLine("// ============================================================");
        sb.AppendLine("// End of Level Objects");
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

        if (map.GetHeight(51, 51) != 145 * UnitsPerMetre
            || map.GetHeight(153, 122) != 14 * UnitsPerMetre
            || map.GetHeight(194, 199) != 0)
        {
            throw new InvalidOperationException("The lookout, compound, or lake basin is not at its intended height.");
        }

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
        if (mission.Length == 0 || !System.Text.Encoding.Latin1.GetString(mission).Contains("DefineMission"))
        {
            throw new InvalidOperationException("Mission script generation failed.");
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
