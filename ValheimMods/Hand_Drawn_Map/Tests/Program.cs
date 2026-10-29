using Hand_Drawn_Map;

int passed = 0;
void Test(string name, Action action)
{
    action();
    Console.WriteLine("PASS " + name);
    passed++;
}
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
void Near(float actual, float expected, float tolerance = 0.002f)
    => Check(Math.Abs(actual - expected) <= tolerance, $"Expected {expected}, got {actual}");
ChartStroke Stroke(params ChartPoint[] points)
{
    var stroke = new ChartStroke(2f);
    stroke.Points.AddRange(points);
    return stroke;
}
ChartPoint P(float x, float z) => new ChartPoint(x, z);

Test("Pan and zoom preserve stored world coordinates", () =>
{
    ChartPoint world = P(2345.25f, -4123.5f);
    foreach (float scale in new[] { 0.1f, 1f, 32f })
    foreach (ChartPoint center in new[] { P(0, 0), P(1000, -2000), P(-8000, 8000) })
    {
        ChartPoint screen = ChartGeometry.ToScreen(world, center, scale);
        ChartPoint recovered = ChartGeometry.ToWorld(screen, center, scale);
        Near(recovered.X, world.X);
        Near(recovered.Z, world.Z);
    }
});
Test("Pointer anchored zoom preserves the world point", () =>
{
    ChartPoint pointer = P(170, -85), center = P(1000, 2000);
    ChartPoint before = ChartGeometry.ToWorld(pointer, center, 4f);
    ChartPoint next = center + before - ChartGeometry.ToWorld(pointer, center, 3.6f);
    ChartPoint after = ChartGeometry.ToWorld(pointer, next, 3.6f);
    Near(before.X, after.X); Near(before.Z, after.Z);
});
Test("Partial eraser splits a long segment without deleting its ends", () =>
{
    var pieces = ChartGeometry.Erase(Stroke(P(-10, 0), P(10, 0)), P(0, 0), P(0, 0), 1f, out bool changed);
    Check(changed && pieces.Count == 2, "Expected two fragments");
    Near(pieces[0].Points[0].X, -10);
    Near(pieces[0].Points[^1].X, -2);
    Near(pieces[1].Points[0].X, 2);
    Near(pieces[1].Points[^1].X, 10);
});
Test("Fast eraser sweep leaves no gaps between frames", () =>
{
    var pieces = ChartGeometry.Erase(Stroke(P(-10, 0), P(10, 0)), P(0, -100), P(0, 100), 1f, out bool changed);
    Check(changed && pieces.Count == 2, "Swept capsule must split the line");
    Near(pieces[0].Points[^1].X, -2);
    Near(pieces[1].Points[0].X, 2);
});
Test("Eraser parallel to ink keeps capsule endpoint arcs", () =>
{
    var pieces = ChartGeometry.Erase(Stroke(P(-10, 0), P(10, 0)), P(-3, 0), P(3, 0), 1f, out bool changed);
    Check(changed && pieces.Count == 2, "Expected two surviving ends");
    Near(pieces[0].Points[^1].X, -5);
    Near(pieces[1].Points[0].X, 5);
});
Test("Unrelated ink stays unchanged", () =>
{
    ChartStroke original = Stroke(P(-10, 10), P(10, 10));
    var pieces = ChartGeometry.Erase(original, P(0, -1), P(0, 1), 1f, out bool changed);
    Check(!changed && pieces.Count == 1 && ReferenceEquals(original, pieces[0]), "Unrelated stroke changed");
});
Test("Erasing an endpoint preserves the remaining polyline", () =>
{
    var pieces = ChartGeometry.Erase(Stroke(P(0, 0), P(5, 0), P(10, 0)), P(0, 0), P(0, 0), 1f, out bool changed);
    Check(changed && pieces.Count == 1, "Expected one surviving fragment");
    Near(pieces[0].Points[0].X, 2);
    Near(pieces[0].Points[^1].X, 10);
});
Test("A click dot can be erased", () =>
{
    var pieces = ChartGeometry.Erase(Stroke(P(1, 1)), P(1, 1), P(1, 1), 1f, out bool changed);
    Check(changed && pieces.Count == 0, "Dot survived eraser");
});
Test("Distant long strokes retain accurate circle intersections", () =>
{
    var pieces = ChartGeometry.Erase(Stroke(P(-10000, 1), P(10000, 1)), P(0, 0), P(0, 0), 1f, out bool changed);
    Check(changed && pieces.Count == 2, "Expected two fragments");
    Near(pieces[0].Points[^1].X, -(float)Math.Sqrt(3), 0.005f);
    Near(pieces[1].Points[0].X, (float)Math.Sqrt(3), 0.005f);
});
Test("Document counters follow partial erasing", () =>
{
    var doc = new ChartDocument { PointCount = 2 };
    doc.Strokes.Add(Stroke(P(-10, 0), P(10, 0)));
    Check(doc.Erase(P(0, 0), P(0, 0), 1), "Erase not applied");
    Check(doc.Strokes.Count == 2 && doc.PointCount == 4, "Counters out of sync");
});
Test("Survey lines clip at both viewport edges", () =>
{
    ChartPoint a = P(-10000, -10000), b = P(10000, 10000);
    Check(ChartGeometry.ClipLine(ref a, ref b, 300, 200), "Diagonal disappeared");
    Near(a.X, -200); Near(a.Z, -200); Near(b.X, 200); Near(b.Z, 200);
    a = P(20, -10000); b = P(20, 10000);
    Check(ChartGeometry.ClipLine(ref a, ref b, 300, 200), "Vertical disappeared");
    Near(a.Z, -200); Near(b.Z, 200);
    a = P(-1000, 500); b = P(1000, 500);
    Check(!ChartGeometry.ClipLine(ref a, ref b, 300, 200), "Offscreen line survived");
});
Test("Two rotated bearings intersect at the unmarked observer", () =>
{
    ChartPoint observer = P(3500, -4700);
    ChartPoint[] directions = { P(0, 1), P(0.6f, 0.8f) };
    foreach (ChartPoint direction in directions)
    {
        ChartPoint center = P(-1000, 2000);
        ChartPoint closest = observer + direction * ChartPoint.Dot(center - observer, direction);
        Near(ChartGeometry.DistanceSquared(observer, closest - direction * 20000, closest + direction * 20000), 0f, 0.001f);
    }
});

string folder = Path.Combine(Path.GetTempPath(), "HandDrawnMapTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    Test("Save/reload preserves fragments, width, pan, and zoom", () =>
    {
        var doc = new ChartDocument { Center = P(1345.25f, -5678.5f), Zoom = 0.125f, PointCount = 3 };
        doc.Strokes.Add(Stroke(P(-10, 0), P(10, 0)));
        doc.Strokes.Add(Stroke(P(25, 25)));
        doc.Erase(P(0, 0), P(0, 0), 1);
        string path = Path.Combine(folder, "world_player.chart");
        ChartStore.Save(path, doc);
        ChartDocument loaded = ChartStore.Load(path);
        Check(loaded.Strokes.Count == 3 && loaded.PointCount == 5, "Fragments not preserved");
        Near(loaded.Center.X, doc.Center.X); Near(loaded.Center.Z, doc.Center.Z);
        Near(loaded.Zoom, doc.Zoom); Near(loaded.Strokes[0].Width, 2);
        Near(loaded.Strokes[0].Points[^1].X, -2);
        loaded.Zoom = 0.25f;
        ChartStore.Save(path, loaded);
        Near(ChartStore.Load(path).Zoom, 0.25f);
        Near(ChartStore.Load(path + ".bak").Zoom, 0.125f);
    });
    Test("Separate world files cannot mix drawings", () =>
    {
        var doc = new ChartDocument { PointCount = 1 };
        doc.Strokes.Add(Stroke(P(5, 8)));
        ChartStore.Save(Path.Combine(folder, "worldA.chart"), doc);
        Check(ChartStore.Load(Path.Combine(folder, "worldB.chart")).Strokes.Count == 0, "World data leaked");
    });
    Test("Truncated and invalid files fail without changing the file", () =>
    {
        string good = Path.Combine(folder, "world_player.chart");
        byte[] bytes = File.ReadAllBytes(good);
        string broken = Path.Combine(folder, "broken.chart");
        File.WriteAllBytes(broken, bytes[..^1]);
        bool rejected = false;
        try { ChartStore.Load(broken); } catch (EndOfStreamException) { rejected = true; }
        Check(rejected, "Truncated chart accepted");
        Check(File.ReadAllBytes(broken).SequenceEqual(bytes[..^1]), "Load changed corrupt file");
        bytes[0] ^= 0xFF;
        File.WriteAllBytes(broken, bytes);
        rejected = false;
        try { ChartStore.Load(broken); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Invalid header accepted");
    });
}
finally
{
    foreach (string file in Directory.GetFiles(folder)) File.Delete(file);
    Directory.Delete(folder);
}
Console.WriteLine($"{passed} checks passed.");
