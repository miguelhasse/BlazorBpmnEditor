using System.Diagnostics;
using System.Text.Json;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Benchmarks;

/// <summary>
/// What stays in memory, rather than what a call allocates: BenchmarkDotNet's memory diagnoser reports the second and cannot see the first.
/// </summary>
/// <remarks>
/// Run with <c>dotnet run -c Release --project tests/Bpmn.Benchmarks -- memory [path-to.bpmn] [copies]</c>. Retained sizes come from
/// <see cref="GC.GetTotalMemory(bool)"/> after a forced collection, so they are exact for the managed heap and ignore JIT and runtime overhead.
/// <c>copies</c> repeats the fixture's process contents to scale it up (the default fixture has 602 shapes).
/// </remarks>
internal static class MemoryReport
{
    private const int HistoryEdits = 100;
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static void Run(string[] args)
    {
        var path = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "large-virtualization-stress.bpmn");
        var xml = File.ReadAllText(path);
        Console.WriteLine($"fixture: {Path.GetFileName(path)}, {xml.Length:N0} characters");

        Allocation("XmlReader read", () => BpmnXmlReader.Read(xml));
        var document = BpmnXmlReader.Read(xml).Document;
        Allocation("canonical write", () => BpmnXmlWriter.Write(document));
        Allocation("canonical write to stream", () => BpmnXmlWriter.Write(document, Stream.Null));
        Allocation("JSON write", () => BpmnJsonWriter.Write(document));

        // What a canvas load sends across interop: the document through the runtime's default (web) serializer settings.
        var timer = Stopwatch.StartNew();
        var payload = JsonSerializer.Serialize(document, Web);
        Console.WriteLine($"interop payload (loadDocument): {payload.Length:N0} characters, {timer.Elapsed.TotalMilliseconds:F1} ms");

        _ = Interop.CanvasDocumentPayload.Serialize(document);
        timer.Restart();
        var canvasPayload = Interop.CanvasDocumentPayload.Serialize(document);
        Console.WriteLine($"canvas payload (what loadDocument sends now): {canvasPayload.Length:N0} characters, {timer.Elapsed.TotalMilliseconds:F1} ms");

        // Where the interop payload goes, by top-level property of the document and by the biggest element collections.
        using (var json = JsonDocument.Parse(payload))
        {
            foreach (var property in json.RootElement.EnumerateObject().OrderByDescending(p => p.Value.GetRawText().Length).Take(8))
            {
                Console.WriteLine($"  payload.{property.Name}: {property.Value.GetRawText().Length:N0} characters");
            }
        }

        if (Environment.GetEnvironmentVariable("BPMN_PAYLOAD_SAMPLE") == "1")
        {
            using var sample = JsonDocument.Parse(payload);
            Console.WriteLine(sample.RootElement.GetProperty("flowNodes")[0].GetRawText());
            Console.WriteLine(sample.RootElement.GetProperty("sequenceFlows")[0].GetRawText());
            Console.WriteLine(sample.RootElement.GetProperty("diagrams")[0].GetRawText()[..900]);
        }

        Retained("document (as read, with source)", () => BpmnXmlReader.Read(xml).Document);
        Retained("document without the source tree", () => BpmnXmlReader.Read(xml).Document with { Source = null });
        Retained("document with its element index built", () =>
        {
            var read = BpmnXmlReader.Read(xml).Document;
            _ = read.Find(read.FlowNodes[0].Id);
            return read;
        });

        var first = document.Diagrams[0];
        foreach (var warmIndex in new[] { false, true })
        {
            Retained($"history of {HistoryEdits} move edits{(warmIndex ? " (element index built for every step)" : "")}", () =>
            {
                var history = new BpmnEditHistory(BpmnXmlReader.Read(xml).Document, new EditingOptions(true, 0d, HistoryEdits));
                var shapes = history.Document.Diagrams[0].Plane.Shapes;
                for (var edit = 0; edit < HistoryEdits; edit++)
                {
                    var shape = shapes[edit % shapes.Count];
                    history.Apply(new MoveShapesCommand([new ShapeGeometry(shape.Id, shape.Bounds with { X = shape.Bounds.X + edit + 1 })]));
                    if (warmIndex) _ = history.Document.Find(shape.Id);
                }

                return history;
            });
        }

        _ = first;
    }

    private static void Allocation(string name, Func<object> action)
    {
        _ = action();
        GC.Collect();
        var gen0 = GC.CollectionCount(0);
        var gen2 = GC.CollectionCount(2);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        _ = action();
        timer.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine($"{name}: {allocated / 1048576d:F1} MB allocated, {timer.Elapsed.TotalMilliseconds:F1} ms, gen0 {GC.CollectionCount(0) - gen0}, gen2 {GC.CollectionCount(2) - gen2}");
    }

    private static void Retained(string name, Func<object> build)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var before = GC.GetTotalMemory(forceFullCollection: true);
        var kept = build();
        var after = GC.GetTotalMemory(forceFullCollection: true);
        GC.KeepAlive(kept);
        Console.WriteLine($"retained, {name}: {(after - before) / 1048576d:F2} MB");
    }
}
