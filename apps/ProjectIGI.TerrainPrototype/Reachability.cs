using ProjectIGI.Core.Numerics;

namespace ProjectIGI.TerrainPrototype;

/// <summary>
/// Where a player can walk from the open ground, over the collision triangles of placed buildings and the terrain,
/// with the engine's movement rules: a grounded human is stopped only by a near-vertical face (|normal.Z| below 0.1)
/// met 0.4 m or 1.8 m above its feet, so it climbs any step lower than 0.4 m, and it needs 1.8 m clear overhead.
/// Doors are taken as open. Not a substitute for walking the mission, but it finds walls, ledges and missing stairs.
/// </summary>
internal sealed class Reachability
{
    // Stairs need cells finer than a tread: at 0.5 m a 0.25 m riser on 0.3 m treads reads as a 0.4 m wall.
    private static readonly double Cell = double.TryParse(Environment.GetEnvironmentVariable("IGI_CELL"),
        System.Globalization.CultureInfo.InvariantCulture, out double cell) && cell > 0 ? cell : 0.5;
    private const double Bucket = 1.0;
    private const double MiddleProbe = 0.4;
    private const double HighProbe = 1.8;
    private const double Headroom = 1.8;
    private const double MaxDrop = 3.0;

    private readonly List<(Vector3d A, Vector3d B, Vector3d C, Vector3d N)> _triangles = [];
    private readonly Dictionary<(int, int), List<int>> _buckets = [];
    private readonly Func<double, double, double> _ground;
    private readonly double _minX, _minY;
    private readonly int _width, _height;
    private readonly List<double>[] _levels;
    private readonly HashSet<(int X, int Y, int Level)> _reached = [];

    /// <param name="triangles">Solid triangles in metres.</param>
    /// <param name="ground">The terrain height in metres at a point in metres.</param>
    /// <param name="minX">The region searched, in metres; its border is open ground the walk starts from.</param>
    /// <param name="minY">The region's minimum Y, in metres.</param>
    /// <param name="maxX">The region's maximum X, in metres.</param>
    /// <param name="maxY">The region's maximum Y, in metres.</param>
    public Reachability(IEnumerable<(Vector3d A, Vector3d B, Vector3d C)> triangles, Func<double, double, double> ground,
        double minX, double minY, double maxX, double maxY)
    {
        _ground = ground;
        _minX = minX;
        _minY = minY;
        _width = (int)Math.Ceiling((maxX - minX) / Cell);
        _height = (int)Math.Ceiling((maxY - minY) / Cell);
        foreach ((Vector3d a, Vector3d b, Vector3d c) in triangles)
        {
            // Retail collision meshes wind clockwise seen from outside: floors' tops come out facing down otherwise.
            Vector3d n = Vector3d.Cross(c - a, b - a);
            if (n.Length < 1e-9)
            {
                continue;
            }

            int index = _triangles.Count;
            _triangles.Add((a, b, c, n / n.Length));
            int x0 = (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X)) / Bucket);
            int x1 = (int)Math.Floor(Math.Max(a.X, Math.Max(b.X, c.X)) / Bucket);
            int y0 = (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y)) / Bucket);
            int y1 = (int)Math.Floor(Math.Max(a.Y, Math.Max(b.Y, c.Y)) / Bucket);
            for (int bx = x0; bx <= x1; bx++)
            {
                for (int by = y0; by <= y1; by++)
                {
                    if (!_buckets.TryGetValue((bx, by), out List<int>? list))
                    {
                        _buckets[(bx, by)] = list = [];
                    }

                    list.Add(index);
                }
            }
        }

        _levels = new List<double>[_width * _height];
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                _levels[(y * _width) + x] = Standing(CentreX(x), CentreY(y));
            }
        }

        Flood();
    }

    public double CentreX(int x) => _minX + ((x + 0.5) * Cell);

    public double CentreY(int y) => _minY + ((y + 0.5) * Cell);

    /// <summary>Whether a standing place within <paramref name="radius"/> metres of a point and 0.6 m of a height is reached.</summary>
    public bool Reaches(double x, double y, double z, double radius = 1.0)
    {
        foreach ((int cx, int cy, int level) in _reached)
        {
            double dx = CentreX(cx) - x, dy = CentreY(cy) - y;
            if ((dx * dx) + (dy * dy) <= radius * radius && Math.Abs(_levels[(cy * _width) + cx][level] - z) <= 0.6)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Reached standing area, in square metres, per height band of <paramref name="band"/> metres, inside a box.</summary>
    public SortedDictionary<double, double> ReachedArea(double minX, double minY, double maxX, double maxY, double band = 1.0)
    {
        SortedDictionary<double, double> area = [];
        foreach ((int cx, int cy, int level) in _reached)
        {
            double x = CentreX(cx), y = CentreY(cy);
            if (x < minX || x > maxX || y < minY || y > maxY)
            {
                continue;
            }

            double key = Math.Floor(_levels[(cy * _width) + cx][level] / band) * band;
            area[key] = area.GetValueOrDefault(key) + (Cell * Cell);
        }

        return area;
    }

    /// <summary>Reached standing area on structures (not bare terrain), in square metres, per height band, inside a box.</summary>
    public SortedDictionary<double, double> ReachedOnStructure(double minX, double minY, double maxX, double maxY, double band = 1.0)
    {
        SortedDictionary<double, double> area = [];
        foreach ((int cx, int cy, int level) in _reached)
        {
            double x = CentreX(cx), y = CentreY(cy);
            double z = _levels[(cy * _width) + cx][level];
            if (x < minX || x > maxX || y < minY || y > maxY || Math.Abs(z - _ground(x, y)) < 0.05)
            {
                continue;
            }

            double key = Math.Floor(z / band) * band;
            area[key] = area.GetValueOrDefault(key) + (Cell * Cell);
        }

        return area;
    }

    /// <summary>For diagnostics: every surface over a point, with its height and its unit normal's Z.</summary>
    public IEnumerable<(double Z, double Nz)> Surfaces(double x, double y)
    {
        foreach (int index in Near(x, y, x, y))
        {
            var t = _triangles[index];
            if (VerticalHit(t, x, y) is { } z)
            {
                yield return (z, t.N.Z);
            }
        }
    }

    /// <summary>For diagnostics: the standing heights at the cell nearest a point, each with whether it is reached.</summary>
    public IEnumerable<(double Z, bool Reached)> At(double x, double y)
    {
        int cx = (int)Math.Floor((x - _minX) / Cell), cy = (int)Math.Floor((y - _minY) / Cell);
        if (cx < 0 || cy < 0 || cx >= _width || cy >= _height)
        {
            yield break;
        }

        List<double> levels = _levels[(cy * _width) + cx];
        for (int level = 0; level < levels.Count; level++)
        {
            yield return (levels[level], _reached.Contains((cx, cy, level)));
        }
    }

    /// <summary>For diagnostics: whether the wall probes pass between two points for feet at a height.</summary>
    public bool ProbesClear(double x0, double y0, double x1, double y1, double z) => Clear(x0, y0, x1, y1, z);

    /// <summary>All standing areas, reached or not, per height band inside a box: what the floors offer.</summary>
    public SortedDictionary<double, double> StandingArea(double minX, double minY, double maxX, double maxY, double band = 1.0)
    {
        SortedDictionary<double, double> area = [];
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                double px = CentreX(x), py = CentreY(y);
                if (px < minX || px > maxX || py < minY || py > maxY)
                {
                    continue;
                }

                foreach (double z in _levels[(y * _width) + x])
                {
                    double key = Math.Floor(z / band) * band;
                    area[key] = area.GetValueOrDefault(key) + (Cell * Cell);
                }
            }
        }

        return area;
    }

    // Every height a person can stand at over a point: upward-facing surfaces with the headroom above them clear.
    private List<double> Standing(double x, double y)
    {
        List<(double Z, bool Up)> hits = [];
        foreach (int index in Near(x, y, x, y))
        {
            var t = _triangles[index];
            if (VerticalHit(t, x, y) is { } z)
            {
                hits.Add((z, t.N.Z > 0.7));
            }
        }

        double ground = _ground(x, y);
        hits.Add((ground, true));
        hits.Sort((p, q) => p.Z.CompareTo(q.Z));
        List<double> levels = [];
        foreach ((double z, bool up) in hits)
        {
            if (!up || z < ground - 0.01)
            {
                continue;
            }

            bool covered = hits.Any(other => other.Z > z + 0.05 && other.Z < z + Headroom);
            if (!covered && (levels.Count == 0 || z - levels[^1] > 0.3))
            {
                levels.Add(z);
            }
        }

        return levels;
    }

    private void Flood()
    {
        Queue<(int X, int Y, int Level)> open = new();
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                if (x == 0 || y == 0 || x == _width - 1 || y == _height - 1)
                {
                    List<double> levels = _levels[(y * _width) + x];
                    double ground = _ground(CentreX(x), CentreY(y));
                    int level = levels.FindIndex(z => Math.Abs(z - ground) < 0.05);
                    if (level >= 0 && _reached.Add((x, y, level)))
                    {
                        open.Enqueue((x, y, level));
                    }
                }
            }
        }

        (int, int)[] steps = [(1, 0), (-1, 0), (0, 1), (0, -1)];
        while (open.TryDequeue(out var node))
        {
            double from = _levels[(node.Y * _width) + node.X][node.Level];
            foreach ((int dx, int dy) in steps)
            {
                int nx = node.X + dx, ny = node.Y + dy;
                if (nx < 0 || ny < 0 || nx >= _width || ny >= _height)
                {
                    continue;
                }

                List<double> levels = _levels[(ny * _width) + nx];
                for (int level = 0; level < levels.Count; level++)
                {
                    double to = levels[level];
                    if (to - from >= MiddleProbe || from - to > MaxDrop || _reached.Contains((nx, ny, level)))
                    {
                        continue;
                    }

                    if (Clear(CentreX(node.X), CentreY(node.Y), CentreX(nx), CentreY(ny), from)
                        && (Math.Abs(to - from) < 0.05 || Clear(CentreX(node.X), CentreY(node.Y), CentreX(nx), CentreY(ny), to)))
                    {
                        _reached.Add((nx, ny, level));
                        open.Enqueue((nx, ny, level));
                    }
                }
            }
        }
    }

    // Whether the engine's two grounded probes, at 0.4 m and 1.8 m above feet at height z, pass between two points.
    private bool Clear(double x0, double y0, double x1, double y1, double z)
    {
        foreach (double up in (double[])[MiddleProbe, HighProbe])
        {
            Vector3d from = new(x0, y0, z + up);
            Vector3d to = new(x1, y1, z + up);
            foreach (int index in Near(x0, y0, x1, y1))
            {
                var t = _triangles[index];
                if (Math.Abs(t.N.Z) < 0.1 && SegmentHits(from, to, t.A, t.B, t.C))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private IEnumerable<int> Near(double x0, double y0, double x1, double y1)
    {
        HashSet<int> seen = [];
        for (int bx = (int)Math.Floor(Math.Min(x0, x1) / Bucket); bx <= (int)Math.Floor(Math.Max(x0, x1) / Bucket); bx++)
        {
            for (int by = (int)Math.Floor(Math.Min(y0, y1) / Bucket); by <= (int)Math.Floor(Math.Max(y0, y1) / Bucket); by++)
            {
                if (_buckets.TryGetValue((bx, by), out List<int>? list))
                {
                    foreach (int index in list)
                    {
                        if (seen.Add(index))
                        {
                            yield return index;
                        }
                    }
                }
            }
        }
    }

    private static double? VerticalHit((Vector3d A, Vector3d B, Vector3d C, Vector3d N) t, double x, double y)
    {
        // Barycentric test in the XY plane, then the plane's height there.
        double d = ((t.B.Y - t.C.Y) * (t.A.X - t.C.X)) + ((t.C.X - t.B.X) * (t.A.Y - t.C.Y));
        if (Math.Abs(d) < 1e-12)
        {
            return null;
        }

        double l1 = (((t.B.Y - t.C.Y) * (x - t.C.X)) + ((t.C.X - t.B.X) * (y - t.C.Y))) / d;
        double l2 = (((t.C.Y - t.A.Y) * (x - t.C.X)) + ((t.A.X - t.C.X) * (y - t.C.Y))) / d;
        double l3 = 1 - l1 - l2;
        return l1 < 0 || l2 < 0 || l3 < 0 ? null : (l1 * t.A.Z) + (l2 * t.B.Z) + (l3 * t.C.Z);
    }

    private static bool SegmentHits(Vector3d from, Vector3d to, Vector3d a, Vector3d b, Vector3d c)
    {
        Vector3d direction = to - from;
        Vector3d e1 = b - a;
        Vector3d e2 = c - a;
        Vector3d p = Vector3d.Cross(direction, e2);
        double det = Vector3d.Dot(e1, p);
        if (Math.Abs(det) < 1e-12)
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
}
