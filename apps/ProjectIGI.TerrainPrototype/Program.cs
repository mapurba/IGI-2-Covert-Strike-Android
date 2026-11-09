using System.Buffers.Binary;
using System.Text;
using ProjectIGI.Core.Numerics;
using ProjectIGI.Engine.Tasks;
using ProjectIGI.Formats.Ai;
using ProjectIGI.Formats.Ilff;
using ProjectIGI.Formats.Textures;
using ProjectIGI.Formats.World;
using ProjectIGI.Game.Igi2.Ai;
using ProjectIGI.Game.Igi2.Flow;
using ProjectIGI.Game.Igi2.Interaction;
using ProjectIGI.Game.Igi2.Player;
using ProjectIGI.Game.Igi2.Scripting;
using ProjectIGI.Game.Igi2.World;
using ProjectIGI.Game.Player;
using ProjectIGI.Game.World;
using ProjectIGI.Scripting.Qvm;
using ProjectIGI.Scripting.Runtime;

namespace ProjectIGI.TerrainPrototype;

internal static class Program
{
    private const int Size = 1024;
    private const float UnitsPerMetre = 4096f;
    private const float WorldWidthMetres = 500f;
    private const float WorldScale = WorldWidthMetres / 1200f;
    private const float StartMetres = 240f * WorldScale;
    private const float CompoundX = 720f * WorldScale;
    private const float CompoundY = 575f * WorldScale;
    private const int MissionId = 99;
    private const string MissionDirectory = "missions/long_descent";
    private const string HeightName = "heightmap000.thm";
    private const string GroundTexture = "LOCAL:missions/multiplayer/jungle/textures/lushgrass.jpg";
    private const int GuardGraphId = 300;
    private const int GuardCount = 15;
    private readonly record struct ForestSpec(int Id, string Model, int Count, float Cutoff,
        float CentreX, float CentreY, float Size, bool GroundCover);

    private static readonly ForestSpec[] Forests =
    [
        new(400, "940_03_1", 313, 300f, WorldWidthMetres / 2f, WorldWidthMetres / 2f, 1000f * WorldScale, false),
        new(401, "940_06_1", 243, 300f, WorldWidthMetres / 2f, WorldWidthMetres / 2f, 1000f * WorldScale, false),
        new(402, "940_07_1", 208, 300f, WorldWidthMetres / 2f, WorldWidthMetres / 2f, 1000f * WorldScale, false),
        new(403, "940_22_1", 313, 120f, WorldWidthMetres / 2f, WorldWidthMetres / 2f, 1000f * WorldScale, false),
        new(404, "940_25_1", 4167, 25f, WorldWidthMetres / 2f, WorldWidthMetres / 2f, 1000f * WorldScale, true),
        new(405, "940_03_1", 61, 180f, 260f * WorldScale, 260f * WorldScale, 220f * WorldScale, false),
        new(406, "940_25_1", 955, 25f, StartMetres, StartMetres, 100f * WorldScale, true),
        new(407, "940_25_1", 955, 25f, 480f * WorldScale, 400f * WorldScale, 120f * WorldScale, true),
        new(408, "940_25_1", 955, 25f, 620f * WorldScale, 480f * WorldScale, 120f * WorldScale, true),
    ];

    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--self-test")
        {
            SelfTest();
            Console.WriteLine("Mission 99 asset self-test passed.");
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
        byte[] missionScript = CreateMissionScript();
        byte[] objectsScript = CreateObjectsScript();
        byte[] graph = CreateGuardGraph();
        VerifyArchive(archive, heightmap);
        VerifyMissionScript(missionScript);
        VerifyObjectsScript(objectsScript, archive);
        VerifyGuardGraph(graph);

        Directory.CreateDirectory(Path.Combine(output, "heightmaps"));
        Directory.CreateDirectory(Path.Combine(output, "textures"));
        Directory.CreateDirectory(Path.Combine(output, "graphs"));
        Directory.CreateDirectory(Path.Combine(output, "AI"));
        File.WriteAllBytes(Path.Combine(output, "heightmaps", "heightmaps.res"), archive);
        File.WriteAllBytes(Path.Combine(output, "textures", "long_descent_ground.tga"), CreateGroundTexture());
        File.WriteAllBytes(Path.Combine(output, "textures", "long_descent_clouds.tga"), CreateCloudTexture());
        File.WriteAllBytes(Path.Combine(output, "terrain-preview.pgm"), CreatePreview(heightmap));
        File.WriteAllBytes(Path.Combine(output, "mission.qvm"), missionScript);
        File.WriteAllBytes(Path.Combine(output, "objects.qvm"), objectsScript);
        File.WriteAllBytes(Path.Combine(output, "graphs", $"graph{GuardGraphId}.dat"), graph);
        byte[] guardScript = CreateDefaultScript("AIFunction_DefaultHandler");
        for (int id = 200; id < 200 + GuardCount; id++)
        {
            File.WriteAllBytes(Path.Combine(output, "AI", $"{id}.qvm"), guardScript);
        }

        byte[] squadScript = CreateDefaultScript("AISquad_DefaultHandler");
        File.WriteAllBytes(Path.Combine(output, "AI", "Squad_301.qvm"), squadScript);
        File.WriteAllBytes(Path.Combine(output, "AI", "Squad_302.qvm"), squadScript);
        foreach (var forest in Forests)
        {
            byte[] placement = CreateForest(forest);
            VerifyForest(placement, forest);
            File.WriteAllBytes(Path.Combine(output, $"forest_{forest.Id}.dat"), placement);
        }

        Console.WriteLine($"Created experimental Mission 99 assets in {output}");
        Console.WriteLine("heightmaps/heightmaps.res: original 1024 x 1024 terrain tile");
        Console.WriteLine("mission.qvm: compiled mission registration");
        Console.WriteLine("objects.qvm: compiled terrain, player spawn, and experimental compound");
        Console.WriteLine("terrain-preview.pgm: grayscale height preview (bright = high)");
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
        height = BlendPlateau(height, x, y, 0.60, 575.0 / 1200.0, 84.0 / WorldWidthMetres, 156.0 / WorldWidthMetres, 14);
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

    private static byte[] CreateGroundTexture()
    {
        const int size = 64;
        byte[] image = new byte[TgaImage.HeaderSize + (size * size * 3)];
        image[2] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(12), size);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(14), size);
        image[16] = 24;
        image[17] = 0x20;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int variation = ((x * 37 + y * 19 + x * y * 3) & 15) - 8;
                int offset = TgaImage.HeaderSize + ((y * size + x) * 3);
                image[offset] = (byte)(56 + variation);
                image[offset + 1] = (byte)(91 + variation);
                image[offset + 2] = (byte)(94 + variation);
            }
        }

        return image;
    }

    private static byte[] CreateCloudTexture()
    {
        const int size = 256;
        byte[] image = new byte[TgaImage.HeaderSize + (size * size * 4)];
        image[2] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(12), size);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(14), size);
        image[16] = 32;
        image[17] = 0x28;
        float Noise(int x, int y, int grid, int seed)
        {
            int cell = size / grid;
            int gx = x / cell, gy = y / cell;
            float tx = (x % cell) / (float)cell, ty = (y % cell) / (float)cell;
            tx = tx * tx * (3f - (2f * tx));
            ty = ty * ty * (3f - (2f * ty));
            float Value(int a, int b)
            {
                uint n = unchecked((uint)((a % grid) * 73856093 ^ (b % grid) * 19349663 ^ seed));
                n ^= n >> 16;
                n *= 0x7feb352du;
                n ^= n >> 15;
                return (n & 0xffff) / 65535f;
            }
            float upper = Value(gx, gy) * (1f - tx) + Value(gx + 1, gy) * tx;
            float lower = Value(gx, gy + 1) * (1f - tx) + Value(gx + 1, gy + 1) * tx;
            return upper * (1f - ty) + lower * ty;
        }
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float noise = (Noise(x, y, 4, 19) * 0.55f)
                    + (Noise(x, y, 8, 41) * 0.3f) + (Noise(x, y, 16, 73) * 0.15f);
                byte alpha = (byte)(Math.Clamp((noise - 0.43f) * 3f, 0f, 1f) * 220f);
                int offset = TgaImage.HeaderSize + ((y * size + x) * 4);
                image[offset] = 225;
                image[offset + 1] = 235;
                image[offset + 2] = 245;
                image[offset + 3] = alpha;
            }
        }
        return image;
    }

    private static byte[] CreateMissionScript() =>
        QvmExpression.BuildProgram(
            QvmExpression.Call(
                "DefineMission",
                QvmExpression.Int(MissionId),
                QvmExpression.Text("S"),
                QvmExpression.Text("N"),
                QvmExpression.Text("G"),
                QvmExpression.Text("L"),
                QvmExpression.Text("The Long Descent"),
                QvmExpression.Text("An experimental mountain terrain prototype."),
                QvmExpression.Text(string.Empty),
                QvmExpression.Text(MissionDirectory),
                QvmExpression.Text("missions/location1/common"),
                QvmExpression.Text("missions/location1/level1"),
                QvmExpression.Text("location1"),
                QvmExpression.Text("level1"),
                QvmExpression.Text(string.Empty)));

    private static byte[] CreateObjectsScript()
    {
        float mapCentre = WorldWidthMetres * UnitsPerMetre / 2f;
        const float startX = StartMetres;
        const float startY = StartMetres;
        float playerX = startX * UnitsPerMetre;
        float playerY = startY * UnitsPerMetre;
        float playerZ = (float)HeightMetres(startX / WorldWidthMetres, startY / WorldWidthMetres) * UnitsPerMetre;

        QvmExpression terrainMap = NewTask(
            1,
            TerrainMapTask.TypeName,
            "Long Descent heightmap",
            [
                QvmExpression.Int(0),
                QvmExpression.Int(Size),
                QvmExpression.Int(Size),
                .. Position(mapCentre, mapCentre, 0f),
                QvmExpression.Real(WorldWidthMetres),
                QvmExpression.Real(WorldWidthMetres),
                QvmExpression.Real(0f),
                QvmExpression.Text(string.Empty),
            ]);

        QvmExpression material = NewTask(
            3,
            TerrainMaterialTask.TypeName,
            "Ground",
            [
                QvmExpression.Int(0),
                QvmExpression.Int(0),
                QvmExpression.Text(GroundTexture),
                QvmExpression.Text(string.Empty),
                QvmExpression.Real(16f),
                QvmExpression.Real(1f),
                QvmExpression.Int(0),
            ]);

        QvmExpression terrain = NewTask(
            0,
            TerrainTask.TypeName,
            "Long Descent terrain",
            [
                .. Position(0f, 0f, 0f),
                QvmExpression.Real(WorldWidthMetres),
                QvmExpression.Real(WorldWidthMetres),
                QvmExpression.Real(1f),
                QvmExpression.Real(1f),
                QvmExpression.Real(0f),
                QvmExpression.Real(0f),
                QvmExpression.Int(0),
                QvmExpression.Real(0f),
                QvmExpression.Real(0f),
                QvmExpression.Real(0f),
                QvmExpression.Real(0f),
                QvmExpression.Real(0f),
                QvmExpression.Int(0),
                terrainMap,
                material,
            ]);

        QvmExpression player = NewTask(
            2,
            "HumanPlayer",
            "Player start",
            [
                .. Position(playerX, playerY, playerZ),
                QvmExpression.Real(-0.96f),
                QvmExpression.Text("jones_1"),
                QvmExpression.Int(0),
                QvmExpression.Text("Human_AddWeapon(\"WEAPON_ID_M16A2\")"),
                QvmExpression.Text("arms1_1"),
                QvmExpression.Real(2f),
                QvmExpression.Real(0.1f),
                QvmExpression.Real(0.9f),
                QvmExpression.Int(0),
                NewTask(10, Igi2HumanPlayerInputTask.TypeName, "Player input",
                    QvmExpression.Text(string.Empty), QvmExpression.Text(string.Empty)),
            ]);
        QvmExpression[] guards =
        [
            Guard(100, 670, 535, 0.8f), Guard(101, 700, 530, 1.4f),
            Guard(102, 740, 530, 1.8f), Guard(103, 770, 545, 2.4f),
            Guard(104, 770, 585, 3.1f), Guard(105, 750, 620, 3.7f),
            Guard(106, 710, 625, 4.5f), Guard(107, 675, 610, 5.2f),
            Guard(108, 700, 558, 0.7f), Guard(109, 735, 558, 1.6f),
            Guard(110, 748, 585, 3.2f), Guard(111, 720, 600, 4.0f),
            Guard(112, 690, 585, 5.3f), Guard(113, 685, 568, 0f),
            Guard(114, 750, 570, 3f),
        ];
        QvmExpression dynamic = NewTask(9, EngineTaskTypes.Dynamic, "Dynamic", [player,
            Squad(301, guards[..8]), Squad(302, guards[8..]),
            Camera(150, 690, 540, 0f), Camera(151, 750, 605, 3.14f)]);
        QvmExpression flow = NewTask(11, Igi2LevelFlowTask.TypeName, "Mission clock",
            [
            .. Position(0f, 0f, 0f), .. Position(0f, 0f, 0f),
            QvmExpression.Real(12f), QvmExpression.Text(string.Empty), QvmExpression.Text(string.Empty),
            QvmExpression.Int(0), QvmExpression.Real(0f),
            ]);
        QvmExpression graph = NewTask(GuardGraphId, Igi2AiGraphTask.TypeName, "Compound navigation",
            [
            .. Position(0f, 0f, 0f), QvmExpression.Int(0), QvmExpression.Int(0),
            QvmExpression.Real(0f), QvmExpression.Real(GuardCount), QvmExpression.Real(0f),
            QvmExpression.Real(1f), QvmExpression.Real(2f), QvmExpression.Real(2f),
            QvmExpression.Real(2f), QvmExpression.Real(2f), QvmExpression.Real(50f),
            QvmExpression.Int(0), QvmExpression.Real(1f), QvmExpression.Int(0),
            ]);
        QvmExpression[] buildings =
        [
            Building(160, "Guard blockhouse", "434_26_1", 700, 565, 0f),
            Building(161, "Generator building", "432_10_1", 738, 555, 0f),
            Building(162, "Warehouse", "400_30_1", 725, 590, 0f),
        ];

        QvmExpression sunlight = NewTask(
            5, Igi2DirlightKeyframeTask.TypeName, "Midday sun",
            [
                QvmExpression.Real(0.9f), QvmExpression.Real(0.5f),
                .. Position(1f, 0.96f, 0.88f),
                .. Position(0.15f, 0.16f, 0.18f),
                QvmExpression.Real(12f),
            ]);
        QvmExpression sun = NewTask(
            4, Igi2DirlightTask.TypeName, "Sun",
            [
                QvmExpression.Int(1), QvmExpression.Int(1), QvmExpression.Int(1),
                QvmExpression.Real(1f), QvmExpression.Real(0.25f),
                QvmExpression.Text("sun.tex"), QvmExpression.Real(32f), QvmExpression.Int(0),
                sunlight,
            ]);
        QvmExpression day = NewTask(
            7, Igi2GlobalLightKeyframeTask.TypeName, "Midday",
            [
                QvmExpression.Int(0),
                .. DaylightCategory(), .. DaylightCategory(), .. DaylightCategory(),
                .. DaylightCategory(), .. DaylightCategory(),
                .. Position(0.55f, 0.7f, 1f),
                .. Position(0.35f, 0.4f, 0.45f),
                .. Position(0.3f, 0.45f, 0.6f),
                QvmExpression.Real(12f),
            ]);
        QvmExpression globalLight = NewTask(
            6, Igi2GlobalLightTask.TypeName, "Daylight",
            [
                QvmExpression.Real(1000f), QvmExpression.Real(0.7f),
                QvmExpression.Real(20f), QvmExpression.Real(0.15f), QvmExpression.Int(0),
                .. Position(0f, 0f, 0f), .. Position(1f, 1f, 1f),
                QvmExpression.Real(1f), day,
            ]);

        QvmExpression sky = NewTask(
            8, FlatSkyTask.TypeName, "Daytime sky",
            [
                QvmExpression.Real(0f), QvmExpression.Real(400f), QvmExpression.Real(2000f),
                .. Position(0.65f, 0.75f, 0.85f),
                QvmExpression.Int(0), QvmExpression.Real(90f),
                .. Position(0.22f, 0.44f, 0.75f),
                .. Position(0.34f, 0.56f, 0.82f),
                .. Position(0.46f, 0.65f, 0.86f),
                .. Position(0.62f, 0.75f, 0.88f),
                .. Position(0.72f, 0.82f, 0.91f),
                NewTask(12, FlatSkyLayerTask.TypeName, "Daylight clouds",
                [
                    QvmExpression.Text("long_descent_clouds.tga"),
                    QvmExpression.Real(0.000002f), QvmExpression.Real(0.00001f),
                    QvmExpression.Real(0.000004f), QvmExpression.Real(0.6f),
                    .. Position(1f, 1f, 1f),
                ]),
            ]);

        QvmExpression[] forests = [.. Forests.Select(Forest)];
        return QvmExpression.BuildProgram([terrain, dynamic, flow, graph, sun, globalLight, sky, .. buildings, .. forests]);
    }

    private static QvmExpression Forest(ForestSpec forest) =>
        NewTask(forest.Id, ForestTask.TypeName, "Valley vegetation",
        [
            .. Position(forest.CentreX * UnitsPerMetre, forest.CentreY * UnitsPerMetre, 0f),
            QvmExpression.Text(forest.Model), QvmExpression.Real(forest.Size),
            QvmExpression.Int(0), QvmExpression.Int(0), QvmExpression.Int(0),
            QvmExpression.Real(0f), QvmExpression.Real(6.283185f),
            QvmExpression.Real(0.3f), QvmExpression.Real(0.3f), QvmExpression.Real(0.3f),
            QvmExpression.Int(1), QvmExpression.Int(forest.Count), QvmExpression.Real(0f),
            QvmExpression.Int(0), QvmExpression.Real(forest.Cutoff), QvmExpression.Int(0), QvmExpression.Int(0),
        ]);

    private static byte[] CreateForest(ForestSpec forest)
    {
        Random random = new(forest.Id);
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        byte[] header = Encoding.ASCII.GetBytes(Igi2ForestData.CurrentHeader);
        writer.Write(header.Length);
        writer.Write(header);
        for (int i = 0; i < forest.Count; i++)
        {
            float x, y;
            do
            {
                x = forest.CentreX + (((float)random.NextDouble() - 0.5f) * forest.Size * 0.96f);
                y = forest.CentreY + (((float)random.NextDouble() - 0.5f) * forest.Size * 0.96f);
            }
            while ((!forest.GroundCover && ((x - CompoundX) * (x - CompoundX)) + ((y - CompoundY) * (y - CompoundY)) < 115f * 115f)
                || ((x - StartMetres) * (x - StartMetres)) + ((y - StartMetres) * (y - StartMetres))
                    < (forest.GroundCover ? 1.5f * 1.5f : 8f * 8f));

            float scale = forest.GroundCover ? 1.1f + ((float)random.NextDouble() * 0.9f)
                : 0.8f + ((float)random.NextDouble() * 0.4f);
            writer.Write((x - forest.CentreX) * UnitsPerMetre);
            writer.Write((y - forest.CentreY) * UnitsPerMetre);
            writer.Write((float)HeightMetres(x / WorldWidthMetres, y / WorldWidthMetres) * UnitsPerMetre);
            writer.Write(0f);
            writer.Write(0f);
            writer.Write((float)(random.NextDouble() * Math.Tau));
            writer.Write(scale);
            writer.Write(scale);
            writer.Write(scale);
            writer.Write(0xffc8d4c8u);
        }

        return stream.ToArray();
    }

    private static void VerifyForest(byte[] bytes, ForestSpec forest)
    {
        Igi2ForestData data = Igi2ForestData.Read(bytes);
        if (data.Count != forest.Count || data.Trees().Any(tree =>
            !float.IsFinite(tree.Position.Z)
            || Math.Abs(tree.Position.X) > forest.Size * UnitsPerMetre / 2
            || Math.Abs(tree.Position.Y) > forest.Size * UnitsPerMetre / 2
            || tree.Scale.X <= 0))
        {
            throw new InvalidOperationException("The generated forest placement is invalid.");
        }
    }

    private static QvmExpression Squad(int id, QvmExpression[] guards) =>
        NewTask(id, Igi2AiSquadTask.TypeName, "Compound squad",
        [
            QvmExpression.Real(2f), QvmExpression.Int(1), QvmExpression.Int(-1),
            QvmExpression.Int(-1), QvmExpression.Int(-1), QvmExpression.Real(30f),
            QvmExpression.Int(5), QvmExpression.Int(60), .. guards,
        ]);

    private static QvmExpression Guard(int id, float x, float y, float heading) =>
        NewTask(id, Igi2HumanSoldierTask.TypeName, $"Compound guard {id - 99}",
        [
            .. Position((x - 720f + CompoundX) * UnitsPerMetre, (y - 575f + CompoundY) * UnitsPerMetre, 14f * UnitsPerMetre),
            QvmExpression.Real(heading),
            QvmExpression.Text("rsg3_1"),
            QvmExpression.Int(1),
            QvmExpression.Text("Human_AddWeapon(\"WEAPON_ID_AK47\")"),
            QvmExpression.Int(1),
            QvmExpression.Int(-1),
            NewTask(id + 100, Igi2HumanAiTask.TypeName, "Guard AI",
                QvmExpression.Text("HUMANAI_TYPE_C1_NORMAL_SOLDIER"),
                QvmExpression.Text("HUMANAI_ANIMTYPE_SOLDIER_RIFLE"),
                QvmExpression.Int(GuardGraphId)),
        ]);

    private static byte[] CreateGuardGraph()
    {
        (float X, float Y)[] nodes =
        [
            (670, 535), (700, 530), (740, 530), (770, 545), (770, 585),
            (750, 620), (710, 625), (675, 610), (700, 558), (735, 558),
            (748, 585), (720, 600), (690, 585), (685, 568), (750, 570),
        ];
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write(GraphFile.Magic);

        void Field(string name, GraphValueType type)
        {
            writer.Write((byte)GraphValueType.UInt32);
            writer.Write(GraphFieldHash.Of(name));
            writer.Write((byte)GraphValueType.Bool8);
            writer.Write((byte)type);
            writer.Write((byte)type);
        }

        void IntField(string name, int value)
        {
            Field(name, GraphValueType.Int32);
            writer.Write(value);
        }

        IntField("nMaxNodes", nodes.Length);
        for (int i = 0; i < nodes.Length; i++)
        {
            IntField("nNodeID", i);
            Field("tNodePosition", GraphValueType.Real64x3);
            writer.Write(((double)nodes[i].X - 720d + CompoundX) * UnitsPerMetre);
            writer.Write(((double)nodes[i].Y - 575d + CompoundY) * UnitsPerMetre);
            writer.Write(14d * UnitsPerMetre);
            Field("vNodeRadius", GraphValueType.Real32);
            writer.Write(2f * UnitsPerMetre);
            Field("vNodeLightValue", GraphValueType.Real32);
            writer.Write(1f);
            Field("nNodeCriteria", GraphValueType.String8);
            writer.Write((byte)1);
            writer.Write((byte)0);
        }

        for (int i = 0; i < nodes.Length; i++)
        {
            for (int j = i + 1; j < nodes.Length; j++)
            {
                float dx = nodes[i].X - nodes[j].X;
                float dy = nodes[i].Y - nodes[j].Y;
                if ((dx * dx) + (dy * dy) <= 45f * 45f)
                {
                    IntField("nLinkID1", i);
                    IntField("nLinkID2", j);
                    IntField("eLinkType", 0);
                }
            }
        }

        return stream.ToArray();
    }

    private static void VerifyGuardGraph(byte[] bytes)
    {
        Igi2AiGraph graph = Igi2AiGraph.Read(bytes, new Igi2AiGraphPlacement(GuardCount, false, default), _ => 0);
        if (graph.Nodes.Count != GuardCount || graph.Links.Count == 0
            || graph.Nodes.Values.Any(node => node.Position.Z != 14d * UnitsPerMetre))
        {
            throw new InvalidOperationException("The generated guard graph did not round-trip.");
        }

        HashSet<int> reached = [0];
        bool changed;
        do
        {
            changed = false;
            foreach (Igi2AiGraphLink link in graph.Links)
            {
                if (reached.Contains(link.NodeA))
                {
                    changed |= reached.Add(link.NodeB);
                }
                if (reached.Contains(link.NodeB))
                {
                    changed |= reached.Add(link.NodeA);
                }
            }
        }
        while (changed);
        if (reached.Count != GuardCount)
        {
            throw new InvalidOperationException("The guard navigation graph is disconnected.");
        }
    }

    private static byte[] CreateDefaultScript(string handler) =>
        QvmExpression.BuildProgram(QvmExpression.Call(handler));

    private static QvmExpression Building(int id, string label, string model, float x, float y, float heading) =>
        NewTask(id, Igi2BuildingTask.TypeName, label,
        [
            .. Position((x - 720f + CompoundX) * UnitsPerMetre, (y - 575f + CompoundY) * UnitsPerMetre, 14f * UnitsPerMetre),
            .. Position(0f, 0f, heading),
            QvmExpression.Text(model),
            .. Position(0.35f, 0.35f, 0.35f),
        ]);

    private static QvmExpression Camera(int id, float x, float y, float heading) =>
        NewTask(id, Igi2SecurityCameraTask.TypeName, "Compound camera",
        [
            .. Position((x - 720f + CompoundX) * UnitsPerMetre, (y - 575f + CompoundY) * UnitsPerMetre, 17f * UnitsPerMetre),
            QvmExpression.Real(heading), QvmExpression.Text("335_01_1"), QvmExpression.Int(1),
            QvmExpression.Real(-0.2f), QvmExpression.Real(0f),
            QvmExpression.Text("335_02_1"), QvmExpression.Text("335_03_1"),
            QvmExpression.Int(45), QvmExpression.Int(45), QvmExpression.Int(30),
            QvmExpression.Real(1f), QvmExpression.Int(60), QvmExpression.Int(80),
            QvmExpression.Real(30f), QvmExpression.Text("1"), QvmExpression.Real(0.5f),
        ]);

    private static QvmExpression[] DaylightCategory() =>
    [
        .. Position(0.45f, 0.45f, 0.45f),
        .. Position(0.65f, 0.75f, 0.85f),
        QvmExpression.Real(0f),
        QvmExpression.Int(-1),
    ];

    private static QvmExpression NewTask(int id, string typeName, string label, params QvmExpression[] arguments)
    {
        QvmExpression[] callArguments = new QvmExpression[arguments.Length + 3];
        callArguments[0] = QvmExpression.Int(id);
        callArguments[1] = QvmExpression.Text(typeName);
        callArguments[2] = QvmExpression.Text(label);
        arguments.CopyTo(callArguments, 3);
        return QvmExpression.Call(TaskScriptCommands.TaskNew, callArguments);
    }

    private static QvmExpression[] Position(float x, float y, float z) =>
    [
        QvmExpression.Real(x),
        QvmExpression.Real(y),
        QvmExpression.Real(z),
    ];

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

    private static void VerifyMissionScript(byte[] missionScript)
    {
        Igi2MissionCatalogue catalogue = new();
        NativeRegistry natives = new();
        catalogue.Register(natives);
        RunProgram(missionScript, natives);

        if (!catalogue.TryGet(MissionId, out MissionDescriptor? mission)
            || mission.MissionDirectory != MissionDirectory
            || mission.GameMode != Igi2MissionCatalogue.SinglePlayerMode
            || mission.Name != "The Long Descent"
            || mission.CommonPackDirectory != "missions/location1/common"
            || mission.LevelPackDirectory != "missions/location1/level1"
            || mission.CommonPackName != "location1"
            || mission.LevelPackName != "level1"
            || mission.PacksToOpen.Count != 4)
        {
            throw new InvalidOperationException("The compiled mission script did not register Mission 99 correctly.");
        }
    }

    private static void VerifyObjectsScript(byte[] objectsScript, byte[] archiveBytes)
    {
        TaskTypeRegistry types = new();
        EngineTaskTypes.Register(types);
        types.Register<TerrainTask>(TerrainTask.TypeName);
        types.Register<TerrainMapTask>(TerrainMapTask.TypeName);
        types.Register<TerrainMaterialTask>(TerrainMaterialTask.TypeName);
        types.Register<Igi2HumanPlayerTask>("HumanPlayer");
        types.Register<Igi2HumanPlayerInputTask>(Igi2HumanPlayerInputTask.TypeName);
        types.Register<Igi2DirlightTask>(Igi2DirlightTask.TypeName);
        types.Register<Igi2DirlightKeyframeTask>(Igi2DirlightKeyframeTask.TypeName);
        types.Register<Igi2GlobalLightTask>(Igi2GlobalLightTask.TypeName);
        types.Register<Igi2GlobalLightKeyframeTask>(Igi2GlobalLightKeyframeTask.TypeName);
        types.Register<FlatSkyTask>(FlatSkyTask.TypeName);
        types.Register<FlatSkyLayerTask>(FlatSkyLayerTask.TypeName);
        types.Register<Igi2HumanSoldierTask>(Igi2HumanSoldierTask.TypeName);
        types.Register<Igi2HumanAiTask>(Igi2HumanAiTask.TypeName);
        types.Register<Igi2AiSquadTask>(Igi2AiSquadTask.TypeName);
        types.Register<Igi2AiGraphTask>(Igi2AiGraphTask.TypeName);
        types.Register<Igi2LevelFlowTask>(Igi2LevelFlowTask.TypeName);
        types.Register<ForestTask>(ForestTask.TypeName);
        types.Register<Igi2SecurityCameraTask>(Igi2SecurityCameraTask.TypeName);
        types.Register<Igi2BuildingTask>(Igi2BuildingTask.TypeName);
        TaskTree tree = new(types);
        GameTask levelRoot = tree.New(tree.Root, types.Get(EngineTaskTypes.Level));
        NativeRegistry natives = new();
        TaskScriptCommands commands = new(tree, new ScriptVarRegistry(natives));
        commands.Register(natives, levelRoot);
        RunProgram(objectsScript, natives);

        TerrainTask terrain = tree.Root.Descendants().OfType<TerrainTask>().Single();
        TerrainMapTask terrainMap = tree.Root.Descendants().OfType<TerrainMapTask>().Single();
        Igi2HumanPlayerTask player = tree.Root.Descendants().OfType<Igi2HumanPlayerTask>().Single();
        Igi2TerrainSurface surface = Igi2TerrainSurface.Build(tree.Root, IresArchive.Read(archiveBytes));
        Igi2DirlightTask sun = tree.Root.Descendants().OfType<Igi2DirlightTask>().Single();
        Igi2GlobalLightTask daylight = tree.Root.Descendants().OfType<Igi2GlobalLightTask>().Single();
        FlatSkyTask sky = tree.Root.Descendants().OfType<FlatSkyTask>().Single();
        FlatSkyLayerTask cloud = sky.Children.OfType<FlatSkyLayerTask>().Single();
        Igi2HumanSoldierTask[] soldiers = [.. tree.Root.Descendants().OfType<Igi2HumanSoldierTask>()];
        Igi2SecurityCameraTask[] cameras = [.. tree.Root.Descendants().OfType<Igi2SecurityCameraTask>()];
        Igi2BuildingTask[] buildings = [.. tree.Root.Descendants().OfType<Igi2BuildingTask>()];
        Igi2AiSquadTask[] squads = [.. tree.Root.Descendants().OfType<Igi2AiSquadTask>()];
        Igi2LevelFlowTask flow = tree.Root.Descendants().OfType<Igi2LevelFlowTask>().Single();
        Igi2AiGraphTask graph = tree.Root.Descendants().OfType<Igi2AiGraphTask>().Single();
        ForestTask[] forests = [.. tree.Root.Descendants().OfType<ForestTask>()];
        foreach (Igi2HumanSoldierTask soldier in soldiers)
        {
            double? height = surface.WaterGroundAt(soldier.Position.X, soldier.Position.Y);
            if (height is null || Math.Abs(height.Value - soldier.Position.Z) > 0.1 * UnitsPerMetre)
            {
                throw new InvalidOperationException("A compound guard is not on the terrain.");
            }
        }
        foreach (var forest in Forests)
        {
            foreach (Igi2ForestTree plant in Igi2ForestData.Read(CreateForest(forest)).Trees())
            {
                double x = plant.Position.X + (forest.CentreX * UnitsPerMetre);
                double y = plant.Position.Y + (forest.CentreY * UnitsPerMetre);
                double? groundHeight = surface.WaterGroundAt(x, y);
                if (groundHeight is null || Math.Abs(groundHeight.Value - plant.Position.Z) > 0.1 * UnitsPerMetre)
                {
                    throw new InvalidOperationException("A vegetation placement does not match the terrain height.");
                }
            }
        }
        ForestSpec lookoutGrass = Forests.Single(forest => forest.Id == 406);
        Igi2ForestTree[] nearbyGrass = [.. Igi2ForestData.Read(CreateForest(lookoutGrass)).Trees()
            .Where(plant => ((double)plant.Position.X * plant.Position.X) + ((double)plant.Position.Y * plant.Position.Y)
                < Math.Pow(20 * UnitsPerMetre, 2))];
        if (nearbyGrass.Length < 600
            || nearbyGrass.Select(plant => (plant.Position.X > 0, plant.Position.Y > 0)).Distinct().Count() != 4)
        {
            throw new InvalidOperationException("The lookout does not have dense grass in every direction.");
        }
        if (soldiers.Length != GuardCount || soldiers.Any(s => s.Parent is not Igi2AiSquadTask
                || s.Ai is not { GraphId: GuardGraphId } || s.Model != "rsg3_1" || s.Team != 1)
            || squads.Length != 2 || squads.Any(s => s.Parent?.Type.Name != EngineTaskTypes.Dynamic
                || s.Soldiers.Count() is < 1 or > 8)
            || graph.Id != GuardGraphId
            || forests.Length != Forests.Length
            || Forests.Any(expected => !forests.Any(actual => actual.Id == expected.Id
                && actual.Model == expected.Model && actual.TreeCount == expected.Count))
            || cameras.Length != 2 || cameras.Any(c => c.Parent?.Type.Name != EngineTaskTypes.Dynamic || c.OnScript != "1")
            || buildings.Length != 3)
        {
            throw new InvalidOperationException("The compound task layout is invalid.");
        }
        if (player.Parent?.Type.Name != EngineTaskTypes.Dynamic
            || player.Children.OfType<Igi2HumanPlayerInputTask>().Count() != 1)
        {
            throw new InvalidOperationException("The player is not under Dynamic with its input task.");
        }

        Igi2LevelRun level = new(levelRoot, natives, new Igi2ScriptFunctions());
        int delayedUpdates = 0;
        int soldierUpdates = 0;
        level.Bind(player, () => level.Delay(() => delayedUpdates++));
        foreach (Igi2HumanSoldierTask soldier in soldiers)
        {
            level.Bind(soldier, () => level.Delay(() => soldierUpdates++));
        }
        level.Tick();
        level.Tick();
        if (delayedUpdates != 2 || soldierUpdates != 30 || flow.Tick != 2 || cameras.Any(c => !c.IsOn))
        {
            throw new InvalidOperationException("The player, guards, or cameras did not run on the level tick.");
        }
        sun.Prepare();
        daylight.Prepare();

        if (surface.Tiles.Count != 1
            || !surface.Materials.TryGetValue(0, out TerrainMaterialTask? ground)
            || ground.Texture != GroundTexture
            || surface.Tiles[0].Width != Size
            || terrainMap.MapWidth != Size
            || terrainMap.MapHeight != Size
            || terrain.WorldWidth != WorldWidthMetres
            || player.LevelModel != "jones_1"
            || player.FirstPersonModel != "arms1_1"
            || player.WeaponScript != "Human_AddWeapon(\"WEAPON_ID_M16A2\")"
            || sun.FrontColour.X < 0.9f
            || daylight.Category[0].Ambient.X < 0.4f
            || sky.TopColour.Z < 0.7f
            || cloud.TextureResource != "MISSION:textures/long_descent_clouds.tga"
            || player.LevelPosition.X != StartMetres * UnitsPerMetre)
        {
            throw new InvalidOperationException("The compiled level script did not produce a playable terrain and player start.");
        }

        Igi2TerrainView view = new(new Vector3d(player.LevelPosition.X, player.LevelPosition.Y, player.LevelPosition.Z), []);
        Igi2TerrainTessellation tessellation = new Igi2TerrainLod(surface.Tiles[0], surface.Materials)
            .Tessellate(view, new Igi2TerrainLodSettings(1f, 1f));
        if (tessellation.Mesh.Passes.Count == 0 || tessellation.Mesh.TriangleCount == 0)
        {
            throw new InvalidOperationException("The terrain does not produce any drawable material triangles.");
        }

        string? weaponId = null;
        Igi2ScriptFunctions functions = new();
        functions.Register(natives, "Human_AddWeapon", call =>
        {
            weaponId = call.GetText(0);
            return QvmValue.FromInt32(1);
        }, 1);
        Igi2LineScript.Evaluate(player.WeaponScript, natives, functions);
        if (weaponId != "WEAPON_ID_M16A2")
        {
            throw new InvalidOperationException("The player's weapon script did not request the starting weapon.");
        }
    }

    private static void RunProgram(byte[] bytes, NativeRegistry natives)
    {
        QvmInterpreter interpreter = new(QvmProgram.Read(bytes), natives);
        QvmError error = interpreter.Run();
        if (error != QvmError.None)
        {
            throw new InvalidOperationException($"Generated QVM faulted with {error}: {interpreter.ErrorMessage}");
        }
    }

    private static void SelfTest()
    {
        if (MouseLookSettings.TouchScopeSensitivityScale(false) != 1f
            || MouseLookSettings.TouchScopeSensitivityScale(true) != 2f)
        {
            throw new InvalidOperationException("Touch scope sensitivity is invalid.");
        }
        byte[] heightmap = CreateHeightmap();
        byte[] archive = CreateArchive(heightmap);
        byte[] missionScript = CreateMissionScript();
        byte[] objectsScript = CreateObjectsScript();
        VerifyArchive(archive, heightmap);
        VerifyMissionScript(missionScript);
        VerifyObjectsScript(objectsScript, archive);
        VerifyGuardGraph(CreateGuardGraph());
        foreach (var forest in Forests)
        {
            byte[] placement = CreateForest(forest);
            VerifyForest(placement, forest);
            if (!placement.AsSpan().SequenceEqual(CreateForest(forest)))
            {
                throw new InvalidOperationException("Forest generation is not deterministic.");
            }
        }
        foreach (string handler in new[] { "AIFunction_DefaultHandler", "AISquad_DefaultHandler" })
        {
            int calls = 0;
            NativeRegistry natives = new();
            natives.RegisterFunction(handler, _ =>
            {
                calls++;
                return QvmValue.FromInt32(1);
            });
            RunProgram(CreateDefaultScript(handler), natives);
            if (calls != 1)
            {
                throw new InvalidOperationException($"The generated {handler} script did not invoke its handler.");
            }
        }

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

        if (!heightmap.AsSpan().SequenceEqual(CreateHeightmap())
            || !archive.AsSpan().SequenceEqual(CreateArchive(CreateHeightmap())))
        {
            throw new InvalidOperationException("Terrain generation is not deterministic.");
        }

        byte[] preview = CreatePreview(heightmap);
        var texture = TgaImage.Read(CreateGroundTexture());
        if (texture.Width != 64 || texture.Height != 64)
        {
            throw new InvalidOperationException("The generated ground texture has an invalid size.");
        }
        string header = FormattableString.Invariant($"P5\n{Size} {Size}\n255\n");
        if (preview.Length != Encoding.ASCII.GetByteCount(header) + (Size * Size)
            || !preview.AsSpan(0, header.Length).SequenceEqual(Encoding.ASCII.GetBytes(header)))
        {
            throw new InvalidOperationException("The terrain preview has an invalid PGM header or size.");
        }
    }
}
