using System.Xml.Linq;

using Hasseware.AspNetCore.Components.Bpmn.Model;

namespace Hasseware.AspNetCore.Components.Bpmn.Serialization;

/// <summary>
/// Reconciles a canonically-built document onto the XML it was read from.
/// </summary>
/// <remarks>
/// <para>
/// The alternative — a second element-construction pass that edits in place — would mean two
/// implementations of "what does this element look like" that drift apart. Instead the canonical
/// writer produces the document the model describes, and this merges that onto the original: one
/// set of construction logic, applied as a diff.
/// </para>
/// <para>
/// The rule is ownership. An element the writer knows how to produce is the model's to update or
/// delete; anything else was never read into the model and is therefore not the model's to change,
/// so it stays exactly where it was. That is how comments, processing instructions,
/// <c>ioSpecification</c>, vendor root elements and out-of-scope Choreography subtrees survive
/// (specification section 10.6) without this class knowing what any of them are.
/// </para>
/// <para>
/// Attributes follow the same rule with one asymmetry: attributes the canonical tree declares are
/// overwritten. Captured foreign attributes come from the model's Extras, so clearing one removes
/// it here; attributes the reader did not capture remain recoverable from the source.
/// </para>
/// </remarks>
internal sealed class BpmnFidelityWriter(BpmnDocument document, List<BpmnExportDiagnostic> diagnostics)
{
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    /// <summary>Merges <paramref name="canonical"/> into <paramref name="source"/>, in place.</summary>
    /// <param name="source">The retained original, which is modified.</param>
    /// <param name="canonical">The document the model describes.</param>
    internal void Merge(XElement source, XElement canonical)
    {
        MergeAttributes(source, canonical);
        MergeChildren(source, canonical);
    }

    /// <summary>
    /// Rewrites the redundant <c>incoming</c>/<c>outgoing</c> children the source chose to carry.
    /// </summary>
    /// <remarks>
    /// These restate what <c>sequenceFlow</c> already says, so the model does not store them and
    /// the ownership rule leaves them alone — which would let them go stale the moment a flow is
    /// added or removed. Rewriting them only where the document already had them keeps them true
    /// without adding a single element to a file that did without them.
    /// </remarks>
    /// <param name="root">The merged document root.</param>
    internal void SynchroniseFlowReferences(XElement root)
    {
        foreach (var element in root.Descendants().ToList())
        {
            var incoming = element.Elements(BpmnXmlNames.Model + "incoming").ToList();
            var outgoing = element.Elements(BpmnXmlNames.Model + "outgoing").ToList();
            if (incoming.Count == 0 && outgoing.Count == 0) continue;

            var id = Id(element);
            if (id is null) continue;

            Rewrite(element, incoming, "incoming", document.SequenceFlows
                .Where(flow => flow.TargetRef == id)
                .Select(flow => flow.Id));

            Rewrite(element, outgoing, "outgoing", document.SequenceFlows
                .Where(flow => flow.SourceRef == id)
                .Select(flow => flow.Id));
        }

        static void Rewrite(XElement owner, List<XElement> existing, string name, IEnumerable<string> references)
        {
            if (existing.Count == 0) return;

            var wanted = references.ToList();

            // Nothing to do is the common case, and doing it anyway would strip the whitespace
            // between these elements for no gain.
            if (existing.Select(element => element.Value.Trim()).SequenceEqual(wanted, StringComparer.Ordinal))
            {
                return;
            }

            var anchor = existing[0];
            foreach (var element in existing.Skip(1)) element.Remove();

            var written = false;
            foreach (var reference in wanted)
            {
                var element = new XElement(BpmnXmlNames.Model + name, reference);
                if (written)
                {
                    anchor.AddAfterSelf(element);
                    anchor = element;
                }
                else
                {
                    anchor.ReplaceWith(element);
                    anchor = element;
                    written = true;
                }
            }

            if (!written) anchor.Remove();
        }
    }

    private static void MergeAttributes(XElement source, XElement canonical)
    {
        foreach (var attribute in canonical.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
            {
                // Only add a declaration the source lacks: rebinding a prefix the document already
                // uses would change what every QName in it resolves to.
                if (source.Attribute(attribute.Name) is null) source.Add(new XAttribute(attribute));
                continue;
            }

            source.SetAttributeValue(attribute.Name, attribute.Value);
        }

        // Only elements whose model captures foreign attributes are authoritative for them; the rest
        // (formal expressions, loop data items, ...) never carry them through the model.
        if (canonical.Annotation<BpmnCanonicalWriter.CapturesForeignAttributes>() is not null)
        {
            foreach (var attribute in source.Attributes().Where(IsForeignAttribute).ToList())
            {
                if (canonical.Attribute(attribute.Name) is null) attribute.Remove();
            }
        }

        if (BpmnXmlNames.FormalExpressionElements.Contains(source.Name.LocalName)
            && source.Name.Namespace == BpmnXmlNames.Model
            && canonical.Attribute("language") is null)
        {
            source.Attribute("language")?.Remove();
        }

        // scriptFormat on a Script Task is modelled, so clearing it in the editor must clear it here.
        if (source.Name.Namespace == BpmnXmlNames.Model
            && source.Name.LocalName == "scriptTask"
            && canonical.Attribute("scriptFormat") is null)
        {
            source.Attribute("scriptFormat")?.Remove();
        }

        // messageRef on a Send or Receive Task is modelled, so clearing it in the editor must clear it here.
        if (source.Name.Namespace == BpmnXmlNames.Model
            && source.Name.LocalName is "sendTask" or "receiveTask"
            && canonical.Attribute("messageRef") is null)
        {
            source.Attribute("messageRef")?.Remove();
        }
    }

    private void MergeChildren(XElement source, XElement canonical)
    {
        var canonicalById = canonical.Elements()
            .Where(element => Id(element) is not null)
            .ToDictionary(element => Id(element)!, StringComparer.Ordinal);

        // Idless canonical children are claimed in document order by same-named source children.
        // That is what keeps a waypoint list, a flowNodeRef list or an element the model stores
        // without its id — multiInstanceLoopCharacteristics, say — updated in place instead of
        // deleted and rebuilt, which would drop both its id and the whitespace around it.
        var canonicalByName = canonical.Elements()
            .Where(element => Id(element) is null)
            .GroupBy(element => element.Name)
            .ToDictionary(group => group.Key, group => new Queue<XElement>(group));

        var consumed = new HashSet<XElement>();

        // Pass one: everything the writer owns is refreshed from the model, or dropped because the
        // model no longer has it. Everything else is left exactly where it is.
        foreach (var child in source.Elements().ToList())
        {
            if (!BpmnXmlNames.IsOwnedElement(source, child)) continue;

            var id = Id(child);

            if (id is not null && canonicalById.TryGetValue(id, out var byId))
            {
                MergeOne(child, byId);
                consumed.Add(byId);
                continue;
            }

            if (canonicalByName.TryGetValue(child.Name, out var queue) && queue.Count > 0)
            {
                var byName = queue.Dequeue();
                MergeOne(child, byName);
                consumed.Add(byName);
                continue;
            }

            if (id is not null)
            {
                diagnostics.Add(new BpmnExportDiagnostic(
                    BpmnExportSeverity.Info,
                    $"<{child.Name.LocalName}> is no longer in the model and was removed from the output.",
                    id));
            }

            child.Remove();
        }

        // Pass two: add back what the source did not already carry, in schema order.
        foreach (var child in canonical.Elements())
        {
            if (consumed.Contains(child)) continue;

            // Content the writer does not own reached the canonical tree by being preserved
            // verbatim, so the source still has its own copy and adding this one would duplicate it.
            if (!BpmnXmlNames.IsOwnedElement(canonical, child) && AlreadyPresent(source, child)) continue;

            BpmnSchemaOrder.Insert(source, new XElement(child));
        }
    }

    /// <summary>Updates one source element from its canonical counterpart.</summary>
    private void MergeOne(XElement source, XElement canonical)
    {
        if (source.Name.Namespace == BpmnXmlNames.Model
            && (BpmnXmlNames.FormalExpressionElements.Contains(source.Name.LocalName)
                || BpmnXmlNames.TextElements.Contains(source.Name.LocalName))
            && !canonical.Elements().Any())
        {
            MergeAttributes(source, canonical);

            if (!string.Equals(source.Value, canonical.Value, StringComparison.Ordinal))
            {
                if (source.Elements().Any())
                {
                    var elementId = source.AncestorsAndSelf()
                        .Select(Id)
                        .FirstOrDefault(id => id is not null);
                    diagnostics.Add(new BpmnExportDiagnostic(
                        BpmnExportSeverity.Normalized,
                        $"Editing <{source.Name.LocalName}> replaced nested XML content with the text held by the model.",
                        elementId));
                }

                source.ReplaceNodes(canonical.Value);
            }

            return;
        }

        if (!IsWholesale(source))
        {
            Merge(source, canonical);
            return;
        }

        // A container whose entire contents the model holds verbatim. Merging into it child by
        // child would mean matching vendor XML the writer knows nothing about, so it is compared as
        // a unit and replaced only if it actually differs — which for an untouched document is
        // never, leaving the block exactly as the author formatted it.
        if (!Equivalent(source, canonical)) source.ReplaceWith(new XElement(canonical));
    }

    /// <summary>
    /// True for the two containers whose contents are preserved verbatim rather than modelled.
    /// </summary>
    /// <remarks>
    /// <c>bpmn:extensionElements</c> and <c>di:extension</c> hold foreign XML that round-trips
    /// untouched, so the model is authoritative for the whole subtree or for none of it.
    /// </remarks>
    private static bool IsWholesale(XElement element) =>
        (element.Name == BpmnXmlNames.Model + "extensionElements")
        || (element.Name == BpmnXmlNames.DiagramInterchange + "extension");

    /// <summary>Compares two elements ignoring whitespace, attribute order and namespace declarations.</summary>
    private static bool Equivalent(XElement left, XElement right)
    {
        if (left.Name != right.Name) return false;

        var leftAttributes = left.Attributes().Where(a => !a.IsNamespaceDeclaration).ToList();
        var rightAttributes = right.Attributes().Where(a => !a.IsNamespaceDeclaration).ToList();

        if (leftAttributes.Count != rightAttributes.Count) return false;

        foreach (var attribute in leftAttributes)
        {
            if (right.Attribute(attribute.Name)?.Value != attribute.Value) return false;
        }

        if (Text(left) != Text(right)) return false;

        var leftChildren = left.Elements().ToList();
        var rightChildren = right.Elements().ToList();

        return leftChildren.Count == rightChildren.Count
               && leftChildren.Zip(rightChildren).All(pair => Equivalent(pair.First, pair.Second));

        static string Text(XElement element) => string.Concat(element.Nodes()
            .OfType<XText>()
            .Select(node => node.Value.Trim()));
    }

    private static bool IsForeignAttribute(XAttribute attribute) =>
        !attribute.IsNamespaceDeclaration
        && attribute.Name.Namespace != XNamespace.None
        && attribute.Name.Namespace != BpmnXmlNames.Model
        && attribute.Name.Namespace != BpmnXmlNames.Diagram
        && attribute.Name.Namespace != BpmnXmlNames.DiagramCommon
        && attribute.Name.Namespace != BpmnXmlNames.DiagramInterchange
        && attribute.Name.Namespace != Xsi;

    private static bool AlreadyPresent(XElement source, XElement candidate)
    {
        var id = Id(candidate);

        return source.Elements(candidate.Name).Any(existing => id is null || Id(existing) == id);
    }

    private static string? Id(XElement element) => element.Attribute("id")?.Value;

}
