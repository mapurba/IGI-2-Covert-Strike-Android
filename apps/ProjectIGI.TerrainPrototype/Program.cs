using System.Buffers.Binary;
using System.Text;
using ProjectIGI.Core.Numerics;
using ProjectIGI.Engine.Tasks;
using ProjectIGI.Formats.Ai;
using ProjectIGI.Formats.Ilff;
using ProjectIGI.Formats.Textures;
using ProjectIGI.Formats.World;
using ProjectIGI.Game.Igi2.Ai;
using ProjectIGI.Game.Igi2.Computer;
using ProjectIGI.Game.Igi2.Flow;
using ProjectIGI.Game.Igi2.Interaction;
using ProjectIGI.Game.Igi2.Player;
using ProjectIGI.Game.Igi2.Pickups;
using ProjectIGI.Game.Igi2.Scripting;
using ProjectIGI.Game.Igi2.World;
using ProjectIGI.Game.Player;
using ProjectIGI.Game.World;
using ProjectIGI.Scripting.Qvm;
using ProjectIGI.Scripting.Runtime;

namespace ProjectIGI.TerrainPrototype;

internal static partial class Program
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
    private const string MaterialName = "heightmap000.tmm";
    private const string GroundTexture = "LOCAL:missions/multiplayer/jungle/textures/lushgrass.jpg";
    private const int GuardGraphId = 300;
    private const int GuardCount = 15;
    private const int PatrolCount = 8;
    private const int PatrolPathBaseId = 500;
    private const float GroundMetres = 14f;
    private const double LookoutMetres = 58;
    private const float GraphLinkMetres = 80f;
    private const int GeneratorSwitchId = 810;
    private const int SecurityConsoleId = 811;
    private const int CoreSwitchId = 812;
    private const int AlarmControlId = 813;
    private const int KeycardPickupId = 850;
    private const int BriefingMessageId = 820;
    private const int BreachMessageId = 821;
    private const int GeneratorMessageId = 822;
    private const int KeycardMessageId = 823;
    private const int ServerAreaMessageId = 824;
    private const int TerminalMessageId = 825;
    private const int CoreMessageId = 826;
    private const int GuardPostMessageId = 827;
    private const int ExtractionMessageId = 828;
    private const int AlarmMessageId = 829;
    private const int ExtractionAreaId = 830;
    private const int BreachAreaId = 831;
    private const int GuardPostAreaId = 832;
    private const int ServerAreaId = 833;
    private const int ObjectivesId = 840;
    private const int GateLeftId = 900;
    private const int GateRightId = 901;
    private const int PadlockId = 902;
    private const int ServiceLeftId = 903;
    private const int ServiceRightId = 904;
    private const int InnerLeftId = 905;
    private const int InnerRightId = 906;
    private const float GateX = 748f;
    private const float GateY = 496f;
    private const float ServiceX = 820f;
    private const float ServiceY = 656f;
    private const float ExtractionX = 800f;
    private const float ExtractionY = 710f;
    private const string MissionName = "Operation Redemption";
    private const string MissionDescription =
        "Uttarakhand, India. The Himalayan Vanguard raided an Indian defence facility and stole the Quantum Encryption Core, a prototype that could compromise the northern border radar and defence grid. HALO drop at dawn on the ridge. Bypass the perimeter fence, avoid the main watchtower, sabotage the generator at Guard Post A to disable CCTV, take the keycard, infiltrate Server Building 03, hack the vault terminal, secure the Core, and exfiltrate to the canyon extraction. Stay ghost: an alarm locks the Core down.";
    private const string BriefingSprite = $"LOCAL:{MissionDirectory}/briefing.jpg";
    private const string MissionCompleteExpression = "StatusMessage_828.isSendt";

    private readonly record struct BuildingSpec(int Id, string Label, string Model, float X, float Y, float Heading, float Z, float HalfX, float HalfY);

    // A door as an installed retail level fits it to a building model: its place and turn in the building's own frame.
    private readonly record struct DoorFit(string Model, float X, float Y, float Z, float Heading, float MaxAngle, float Delta = 1f);

    private readonly record struct DoorSpec(int Id, BuildingSpec Building, DoorFit Fit, string Locked, bool Pickable);

    private readonly record struct CameraSpec(int Id, float X, float Y, float Z, float Heading, string OnScript);

    private readonly record struct GuardPost(float X, float Y, float Heading, float ZOffset = 0f);

    // A prop as a retail level furnishes a building model with it: model, place and turn in the building's own frame.
    private readonly record struct Furnishing(string Model, float X, float Y, float Z, float Heading, float OrientX, float OrientY);
    private readonly record struct PropSpec(string Model, float X, float Y, float Z, float Heading);

    // Compound plan coordinates are metres, the plateau centre at (720, 575); the lookout lies south-west, the lake north-east.
    // The gate faces the mountain (south, low Y); the rear service area and the lake lie north (high Y).
    // location2's buildings stand with their walkable floor 0.1 m above the plateau; their floors are at local 0, -0.28, -0.01.
    private static readonly BuildingSpec GateHut = new(171, "Main gate booth", "434_09_1", 738, 507, 0f, GroundMetres, 2.9f, 2.4f);
    private static readonly BuildingSpec Barracks = new(177, "Barracks", "434_25_1", 700, 614, 0f, GroundMetres + 0.1f, 11.7f, 33.8f);
    private static readonly BuildingSpec Administration = new(181, "Command building", "434_06_1", 650, 625, 0f, GroundMetres + 0.1f, 10.2f, 15.1f);
    private static readonly BuildingSpec Garage = new(176, "Vehicle depot", "434_05_1", 800, 528, 0f, GroundMetres + 0.38f, 14.8f, 10.3f);

    // The warehouse floor is 7 m below its origin; place its floor level with the compound.
    private static readonly BuildingSpec Warehouse = new(179, "Logistics warehouse", "400_30_1", 776, 628, MathF.PI, GroundMetres + 7.03f, 22.7f, 18.9f);
    private static readonly BuildingSpec IntelBuilding = new(180, "Server Building 03", "434_37_1", 818, 600, 0f, GroundMetres + 0.1f, 13.1f, 18f);
    private static readonly BuildingSpec Security = new(178, "Communications bunker", "434_03_1", 764, 586, 0f, GroundMetres + 0.11f, 6.5f, 9.9f);
    private static readonly BuildingSpec ServiceHut = new(170, "Guard Post A", "434_09_1", 650, 528, 0f, GroundMetres, 2.9f, 2.4f);
    private static readonly BuildingSpec Armory = new(175, "Armory", "434_03_1", 708, 530, 0f, GroundMetres + 0.11f, 6.5f, 9.9f);
    private static readonly BuildingSpec Watchtower1 = new(172, "Watchtower 1", "300_08_1", 626, 506, 0f, GroundMetres, 2.4f, 1.5f);
    private static readonly BuildingSpec Watchtower2 = new(173, "Watchtower 2", "300_08_1", 758, 503, 0f, GroundMetres, 2.4f, 1.5f);
    private static readonly BuildingSpec Watchtower3 = new(174, "Rear watchtower", "300_08_1", 834, 650, 0f, GroundMetres, 2.4f, 1.5f);

    private static readonly BuildingSpec[] Buildings =
        [ServiceHut, GateHut, Watchtower1, Watchtower2, Watchtower3, Armory, Garage, Barracks,
            Security, Warehouse, IntelBuilding, Administration];

    private static readonly BuildingSpec[] Perimeter = CreatePerimeter();

    private static readonly PropSpec[] Props =
    [
        new("400_45_1", 657, 535, 0, MathF.PI / 2), new("400_45_1", 657, 521, 0, MathF.PI / 2),
        new("400_45_1", 739, 500, 0, 0), new("400_45_1", 718, 528, 0, MathF.PI / 2),
        new("631_15_1", 662, 528, 0, 0), new("337_11_1", 634, 530, 0, 0),
        new("333_01_1", 640.5f, 523, 0, 0), new("333_01_1", 641.4f, 523.6f, 0, 0),
        new("303_05_1", 622, 596, 0, 0),
        new("600_00_1", 795, 524, 0, 0), new("661_01_1", 806, 528, 0, 0),
        new("663_01_1", 830, 524, 0, 0), new("667_01_1", 822, 506, 0, 0),
        new("408_04_1", 732, 600, 0, 0), new("408_04_1", 732, 630, 0, 0),
        new("660_03_1", 781, 588, 0, 0), new("660_04_1", 790, 574, 0, 0),
        new("605_01_1", 764, 606, 0, 0), new("342_09_1", 790, 604, 0, 0),
        new("342_10_1", 786, 604, 0, 0), new("342_09_1", 782, 603, 0, 0),
        new("337_11_1", 801, 596, 0, MathF.PI / 2),
        .. new[] { 476f, 520f, 552f, 590f, 625f }.Select(y => new PropSpec("332_01_1", 744.6f, y, 0, MathF.PI / 2)),
        .. new[] { 690f, 720f, 780f, 810f }.Select(x => new PropSpec("332_01_1", x, 560.6f, 0, MathF.PI)),
        .. new[] { 540f, 580f, 620f }.Select(y => new PropSpec("332_01_1", 669.4f, y, 0, -MathF.PI / 2)),
        .. new[] { 720f, 790f }.Select(x => new PropSpec("332_01_1", x, 648.6f, 0, MathF.PI)),
        new("900_15_1", 611, 571, 0, 0), new("900_15_1", 612, 575.5f, 0, 0),
        new("900_15_1", 612, 587, 0, 0), new("900_15_1", 611, 590, 0, 0),
        new("900_15_1", 620, 574, 0, 0), new("900_15_1", 620, 586, 0, 0),
        .. new[] { 515f, 530f, 545f, 575f, 590f, 605f, 620f }.Select(y => new PropSpec("900_15_1", 741, y, 0, 0)),
        .. new[] { 520f, 540f, 580f, 592f }.Select(y => new PropSpec("900_15_1", 755, y, 0, 0)),
    ];

    private static readonly (float X0, float Y0, float X1, float Y1)[] RoadRectangles =
    [
        (745, 470, 751, 496), (745, 496, 751, 652), (666, 561, 836, 567),
        (663, 500, 669, 650), (700, 649, 830, 655), (807, 564, 813, 582),
        (797, 538, 803, 564), (712, 637, 748, 643), (642, 601, 666, 607),
        (652, 526, 664, 530), (704, 539, 710, 561), (760, 567, 766, 576),
        (751, 600, 766, 610), (817, 655, 823, 690),
    ];

    private static BuildingSpec[] CreatePerimeter()
    {
        List<BuildingSpec> panels = [];
        for (int i = 0; i < 28; i++)
        {
            if (i != 16)
            {
                Add(616 + (8 * i), 496, 0);
            }
            if (i != 25)
            {
                Add(616 + (8 * i), 656, 0);
            }
            if (i < 20)
            {
                if (i != 10)
                {
                    Add(616, 496 + (8 * i), MathF.PI / 2);
                }
                Add(840, 496 + (8 * i), MathF.PI / 2);
                if (i != 8)
                {
                    Add(676, 496 + (8 * i), MathF.PI / 2);
                }
            }
        }
        Add(630, 525, 0);
        Add(630, 533, 0);
        Add(630, 525, MathF.PI / 2);
        return [.. panels];

        void Add(float x, float y, float heading)
        {
            // Sink the panel slightly into the lowest terrain under its length, avoiding daylight beneath the fence.
            float z = Enumerable.Range(0, 9).Select(step => (float)HeightMetres(
                (x + (step * MathF.Cos(heading)) - 720 + CompoundX) / WorldWidthMetres,
                (y + (step * MathF.Sin(heading)) - 575 + CompoundY) / WorldWidthMetres)).Min() - 0.1f;
            panels.Add(new(1000 + panels.Count, "Perimeter fence", "303_07_1", x, y, heading, z, 0, 0));
        }
    }

    // The retail packs past the mission's own that the buildings and their doors come from: location2's levels 6 and 4.
    private static readonly (string Directory, string Name)[] ExtraPacks =
    [
        ("missions/location2/LEVEL6", "level6"),
        ("missions/location2/LEVEL4", "level4"),
        ("missions/location1/LEVEL7", "level7"),
        ("missions/location1/LEVEL5", "level5"),
    ];

    // Where the security console and the codebook locker switch stand, in their building's frame, metres.
    // The console stands where retail's location2 level 4 puts its terminal in this building.
    private static readonly BuildingSpec GeneratorPen = new(169, "Generator pen", "337_11_1", 634, 530, 0f, GroundMetres, 1f, 1f);
    private static readonly (string Label, BuildingSpec Building, float X, float Y, float Z)[] ObjectivePoints =
    [
        ("Generator switch", GeneratorPen, 3.2f, -1f, 0f),
        ("Guard Post A keycard", ServiceHut, 1.25f, -0.75f, 1.03f),
        ("Server terminal", IntelBuilding, -8.97f, -8.55f, 1.11f),
        ("Quantum Core", IntelBuilding, -6f, -12f, 6.53f),
    ];

    // Door fits as retail location2 levels 6 and 4 hang them on these models, turned into each model's own frame.
    private static readonly DoorFit[] IntelDoors =
    [
        new("501_06_1", -8.08f, -15.04f, 0.21f, 0f, 90f),
        new("501_06_1", -2.79f, -9.63f, 0.14f, MathF.PI / 2f, 90f),
        new("501_06_1", 8.25f, 14.86f, 0.14f, MathF.PI, 90f),
        new("501_06_1", 10.14f, 11.03f, 5.38f, 3f * MathF.PI / 2f, 90f),
        new("501_06_1", -10.05f, 11.05f, 5.38f, 3f * MathF.PI / 2f, 90f),
    ];

    private static readonly DoorFit[] OfficeDoors =
    [
        new("501_06_1", -8.07f, -15.07f, 0.19f, 0f, -90f),
        new("501_04_1", -2.71f, -9.63f, 0.18f, MathF.PI / 2f, -90f),
        new("501_04_1", 8.25f, 15.06f, 0.20f, MathF.PI, -90f),
    ];

    private static readonly DoorFit[] HallDoors =
    [
        new("501_04_1", 10.92f, 26.80f, 0.20f, MathF.PI / 2f, 90f),
        new("501_04_1", 8.72f, 9.89f, 0.20f, 0f, 90f),
        new("501_04_1", -0.60f, 1.32f, 0.20f, 0f, -90f),
        new("501_04_1", -5.43f, -1.72f, 0.20f, 0f, -90f),
        new("501_04_1", 4.36f, -1.75f, 0.21f, 0f, 90f),
    ];

    private static readonly DoorFit[] GuardhouseDoors =
    [
        new("501_04_1", -1.46f, -4.69f, 0.48f, MathF.PI, 90f),
        new("501_04_1", -0.44f, -1.22f, 0.48f, MathF.PI, 90f),
        new("501_06_1", -1.08f, 8.22f, 0.48f, MathF.PI, 90f),
        new("501_04_1", 4.34f, 6.10f, 0.46f, 0f, -90f),
    ];

    private static readonly DoorFit HutDoor = new("515_01_1", 1.96f, 1.50f, 0.01f, MathF.PI / 2f, -90f);
    private static readonly DoorFit WarehouseLoadingDoor = new("515_01_1", 16.66f, 15.11f, -6.21f, 0f, 90f, 0.95f);
    private static readonly DoorFit WarehouseSideDoor = new("515_01_1", -20.19f, -11.87f, -6.21f, 3f * MathF.PI / 2f, 90f);

    // Doors do not all behave alike: most open freely, some hold a lock that can be picked, and the intelligence building's
    // rear service door (like the warehouse side door) only unlocks when the security console is used.
    private static readonly DoorSpec[] Doors =
    [
        new(700, IntelBuilding, IntelDoors[0], string.Empty, false),
        new(701, IntelBuilding, IntelDoors[1], "!GenericPickup_850.isPickedUp", false),
        new(702, IntelBuilding, IntelDoors[2], "!Door_702.isPicked", true),
        new(703, IntelBuilding, IntelDoors[3], "!Door_703.isPicked", true),
        new(704, IntelBuilding, IntelDoors[4], "!Switch_811.isOn", false),
        new(705, Administration, OfficeDoors[0], string.Empty, false),
        new(706, Administration, OfficeDoors[1], string.Empty, false),
        new(707, Administration, OfficeDoors[2], "!Door_707.isPicked", true),
        new(708, Barracks, HallDoors[0], string.Empty, false),
        new(709, Barracks, HallDoors[1], string.Empty, false),
        new(710, Barracks, HallDoors[2], "!Door_710.isPicked", true),
        new(711, Barracks, HallDoors[3], string.Empty, false),
        new(712, Barracks, HallDoors[4], string.Empty, false),
        new(713, Armory, GuardhouseDoors[0], "!Door_713.isPicked", true),
        new(714, Armory, GuardhouseDoors[1], "!GenericPickup_850.isPickedUp", false),
        new(715, Armory, GuardhouseDoors[2], string.Empty, false),
        new(716, Armory, GuardhouseDoors[3], string.Empty, false),
        new(717, Security, GuardhouseDoors[0], string.Empty, false),
        new(718, Security, GuardhouseDoors[1], string.Empty, false),
        new(719, Security, GuardhouseDoors[2], "!Door_719.isPicked", true),
        new(720, Security, GuardhouseDoors[3], string.Empty, false),
        new(721, Warehouse, WarehouseLoadingDoor, string.Empty, false),
        new(722, Warehouse, WarehouseSideDoor, "!Door_722.isPicked", true),
    ];

    private static readonly CameraSpec[] Cameras =
    [
        new(150, 624.2f, 505.2f, GroundMetres + 8.6f, 3f * MathF.PI / 4f, "!Switch_810.isOn"),
        new(151, 622, 596, GroundMetres + 3.6f, MathF.PI, "!Switch_810.isOn"),
        new(152, 758, 503, GroundMetres + 8.6f, MathF.PI, "!Switch_811.isOn"),
        new(153, 816, 581.4f, GroundMetres + 4.5f, MathF.PI, "!Switch_811.isOn"),
        new(154, 764, 595.8f, GroundMetres + 4f, 0f, "!Switch_811.isOn"),
        new(155, 833, 650, GroundMetres + 8.6f, 2.4f, "!Switch_811.isOn"),
    ];

    // The first PatrolCount posts are a ring patrolled post to post; the rest are fixed posts.
    private static readonly GuardPost[] GuardPosts =
    [
        new(722, 652, 0f), new(798, 652, 0f), new(690, 552, 0f), new(766, 552, 0f),
        new(836, 548, 0f), new(836, 626, 0f), new(624, 548, 0f), new(624, 626, 0f),
        new(649.7f, 529, -MathF.PI / 2f, 0.2f), new(664, 590, 0f), new(737.7f, 508, MathF.PI, 0.2f),
        new(754, 512, 0f), new(811, 578, MathF.PI), new(720, 572, 0f), new(816, 646, 0f),
    ];
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
        new(408, "940_25_1", 955, 25f, 422f * WorldScale, 587f * WorldScale, 120f * WorldScale, true),
    ];

    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--entry-test")
        {
            VerifyEntryGeometry(args[1]);
            return 0;
        }
        if (args.Length == 2 && args[0] == "--inspect-buildings")
        {
            BuildingInspector.PrintCandidates(args[1], Console.Out);
            return 0;
        }

        static void VerifyEntryGeometry(string gameDirectory)
        {
            Func<string, MefModel?> resolve = BuildingInspector.CreateResolver(gameDirectory);
            foreach (string model in (string[])["303_07_1", "303_08_1", "306_02_1", "306_03_1"])
            {
                if (resolve(model) is null)
                {
                    throw new InvalidOperationException($"Missing compound entry model {model}.");
                }
            }
            var fence = BuildingInspector.SolidTriangles(resolve, Perimeter.Select(panel =>
                new BuildingInspector.Placed(panel.Model, Point(panel.X, panel.Y, panel.Z), new(0, 0, panel.Heading))));
            foreach (BuildingSpec panel in Perimeter)
            {
                float c = MathF.Cos(panel.Heading), s = MathF.Sin(panel.Heading);
                for (float along = 0.1f; along < 8; along += 0.4f)
                {
                    Vector3d centre = Point(panel.X + (along * c), panel.Y + (along * s), panel.Z + 1);
                    Vector3d across = new(-s * UnitsPerMetre, c * UnitsPerMetre, 0);
                    if (BuildingInspector.FirstHit(fence, centre - across, centre + across) is null)
                    {
                        throw new InvalidOperationException($"Fence panel {panel.Id} has a collision gap at {along} m.");
                    }
                }
            }
            foreach ((float x, float y) in ((float, float)[])[(GateX, GateY), (ServiceX, ServiceY)])
            {
                var closed = GateTriangles(x, y, false);
                var open = GateTriangles(x, y, true);
                foreach (float offset in (float[])[-2, 0, 2])
                {
                    foreach (float height in (float[])[0.5f, 1, 1.7f])
                    {
                        Vector3d from = Point(x + offset, y - 2, GroundMetres + height);
                        Vector3d to = Point(x + offset, y + 2, GroundMetres + height);
                        if (BuildingInspector.FirstHit(closed, from, to) is null
                            || BuildingInspector.FirstHit(open, from, to) is not null
                            || BuildingInspector.FirstHit(fence, from, to) is not null)
                        {
                            throw new InvalidOperationException("A compound gate does not block when shut and clear when open.");
                        }
                    }
                }
            }
            var padlock = BuildingInspector.SolidTriangles(resolve,
                [new("306_02_1", Point(GateX, GateY - 0.18f, GroundMetres), new(0, 0, MathF.PI))], level: 0);
            var gate = GateTriangles(GateX, GateY, false, level: 0);
            bool shootable = padlock.Any(face =>
            {
                Vector3d target = (face.A + face.B + face.C) / 3;
                Vector3d shotFrom = target - new Vector3d(0, 5 * UnitsPerMetre, 0);
                Vector3d shotTo = target + new Vector3d(0, UnitsPerMetre, 0);
                double? lockHit = BuildingInspector.FirstHit(padlock, shotFrom, shotTo);
                double? gateHit = BuildingInspector.FirstHit(gate, shotFrom, shotTo);
                return lockHit is not null && gateHit is not null && lockHit < gateHit;
            });
            if (!shootable)
            {
                throw new InvalidOperationException("The entrance padlock cannot be shot from outside before the gate blocks the bullet.");
            }
            Console.WriteLine($"Entry geometry passed: {Perimeter.Length} continuous fence panels, two blocked/clear gates, exposed shootable padlock.");

            static Vector3d Point(float x, float y, float z) => new(CompoundWorldX(x), CompoundWorldY(y), z * UnitsPerMetre);
            List<(Vector3d A, Vector3d B, Vector3d C)> GateTriangles(float x, float y, bool open, int level = 1) =>
                BuildingInspector.SolidTriangles(resolve,
                [
                    new("303_08_1", Point(x - 4, y, GroundMetres), new(0, 0, open ? MathF.PI / 2 : 0)),
                    new("303_08_1", Point(x + 4, y, GroundMetres), new(0, 0, open ? MathF.PI / 2 : MathF.PI)),
                ], level);
        }

        if (args.Length >= 3 && args[0] == "--inspect-floors")
        {
            BuildingInspector.PrintFloors(args[1], args[2..], Console.Out);
            return 0;
        }

        if (args.Length == 2 && args[0] == "--check-doors")
        {
            Func<string, ProjectIGI.Formats.World.MefModel?> resolve = BuildingInspector.CreateResolver(args[1]);
            BuildingInspector.Placed[] placed = [.. Buildings.Select(b => new BuildingInspector.Placed(b.Model,
                new Vector3d(CompoundWorldX(b.X), CompoundWorldY(b.Y), b.Z * UnitsPerMetre), new Vector3f(0f, 0f, b.Heading)))];
            int crossing = 0;
            foreach (DoorSpec door in Doors)
            {
                (float x, float y, float z) = DoorPosition(door);
                int hits = BuildingInspector.DoorPortalHits(resolve, placed,
                    new BuildingInspector.Placed(door.Fit.Model, new Vector3d(x, y, z), new Vector3f(0f, 0f, DoorHeading(door))));
                crossing += hits > 0 ? 1 : 0;
                Console.WriteLine($"Door {door.Id} {door.Building.Label} {door.Fit.Model} locked '{door.Locked}': {hits} portal faces");
            }

            Console.WriteLine($"{crossing} of {Doors.Length} doors cross a doorway portal");
            return 0;
        }

        if (args.Length == 2 && args[0] == "--walk-test")
        {
            return WalkTest(args[1], Console.Out) ? 0 : 2;
        }

        if (args.Length >= 2 && args[0] == "--survey-interiors")
        {
            SurveyInteriors(args[1], args[2..], Console.Out);
            return 0;
        }

        if (args.Length >= 3 && args[0] == "--mesh-parts")
        {
            Func<string, ProjectIGI.Formats.World.MefModel?> resolve = BuildingInspector.CreateResolver(args[1]);
            foreach (string model in args[2..])
            {
                if (resolve(model) is { } mef)
                {
                    var vertices = mef.Vertices.ToArray();
                    Vector3f min = new(vertices.Min(v => v.Position.X), vertices.Min(v => v.Position.Y), vertices.Min(v => v.Position.Z));
                    Vector3f max = new(vertices.Max(v => v.Position.X), vertices.Max(v => v.Position.Y), vertices.Max(v => v.Position.Z));
                    Console.WriteLine($"{model}: bounds {min / UnitsPerMetre} to {max / UnitsPerMetre} m");
                }
                foreach (int level in (int[])[0, 1])
                {
                    List<string> parts = [];
                    BuildingInspector.SolidTriangles(resolve, [new BuildingInspector.Placed(model, default, default)], level, parts);
                    Console.WriteLine($"{model} level {level}: " + string.Join(", ", parts.GroupBy(p => p).Select(g => $"{g.Key} {g.Count()}")));
                }
            }

            return 0;
        }

        if (args.Length >= 4 && args[0] == "--render")
        {
            // --render <game> <location/level or -> <out-dir> models...: a shaded PNG of each model's detailed collision mesh.
            string[]? packs = args[2] == "-" ? null : BuildingInspector.LevelPacks(args[2].Split('/')[0], args[2].Split('/')[1]);
            Func<string, ProjectIGI.Formats.World.MefModel?> resolve = BuildingInspector.CreateResolver(args[1], packs);
            Directory.CreateDirectory(args[3]);
            foreach (string model in args[4..])
            {
                var triangles = BuildingInspector.SolidTriangles(resolve, [new BuildingInspector.Placed(model, default, default)], 0)
                    .Select(t => (t.A / UnitsPerMetre, t.B / UnitsPerMetre, t.C / UnitsPerMetre)).ToList();
                ModelPreview.Render(triangles, Path.Combine(args[3], model + ".png"));
                ModelPreview.Render(triangles, Path.Combine(args[3], model + "-back.png"), 215);
                Console.WriteLine($"{model}: {triangles.Count} triangles");
            }

            return 0;
        }

        if (args.Length >= 4 && args[0] == "--render-visual")
        {
            // --render-visual <game> <location/level or -> <out-dir> models...: the drawn mesh, back faces culled.
            string[]? packs = args[2] == "-" ? null : BuildingInspector.LevelPacks(args[2].Split('/')[0], args[2].Split('/')[1]);
            Func<string, ProjectIGI.Formats.World.MefModel?> resolve = BuildingInspector.CreateResolver(args[1], packs);
            Directory.CreateDirectory(args[3]);
            foreach (string model in args[4..])
            {
                if (resolve(model) is not { } mef)
                {
                    Console.WriteLine($"{model}: not found");
                    continue;
                }

                List<(Vector3d, Vector3d, Vector3d)> triangles = [];
                List<Vector3d> normals = [];
                var vertices = mef.Vertices.ToArray();
                foreach (var record in mef.DrawRecords)
                {
                    ReadOnlySpan<ushort> indices = record.Indices.Span;
                    for (int i = 0; i + 2 < indices.Length; i += 3)
                    {
                        var (a, b, c) = (vertices[record.ToVertexIndex(indices[i])], vertices[record.ToVertexIndex(indices[i + 1])],
                            vertices[record.ToVertexIndex(indices[i + 2])]);
                        Vector3d pa = new(a.Position.X, a.Position.Y, a.Position.Z), pb = new(b.Position.X, b.Position.Y, b.Position.Z),
                            pc = new(c.Position.X, c.Position.Y, c.Position.Z);
                        // Drawn faces wind the opposite way to collision faces: this is the outward normal.
                        Vector3d n = Vector3d.Cross(pc - pa, pb - pa);
                        if (n.Length < 1e-9)
                        {
                            continue;
                        }

                        triangles.Add((pa, pb, pc));
                        normals.Add(n / n.Length);
                    }
                }

                ModelPreview.Render(triangles, Path.Combine(args[3], model + "-visual.png"), facing: normals);
                ModelPreview.Render(triangles, Path.Combine(args[3], model + "-visual-back.png"), 215, facing: normals);
                if (Environment.GetEnvironmentVariable("IGI_CUT") is { } cutText)
                {
                    // A section: drop what lies above this fraction of the model's height, to look inside from above.
                    double cut = double.Parse(cutText, System.Globalization.CultureInfo.InvariantCulture);
                    double zMin = triangles.Min(t => Math.Min(t.Item1.Z, Math.Min(t.Item2.Z, t.Item3.Z)));
                    double zMax = triangles.Max(t => Math.Max(t.Item1.Z, Math.Max(t.Item2.Z, t.Item3.Z)));
                    double limit = zMin + (cut * (zMax - zMin));
                    int[] keep = [.. Enumerable.Range(0, triangles.Count)
                        .Where(i => Math.Max(triangles[i].Item1.Z, Math.Max(triangles[i].Item2.Z, triangles[i].Item3.Z)) <= limit)];
                    ModelPreview.Render([.. keep.Select(i => triangles[i])], Path.Combine(args[3], model + "-section.png"),
                        facing: [.. keep.Select(i => normals[i])]);
                }
                Console.WriteLine($"{model}: {triangles.Count} drawn triangles");
            }

            return 0;
        }

        if (args.Length >= 3 && args[0] == "--which-pack")
        {
            // Which retail pack (.mtp, by path) declares each model, so a mission knows what its packs must hold.
            string root = Path.GetFullPath(args[1]);
            var packs = Directory.EnumerateFiles(root, "*.mtp", SearchOption.AllDirectories)
                .Select(path => (Path: Path.GetRelativePath(root, path), Names: ProjectIGI.Formats.World.MtpPack.Read(File.ReadAllBytes(path))
                    .Instances.Select(instance => instance.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)))
                .ToList();
            foreach (string model in args[2..])
            {
                Console.WriteLine($"{model}: " + string.Join(", ", packs.Where(p => p.Names.Contains(model)).Select(p => p.Path)));
            }

            return 0;
        }
        if (args.Length >= 3 && args[0] == "--model-textures")
        {
            // The textures each model's draw records sample, and which of the mission's texture archives holds each one.
            string root = Path.GetFullPath(args[1]);
            var paths = BuildingInspector.MissionPackPaths;
            var mtps = paths.Select(p => ProjectIGI.Formats.World.MtpPack.Read(File.ReadAllBytes(Path.Combine(root, p + ".mtp")))).ToArray();
            var table = ProjectIGI.Game.Igi2.World.Igi2ModelTable.Create(mtps);
            var archives = paths.Select(p => Path.Combine(root, Path.GetDirectoryName(p)!, "textures", Path.GetFileName(p) + ".res"))
                .Prepend(Path.Combine(root, "COMMON", "textures", "textures.res"))
                .Where(File.Exists)
                .Select(p => (Path: Path.GetRelativePath(root, p), Archive: ProjectIGI.Formats.Ilff.IresArchive.Read(File.ReadAllBytes(p))))
                .ToList();
            Func<string, ProjectIGI.Formats.World.MefModel?> resolve = BuildingInspector.CreateResolver(args[1]);
            foreach (string model in args[2..])
            {
                if (resolve(model) is not { } mef)
                {
                    Console.WriteLine($"{model}: not resolvable");
                    continue;
                }

                foreach (int slot in mef.DrawRecords.Select(d => d.MaterialSlot).Distinct().Order())
                {
                    string where = "UNRESOLVED";
                    if (table.TryResolveTexture(model, slot, out string? texture))
                    {
                        var first = archives.FirstOrDefault(a => a.Archive.FindBody(texture + ".tex") is not null);
                        var holders = archives.Where(a => a.Archive.FindBody(texture + ".tex") is not null).Select(a => a.Path).ToList();
                        where = $"{texture}: " + (holders.Count == 0 ? "MISSING" : string.Join(" | ", holders));
                    }

                    Console.WriteLine($"{model} slot {slot}: {where}");
                }
            }

            return 0;
        }
        if (args.Length >= 3 && args[0] == "--openings")
        {
            Func<string, ProjectIGI.Formats.World.MefModel?> resolve = BuildingInspector.CreateResolver(args[1]);
            foreach (string model in args[2..])
            {
                var openings = BuildingInspector.Openings(resolve, model);
                Console.WriteLine($"{model}: {openings.Count} openings");
                foreach (var o in openings)
                {
                    Console.WriteLine(FormattableString.Invariant(
                        $"  {o.Part} d{o.Depth} at ({o.Centre.X:F1},{o.Centre.Y:F1},{o.Centre.Z:F1}) {o.Width:F1} x {o.Height:F1} m normal ({o.Normal.X:F2},{o.Normal.Y:F2},{o.Normal.Z:F2})"));
                }
            }

            BuildingInspector.PrintFloors(args[1], args[2..], Console.Out);
            return 0;
        }

        if (args.Length == 3 && args[0] == "--check-level-doors")
        {
            LevelInspector.CheckDoors(args[1], args[2], Console.Out);
            return 0;
        }

        if (args.Length >= 3 && args[0] == "--dump-types")
        {
            LevelInspector.PrintTypes(args[1], args[2..], Console.Out);
            return 0;
        }

        if (args.Length >= 3 && args[0] == "--furnishings")
        {
            LevelInspector.PrintFurnishings(args[1], args[2..], Console.Out);
            return 0;
        }

        if (args.Length == 2 && args[0] == "--inspect-level")
        {
            LevelInspector.Print(args[1], Console.Out);
            return 0;
        }

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
            Console.Error.WriteLine("       dotnet run --project apps/ProjectIGI.TerrainPrototype -- --inspect-buildings <game-directory>");
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
        foreach (string briefingFile in new[] { "briefing.wav", "briefing_captions.txt", "briefing.jpg" })
        {
            string source = FindRepositoryFile(Path.Combine("custom-mission", "briefing", briefingFile));
            File.Copy(source, Path.Combine(output, briefingFile));
        }
        for (int id = 200; id < 200 + GuardCount; id++)
        {
            File.WriteAllBytes(Path.Combine(output, "AI", $"{id}.qvm"), CreateGuardScript(id - 200));
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
        Console.WriteLine("briefing.wav, briefing_captions.txt, briefing.jpg: mission briefing assets");
        Console.WriteLine("terrain-preview.pgm: grayscale height preview (bright = high)");
        return 0;
    }

    /// <summary>
    /// Checks with the installed game's collision meshes that a person can pass each doorway (its door open): a clear line
    /// at knee, waist and head height 3 m either side of the door, a floor under both ends, and headroom above them.
    /// </summary>
    private static bool WalkTest(string gameDirectory, TextWriter output)
    {
        Func<string, ProjectIGI.Formats.World.MefModel?> resolve = BuildingInspector.CreateResolver(gameDirectory);
        BuildingInspector.Placed[] placed = [.. Buildings.Select(b => new BuildingInspector.Placed(b.Model,
            new Vector3d(CompoundWorldX(b.X), CompoundWorldY(b.Y), b.Z * UnitsPerMetre), new Vector3f(0f, 0f, b.Heading)))];
        var triangles = BuildingInspector.SolidTriangles(resolve, placed);
        output.WriteLine($"{triangles.Count} solid triangles in {placed.Length} buildings");
        int passable = 0;
        foreach (DoorSpec door in Doors)
        {
            (float x, float y, float z) = DoorPosition(door);
            if (BuildingInspector.DoorAxis(resolve, new BuildingInspector.Placed(door.Fit.Model, new Vector3d(x, y, z),
                new Vector3f(0f, 0f, DoorHeading(door)))) is not { } axis)
            {
                output.WriteLine($"Door {door.Id}: model {door.Fit.Model} missing");
                continue;
            }

            Vector3d through = new(axis.Through.X, axis.Through.Y, 0);
            through /= through.Length;
            Vector3d Point(double along, double up) => new(axis.Centre.X + (through.X * along * UnitsPerMetre),
                axis.Centre.Y + (through.Y * along * UnitsPerMetre), z + (up * UnitsPerMetre));
            List<string> blocked = [];
            foreach (double up in (double[])[0.5, 1.0, 1.7])
            {
                if (BuildingInspector.FirstHit(triangles, Point(-1.5, up), Point(1.5, up)) is { } t)
                {
                    blocked.Add(FormattableString.Invariant($"{up:F1}m@{(t * 3) - 1.5:F2}"));
                }
            }

            string Floor(double along)
            {
                double? down = BuildingInspector.FirstHit(triangles, Point(along, 1.2), Point(along, -1.5));
                double? head = BuildingInspector.FirstHit(triangles, Point(along, 0.3), Point(along, 3.0));
                string floor = down is { } d ? FormattableString.Invariant($"floor {1.2 - (d * 2.7):F2}m") : "no floor";
                string room = head is { } h ? FormattableString.Invariant($"head {0.3 + (h * 2.7):F2}m") : "open sky";
                return $"{floor}, {room}";
            }

            bool clear = blocked.Count == 0;
            passable += clear ? 1 : 0;
            output.WriteLine($"Door {door.Id} {door.Building.Label}: {(clear ? "CLEAR" : "BLOCKED " + string.Join(' ', blocked))}; "
                + $"side A {Floor(-2.0)}; side B {Floor(2.0)}");
        }

        output.WriteLine($"{passable} of {Doors.Length} doorways are clear at knee, waist and head height");

        // Reachability over the whole compound and its surrounds, in metres, from the open ground at its border.
        double Metres(double units) => units / UnitsPerMetre;
        var metric = triangles.Select(t => (new Vector3d(Metres(t.A.X), Metres(t.A.Y), Metres(t.A.Z)),
            new Vector3d(Metres(t.B.X), Metres(t.B.Y), Metres(t.B.Z)), new Vector3d(Metres(t.C.X), Metres(t.C.Y), Metres(t.C.Z))));
        Reachability walk = new(metric, (x, y) => HeightMetres(x / WorldWidthMetres, y / WorldWidthMetres),
            Metres(CompoundWorldX(625f)), Metres(CompoundWorldY(480f)), Metres(CompoundWorldX(815f)), Metres(CompoundWorldY(675f)));
        bool everyBuilding = true;
        foreach (BuildingSpec building in Buildings)
        {
            var own = BuildingInspector.SolidTriangles(resolve, [new BuildingInspector.Placed(building.Model,
                new Vector3d(CompoundWorldX(building.X), CompoundWorldY(building.Y), building.Z * UnitsPerMetre), new Vector3f(0f, 0f, building.Heading))]);
            double[] xs = [.. own.SelectMany(t => (double[])[t.A.X, t.B.X, t.C.X]).Select(Metres)];
            double[] ys = [.. own.SelectMany(t => (double[])[t.A.Y, t.B.Y, t.C.Y]).Select(Metres)];
            // Inset half a metre so the open ground hugging the walls does not count as inside.
            (double x0, double y0, double x1, double y1) = (xs.Min() + 0.5, ys.Min() + 0.5, xs.Max() - 0.5, ys.Max() - 0.5);
            var offered = walk.StandingArea(x0, y0, x1, y1);
            var reached = walk.ReachedArea(x0, y0, x1, y1);
            double ground = HeightMetres(Metres(CompoundWorldX(building.X)) / WorldWidthMetres, Metres(CompoundWorldY(building.Y)) / WorldWidthMetres);
            var structure = walk.ReachedOnStructure(x0, y0, x1, y1);
            double inside = structure.Sum(pair => pair.Value);
            double raised = structure.Where(pair => pair.Key >= Math.Floor(ground) + 2).Sum(pair => pair.Value);
            everyBuilding &= inside > 4;
            output.WriteLine(FormattableString.Invariant(
                $"{building.Label} ({building.Model}): reachable floor {inside:F0} m2, of which {raised:F0} m2 is 2.5 m or more above the ground"));
            output.WriteLine("  standing area offered by height (m above ground: m2 reached/offered): " + string.Join("  ", offered
                .Where(pair => pair.Value >= 2)
                .Select(pair => FormattableString.Invariant($"{pair.Key - Math.Floor(ground):F0}:{reached.GetValueOrDefault(pair.Key):F0}/{pair.Value:F0}"))));
            if (Environment.GetEnvironmentVariable("IGI_TRACE") is { } trace && (trace == building.Label || trace == "all"))
            {
                // A plan of the building in its own frame, 1 m per character, +X right and +Y up: '#' a reached floor,
                // 'o' a floor not reached, '^' a reached floor 2 m or more up, '+' an unreached one, '.' reached terrain.
                int reach = int.Parse(Environment.GetEnvironmentVariable("IGI_TRACE_SIZE") ?? "16", System.Globalization.CultureInfo.InvariantCulture);
                for (int ly = reach; ly >= -reach; ly--)
                {
                    StringBuilder row = new(FormattableString.Invariant($"    {ly,4} "));
                    for (int lx = -reach; lx <= reach; lx++)
                    {
                        (float px, float py, _) = InBuilding(building, lx, ly, 0f);
                        double terrain = HeightMetres(Metres(px) / WorldWidthMetres, Metres(py) / WorldWidthMetres);
                        var levels = walk.At(Metres(px), Metres(py)).ToArray();
                        var raisedLevels = levels.Where(l => l.Z > terrain + 2).ToArray();
                        var floorLevels = levels.Where(l => Math.Abs(l.Z - terrain) >= 0.05 && l.Z <= terrain + 2).ToArray();
                        char c = raisedLevels.Any(l => l.Reached) ? '^'
                            : floorLevels.Any(l => l.Reached) ? '#'
                            : floorLevels.Length > 0 ? 'o'
                            : raisedLevels.Length > 0 ? '+'
                            : levels.Any(l => l.Reached) ? '.' : ' ';
                        row.Append(c);
                    }

                    output.WriteLine(row.ToString());
                }
            }
        }
        if (Environment.GetEnvironmentVariable("IGI_PROBE") is { } probeSpec)
        {
            // "Label;x,y;x,y..." prints every surface over each local point of that building.
            string[] parts = probeSpec.Split(';');
            BuildingSpec target = Buildings.First(b => b.Label == parts[0]);
            foreach (string point in parts.Skip(1))
            {
                float[] xy = [.. point.Split(',').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture))];
                (float px, float py, _) = InBuilding(target, xy[0], xy[1], 0f);
                output.WriteLine($"  probe {point}: " + string.Join(' ', walk.Surfaces(Metres(px), Metres(py)).OrderBy(s => s.Z)
                    .Select(s => FormattableString.Invariant($"{s.Z:F2}/{s.Nz:F2}"))) + "  levels " + string.Join(' ', walk.At(Metres(px), Metres(py))
                    .Select(l => FormattableString.Invariant($"{l.Z:F2}{(l.Reached ? "*" : "")}"))));
            }
        }
        foreach ((string label, BuildingSpec building, float x, float y, float z) in ObjectivePoints)
        {
            (float wx, float wy, float wz) = InBuilding(building, x, y, z);
            bool reaches = walk.Reaches(Metres(wx), Metres(wy), Metres(wz) - 1.0, 1.5);
            everyBuilding &= reaches;
            output.WriteLine($"{label}: {(reaches ? "reachable" : "NOT REACHABLE")} on foot");
        }
        return passable == Doors.Length && everyBuilding;
    }

    /// <summary>
    /// Places each building-sized model alone on flat ground, its candidate ground floors in turn at grade, and floods it
    /// with the engine's movement rules: how much floor inside, and how much of it upstairs, a person can walk onto.
    /// </summary>
    private static void SurveyInteriors(string gameDirectory, IEnumerable<string> only, TextWriter output)
    {
        // IGI_PACKS=location2/level3 surveys another retail level's packs instead of the mission's.
        string[]? packs = Environment.GetEnvironmentVariable("IGI_PACKS") is { Length: > 0 } level
            ? BuildingInspector.LevelPacks(level.Split('/')[0], level.Split('/')[1]) : null;
        Func<string, ProjectIGI.Formats.World.MefModel?> resolve = BuildingInspector.CreateResolver(gameDirectory, packs);
        string[] filter = [.. only];
        IEnumerable<string> names = filter.Length > 0 ? filter : BuildingInspector.ModelNames(gameDirectory, packs);
        List<(string Name, string Line, double Inside)> results = [];
        foreach (string name in names)
        {
            List<(Vector3d A, Vector3d B, Vector3d C)> metric;
            try
            {
                metric = [.. BuildingInspector.SolidTriangles(resolve, [new BuildingInspector.Placed(name, default, default)])
                    .Select(t => (t.A / UnitsPerMetre, t.B / UnitsPerMetre, t.C / UnitsPerMetre))];
            }
            catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or ArgumentException)
            {
                continue;
            }

            if (metric.Count < 12)
            {
                continue;
            }

            if (filter.Length == 1 && Environment.GetEnvironmentVariable("IGI_PROBE_XY") is { } probeXy)
            {
                List<string> parts = [];
                var labelled = BuildingInspector.SolidTriangles(resolve, [new BuildingInspector.Placed(name, default, default)], 1, parts);
                foreach (string point in probeXy.Split(';'))
                {
                    double[] xy = [.. point.Split(',').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture))];
                    output.WriteLine($"probe {point}:");
                    for (int i = 0; i < labelled.Count; i++)
                    {
                        var (a, b, c) = (labelled[i].A / UnitsPerMetre, labelled[i].B / UnitsPerMetre, labelled[i].C / UnitsPerMetre);
                        double d = ((b.Y - c.Y) * (a.X - c.X)) + ((c.X - b.X) * (a.Y - c.Y));
                        if (Math.Abs(d) < 1e-12)
                        {
                            continue;
                        }

                        double l1 = (((b.Y - c.Y) * (xy[0] - c.X)) + ((c.X - b.X) * (xy[1] - c.Y))) / d;
                        double l2 = (((c.Y - a.Y) * (xy[0] - c.X)) + ((a.X - c.X) * (xy[1] - c.Y))) / d;
                        if (l1 < 0 || l2 < 0 || l1 + l2 > 1)
                        {
                            continue;
                        }

                        Vector3d n = Vector3d.Cross(c - a, b - a);
                        output.WriteLine(FormattableString.Invariant(
                            $"  z {(l1 * a.Z) + (l2 * b.Z) + ((1 - l1 - l2) * c.Z):F2} nz {n.Z / n.Length:F2} {parts[i]}"));
                    }
                }
            }
            if (filter.Length == 1 && Environment.GetEnvironmentVariable("IGI_WALLS") is { Length: > 0 } wallBox)
            {
                // Vertical faces in a box "x0,y0,x1,y1": where the walls are, and which part each belongs to.
                double[] box = [.. wallBox.Split(',').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture))];
                List<string> parts = [];
                var labelled = BuildingInspector.SolidTriangles(resolve, [new BuildingInspector.Placed(name, default, default)], 1, parts);
                for (int i = 0; i < labelled.Count; i++)
                {
                    var (a, b, c) = (labelled[i].A / UnitsPerMetre, labelled[i].B / UnitsPerMetre, labelled[i].C / UnitsPerMetre);
                    Vector3d n = Vector3d.Cross(c - a, b - a);
                    if (n.Length < 1e-9 || Math.Abs(n.Z / n.Length) >= 0.1
                        || Math.Max(a.X, Math.Max(b.X, c.X)) < box[0] || Math.Min(a.X, Math.Min(b.X, c.X)) > box[2]
                        || Math.Max(a.Y, Math.Max(b.Y, c.Y)) < box[1] || Math.Min(a.Y, Math.Min(b.Y, c.Y)) > box[3])
                    {
                        continue;
                    }

                    output.WriteLine(FormattableString.Invariant(
                        $"  wall ({a.X:F2},{a.Y:F2},{a.Z:F2}) ({b.X:F2},{b.Y:F2},{b.Z:F2}) ({c.X:F2},{c.Y:F2},{c.Z:F2}) {parts[i]}"));
                }
            }

            double[] xs = [.. metric.SelectMany(t => (double[])[t.Item1.X, t.Item2.X, t.Item3.X])];
            double[] ys = [.. metric.SelectMany(t => (double[])[t.Item1.Y, t.Item2.Y, t.Item3.Y])];
            double[] zs = [.. metric.SelectMany(t => (double[])[t.Item1.Z, t.Item2.Z, t.Item3.Z])];
            double width = xs.Max() - xs.Min(), length = ys.Max() - ys.Min(), height = zs.Max() - zs.Min();
            if (filter.Length == 0 && (Math.Max(width, length) < 6 || Math.Min(width, length) < 3 || height < 2.5 || width * length > 20000))
            {
                continue;
            }

            // Upward faces (clockwise winding) by quarter-metre band of height: the candidate ground floors.
            Dictionary<double, double> up = [];
            foreach ((Vector3d a, Vector3d b, Vector3d c) in metric)
            {
                Vector3d n = Vector3d.Cross(c - a, b - a);
                if (n.Length > 1e-9 && n.Z / n.Length > 0.7)
                {
                    double key = Math.Round((a.Z + b.Z + c.Z) / 3 * 4) / 4;
                    up[key] = up.GetValueOrDefault(key) + (n.Length / 2);
                }
            }

            if (filter.Length > 0)
            {
                output.WriteLine($"{name} upward faces by height (m: m2): " + string.Join(' ', up.OrderBy(pair => pair.Key)
                    .Where(pair => pair.Value >= 1).Select(pair => FormattableString.Invariant($"{pair.Key:F2}:{pair.Value:F0}"))));
            }
            double[] floors = [.. up.Where(pair => pair.Value >= 6 && pair.Key <= zs.Min() + 4).Select(pair => pair.Key).Order().Take(4)];
            (double Inside, double Raised, double Floor, string Bands) best = (-1, 0, 0, string.Empty);
            foreach (double floor in floors.Append(zs.Min()).Distinct())
            {
                // Set the floor 0.1 m above flat ground at 0, and walk in from a 6 m margin of open ground.
                double lift = 0.1 - floor;
                var lifted = metric.Select(t => (t.Item1 + new Vector3d(0, 0, lift), t.Item2 + new Vector3d(0, 0, lift), t.Item3 + new Vector3d(0, 0, lift)));
                Reachability walk = new(lifted, (_, _) => 0.0, xs.Min() - 6, ys.Min() - 6, xs.Max() + 6, ys.Max() + 6);
                var reached = walk.ReachedOnStructure(xs.Min(), ys.Min(), xs.Max(), ys.Max());
                double inside = reached.Sum(pair => pair.Value);
                double raised = reached.Where(pair => pair.Key >= 2).Sum(pair => pair.Value);
                if (filter.Length == 1 && Environment.GetEnvironmentVariable("IGI_PLAN") is { } planFloor
                    && Math.Abs(double.Parse(planFloor, System.Globalization.CultureInfo.InvariantCulture) - floor) < 0.01)
                {
                    // Plan, 1 m per character, +X right, +Y up: a reached level shows its storey (0 ground, 1 from 2 m,
                    // 2 from 5 m ...) as a digit, an unreached floor 'o', unreached upstairs '+', reached bare ground '.'.
                    for (double py = Math.Ceiling(ys.Max()); py >= Math.Floor(ys.Min()); py--)
                    {
                        StringBuilder row = new(FormattableString.Invariant($"{py,6:F0} "));
                        for (double px = Math.Floor(xs.Min()); px <= Math.Ceiling(xs.Max()); px++)
                        {
                            var levels = walk.At(px, py).ToArray();
                            var high = levels.Where(l => l.Reached).OrderByDescending(l => l.Z).FirstOrDefault();
                            row.Append(levels.Any(l => l.Reached && l.Z > 0.05)
                                ? (char)('0' + Math.Clamp((int)Math.Floor((levels.Where(l => l.Reached).Max(l => l.Z) + 1) / 3), 0, 9))
                                : levels.Any(l => l.Z > 2) ? '+'
                                : levels.Any(l => l.Z > 0.05) ? 'o'
                                : levels.Any(l => l.Reached) ? '.' : ' ');
                        }

                        output.WriteLine(row.ToString());
                    }
                }

                if (inside > best.Inside)
                {
                    best = (inside, raised, floor, string.Join(' ', reached.Where(pair => pair.Value >= 2)
                        .Select(pair => FormattableString.Invariant($"{pair.Key:F0}:{pair.Value:F0}"))));
                }
            }

            results.Add((name, FormattableString.Invariant(
                $"{name}: {width:F1} x {length:F1} x {height:F1} m [x {xs.Min():F1}..{xs.Max():F1} y {ys.Min():F1}..{ys.Max():F1} z {zs.Min():F1}..{zs.Max():F1}], exterior {ExteriorScore(resolve(name)):P0}, ground floor at local {best.Floor:F2} m: reached {best.Inside:F0} m2, {best.Raised:F0} m2 upstairs; by height {best.Bands}"),
                best.Inside));
        }

        foreach (var result in results.OrderByDescending(r => r.Inside))
        {
            output.WriteLine(result.Line);
        }
    }
    /// <summary>
    /// How much of a model a person outside would see: rays cast inward from above and from all four sides, the share whose
    /// first drawn face points back at them. An interior-only piece, meant to sit inside a shell, scores low.
    /// </summary>
    private static double ExteriorScore(ProjectIGI.Formats.World.MefModel? model)
    {
        if (model is null || model.IsEmpty)
        {
            return 0;
        }

        List<(Vector3d A, Vector3d B, Vector3d C)> drawn = [];
        var vertices = model.Vertices.ToArray();
        foreach (var record in model.DrawRecords)
        {
            ReadOnlySpan<ushort> indices = record.Indices.Span;
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                var (a, b, c) = (vertices[record.ToVertexIndex(indices[i])].Position, vertices[record.ToVertexIndex(indices[i + 1])].Position,
                    vertices[record.ToVertexIndex(indices[i + 2])].Position);
                drawn.Add((new(a.X, a.Y, a.Z), new(b.X, b.Y, b.Z), new(c.X, c.Y, c.Z)));
            }
        }

        if (drawn.Count == 0)
        {
            return 0;
        }

        Vector3d min = new(drawn.Min(t => Math.Min(t.A.X, Math.Min(t.B.X, t.C.X))), drawn.Min(t => Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y))),
            drawn.Min(t => Math.Min(t.A.Z, Math.Min(t.B.Z, t.C.Z))));
        Vector3d max = new(drawn.Max(t => Math.Max(t.A.X, Math.Max(t.B.X, t.C.X))), drawn.Max(t => Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y))),
            drawn.Max(t => Math.Max(t.A.Z, Math.Max(t.B.Z, t.C.Z))));
        int front = 0, hits = 0;
        const int Steps = 12;
        for (int i = 1; i < Steps; i++)
        {
            for (int j = 1; j < Steps; j++)
            {
                double u = (double)i / Steps, v = (double)j / Steps;
                double x = min.X + (u * (max.X - min.X)), y = min.Y + (v * (max.Y - min.Y));
                double zSide = min.Z + (v * (max.Z - min.Z) * 0.8);
                (Vector3d Origin, Vector3d Direction)[] rays =
                [
                    (new(x, y, max.Z + 1), new(0, 0, -1)),
                    (new(min.X - 1, y, zSide), new(1, 0, 0)),
                    (new(max.X + 1, y, zSide), new(-1, 0, 0)),
                    (new(x, min.Y - 1, zSide), new(0, 1, 0)),
                    (new(x, max.Y + 1, zSide), new(0, -1, 0)),
                ];
                foreach (var (origin, direction) in rays)
                {
                    double nearest = double.MaxValue;
                    Vector3d normal = default;
                    foreach (var (a, b, c) in drawn)
                    {
                        Vector3d e1 = b - a, e2 = c - a, p = Vector3d.Cross(direction, e2);
                        double det = Vector3d.Dot(e1, p);
                        if (Math.Abs(det) < 1e-12)
                        {
                            continue;
                        }

                        Vector3d s = origin - a;
                        double bu = Vector3d.Dot(s, p) / det;
                        Vector3d q = Vector3d.Cross(s, e1);
                        double bv = Vector3d.Dot(direction, q) / det;
                        double t = Vector3d.Dot(e2, q) / det;
                        if (bu >= 0 && bv >= 0 && bu + bv <= 1 && t > 0 && t < nearest)
                        {
                            nearest = t;
                            normal = Vector3d.Cross(c - a, b - a);
                        }
                    }

                    if (nearest < double.MaxValue)
                    {
                        hits++;
                        front += Vector3d.Dot(normal, direction) < 0 ? 1 : 0;
                    }
                }
            }
        }

        return hits == 0 ? 0 : (double)front / hits;
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
        // A broad ridge running across the line of sight to the compound, with lower shoulders and gentle irregularity,
        // that fades out toward the valley instead of rising as a single steep cone.
        double along = (x - 0.16 - (y - 0.16)) / Math.Sqrt(2);
        double across = (x - 0.16 + (y - 0.16)) / Math.Sqrt(2);
        double ridge = 55 * Math.Exp(-((along * along / 0.16) + (across * across / 0.10)));
        double shoulders = (30 * Math.Exp(-((((x - 0.06) * (x - 0.06)) + ((y - 0.40) * (y - 0.40))) / 0.04)))
            + (25 * Math.Exp(-((((x - 0.40) * (x - 0.40)) + ((y - 0.05) * (y - 0.05))) / 0.035)));
        double rough = 1 + (0.06 * Math.Sin((x * 31) + 1.3) * Math.Sin((y * 27) + 0.4))
            + (0.03 * Math.Sin((x * 71) + (y * 13) + 2.1));
        double valley = Math.Sqrt(((x - 0.60) * (x - 0.60)) + ((y - (575.0 / 1200.0)) * (y - (575.0 / 1200.0))));
        double fade = SmoothStep((valley - 0.21) / (0.60 - 0.21));
        double hills = 12 * Math.Exp(-(((x - 0.78) * (x - 0.78) / 0.05) + ((y - 0.22) * (y - 0.22) / 0.08)));
        double height = 8 + ((ridge + shoulders) * rough * fade) + hills;

        // The lookout ledge sits at the slope's own height there, blended wide so it does not cut a cliff.
        height = BlendPlateau(height, x, y, 0.20, 0.20, 0.025, 0.10, LookoutMetres);
        height = BlendPlateau(height, x, y, 0.76, 0.78, 0.11, 0.19, 0);

        // The compound is a rounded rectangle rather than a circular tabletop; the gently blended shoulder keeps
        // its fence line and the outer watchtowers on walkable ground without flattening the whole valley.
        double dx = Math.Max(Math.Abs(x - 0.616) - 0.248, 0);
        double dy = Math.Max(Math.Abs(y - 0.48117) - 0.184, 0);
        double edge = Math.Sqrt((dx * dx) + (dy * dy));
        return GroundMetres + ((height - GroundMetres) * SmoothStep(edge / 0.08));
    }

    private static double SmoothStep(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - (2 * t));
    }

    private static double BlendPlateau(double height, double x, double y, double cx, double cy, double inner, double outer, double target)
    {
        double distance = Math.Sqrt(((x - cx) * (x - cx)) + ((y - cy) * (y - cy)));
        double t = Math.Clamp((distance - inner) / (outer - inner), 0, 1);
        double blend = t * t * (3 - (2 * t));
        return target + ((height - target) * blend);
    }

    private static byte[] CreateArchive(byte[] heightmap) =>
        IresWriter.Write(
        [
            new IresWriter.Entry(HeightName, IresEntryKind.Body, heightmap),
            new IresWriter.Entry(MaterialName, IresEntryKind.Body, CreateMaterialMap()),
        ]);

    private static byte[] CreateMaterialMap()
    {
        const int headerSize = 44;
        byte[] data = new byte[headerSize + (Size * Size)];
        Span<byte> bytes = data;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, Igi2TerrainMap.VersionWord);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[32..], 0);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[36..], Size);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[40..], Size);

        for (int y = 0; y < Size; y++)
        {
            float planY = ((y + 0.5f) * WorldWidthMetres / Size) + (335.4167f);
            for (int x = 0; x < Size; x++)
            {
                float planX = ((x + 0.5f) * WorldWidthMetres / Size) + 420f;
                if (RoadRectangles.Any(r => planX >= r.X0 && planX <= r.X1 && planY >= r.Y0 && planY <= r.Y1))
                {
                    data[headerSize + (y * Size) + x] = 1;
                }
            }
        }

        return data;
    }

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
                QvmExpression.Text(MissionName),
                QvmExpression.Text(MissionDescription),
                QvmExpression.Text(string.Empty),
                QvmExpression.Text(MissionDirectory),
                QvmExpression.Text("missions/location1/common"),
                QvmExpression.Text("missions/location1/level1"),
                QvmExpression.Text("location1"),
                QvmExpression.Text("level1"),
                QvmExpression.Text(BriefingSprite),
                // Mission-specific buildings, interiors and door models.
                QvmExpression.Text(ExtraPacks[0].Directory),
                QvmExpression.Text(ExtraPacks[0].Name),
                QvmExpression.Text(ExtraPacks[1].Directory),
                QvmExpression.Text(ExtraPacks[1].Name),
                QvmExpression.Text(ExtraPacks[2].Directory),
                QvmExpression.Text(ExtraPacks[2].Name),
                QvmExpression.Text(ExtraPacks[3].Directory),
                QvmExpression.Text(ExtraPacks[3].Name)));

    private static string FindRepositoryFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Required mission asset was not found: {relativePath}");
    }

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

        QvmExpression roadMaterial = NewTask(
            13,
            TerrainMaterialTask.TypeName,
            "Compound roads",
            [
                QvmExpression.Int(1),
                QvmExpression.Int(0),
                QvmExpression.Text("LOCAL:missions/location1/level1/textures/dirtgrey.jpg"),
                QvmExpression.Text(string.Empty),
                QvmExpression.Real(8f),
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
                roadMaterial,
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
        QvmExpression[] guards = [.. GuardPosts.Select((post, index) => Guard(100 + index, post.X, post.Y, post.Heading, post.ZOffset))];
        QvmExpression dynamic = NewTask(9, EngineTaskTypes.Dynamic, "Dynamic", [player,
            Squad(301, guards[..8]), Squad(302, guards[8..]),
            .. Cameras.Select(Camera), .. EntryTasks(), .. Props.Select((prop, index) => RigidObject(2000 + index, prop)),
            .. BuildingFurnishings()]);
        QvmExpression flow = NewTask(11, Igi2LevelFlowTask.TypeName, "LevelFlow_11",
            [
            .. Position(0f, 0f, 0f), .. Position(0f, 0f, 0f),
            QvmExpression.Real(12f), QvmExpression.Text(MissionCompleteExpression), QvmExpression.Text(string.Empty),
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
        QvmExpression[] buildings = [.. Buildings.Concat(Perimeter).Select(Building)];
        QvmExpression[] doors = [.. Doors.Select(Door)];
        QvmExpression[] mission = MissionTasks(playerX, playerY, playerZ);
        QvmExpression[] patrols = [.. Enumerable.Range(0, PatrolCount).Select(PatrolPath)];

        QvmExpression sunlight = NewTask(
            5, Igi2DirlightKeyframeTask.TypeName, "Midday sun",
            [
                QvmExpression.Real(0.2f), QvmExpression.Real(-3.351032f),
                .. Position(1f, 0.72f, 0.48f),
                .. Position(0.12f, 0.15f, 0.22f),
                QvmExpression.Real(12f),
            ]);
        QvmExpression sun = NewTask(
            4, Igi2DirlightTask.TypeName, "Sun",
            [
                QvmExpression.Int(1), QvmExpression.Int(1), QvmExpression.Int(1),
                QvmExpression.Real(0.5f), QvmExpression.Real(0.25f),
                QvmExpression.Text("sun.tex"), QvmExpression.Real(32f), QvmExpression.Int(0),
                sunlight,
            ]);
        QvmExpression day = NewTask(
            7, Igi2GlobalLightKeyframeTask.TypeName, "Midday",
            [
                QvmExpression.Int(0),
                .. DaylightCategory(), .. DaylightCategory(), .. DaylightCategory(),
                .. DaylightCategory(), .. DaylightCategory(),
                .. Position(0.4f, 0.4f, 0.4f),
                .. Position(0.34f, 0.29f, 0.28f),
                .. Position(0.45f, 0.34f, 0.3f),
                QvmExpression.Real(12f),
            ]);
        QvmExpression globalLight = NewTask(
            6, Igi2GlobalLightTask.TypeName, "Daylight",
            [
                QvmExpression.Real(1000f), QvmExpression.Real(0.8f),
                QvmExpression.Real(20f), QvmExpression.Real(0.18f), QvmExpression.Int(0),
                .. Position(0f, 0f, 0f), .. Position(1f, 1f, 1f),
                QvmExpression.Real(1f), day,
            ]);

        QvmExpression sky = NewTask(
            8, FlatSkyTask.TypeName, "Dawn sky",
            [
                QvmExpression.Real(0.4f), QvmExpression.Real(2.7f), QvmExpression.Real(30f),
                .. Position(0.46f, 0.36f, 0.33f),
                QvmExpression.Int(0), QvmExpression.Real(20.25f),
                .. Position(0.14f, 0.32f, 0.58f),
                .. Position(0.58f, 0.61f, 0.68f),
                .. Position(0.60f, 0.46f, 0.34f),
                .. Position(0.92f, 0.49f, 0.28f),
                .. Position(0.42f, 0.38f, 0.43f),
                NewTask(12, FlatSkyLayerTask.TypeName, "Dawn cloud base",
                [
                    QvmExpression.Text("long_descent_clouds.tga"),
                    QvmExpression.Real(0.00001f), QvmExpression.Real(0.00001f),
                    QvmExpression.Real(0.00004f), QvmExpression.Real(0.16f),
                    .. Position(0.9f, 0.9f, 0.95f),
                ]),
                NewTask(999, FlatSkyLayerTask.TypeName, "High dawn clouds",
                [
                    QvmExpression.Text("long_descent_clouds.tga"),
                    QvmExpression.Real(0.000001f), QvmExpression.Real(-0.00001f),
                    QvmExpression.Real(0.00001f), QvmExpression.Real(0.1f),
                    .. Position(0.78f, 0.85f, 1f),
                ]),
            ]);

        QvmExpression[] forests = [.. Forests.Select(Forest)];
        return QvmExpression.BuildProgram([terrain, dynamic, flow, graph, sun, globalLight, sky, .. buildings, .. doors, .. mission, .. patrols, .. forests]);
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
            while ((!forest.GroundCover && (IsInCompoundTreeClearance(x, y) || IsNearInsertionTreeClearance(x, y)))
                || (forest.GroundCover && (IsInGroundCoverClearance(x, y) || IsNearRoad(x, y, 1f)))
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

    private static bool IsInCompoundTreeClearance(float worldX, float worldY)
    {
        float planX = worldX + 420f;
        float planY = worldY + 335.4167f;
        return planX is >= 596f and <= 860f && planY is >= 476f and <= 676f;
    }

    private static bool IsInGroundCoverClearance(float worldX, float worldY)
    {
        float planX = worldX + 420f;
        float planY = worldY + 335.4167f;
        return planX is >= 616f and <= 840f && planY is >= 496f and <= 656f;
    }

    private static bool IsNearRoad(float worldX, float worldY, float clearance)
    {
        float planX = worldX + 420f;
        float planY = worldY + 335.4167f;
        return RoadRectangles.Any(road =>
            planX >= road.X0 - clearance && planX <= road.X1 + clearance
            && planY >= road.Y0 - clearance && planY <= road.Y1 + clearance);
    }

    private static bool IsNearInsertionTreeClearance(float worldX, float worldY)
    {
        float deltaX = worldX - StartMetres;
        float deltaY = worldY - StartMetres;
        return (deltaX * deltaX) + (deltaY * deltaY) < 24f * 24f;
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
            QvmExpression.Real(2f), QvmExpression.Int(1), QvmExpression.Int(AlarmControlId),
            QvmExpression.Int(AlarmControlId), QvmExpression.Int(-1), QvmExpression.Real(30f),
            QvmExpression.Int(5), QvmExpression.Int(60), .. guards,
        ]);

    private static QvmExpression Guard(int id, float x, float y, float heading, float zOffset = 0f) =>
        NewTask(id, Igi2HumanSoldierTask.TypeName, $"Compound guard {id - 99}",
        [
            .. Position((x - 720f + CompoundX) * UnitsPerMetre, (y - 575f + CompoundY) * UnitsPerMetre,
                ((float)HeightMetres((x - 720f + CompoundX) / WorldWidthMetres, (y - 575f + CompoundY) / WorldWidthMetres) + zOffset) * UnitsPerMetre),
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
        (float X, float Y)[] nodes = [.. GuardPosts.Select(post => (post.X, post.Y))];
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

        (int A, int B)[] links =
        [
            (6, 7), (6, 8), (7, 9), (8, 9),
            (0, 1), (0, 13), (1, 14), (1, 5), (2, 3), (2, 13), (3, 13), (3, 12),
            (3, 11), (10, 11), (3, 4), (4, 5), (5, 14), (12, 4),
        ];
        foreach ((int a, int b) in links)
        {
            if (!Blocked(nodes[a], nodes[b]))
            {
                IntField("nLinkID1", a);
                IntField("nLinkID2", b);
                IntField("eLinkType", 0);
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

        HashSet<int> west = [6, 7, 8, 9];
        HashSet<int> ConnectedTo(int start)
        {
            HashSet<int> reached = [start];
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
            return reached;
        }

        HashSet<int> main = [0, 1, 2, 3, 4, 5, 10, 11, 12, 13, 14];
        if (!ConnectedTo(0).SetEquals(main) || !ConnectedTo(6).SetEquals(west)
            || graph.Links.Any(link => west.Contains(link.NodeA) != west.Contains(link.NodeB)))
        {
            throw new InvalidOperationException(
                $"The guard navigation graph is disconnected: main [{string.Join(',', ConnectedTo(0))}], west [{string.Join(',', ConnectedTo(6))}].");
        }
        for (int i = 0; i < PatrolCount; i++)
        {
            int other = i ^ 1;
            if (!graph.Links.Any(link => (link.NodeA == i && link.NodeB == other)
                || (link.NodeA == other && link.NodeB == i)))
            {
                throw new InvalidOperationException($"Perimeter patrol {i + 1} has no direct graph link.");
            }
        }
    }

    private static byte[] CreateDefaultScript(string handler) =>
        QvmExpression.BuildProgram(QvmExpression.Call(handler));

    private static byte[] CreateGuardScript(int index) =>
        index < PatrolCount
            ? Igi2SoldierScript.Build(new Igi2SoldierOrders { PatrolPathId = PatrolPathBaseId + index })
            : CreateDefaultScript("AIFunction_DefaultHandler");

    private static QvmExpression PatrolPath(int index)
    {
        int other = index ^ 1;
        return NewTask(PatrolPathBaseId + index, Igi2PatrolPathTask.TypeName, $"Patrol Route {index / 2 + 1}",
        [
            QvmExpression.Int(GuardGraphId),
            PatrolStep(600 + (index * 2), other),
            PatrolStep(601 + (index * 2), index),
        ]);
    }

    private static QvmExpression PatrolStep(int id, int node) =>
        NewTask(id, Igi2PatrolPathCommandTask.TypeName, "Walk to perimeter post",
        [
            QvmExpression.Int((int)Igi2PatrolCommand.WalkTo), QvmExpression.Int(node),
            QvmExpression.Int(node), QvmExpression.Text(string.Empty), QvmExpression.Text(string.Empty),
        ]);

    private static QvmExpression Building(BuildingSpec building) =>
        NewTask(building.Id, Igi2BuildingTask.TypeName, $"Building_{building.Id}",
        [
            .. Position(CompoundWorldX(building.X), CompoundWorldY(building.Y), building.Z * UnitsPerMetre),
            .. Position(0f, 0f, building.Heading),
            QvmExpression.Text(building.Model),
            .. Position(0.35f, 0.35f, 0.35f),
        ]);

    private static QvmExpression RigidObject(int id, PropSpec prop) =>
        NewTask(id, Igi2EditRigidObjTask.TypeName, $"CompoundProp_{id}",
        [
            .. Position(CompoundWorldX(prop.X), CompoundWorldY(prop.Y), (GroundMetres + prop.Z) * UnitsPerMetre),
            .. Position(0f, 0f, prop.Heading),
            QvmExpression.Text(prop.Model),
            QvmExpression.Int(5),
            QvmExpression.Int(0),
            QvmExpression.Int(0),
        ]);

    private static QvmExpression[] BuildingFurnishings()
    {
        List<QvmExpression> tasks = [];
        int id = 3000;
        Add(ServiceHut, BoothFurnishings);
        Add(GateHut, BoothFurnishings);
        Add(Armory, ArmoryFurnishings);
        Add(Security, ArmoryFurnishings);
        Add(Garage,
        [
            new("331_01_1", -7.5f, -2f, 0f, MathF.PI / 2f, 0f, 0f),
            new("331_01_1", -7.5f, 2f, 0f, MathF.PI / 2f, 0f, 0f),
            new("342_09_1", 5f, 4f, 0f, 0f, 0f, 0f),
            new("342_10_1", 7f, 4f, 0f, 0f, 0f, 0f),
        ]);
        Add(Barracks, BarracksFurnishings);
        Add(Warehouse, WarehouseFurnishings);
        Add(IntelBuilding, ServerFurnishings);
        Add(IntelBuilding, [new("231_15_1", -6f, -12f, 5.22f, 0f, 0f, 0f)]);
        Add(Administration, CommandFurnishings);
        return [.. tasks];

        void Add(BuildingSpec building, IEnumerable<Furnishing> furnishings)
        {
            foreach (Furnishing furnishing in furnishings)
            {
                (float x, float y, float z) = InBuilding(building, furnishing.X, furnishing.Y, furnishing.Z);
                tasks.Add(NewTask(id, Igi2EditRigidObjTask.TypeName, $"BuildingFurnishing_{id}",
                [
                    .. Position(x, y, z),
                    .. Position(furnishing.OrientX, furnishing.OrientY, furnishing.Heading + building.Heading),
                    QvmExpression.Text(furnishing.Model),
                    QvmExpression.Int(5),
                    QvmExpression.Int(0),
                    QvmExpression.Int(0),
                ]));
                id++;
            }
        }
    }

    private static float CompoundWorldX(float x) => (x - 720f + CompoundX) * UnitsPerMetre;

    private static float CompoundWorldY(float y) => (y - 575f + CompoundY) * UnitsPerMetre;

    // A point given in a building's own frame, in metres, in world units.
    private static (float X, float Y, float Z) InBuilding(BuildingSpec building, float x, float y, float z)
    {
        float cos = MathF.Cos(building.Heading);
        float sin = MathF.Sin(building.Heading);
        return (
            CompoundWorldX(building.X + (x * cos) - (y * sin)),
            CompoundWorldY(building.Y + (x * sin) + (y * cos)),
            (building.Z + z) * UnitsPerMetre);
    }

    private static (float X, float Y, float Z) DoorPosition(DoorSpec door) =>
        InBuilding(door.Building, door.Fit.X, door.Fit.Y, door.Fit.Z);

    private static float DoorHeading(DoorSpec door) =>
        (door.Fit.Heading + door.Building.Heading) % (2f * MathF.PI);

    private static QvmExpression Door(DoorSpec door)
    {
        (float x, float y, float z) = DoorPosition(door);
        return NewTask(door.Id, Igi2DoorTask.TypeName, $"Door_{door.Id}",
        [
            .. Position(x, y, z),
            QvmExpression.Real(0f), QvmExpression.Real(0f), QvmExpression.Real(0f),
            .. Position(0f, 0f, DoorHeading(door)),
            QvmExpression.Text(door.Fit.Model),
            QvmExpression.Real(door.Fit.MaxAngle), QvmExpression.Real(0.5f),
            QvmExpression.Int(door.Pickable ? 1 : 0), QvmExpression.Int(0), QvmExpression.Real(door.Pickable ? 4f : 0f),
            QvmExpression.Text(door.Locked), QvmExpression.Text(string.Empty), QvmExpression.Text(string.Empty),
            QvmExpression.Int(1), QvmExpression.Int(1), QvmExpression.Real(door.Fit.Delta),
            QvmExpression.Text("door_ext_3"), QvmExpression.Text(string.Empty), QvmExpression.Text(string.Empty),
            QvmExpression.Text("door_ext_1"), QvmExpression.Text("door_ext_2"), QvmExpression.Text("tba_picklock"),
        ]);
    }

    private static QvmExpression Switch(int id, BuildingSpec building, float x, float y, float z, string onModel, string offModel, string sound, string active)
    {
        (float wx, float wy, float wz) = InBuilding(building, x, y, z);
        return NewTask(id, Igi2SwitchTask.TypeName, $"Switch_{id}",
        [
            .. Position(wx, wy, wz), .. Position(0f, 0f, building.Heading),
            QvmExpression.Text(active), QvmExpression.Int(0), QvmExpression.Text(sound),
            QvmExpression.Text(onModel), QvmExpression.Text(onModel),
            QvmExpression.Text(offModel), QvmExpression.Text(offModel), QvmExpression.Text(offModel),
            QvmExpression.Int(0), QvmExpression.Text("STATUSSCREEN:action_pressbutton.spr"),
        ]);
    }

    private static QvmExpression[] EntryTasks() =>
    [
        GateLeaf(GateLeftId, GateX - 4, GateY, 0, 90, $"GenericTBA_{PadlockId}.isExploded"),
        GateLeaf(GateRightId, GateX + 4, GateY, MathF.PI, -90, $"GenericTBA_{PadlockId}.isExploded"),
        GateLeaf(ServiceLeftId, ServiceX - 4, ServiceY, 0, -90, $"Switch_{CoreSwitchId}.isOn"),
        GateLeaf(ServiceRightId, ServiceX + 4, ServiceY, MathF.PI, 90, $"Switch_{CoreSwitchId}.isOn"),
        GateLeaf(InnerLeftId, 672, 564, MathF.PI / 2f, -90, $"GenericPickup_{KeycardPickupId}.isPickedUp"),
        GateLeaf(InnerRightId, 680, 564, 3f * MathF.PI / 2f, 90, $"GenericPickup_{KeycardPickupId}.isPickedUp"),
        NewTask(PadlockId, Igi2GenericTbaTask.TypeName, $"GenericTBA_{PadlockId}",
        [
            .. Position(CompoundWorldX(GateX), CompoundWorldY(GateY - 0.18f), GroundMetres * UnitsPerMetre),
            .. Position(0f, 0f, MathF.PI),
            QvmExpression.Text("306_02_1"), QvmExpression.Text("306_03_1"),
            QvmExpression.Real(0.4f), QvmExpression.Real(0f), QvmExpression.Real(0f),
            QvmExpression.Real(0f), QvmExpression.Real(0f),
            QvmExpression.Int(0), QvmExpression.Int(0),
            QvmExpression.Text($"GenericTBA_{PadlockId}.isFinished"), QvmExpression.Text(string.Empty),
            QvmExpression.Real(0f), QvmExpression.Real(0f), QvmExpression.Real(0f),
            QvmExpression.Int(1), QvmExpression.Text("1"), QvmExpression.Text("pick_lock"),
            QvmExpression.Real(4f), QvmExpression.Text("STATUSSCREEN:action_picklock.spr"),
            QvmExpression.Text("tba_picklock"),
        ]),
    ];

    private static QvmExpression GateLeaf(int id, float x, float y, float heading, float angle, string open) =>
        NewTask(id, Igi2DoorTask.TypeName, $"Door_{id}",
        [
            .. Position(CompoundWorldX(x), CompoundWorldY(y), GroundMetres * UnitsPerMetre),
            QvmExpression.Real(0f), QvmExpression.Real(0f), QvmExpression.Real(0f),
            .. Position(0f, 0f, heading), QvmExpression.Text("303_08_1"),
            QvmExpression.Real(angle), QvmExpression.Real(2f),
            QvmExpression.Int(0), QvmExpression.Int(0), QvmExpression.Real(0f),
            QvmExpression.Text("!this.isOpen"), QvmExpression.Text(open), QvmExpression.Text(string.Empty),
            QvmExpression.Int(1), QvmExpression.Int(0), QvmExpression.Real(1f),
            QvmExpression.Text("door_fgate_end"), QvmExpression.Text("door_fgate_end"),
            QvmExpression.Text("door_fgate_loop"), QvmExpression.Text(string.Empty),
            QvmExpression.Text(string.Empty), QvmExpression.Text("tba_picklock"),
        ]);

    private static QvmExpression Message(int id, float x, float y, float z, string send, string text) =>
        NewTask(id, Igi2StatusMessageTask.TypeName, $"StatusMessage_{id}",
        [
            .. Position(x, y, z), .. Position(0f, 0f, 0f),
            QvmExpression.Text(send), QvmExpression.Text(text), QvmExpression.Text(string.Empty),
            QvmExpression.Text("statusmessage"), QvmExpression.Int(1), QvmExpression.Int(0),
            QvmExpression.Real(12f), QvmExpression.Text(string.Empty),
        ]);

    private static QvmExpression[] MissionTasks(float playerX, float playerY, float playerZ)
    {
        float extractionX = CompoundWorldX(ExtractionX);
        float extractionY = CompoundWorldY(ExtractionY);
        float extractionZ = (float)HeightMetres(extractionX / UnitsPerMetre / WorldWidthMetres, extractionY / UnitsPerMetre / WorldWidthMetres) * UnitsPerMetre;
        QvmExpression[] objectives =
        [
            QvmExpression.Text("1"),
            QvmExpression.Text("Breach the perimeter fence"), QvmExpression.Int(BreachAreaId),
            QvmExpression.Text($"StatusMessage_{BreachMessageId}.isSendt"), QvmExpression.Text(string.Empty),
            QvmExpression.Text("Evade the main watchtower cameras"), QvmExpression.Int(Watchtower1.Id),
            QvmExpression.Text("Camera_150.isExploded || Camera_151.isExploded"), QvmExpression.Text(string.Empty),
            QvmExpression.Text("Sabotage Guard Post A generator"), QvmExpression.Int(GeneratorSwitchId),
            QvmExpression.Text($"Switch_{GeneratorSwitchId}.isOn"), QvmExpression.Text(string.Empty),
            QvmExpression.Text("Take the security keycard"), QvmExpression.Int(KeycardPickupId),
            QvmExpression.Text($"GenericPickup_{KeycardPickupId}.isPickedUp"), QvmExpression.Text(string.Empty),
            QvmExpression.Text("Reach Server Building 03"), QvmExpression.Int(ServerAreaId),
            QvmExpression.Text($"StatusMessage_{ServerAreaMessageId}.isSendt"), QvmExpression.Text(string.Empty),
            QvmExpression.Text("Hack the vault terminal"), QvmExpression.Int(SecurityConsoleId),
            QvmExpression.Text($"Switch_{SecurityConsoleId}.isOn"), QvmExpression.Text(string.Empty),
            QvmExpression.Text("Recover the Quantum Encryption Core"), QvmExpression.Int(CoreSwitchId),
            QvmExpression.Text($"Switch_{CoreSwitchId}.isOn"), QvmExpression.Text(string.Empty),
            QvmExpression.Text("Exfiltrate to the canyon extraction"), QvmExpression.Int(ExtractionAreaId),
            QvmExpression.Text($"StatusMessage_{ExtractionMessageId}.isSendt"), QvmExpression.Text(string.Empty),
        ];

        QvmExpression Area(int id, float x, float y, float z, float sx, float sy, float sz) =>
            NewTask(id, Igi2AreaActivateTask.TypeName, $"AreaActivate_{id}",
            [
                .. Position(CompoundWorldX(x), CompoundWorldY(y), z * UnitsPerMetre),
                .. Position(0f, 0f, 0f),
                .. Position(sx * UnitsPerMetre, sy * UnitsPerMetre, sz * UnitsPerMetre),
                QvmExpression.Text("CRITERIA_HUMAN0"),
            ]);

        QvmExpression Pickup(int id, BuildingSpec building, float x, float y, float z, string model)
        {
            (float wx, float wy, float wz) = InBuilding(building, x, y, z);
            return NewTask(id, Igi2GenericPickupTask.TypeName, $"GenericPickup_{id}",
            [
                .. Position(wx, wy, wz),
                .. Position(0f, 0f, building.Heading),
                QvmExpression.Text(model),
            ]);
        }

        (float alarmX, float alarmY, float alarmZ) = InBuilding(ServiceHut, 1.1f, -0.5f, 1.1f);
        string detection = string.Join(" || ", Enumerable.Range(0, GuardCount)
            .Select(index => $"HumanAI_{200 + index}.isDetection"));
        return
        [
            Switch(GeneratorSwitchId, GeneratorPen, 3.2f, -1f, 0f, "334_01_1", "334_01_1", "button_1", "!this.isOn"),
            Switch(SecurityConsoleId, IntelBuilding, -8.97f, -8.55f, 1.11f, "200_01_1", "200_01_1", "button_1", "!this.isOn"),
            Switch(CoreSwitchId, IntelBuilding, -6f, -12f, 6.53f, "360_14_1", "360_15_1", "switch_1",
                $"!this.isOn && Switch_{SecurityConsoleId}.isOn && AlarmControl_{AlarmControlId}.nTriggerLastTick < 0"),
            NewTask(AlarmControlId, Igi2AlarmControlTask.TypeName, $"AlarmControl_{AlarmControlId}",
            [
                .. Position(alarmX, alarmY, alarmZ),
                .. Position(0f, 0f, 0f),
                QvmExpression.Text("200_01_1"), QvmExpression.Text(string.Empty),
                QvmExpression.Real(1f), QvmExpression.Real(0.5f), QvmExpression.Real(0.5f),
                QvmExpression.Real(1f), QvmExpression.Real(0f), QvmExpression.Int(5),
                QvmExpression.Int(4), QvmExpression.Text(string.Empty), QvmExpression.Text("explo_02_m"),
                QvmExpression.Text("1"), QvmExpression.Real(4f), QvmExpression.Text("tba_hackcomp"),
                QvmExpression.Text(detection), QvmExpression.Text("this.isTrigger"),
            ]),
            Pickup(KeycardPickupId, ServiceHut, 1.25f, -0.75f, 1.03f, "202_01_1"),
            Area(ExtractionAreaId, ExtractionX, ExtractionY, extractionZ / UnitsPerMetre, 40, 40, 30),
            Area(BreachAreaId, 646, 576, GroundMetres, 60, 160, 20),
            Area(GuardPostAreaId, 645, 528, GroundMetres, 24, 20, 10),
            Area(ServerAreaId, 818, 600, GroundMetres, 26, 36, 14),
            Message(BriefingMessageId, playerX, playerY, playerZ, "LevelFlow_11.nTick > 5600",
                "Project I.G.I. - Operation Redemption. Disable Guard Post A, take the keycard, and recover the Quantum Encryption Core from Server Building 03."),
            Message(BreachMessageId, playerX, playerY, playerZ, $"AreaActivate_{BreachAreaId}.nActive",
                "Perimeter breached. Avoid the watchtower and reach Guard Post A."),
            Message(GeneratorMessageId, playerX, playerY, playerZ, $"Switch_{GeneratorSwitchId}.isOn",
                "Generator disabled. CCTV feeds are down; retrieve the keycard and enter Server Building 03."),
            Message(KeycardMessageId, playerX, playerY, playerZ, $"GenericPickup_{KeycardPickupId}.isPickedUp",
                "Keycard secured. Infiltrate Server Building 03."),
            Message(ServerAreaMessageId, playerX, playerY, playerZ, $"AreaActivate_{ServerAreaId}.nActive",
                "Server Building 03 reached. Hack the vault terminal."),
            Message(TerminalMessageId, playerX, playerY, playerZ, $"Switch_{SecurityConsoleId}.isOn",
                "Vault terminal unlocked. Recover the Quantum Encryption Core."),
            Message(CoreMessageId, playerX, playerY, playerZ, $"Switch_{CoreSwitchId}.isOn",
                "Quantum Encryption Core secured. Proceed to the canyon extraction."),
            Message(GuardPostMessageId, playerX, playerY, playerZ,
                $"AreaActivate_{GuardPostAreaId}.nActive || Switch_{GeneratorSwitchId}.isOn || (Camera_150.isExploded && Camera_151.isExploded)",
                "Guard Post A is clear. Continue to the target."),
            Message(AlarmMessageId, playerX, playerY, playerZ, $"AlarmControl_{AlarmControlId}.nTriggerLastTick >= 0",
                "Alarm raised. Server Building 03 has sealed the Quantum Encryption Core."),
            Message(ExtractionMessageId, extractionX, extractionY, extractionZ,
                $"AreaActivate_{ExtractionAreaId}.nActive && Switch_{CoreSwitchId}.isOn",
                "Operation Redemption complete. The Quantum Encryption Core is secure."),
            NewTask(ObjectivesId, Igi2DefineComputerObjectiveTask.TypeName, $"DefineComputerObjective_{ObjectivesId}", objectives),
        ];
    }

    private static QvmExpression Camera(CameraSpec camera) =>
        NewTask(camera.Id, Igi2SecurityCameraTask.TypeName, $"SecurityCamera_{camera.Id}",
        [
            .. Position(CompoundWorldX(camera.X), CompoundWorldY(camera.Y), camera.Z * UnitsPerMetre),
            QvmExpression.Real(camera.Heading), QvmExpression.Text("335_01_1"), QvmExpression.Int(1),
            QvmExpression.Real(-0.2f), QvmExpression.Real(0f),
            QvmExpression.Text("335_02_1"), QvmExpression.Text("335_03_1"),
            QvmExpression.Int(45), QvmExpression.Int(45), QvmExpression.Int(30),
            QvmExpression.Real(1f), QvmExpression.Int(60), QvmExpression.Int(80),
            QvmExpression.Real(30f), QvmExpression.Text(camera.OnScript), QvmExpression.Real(0.5f),
        ]);

    // Whether the straight walk between two graph nodes passes through a building's footprint, 1 m inflated.
    private static bool Blocked((float X, float Y) from, (float X, float Y) to)
    {
        foreach (BuildingSpec building in Buildings)
        {
            bool fromInside = Math.Abs(from.X - building.X) <= building.HalfX + 1f
                && Math.Abs(from.Y - building.Y) <= building.HalfY + 1f;
            bool toInside = Math.Abs(to.X - building.X) <= building.HalfX + 1f
                && Math.Abs(to.Y - building.Y) <= building.HalfY + 1f;
            if (fromInside || toInside)
            {
                continue;
            }

            for (int step = 0; step <= 100; step++)
            {
                float t = step / 100f;
                float x = from.X + ((to.X - from.X) * t) - building.X;
                float y = from.Y + ((to.Y - from.Y) * t) - building.Y;
                if (Math.Abs(x) <= building.HalfX + 1f && Math.Abs(y) <= building.HalfY + 1f)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static QvmExpression[] DaylightCategory() =>
    [
        .. Position(0.45f, 0.45f, 0.45f),
        .. Position(0.4f, 0.36f, 0.34f),
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
        IresEntry materialEntry = archive.FindBody(MaterialName)
            ?? throw new InvalidOperationException("The generated archive does not contain its road material map.");
        if (archive.Entries.Count != 2 || !entry.Data.Span.SequenceEqual(heightmap))
        {
            throw new InvalidOperationException("The terrain archive did not round-trip both terrain maps.");
        }

        Igi2TerrainMap map = Igi2TerrainMap.Read(entry.Data, Igi2TerrainMapKind.Height);
        Igi2TerrainMap materialMap = Igi2TerrainMap.Read(materialEntry.Data, Igi2TerrainMapKind.Material);
        if (map.Width != Size || map.Height != Size || !map.Trailing.IsEmpty
            || materialMap.Width != Size || materialMap.Height != Size || !materialMap.Trailing.IsEmpty)
        {
            throw new InvalidOperationException("A terrain tile has an unexpected extent or trailing data.");
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
            || mission.Name != MissionName
            || mission.Description != MissionDescription
            || mission.BriefingSprite != BriefingSprite
            || mission.CommonPackDirectory != "missions/location1/common"
            || mission.LevelPackDirectory != "missions/location1/level1"
            || mission.CommonPackName != "location1"
            || mission.LevelPackName != "level1"
            || Encoding.UTF8.GetByteCount(mission.Description) > 1024
            || mission.PacksToOpen.Count != 4 + ExtraPacks.Length
            || !ExtraPacks.Select((pack, index) => (pack, index)).All(p =>
                mission.PacksToOpen[4 + p.index].Directory == $"LOCAL:{p.pack.Directory}/"
                && mission.PacksToOpen[4 + p.index].Name == p.pack.Name))
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
        types.Register<Igi2PatrolPathTask>(Igi2PatrolPathTask.TypeName);
        types.Register<Igi2PatrolPathCommandTask>(Igi2PatrolPathCommandTask.TypeName);
        types.Register<Igi2LevelFlowTask>(Igi2LevelFlowTask.TypeName);
        types.Register<ForestTask>(ForestTask.TypeName);
        types.Register<Igi2SecurityCameraTask>(Igi2SecurityCameraTask.TypeName);
        types.Register<Igi2BuildingTask>(Igi2BuildingTask.TypeName);
        types.Register<Igi2EditRigidObjTask>(Igi2EditRigidObjTask.TypeName);
        types.Register<Igi2DoorTask>(Igi2DoorTask.TypeName);
        types.Register<Igi2GenericTbaTask>(Igi2GenericTbaTask.TypeName);
        types.Register<Igi2GenericPickupTask>(Igi2GenericPickupTask.TypeName);
        types.Register<Igi2AlarmControlTask>(Igi2AlarmControlTask.TypeName);
        types.Register<Igi2SwitchTask>(Igi2SwitchTask.TypeName);
        types.Register<Igi2StatusMessageTask>(Igi2StatusMessageTask.TypeName);
        types.Register<Igi2AreaActivateTask>(Igi2AreaActivateTask.TypeName);
        types.Register<Igi2DefineComputerObjectiveTask>(Igi2DefineComputerObjectiveTask.TypeName);
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
        FlatSkyLayerTask[] clouds = [.. sky.Children.OfType<FlatSkyLayerTask>()];
        Igi2HumanSoldierTask[] soldiers = [.. tree.Root.Descendants().OfType<Igi2HumanSoldierTask>()];
        Igi2SecurityCameraTask[] cameras = [.. tree.Root.Descendants().OfType<Igi2SecurityCameraTask>()];
        Igi2BuildingTask[] buildings = [.. tree.Root.Descendants().OfType<Igi2BuildingTask>()];
        Igi2AiSquadTask[] squads = [.. tree.Root.Descendants().OfType<Igi2AiSquadTask>()];
        Igi2LevelFlowTask flow = tree.Root.Descendants().OfType<Igi2LevelFlowTask>().Single();
        Igi2AiGraphTask graph = tree.Root.Descendants().OfType<Igi2AiGraphTask>().Single();
        Igi2PatrolPathTask[] patrols = [.. tree.Root.Descendants().OfType<Igi2PatrolPathTask>()];
        ForestTask[] forests = [.. tree.Root.Descendants().OfType<ForestTask>()];
        foreach (Igi2HumanSoldierTask soldier in soldiers)
        {
            double? height = surface.WaterGroundAt(soldier.Position.X, soldier.Position.Y);
            if (height is null || Math.Abs(height.Value - soldier.Position.Z) > 0.25 * UnitsPerMetre)
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
                if (!forest.GroundCover
                    && (Math.Pow(x - (StartMetres * UnitsPerMetre), 2)
                        + Math.Pow(y - (StartMetres * UnitsPerMetre), 2) < Math.Pow(24 * UnitsPerMetre, 2)))
                {
                    throw new InvalidOperationException("A tall tree blocks the cleared HALO insertion zone.");
                }
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
                || s.Soldiers.Count() is < 1 or > 8 || s.AlarmTriggerId != AlarmControlId || s.AlarmControlId != AlarmControlId)
            || graph.Id != GuardGraphId
            || patrols.Length != PatrolCount
            || patrols.Any(path => path.GraphId != GuardGraphId || path.Commands.Count != 2
                || path.Commands.Any(command => command.Command != Igi2PatrolCommand.WalkTo
                    || command.Parameter < 0 || command.Parameter >= PatrolCount))
            || forests.Length != Forests.Length
            || Forests.Any(expected => !forests.Any(actual => actual.Id == expected.Id
                && actual.Model == expected.Model && actual.TreeCount == expected.Count))
            || cameras.Length != Cameras.Length
            || Cameras.Any(expected => cameras.SingleOrDefault(actual => actual.Id == expected.Id) is not { } camera
                || camera.Parent?.Type.Name != EngineTaskTypes.Dynamic || camera.OnScript != expected.OnScript)
            || buildings.Length != Buildings.Length + Perimeter.Length
            || Buildings.Concat(Perimeter).Any(expected => !buildings.Any(actual => actual.Id == expected.Id && actual.Model == expected.Model
                && actual.Name == $"Building_{expected.Id}")))
        {
            throw new InvalidOperationException("The compound task layout is invalid.");
        }

        Igi2DoorTask[] doors = [.. tree.Root.Descendants().OfType<Igi2DoorTask>()];
        Igi2SwitchTask[] switches = [.. tree.Root.Descendants().OfType<Igi2SwitchTask>()];
        Igi2AlarmControlTask[] alarms = [.. tree.Root.Descendants().OfType<Igi2AlarmControlTask>()];
        Igi2StatusMessageTask[] messages = [.. tree.Root.Descendants().OfType<Igi2StatusMessageTask>()];
        Igi2AreaActivateTask[] areas = [.. tree.Root.Descendants().OfType<Igi2AreaActivateTask>()];
        Igi2AreaActivateTask extraction = areas.Single(a => a.Id == ExtractionAreaId);
        Igi2DefineComputerObjectiveTask objectives = tree.Root.Descendants().OfType<Igi2DefineComputerObjectiveTask>().Single();
        foreach (DoorSpec expected in Doors)
        {
            (float x, float y, float z) = DoorPosition(expected);
            Igi2DoorTask? door = doors.SingleOrDefault(d => d.Id == expected.Id);
            if (door is null || door.Model != expected.Fit.Model || door.Name != $"Door_{expected.Id}"
                || Math.Abs(door.Position.X - x) > 1 || Math.Abs(door.Position.Y - y) > 1 || Math.Abs(door.Position.Z - z) > 1
                || Math.Abs(door.Orientation.Z - DoorHeading(expected)) > 1e-4f || door.MaxAngle != expected.Fit.MaxAngle
                || door.LockedScript != expected.Locked || door.Pickable != expected.Pickable || !door.LinkUpToPortal)
            {
                throw new InvalidOperationException($"Door {expected.Id} does not match its fitting.");
            }
        }

        if (doors.Length != Doors.Length + 6
            || switches.Length != 3 || switches.Any(s => s.Name != $"Switch_{s.Id}")
            || messages.Length != 10 || messages.Any(m => m.Name != $"StatusMessage_{m.Id}")
            || areas.Length != 4
            || extraction.Name != $"AreaActivate_{ExtractionAreaId}" || extraction.Criteria != "CRITERIA_HUMAN0"
            || objectives.Objectives.Count != 8
            || objectives.Objectives[0].LinkToPosition != BreachAreaId
            || objectives.Objectives[1].LinkToPosition != Watchtower1.Id
            || objectives.Objectives[2].LinkToPosition != GeneratorSwitchId
            || objectives.Objectives[3].LinkToPosition != KeycardPickupId
            || objectives.Objectives[4].LinkToPosition != ServerAreaId
            || objectives.Objectives[5].LinkToPosition != SecurityConsoleId
            || objectives.Objectives[6].LinkToPosition != CoreSwitchId
            || objectives.Objectives[7].LinkToPosition != ExtractionAreaId
            || flow.CompleteScript != MissionCompleteExpression)
        {
            throw new InvalidOperationException("The doors, security console, objectives or extraction are not wired.");
        }
        if (alarms.Length != 1 || alarms[0].Id != AlarmControlId
            || alarms[0].TriggerScript != string.Join(" || ", Enumerable.Range(0, GuardCount)
                .Select(index => $"HumanAI_{200 + index}.isDetection"))
            || alarms[0].AlarmScript != "this.isTrigger")
        {
            throw new InvalidOperationException($"The guard alarm or the Core lockdown is not wired (count {alarms.Length}, "
                + (alarms.Length == 0 ? "no task)" : $"id {alarms[0].Id}, trigger '{alarms[0].TriggerScript}', alarm '{alarms[0].AlarmScript}')"));
        }
        if (player.Parent?.Type.Name != EngineTaskTypes.Dynamic
            || player.Children.OfType<Igi2HumanPlayerInputTask>().Count() != 1)
        {
            throw new InvalidOperationException("The player is not under Dynamic with its input task.");
        }

        Igi2LevelRun level = new(levelRoot, natives, new Igi2ScriptFunctions());
        Igi2AreaCriteria.Register(natives);
        extraction.Humans = [];
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
            || sky.TopColour.Z < 0.55f
            || sky.BottomColour1.X < 0.8f
            || sky.FogColour.X < 0.4f
            || clouds.Length != 2
            || clouds.Any(cloud => cloud.TextureResource != "MISSION:textures/long_descent_clouds.tga")
            || clouds[0].Alpha < 0.15f || clouds[1].Alpha < 0.09f
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
        VerifyMissionProgress(level, switches, alarms[0], cameras, extraction,
            tree.Root.Descendants().OfType<Igi2GenericPickupTask>().Single(p => p.Id == KeycardPickupId),
            objectives, flow);
    }

    private sealed record EntryTestOccupant(Vector3d RootPosition) : IIgi2AreaOccupant
    {
        public int Team => 0;
        public bool IsDead => false;
        public bool IsActive => true;
        public bool IsAi => false;
    }

    private static void VerifyMissionProgress(Igi2LevelRun level, Igi2SwitchTask[] switches,
        Igi2AlarmControlTask alarm, Igi2SecurityCameraTask[] cameras, Igi2AreaActivateTask extraction, Igi2GenericPickupTask keycard,
        Igi2DefineComputerObjectiveTask objectives, Igi2LevelFlowTask flow)
    {
        Igi2SwitchTask generator = switches.Single(s => s.Id == GeneratorSwitchId);
        Igi2SwitchTask terminal = switches.Single(s => s.Id == SecurityConsoleId);
        Igi2SwitchTask core = switches.Single(s => s.Id == CoreSwitchId);
        if (flow.IsComplete || !generator.IsActive || !terminal.IsActive || core.IsOn
            || alarm.IsAlarm || alarm.TriggerLastTick >= 0
            || core.ActiveScript != $"!this.isOn && Switch_{SecurityConsoleId}.isOn && AlarmControl_{AlarmControlId}.nTriggerLastTick < 0"
            || cameras.Any(c => !c.IsOn) || objectives.Objectives.Any(o => o.IsComplete))
        {
            throw new InvalidOperationException("The mission starts with cameras offline, the Core available, or objectives already complete.");
        }

        generator.Press(0);
        Ticks(4);
        if (!generator.IsOn || cameras.Where(c => c.Id is 150 or 151).Any(c => c.IsOn)
            || cameras.Where(c => c.Id is not (150 or 151)).Any(c => !c.IsOn))
        {
            throw new InvalidOperationException("Disabling Guard Post A does not take the perimeter cameras offline.");
        }

        keycard.Offer(keycard.Position);
        level.Tick();
        if (!keycard.IsPickedUp)
        {
            throw new InvalidOperationException("The Guard Post A keycard cannot be picked up.");
        }

        terminal.Press(0);
        Ticks(4);
        if (!terminal.IsOn || !core.IsActive)
        {
            throw new InvalidOperationException("The vault terminal does not unlock the Core interaction.");
        }

        core.Press(0);
        Ticks(4);
        if (!core.IsOn || flow.IsComplete)
        {
            throw new InvalidOperationException("The Core cannot be secured, or extraction completes prematurely.");
        }

        extraction.Humans = [new EntryTestOccupant(extraction.Position)];
        Ticks(4);
        if (!flow.IsComplete || objectives.Objectives.Count != 8)
        {
            throw new InvalidOperationException("The Core cannot be exfiltrated to complete the mission.");
        }

        void Ticks(int count)
        {
            for (int tick = 0; tick < count; tick++)
            {
                level.Tick();
            }
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
        for (int i = 0; i < PatrolCount; i++)
        {
            int eventType = 4;
            int patrol = -1;
            int idleCalls = 0, passCalls = 0, defaultCalls = 0;
            NativeRegistry natives = new();
            natives.RegisterConstant("AIEVENT_CREATE", 0);
            natives.RegisterConstant("AIEVENT_IDLE", 4);
            natives.RegisterFunction("AIFunction_GetCurrentEventType", _ => QvmValue.FromInt32(eventType));
            natives.RegisterFunction("AIFunction_DefaultHandler", _ => { defaultCalls++; return QvmValue.FromInt32(1); });
            natives.RegisterFunction("AIAction_Patrol", call =>
            {
                patrol = call.GetInt32(0);
                if (call.GetInt32(1) != 0 || call.GetInt32(2) != 1)
                {
                    throw new InvalidOperationException("Invalid patrol start arguments.");
                }
                return QvmValue.FromInt32(1);
            });
            natives.RegisterFunction("AIAction_Idle", _ => { idleCalls++; return QvmValue.FromInt32(1); });
            natives.RegisterFunction("AIFunction_PassEventOnToSquad", _ => { passCalls++; return QvmValue.FromInt32(1); });
            RunProgram(CreateGuardScript(i), natives);
            if (patrol != PatrolPathBaseId + i || idleCalls != 1 || passCalls != 1 || defaultCalls != 0)
            {
                throw new InvalidOperationException("The idle event did not start its perimeter patrol.");
            }
            eventType = 21;
            RunProgram(CreateGuardScript(i), natives);
            if (defaultCalls != 1 || idleCalls != 1 || passCalls != 1)
            {
                throw new InvalidOperationException("The guard's detection event did not reach its default handler.");
            }
        }
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
