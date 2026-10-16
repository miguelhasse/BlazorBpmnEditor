using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

using Hasseware.AspNetCore.Components.Bpmn.Model;

namespace Hasseware.AspNetCore.Components.Bpmn.Serialization;

/// <summary>
/// Writes this library's semantic model back to BPMN 2.0 XML.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart to <see cref="BpmnXmlReader"/>, and owned for the same reason: the reader keeps
/// DI geometry exactly and unknown vendor content verbatim, and a writer that normalised either
/// would throw that away on the first save.
/// </para>
/// <para>
/// Two modes, because specification section 10.6 requires two and they answer different questions.
/// <see cref="BpmnWriteMode.Canonical"/> regenerates the document from the model, deterministically,
/// and is what a test compares against. <see cref="BpmnWriteMode.Fidelity"/> edits the file that was
/// read and is what a user's Save should use on a document the editor did not create. Every
/// difference either mode introduces is reported rather than left for the user to discover in a
/// diff.
/// </para>
/// <para>
/// Nothing here validates. Section 10.3 puts schema validation and the section 5 semantic
/// validators after serialization as separate passes, and folding them in would mean a document
/// that fails a rule cannot be saved at all — which is the wrong answer while it is being edited.
/// </para>
/// </remarks>
public static class BpmnXmlWriter
{
    private static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>What canonical mode declares: the spelling every BPMN tool writes.</summary>
    private const string DefaultDeclaration = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>";

    private static readonly XmlWriterSettings IndentedSettings = new()
    {
        Indent = true,
        IndentChars = "  ",
        // Written by hand instead: XmlWriter lower-cases the encoding name and gives no way to
        // reproduce a source declaration exactly, or to leave one out that was never there.
        OmitXmlDeclaration = true,
        Encoding = Utf8,
    };

    private static readonly XmlWriterSettings CompactSettings = new()
    {
        Indent = false,
        OmitXmlDeclaration = true,
        Encoding = Utf8,
    };

    /// <summary>Writes a document in canonical mode.</summary>
    /// <param name="document">The document to write.</param>
    /// <returns>The XML and the export diagnostics manifest.</returns>
    public static BpmnExportResult Write(BpmnDocument document) => Write(document, BpmnWriteOptions.Canonical);

    /// <summary>Writes a document.</summary>
    /// <param name="document">The document to write.</param>
    /// <param name="options">Which mode to write in, and what to stamp on the root.</param>
    /// <returns>The XML and the export diagnostics manifest.</returns>
    public static BpmnExportResult Write(BpmnDocument document, BpmnWriteOptions options)
    {
        var prepared = Prepare(document, options);
        return new BpmnExportResult(
            Serialize(prepared.Output, prepared.Indent, prepared.Declaration),
            prepared.Mode,
            prepared.Diagnostics);
    }

    /// <summary>Writes a document to a stream.</summary>
    /// <param name="document">The document to write.</param>
    /// <param name="stream">The stream to write UTF-8 XML to.</param>
    /// <param name="options">Which mode to write in.</param>
    /// <returns>The export diagnostics manifest.</returns>
    public static IReadOnlyList<BpmnExportDiagnostic> Write(
        BpmnDocument document,
        Stream stream,
        BpmnWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var prepared = Prepare(document, options ?? BpmnWriteOptions.Canonical);
        Serialize(prepared.Output, stream, prepared.Indent, prepared.Declaration);
        return prepared.Diagnostics;
    }

    private static (
        XDocument Output,
        bool Indent,
        string? Declaration,
        BpmnWriteMode Mode,
        List<BpmnExportDiagnostic> Diagnostics) Prepare(BpmnDocument document, BpmnWriteOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        var canonicalWriter = new BpmnCanonicalWriter(document, options);
        var canonical = canonicalWriter.Build();
        var diagnostics = new List<BpmnExportDiagnostic>(canonicalWriter.Diagnostics);

        var mode = options.Mode;
        if (document.Source is { } source && mode == BpmnWriteMode.Canonical)
        {
            diagnostics.AddRange(BpmnCanonicalLossAnalyzer.FindLosses(
                source.Clone(),
                canonical,
                source.ImportLosses));
        }

        if (mode == BpmnWriteMode.Fidelity && document.Source is null)
        {
            diagnostics.Add(new BpmnExportDiagnostic(
                BpmnExportSeverity.Info,
                "Fidelity mode was requested for a document that was not read from XML; wrote canonical output instead."));
            mode = BpmnWriteMode.Canonical;
        }

        if (mode == BpmnWriteMode.Canonical
            && !options.AllowLossyCanonicalExport
            && diagnostics.Any(diagnostic => diagnostic.Severity == BpmnExportSeverity.Dropped))
        {
            throw new BpmnLossyExportException(
                diagnostics.Where(diagnostic => diagnostic.Severity == BpmnExportSeverity.Dropped).ToList());
        }

        var (output, indent, declaration) = mode == BpmnWriteMode.Fidelity
            ? (MergeOntoSource(document, canonical, diagnostics), false, document.Source!.Declaration)
            : (canonical, options.Indent, DefaultDeclaration);

        return (output, indent, declaration, mode, diagnostics);
    }

    private static XDocument MergeOntoSource(
        BpmnDocument document,
        XDocument canonical,
        List<BpmnExportDiagnostic> diagnostics)
    {
        var merged = document.Source!.Clone();

        if (merged.Root is null || canonical.Root is null)
        {
            diagnostics.Add(new BpmnExportDiagnostic(
                BpmnExportSeverity.Normalized,
                "The retained source had no root element; wrote canonical output instead."));
            return canonical;
        }

        var writer = new BpmnFidelityWriter(document, diagnostics);
        writer.Merge(merged.Root, canonical.Root);
        writer.SynchroniseFlowReferences(merged.Root);

        return merged;
    }

    private static string Serialize(XDocument document, bool indent, string? declaration)
    {
        // A plain StringWriter reports UTF-16, and XmlWriter believes it — which is how a document
        // that is about to be saved as UTF-8 ends up declaring encoding="utf-16". The settings
        // object cannot override it; only the writer's own Encoding is consulted.
        using var text = new Utf8StringWriter();

        using (var writer = XmlWriter.Create(text, indent ? IndentedSettings : CompactSettings))
        {
            document.Save(writer);
        }

        // Trimmed at both ends: whitespace outside the root element is preserved by the parser,
        // so a source that had a newline between its declaration and its root would otherwise
        // gain a blank line every time it was written.
        var body = text.ToString().Trim();

        // Normalising the trailing newline keeps file comparisons stable across platforms.
        return declaration is null ? body + "\n" : declaration + "\n" + body + "\n";
    }

    private static void Serialize(XDocument document, Stream stream, bool indent, string? declaration)
    {
        if (declaration is not null)
        {
            stream.Write(Utf8.GetBytes(declaration));
            stream.WriteByte((byte)'\n');
        }

        var firstNode = document.FirstNode;
        while (firstNode is XText firstText && string.IsNullOrWhiteSpace(firstText.Value))
            firstNode = firstNode.NextNode;

        var lastNode = document.LastNode;
        while (lastNode is XText lastText && string.IsNullOrWhiteSpace(lastText.Value))
            lastNode = lastNode.PreviousNode;

        using (var writer = XmlWriter.Create(stream, indent ? IndentedSettings : CompactSettings))
        {
            for (var node = firstNode; node is not null; node = node.NextNode)
            {
                node.WriteTo(writer);
                if (ReferenceEquals(node, lastNode)) break;
            }
        }

        stream.WriteByte((byte)'\n');
    }

    /// <summary>A <see cref="StringWriter"/> that tells <see cref="XmlWriter"/> the truth about encoding.</summary>
    private sealed class Utf8StringWriter() : StringWriter(CultureInfo.InvariantCulture)
    {
        public override Encoding Encoding => Utf8;
    }
}
