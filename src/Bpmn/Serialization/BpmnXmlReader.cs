using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;
using Hasseware.AspNetCore.Components.Bpmn.Model.Geometry;

namespace Hasseware.AspNetCore.Components.Bpmn.Serialization;

/// <summary>
/// Reads BPMN 2.0 XML into this library's semantic model.
/// </summary>
/// <remarks>
/// <para>
/// Owned rather than delegated. The specification treats the document as two persisted layers
/// (semantics and DI) that the canvas is only a view over, and requires DI geometry to survive a
/// round trip (sections 10.6 and 11) and unknown vendor content to survive it verbatim
/// (section 6.1). General-purpose BPMN libraries built for execution engines model neither —
/// they normalise geometry and drop what they cannot interpret — so the reader is part of this
/// library rather than a dependency of it.
/// </para>
/// <para>
/// Nothing is invented. Where a document omits geometry, the element is reported as unrenderable
/// rather than given synthesised bounds, because a reader that guesses layout silently rewrites
/// the user's diagram on the next save.
/// </para>
/// <para>
/// Out-of-scope Choreography and Conversation content (section 8) is reported and skipped, never
/// deleted: this pass reads, and preserving those subtrees for write-back is the writer's job.
/// </para>
/// </remarks>
public static class BpmnXmlReader
{
    /// <summary>
    /// XML reader settings used for every parse.
    /// </summary>
    /// <remarks>
    /// DTD processing is prohibited and no resolver is supplied, which closes XXE and billion-laughs
    /// entirely rather than mitigating them. Specification section 5.5 puts this first in the
    /// validator pipeline: parse safely, then quarantine unsafe external content.
    /// </remarks>
    private static readonly XmlReaderSettings SafeSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        // Comments, processing instructions and the original whitespace are all kept. Section
        // 10.6 requires them to survive a round trip, and nothing downstream can put back what
        // the parser discards, so carrying them is the price of fidelity mode.
        IgnoreComments = false,
        IgnoreProcessingInstructions = false,
        IgnoreWhitespace = false,
        CloseInput = false,
    };

    /// <summary>Reads a document from XML text.</summary>
    /// <param name="xml">The BPMN 2.0 XML to read.</param>
    /// <returns>The document and every finding produced while reading it.</returns>
    /// <exception cref="BpmnXmlException">The input is not well-formed XML, or is not a BPMN document.</exception>
    public static BpmnImportResult Read(string xml)
        => Read(xml, schemas: null);

    /// <summary>Reads a document and validates it against an already-loaded XML Schema set.</summary>
    /// <param name="xml">The BPMN 2.0 XML to read.</param>
    /// <param name="schemas">A preloaded schema set; the reader never resolves schema URLs from the document.</param>
    /// <returns>The document and every parse, schema, and import finding.</returns>
    public static BpmnImportResult Read(string xml, XmlSchemaSet? schemas)
    {
        ArgumentNullException.ThrowIfNull(xml);

        EnsureNotTooDeep(() => XmlReader.Create(new StringReader(xml), SafeSettings));

        using var stringReader = new StringReader(xml);
        using var reader = XmlReader.Create(stringReader, SafeSettings);
        return Read(reader, schemas);
    }

    /// <summary>Reads a document from a stream.</summary>
    /// <param name="stream">A stream positioned at the start of the BPMN 2.0 XML.</param>
    /// <returns>The document and every finding produced while reading it.</returns>
    /// <exception cref="BpmnXmlException">The input is not well-formed XML, or is not a BPMN document.</exception>
    public static BpmnImportResult Read(Stream stream)
        => Read(stream, schemas: null);

    /// <summary>Reads a stream and validates it against an already-loaded XML Schema set.</summary>
    /// <param name="stream">A stream positioned at the start of the BPMN 2.0 XML.</param>
    /// <param name="schemas">A preloaded schema set; the reader never resolves schema URLs from the document.</param>
    /// <returns>The document and every parse, schema, and import finding.</returns>
    public static BpmnImportResult Read(Stream stream, XmlSchemaSet? schemas)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // The depth is checked on a pass of its own, so the stream has to be able to start again.
        if (!stream.CanSeek)
        {
            var buffered = new MemoryStream();
            stream.CopyTo(buffered);
            buffered.Position = 0;
            stream = buffered;
        }

        var start = stream.Position;
        EnsureNotTooDeep(() => XmlReader.Create(stream, SafeSettings));
        stream.Position = start;

        using var reader = XmlReader.Create(stream, SafeSettings);
        return Read(reader, schemas);
    }

    // Loading a document into a tree costs more than linear in how deep it nests (a hundred thousand levels took half a minute on a megabyte), and the walks that
    // follow recurse once per level. So depth is checked first, on a plain reader that only counts, which is fast however deep the input goes.
    private static void EnsureNotTooDeep(Func<XmlReader> open)
    {
        try
        {
            using var scan = open();
            while (scan.Read())
            {
                if (scan.NodeType == XmlNodeType.Element && scan.Depth >= MaxElementDepth)
                {
                    throw new BpmnXmlException($"The document nests elements more than {MaxElementDepth.ToString(CultureInfo.InvariantCulture)} levels deep, which no BPMN document does.");
                }
            }
        }
        catch (XmlException exception)
        {
            throw new BpmnXmlException("The document is not well-formed XML.", exception);
        }
    }

    private static BpmnImportResult Read(XmlReader reader, XmlSchemaSet? schemas)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException exception)
        {
            throw new BpmnXmlException("The document is not well-formed XML.", exception);
        }

        var definitions = document.Root;
        if (definitions is null || definitions.Name != BpmnXmlNames.Model + "definitions")
        {
            throw new BpmnXmlException(
                "The document root is not a BPMN 2.0 <definitions> element in the BPMN MODEL namespace.");
        }

        // Before anything walks the tree: reading, copying and writing all recurse once per level, so what they are given has to be bounded first.
        var shape = Measure(document);
        if (shape.Depth > MaxElementDepth)
        {
            throw new BpmnXmlException($"The document nests elements more than {MaxElementDepth.ToString(CultureInfo.InvariantCulture)} levels deep, which no BPMN document does.");
        }

        List<BpmnImportDiagnostic> preliminary = [];
        if (shape.NonFiniteNumbers > 0)
        {
            preliminary.Add(new BpmnImportDiagnostic(
                BpmnImportSeverity.Degraded,
                $"{shape.NonFiniteNumbers.ToString(CultureInfo.InvariantCulture)} number(s) in the diagram layout were not a finite number and were ignored."));
        }

        var schemaDiagnostics = ValidateSchema(document, schemas);
        var schemaStatus = schemas is not { Count: > 0 }
            ? BpmnSchemaValidationStatus.NotRun
            : schemaDiagnostics.Count == 0
                ? BpmnSchemaValidationStatus.Passed
                : BpmnSchemaValidationStatus.Failed;
        return new ReadContext(definitions, BpmnSourceDocument.Capture(document, ownsDocument: true), schemaDiagnostics, schemaStatus, preliminary).Build();
    }

    /// <summary>The deepest element nesting a document may have; the JSON reader allows the same.</summary>
    private const int MaxElementDepth = 256;

    // The depth of the tree and how many layout numbers are not finite, found without recursion: a tree this is run on may be as deep as the input made it.
    private static (int Depth, int NonFiniteNumbers) Measure(XDocument document)
    {
        var depth = 0;
        var nonFinite = 0;
        var pending = new Stack<(XElement Element, int Depth)>();
        if (document.Root is { } root) pending.Push((root, 1));

        while (pending.Count > 0)
        {
            var (element, level) = pending.Pop();
            depth = Math.Max(depth, level);

            // Past the limit there is no point in looking further: the answer is already no.
            if (depth > MaxElementDepth) return (depth, nonFinite);

            if (element.Name.Namespace == BpmnXmlNames.DiagramCommon || element.Name.Namespace == BpmnXmlNames.DiagramInterchange)
            {
                foreach (var attribute in element.Attributes())
                {
                    if (attribute.Name.Namespace == XNamespace.None
                        && attribute.Name.LocalName is "x" or "y" or "width" or "height" or "size"
                        && double.TryParse(attribute.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                        && !double.IsFinite(number))
                    {
                        nonFinite++;
                    }
                }
            }

            foreach (var child in element.Elements()) pending.Push((child, level + 1));
        }

        return (depth, nonFinite);
    }

    private static List<BpmnImportDiagnostic> ValidateSchema(XDocument document, XmlSchemaSet? schemas)
    {
        if (schemas is not { Count: > 0 }) return [];

        var diagnostics = new List<BpmnImportDiagnostic>();
        document.Validate(schemas, (_, args) => diagnostics.Add(new BpmnImportDiagnostic(
            BpmnImportSeverity.SchemaError,
            args.Message)));
        return diagnostics;
    }

    /// <summary>
    /// Accumulates state for one read.
    /// </summary>
    /// <remarks>
    /// A class rather than a chain of static methods because a read is inherently stateful: lane
    /// membership, Pool ownership and container nesting are all discovered at different points in
    /// the document and resolved against each other at the end.
    /// </remarks>
    private sealed class ReadContext(
        XElement definitions,
        BpmnSourceDocument source,
        IReadOnlyList<BpmnImportDiagnostic> schemaDiagnostics,
        BpmnSchemaValidationStatus schemaValidationStatus,
        IReadOnlyList<BpmnImportDiagnostic> preliminary)
    {
        private readonly List<BpmnImportDiagnostic> _diagnostics = [];
        private readonly List<BpmnProcess> _processes = [];
        private readonly List<BpmnLaneSet> _laneSets = [];
        private readonly List<BpmnLane> _lanes = [];
        private readonly List<BpmnFlowNode> _flowNodes = [];
        private readonly List<BpmnSequenceFlow> _sequenceFlows = [];
        private readonly List<BpmnMessageFlow> _messageFlows = [];
        private readonly List<BpmnAssociation> _associations = [];
        private readonly List<BpmnDataAssociation> _dataAssociations = [];
        private readonly List<BpmnTextAnnotation> _textAnnotations = [];
        private readonly List<BpmnGroup> _groups = [];
        private readonly List<BpmnDataObjectReference> _dataObjectReferences = [];
        private readonly List<BpmnDataStoreReference> _dataStoreReferences = [];
        private readonly List<BpmnDiagram> _diagrams = [];
        private readonly List<BpmnDataObject> _dataObjects = [];

        /// <summary>Maps every container (Process or Sub-Process) to its root Process.</summary>
        private readonly Dictionary<string, string> _rootProcessOf = new(StringComparer.Ordinal);

        public BpmnImportResult Build()
        {
            _diagnostics.AddRange(preliminary);
            _diagnostics.AddRange(schemaDiagnostics);
            ReportExcludedElements();

            foreach (var process in definitions.Elements(BpmnXmlNames.Model + "process"))
            {
                ReadProcess(process);
            }

            var collaboration = ReadCollaboration();
            var catalog = ReadCatalog();
            ReadDiagrams();

            var document = new BpmnDocument
            {
                Definitions = ReadDefinitions(),
                Catalog = catalog,
                DataObjects = _dataObjects,
                Source = source,
                Processes = _processes,
                Collaboration = collaboration,
                LaneSets = _laneSets,
                Lanes = _lanes,
                FlowNodes = ApplyPoolAndLaneMembership(collaboration),
                SequenceFlows = ApplyDefaultFlows(),
                MessageFlows = _messageFlows,
                Associations = _associations,
                DataAssociations = _dataAssociations,
                TextAnnotations = _textAnnotations,
                Groups = _groups,
                DataObjectReferences = _dataObjectReferences,
                DataStoreReferences = _dataStoreReferences,
                Diagrams = _diagrams,
            };

            /*
             * A Lane subdivides a Pool, so one whose Process no Participant references is given
             * one. The only structural rewrite the reader performs, and it reports itself: what it
             * adds is derived entirely from the lanes already drawn, and every element the file
             * stated is left exactly as it was.
             */
            document = BpmnLaneOwnership.WithPoolsForOrphanLanes(document, (poolId, containerId) =>
                _diagnostics.Add(new BpmnImportDiagnostic(
                    BpmnImportSeverity.Degraded,
                    $"Lanes on '{containerId}' had no Pool to subdivide, so Pool '{poolId}' was added around them.",
                    poolId,
                    containerId)));

            /*
             * A connector end is always on the outline of its shape (section 4.6), so ends a file
             * wrote elsewhere are moved onto it. Reported, because the document that is opened is
             * not byte-for-byte the geometry that was stored.
             */
            var moved = new List<MovedConnector>();
            document = ConnectorEndpoints.Normalize(document, moved);

            // A connector already reported for having no usable route is not reported a second time
            // for the ends that were derived to give it one.
            var reported = _diagnostics
                .Where(diagnostic => diagnostic.Severity == BpmnImportSeverity.Degraded && diagnostic.ElementId is not null)
                .Select(diagnostic => diagnostic.ElementId!)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var connector in moved.Where(connector => !reported.Contains(connector.ConnectorId)))
            {
                // Degraded, not Info: the reader kept the connector but not as the file stated it,
                // which is what the import offer lists under "Changed".
                var message = connector.Reshaped
                    ? $"Connector '{connector.ConnectorId}' had a diagonal segment or an end off the outline of its shape; its route was replaced by a right-angled one on the outlines."
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"Connector '{connector.ConnectorId}' had an end off the outline of its shape; it was moved onto it ({connector.Distance:0.#} units).");

                _diagnostics.Add(new BpmnImportDiagnostic(
                    BpmnImportSeverity.Degraded,
                    message,
                    connector.ConnectorId));
            }

            var dropped = _diagnostics
                .Where(diagnostic => diagnostic.Severity == BpmnImportSeverity.Dropped)
                .ToList();
            document = document with { Source = source.WithImportLosses(dropped) };

            return new BpmnImportResult(document, _diagnostics, schemaValidationStatus);
        }

        private BpmnDefinitions ReadDefinitions() => new(
            Attribute(definitions, "id") ?? "definitions",
            Attribute(definitions, "targetNamespace") ?? string.Empty,
            Attribute(definitions, "exporter"),
            Attribute(definitions, "exporterVersion"),
            Attribute(definitions, "name"),
            Attribute(definitions, "expressionLanguage"),
            Attribute(definitions, "typeLanguage"))
        {
            NamespaceDeclarations = source.Prefixes,
            Extras = ReadExtras(definitions),
        };

        private void ReportExcludedElements()
        {
            foreach (var element in definitions.Descendants()
                         .Where(e => e.Name.Namespace == BpmnXmlNames.Model
                                     && BpmnXmlNames.ExcludedElements.Contains(e.Name.LocalName)))
            {
                // Out of scope for rendering and editing, but the document stays valid and the
                // content is not the editor's to delete (section 8).
                _diagnostics.Add(new BpmnImportDiagnostic(
                    BpmnImportSeverity.Degraded,
                    $"'{element.Name.LocalName}' is a Choreography or Conversation construct, which this editor does not render or edit.",
                    Attribute(element, "id")));
            }
        }


        // --- global definitions (section 1.0) ---------------------------------

        /// <summary>
        /// Reads the definitions that are referenced but never drawn.
        /// </summary>
        /// <remarks>
        /// Everything at root that is neither a Process, a Collaboration nor a diagram, and that
        /// this catalog does not type, is kept verbatim rather than dropped. That is what lets an
        /// export put back Choreography content, vendor root elements and anything a later BPMN
        /// revision adds, without this reader having to understand any of it.
        /// </remarks>
        private BpmnRootCatalog ReadCatalog()
        {
            var messages = new List<BpmnMessage>();
            var signals = new List<BpmnSignal>();
            var errors = new List<BpmnError>();
            var escalations = new List<BpmnEscalation>();
            var items = new List<BpmnItemDefinition>();
            var stores = new List<BpmnDataStore>();
            var categories = new List<BpmnCategory>();
            var resources = new List<BpmnResource>();
            var interfaces = new List<BpmnInterface>();
            var globalTasks = new List<BpmnGlobalTask>();
            var imports = new List<BpmnImport>();
            var relationships = new List<BpmnRelationship>();
            var declarations = new List<BpmnExtensionDeclaration>();
            var preserved = new List<BpmnExtensionElement>();

            foreach (var element in definitions.Elements())
            {
                var localName = element.Name.LocalName;

                if (element.Name.Namespace != BpmnXmlNames.Model)
                {
                    // A foreign root element: not ours to interpret, and not ours to delete.
                    // The DI namespace is excluded because ReadDiagrams owns it.
                    if (element.Name.Namespace != BpmnXmlNames.Diagram)
                    {
                        preserved.Add(ReadExtensionElement(element));
                    }

                    continue;
                }

                if (localName is "process" or "collaboration") continue;

                var id = Attribute(element, "id");

                switch (localName)
                {
                    case "message" when id is not null:
                        messages.Add(new BpmnMessage(id, Attribute(element, "name"), Attribute(element, "itemRef"))
                        {
                            Extras = ReadExtras(element),
                        });
                        continue;

                    case "signal" when id is not null:
                        signals.Add(new BpmnSignal(id, Attribute(element, "name"), Attribute(element, "structureRef"))
                        {
                            Extras = ReadExtras(element),
                        });
                        continue;

                    case "error" when id is not null:
                        errors.Add(new BpmnError(
                            id,
                            Attribute(element, "name"),
                            Attribute(element, "errorCode"),
                            Attribute(element, "structureRef"))
                        {
                            Extras = ReadExtras(element),
                        });
                        continue;

                    case "escalation" when id is not null:
                        escalations.Add(new BpmnEscalation(
                            id,
                            Attribute(element, "name"),
                            Attribute(element, "escalationCode"),
                            Attribute(element, "structureRef"))
                        {
                            Extras = ReadExtras(element),
                        });
                        continue;

                    case "itemDefinition" when id is not null:
                        items.Add(new BpmnItemDefinition(
                            id,
                            Attribute(element, "structureRef"),
                            BoolAttribute(element, "isCollection") ?? false,
                            Attribute(element, "itemKind")));
                        continue;

                    case "dataStore" when id is not null:
                        stores.Add(new BpmnDataStore(
                            id,
                            Attribute(element, "name"),
                            Attribute(element, "capacity"),
                            BoolAttribute(element, "isUnlimited"),
                            Attribute(element, "itemSubjectRef")));
                        continue;

                    case "category" when id is not null:
                        categories.Add(new BpmnCategory(
                            id,
                            Attribute(element, "name"),
                            element.Elements(BpmnXmlNames.Model + "categoryValue")
                                .Select(value => new BpmnCategoryValue(
                                    Attribute(value, "id") ?? string.Empty,
                                    Attribute(value, "value")))
                                .Where(value => value.Id.Length > 0)
                                .ToList()));
                        continue;

                    case "resource" when id is not null:
                        resources.Add(new BpmnResource(id, Attribute(element, "name")));
                        continue;

                    case "interface" when id is not null:
                        interfaces.Add(ReadInterface(element, id));
                        continue;

                    case "globalTask" or "globalUserTask" or "globalManualTask" or "globalScriptTask"
                        or "globalBusinessRuleTask" when id is not null:
                        globalTasks.Add(new BpmnGlobalTask(id, localName, Attribute(element, "name")));
                        continue;

                    case "import":
                        imports.Add(new BpmnImport(
                            Attribute(element, "importType"),
                            Attribute(element, "location"),
                            Attribute(element, "namespace")));
                        continue;

                    case "relationship":
                        relationships.Add(new BpmnRelationship(
                            id,
                            Attribute(element, "type"),
                            Attribute(element, "direction"),
                            element.Elements(BpmnXmlNames.Model + "source").Select(x => x.Value.Trim()).ToList(),
                            element.Elements(BpmnXmlNames.Model + "target").Select(x => x.Value.Trim()).ToList()));
                        continue;

                    case "extension":
                        declarations.Add(new BpmnExtensionDeclaration(
                            id,
                            Attribute(element, "definition"),
                            BoolAttribute(element, "mustUnderstand") ?? false,
                            element.Elements().Select(ReadExtensionElement).ToList()));
                        continue;

                    default:
                        // Includes the Choreography and Conversation roots reported above, plus
                        // anything a later BPMN revision adds at document level.
                        preserved.Add(ReadExtensionElement(element));
                        continue;
                }
            }

            return new BpmnRootCatalog
            {
                Messages = messages,
                Signals = signals,
                Errors = errors,
                Escalations = escalations,
                ItemDefinitions = items,
                DataStores = stores,
                Categories = categories,
                Resources = resources,
                Interfaces = interfaces,
                GlobalTasks = globalTasks,
                Imports = imports,
                Relationships = relationships,
                ExtensionDeclarations = declarations,
                Preserved = preserved,
            };
        }

        private static BpmnInterface ReadInterface(XElement element, string id) => new(
            id,
            Attribute(element, "name"),
            Attribute(element, "implementationRef"),
            element.Elements(BpmnXmlNames.Model + "operation")
                .Select(operation => new BpmnOperation(
                    Attribute(operation, "id") ?? string.Empty,
                    Attribute(operation, "name"),
                    operation.Element(BpmnXmlNames.Model + "inMessageRef")?.Value.Trim(),
                    operation.Element(BpmnXmlNames.Model + "outMessageRef")?.Value.Trim(),
                    Attribute(operation, "implementationRef")))
                .Where(operation => operation.Id.Length > 0)
                .ToList());

        // --- semantic ---------------------------------------------------------

        private void ReadProcess(XElement process)
        {
            var id = Attribute(process, "id");
            if (id is null)
            {
                _diagnostics.Add(new BpmnImportDiagnostic(
                    BpmnImportSeverity.Dropped,
                    "A <process> element without an id cannot be referenced and was skipped."));
                return;
            }

            _processes.Add(new BpmnProcess(
                id,
                Attribute(process, "name"),
                BoolAttribute(process, "isExecutable"),
                Attribute(process, "processType"),
                BoolAttribute(process, "isClosed"))
            {
                Extras = ReadExtras(process),
            });
            _rootProcessOf[id] = id;

            ReadLaneSets(process, id);
            ReadFlowElements(process, id, id);
            ReadArtifacts(process, id);
        }

        private void ReadLaneSets(XElement container, string containerId)
        {
            foreach (var laneSet in container.Elements(BpmnXmlNames.Model + "laneSet"))
            {
                ReadLaneSet(laneSet, containerId);
            }
        }

        private void ReadLaneSet(XElement laneSet, string containerId)
        {
            // A lane set without an id is still meaningful; synthesise a stable one from its
            // owner rather than dropping the Lanes it holds.
            var declaredId = Attribute(laneSet, "id");
            var laneSetId = declaredId ?? $"{containerId}_laneSet";
            var lanes = laneSet.Elements(BpmnXmlNames.Model + "lane").ToList();

            _laneSets.Add(new BpmnLaneSet(
                laneSetId,
                containerId,
                lanes.Select(lane => Attribute(lane, "id") ?? string.Empty).Where(x => x.Length > 0).ToList(),
                declaredId is null));

            foreach (var lane in lanes)
            {
                var laneId = Attribute(lane, "id");
                if (laneId is null) continue;

                var childLaneSet = lane.Element(BpmnXmlNames.Model + "childLaneSet");
                var childLaneSetId = childLaneSet is null ? null : Attribute(childLaneSet, "id") ?? $"{laneId}_laneSet";

                _lanes.Add(new BpmnLane(
                    laneId,
                    Attribute(lane, "name"),
                    laneSetId,
                    lane.Elements(BpmnXmlNames.Model + "flowNodeRef").Select(r => r.Value.Trim()).ToList(),
                    childLaneSetId)
                {
                    Extras = ReadExtras(lane),
                });

                if (childLaneSet is not null) ReadLaneSet(childLaneSet, containerId);
            }
        }

        /// <summary>
        /// Reads the flow elements of a Process or Sub-Process.
        /// </summary>
        /// <remarks>
        /// Recursive, but the result is flat: nested content keeps its own
        /// <c>ContainerId</c> and stays in the document's single collection. That is what lets a
        /// Sub-Process collapse or expand without moving anything, and what makes the
        /// cross-boundary Sequence Flow rules (SF-007) a simple comparison.
        /// </remarks>
        private void ReadFlowElements(XElement container, string containerId, string rootProcessId)
        {
            foreach (var element in container.Elements())
            {
                if (element.Name.Namespace != BpmnXmlNames.Model) continue;

                var localName = element.Name.LocalName;
                if (localName == "sequenceFlow")
                {
                    ReadSequenceFlow(element, containerId);
                    continue;
                }

                if (localName is "dataObjectReference" or "dataStoreReference")
                {
                    ReadDataReference(element, localName, containerId);
                    continue;
                }

                if (localName == "dataObject")
                {
                    // The definition, not the drawn shape. It has no DI of its own and several
                    // References may point at it, so it lives beside them rather than inside one.
                    if (Attribute(element, "id") is { } dataObjectId)
                    {
                        _dataObjects.Add(new BpmnDataObject(
                            dataObjectId,
                            containerId,
                            Attribute(element, "name"),
                            BoolAttribute(element, "isCollection") ?? false,
                            Attribute(element, "itemSubjectRef"))
                        {
                            Extras = ReadExtras(element),
                        });
                    }

                    continue;
                }

                var type = BpmnXmlNames.ResolveFlowNodeType(localName);
                if (type is null) continue;

                ReadFlowNode(element, type.Value, containerId, rootProcessId);
            }
        }

        private void ReadFlowNode(XElement element, BpmnFlowNodeType type, string containerId, string rootProcessId)
        {
            var id = Attribute(element, "id");
            if (id is null)
            {
                _diagnostics.Add(new BpmnImportDiagnostic(
                    BpmnImportSeverity.Dropped,
                    $"A <{element.Name.LocalName}> without an id cannot be referenced and was skipped.",
                    null,
                    containerId));
                return;
            }

            // An Event Sub-Process is a subProcess with triggeredByEvent set; the distinction
            // changes its border, its stencil and whether Sequence Flow may cross its boundary.
            if (type == BpmnFlowNodeType.SubProcess && BoolAttribute(element, "triggeredByEvent") == true)
            {
                type = BpmnFlowNodeType.EventSubProcess;
            }

            var ownsContent = type is BpmnFlowNodeType.SubProcess
                or BpmnFlowNodeType.EventSubProcess
                or BpmnFlowNodeType.Transaction
                or BpmnFlowNodeType.AdHocSubProcess;

            // cancelActivity (Boundary Events) and isInterrupting (Event Sub-Process Start Events)
            // are the same notion under two names, and both default to interrupting.
            var isInterrupting = BoolAttribute(element, "cancelActivity")
                ?? BoolAttribute(element, "isInterrupting")
                ?? true;

            var calledElement = ReadCalledElement(element);

            // Pool and Lane membership are resolved after the whole document is read, because
            // the Participant and the Lane that claim this node may appear after it.
            _flowNodes.Add(new BpmnFlowNode(
                id,
                type,
                containerId,
                Attribute(element, "name"),
                ReadEventDefinition(element),
                isInterrupting,
                Attribute(element, "attachedToRef"),
                PoolId: null,
                LaneId: null,
                BoolAttribute(element, "isForCompensation") ?? false,
                ReadLoopCharacteristics(element),
                BoolAttribute(element, "instantiate") ?? false,
                calledElement.LocalReference,
                ownsContent,
                Attribute(element, "default"))
            {
                EventDefinitions = ReadEventDefinitions(element),
                CalledElementQName = calledElement.QName,
                MessageRef = type is BpmnFlowNodeType.SendTask or BpmnFlowNodeType.ReceiveTask
                    ? Attribute(element, "messageRef")
                    : null,
                ScriptFormat = type == BpmnFlowNodeType.ScriptTask ? Attribute(element, "scriptFormat") : null,
                Script = type == BpmnFlowNodeType.ScriptTask ? element.Element(BpmnXmlNames.Model + "script")?.Value : null,
                StandardLoop = ReadStandardLoop(element),
                MultiInstanceLoop = ReadMultiInstanceLoop(element),
                ActivationCondition = ReadFormalExpression(element.Element(BpmnXmlNames.Model + "activationCondition")),
                Extras = ReadExtras(element),
            });

            if (ownsContent)
            {
                _rootProcessOf[id] = rootProcessId;
                ReadLaneSets(element, id);
                ReadFlowElements(element, id, rootProcessId);
                ReadArtifacts(element, id);
            }

            ReadDataAssociations(element, containerId);
        }

        private static BpmnEventDefinitionType ReadEventDefinition(XElement element)
        {
            var definitionsFound = element.Elements()
                .Where(child => child.Name.Namespace == BpmnXmlNames.Model)
                .Select(child => BpmnXmlNames.ResolveEventDefinition(child.Name.LocalName))
                .Where(definition => definition is not null)
                .Select(definition => definition!.Value)
                .ToList();

            if (definitionsFound.Count == 0) return BpmnEventDefinitionType.None;
            if (definitionsFound.Count == 1) return definitionsFound[0];

            // Several triggers on one Event is Multiple or Parallel Multiple; which one depends
            // on whether all triggers are required (section 1.1).
            return BoolAttribute(element, "parallelMultiple") == true
                ? BpmnEventDefinitionType.ParallelMultiple
                : BpmnEventDefinitionType.Multiple;
        }

        private static BpmnLoopCharacteristics ReadLoopCharacteristics(XElement element)
        {
            if (element.Element(BpmnXmlNames.Model + "standardLoopCharacteristics") is not null)
            {
                return BpmnLoopCharacteristics.Standard;
            }

            var multiInstance = element.Element(BpmnXmlNames.Model + "multiInstanceLoopCharacteristics");
            if (multiInstance is null) return BpmnLoopCharacteristics.None;

            // isSequential defaults to false, which BPMN draws as the parallel three-bar marker.
            return BoolAttribute(multiInstance, "isSequential") == true
                ? BpmnLoopCharacteristics.MultiInstanceSequential
                : BpmnLoopCharacteristics.MultiInstanceParallel;
        }

        private (string? LocalReference, BpmnQName? QName) ReadCalledElement(XElement element)
        {
            var calledElement = Attribute(element, "calledElement");
            if (calledElement is null) return (null, null);

            var separator = calledElement.IndexOf(':', StringComparison.Ordinal);
            var prefix = separator < 0 ? null : calledElement[..separator];
            var localName = separator < 0 ? calledElement : calledElement[(separator + 1)..];

            if (localName.Length == 0 || localName.Contains(':'))
            {
                _diagnostics.Add(new BpmnImportDiagnostic(
                    BpmnImportSeverity.Degraded,
                    $"Call Activity '{Attribute(element, "id")}' has malformed calledElement QName '{calledElement}'.",
                    Attribute(element, "id")));
                return (null, new BpmnQName(localName, null, calledElement));
            }

            // In common BPMN exports, an unprefixed calledElement is a reference into this
            // Definitions' target namespace even when the root's default namespace is the BPMN
            // vocabulary URI. Preserve the legacy document-local convention; prefixed values
            // remain fully namespace-qualified and cannot collide with a local id.
            var targetNamespace = Attribute(definitions, "targetNamespace") ?? string.Empty;
            var namespaceUri = prefix is null
                ? targetNamespace
                : element.GetNamespaceOfPrefix(prefix)?.NamespaceName;

            if (prefix is not null && namespaceUri is null)
            {
                _diagnostics.Add(new BpmnImportDiagnostic(
                    BpmnImportSeverity.Degraded,
                    $"Call Activity '{Attribute(element, "id")}' uses undeclared QName prefix '{prefix}' in calledElement.",
                    Attribute(element, "id")));
            }

            var qname = new BpmnQName(localName, namespaceUri, calledElement);
            var localReference = string.Equals(namespaceUri, targetNamespace, StringComparison.Ordinal)
                ? localName
                : null;

            return (localReference, qname);
        }

        private static BpmnStandardLoopDetails? ReadStandardLoop(XElement element)
        {
            var loop = element.Element(BpmnXmlNames.Model + "standardLoopCharacteristics");
            if (loop is null) return null;

            var maximum = Attribute(loop, "loopMaximum");
            return new BpmnStandardLoopDetails(
                BoolAttribute(loop, "testBefore"),
                ReadFormalExpression(loop.Element(BpmnXmlNames.Model + "loopCondition")),
                int.TryParse(maximum, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null);
        }

        private BpmnMultiInstanceLoopDetails? ReadMultiInstanceLoop(XElement element)
        {
            var loop = element.Element(BpmnXmlNames.Model + "multiInstanceLoopCharacteristics");
            if (loop is null) return null;

            return new BpmnMultiInstanceLoopDetails(
                BoolAttribute(loop, "isSequential"),
                Attribute(loop, "behavior"),
                ReadFormalExpression(loop.Element(BpmnXmlNames.Model + "loopCardinality")),
                loop.Element(BpmnXmlNames.Model + "loopDataInputRef")?.Value.Trim(),
                ReadLoopDataItem(loop.Element(BpmnXmlNames.Model + "inputDataItem")),
                ReadFormalExpression(loop.Element(BpmnXmlNames.Model + "completionCondition")),
                loop.Element(BpmnXmlNames.Model + "loopDataOutputRef")?.Value.Trim(),
                ReadLoopDataItem(loop.Element(BpmnXmlNames.Model + "outputDataItem")),
                loop.Elements(BpmnXmlNames.Model + "complexBehaviorDefinition")
                    .Select(definition => new BpmnComplexBehaviorDefinition(
                        Attribute(definition, "event"),
                        ReadFormalExpression(definition.Element(BpmnXmlNames.Model + "condition"))))
                    .ToList())
            {
                Extras = ReadExtras(loop),
            };
        }

        private static BpmnLoopDataItem? ReadLoopDataItem(XElement? element) => element is null
            ? null
            : new BpmnLoopDataItem(
                Attribute(element, "id"),
                Attribute(element, "name"),
                Attribute(element, "itemSubjectRef"),
                BoolAttribute(element, "isCollection"));

        private static BpmnFormalExpression? ReadFormalExpression(XElement? element)
        {
            if (element is null) return null;

            var typeQName = element.Attribute(XName.Get("type", "http://www.w3.org/2001/XMLSchema-instance"))?.Value;
            return new BpmnFormalExpression(
                element.Value,
                Attribute(element, "language"),
                typeQName)
            {
                TypeNamespaceUri = typeQName is null ? null : ResolveQNameNamespace(element, typeQName),
            };
        }

        private static string? ResolveQNameNamespace(XElement element, string value)
        {
            var separator = value.IndexOf(':');
            var prefix = separator < 0 ? null : value[..separator];
            return prefix is null
                ? element.GetDefaultNamespace().NamespaceName
                : element.GetNamespaceOfPrefix(prefix)?.NamespaceName;
        }

        private void ReadSequenceFlow(XElement element, string containerId)
        {
            var id = Attribute(element, "id");
            var source = Attribute(element, "sourceRef");
            var target = Attribute(element, "targetRef");

            if (id is null || source is null || target is null)
            {
                _diagnostics.Add(new BpmnImportDiagnostic(
                    BpmnImportSeverity.Dropped,
                    "A <sequenceFlow> without an id, sourceRef or targetRef cannot be drawn and was skipped.",
                    id,
                    containerId));
                return;
            }

            var conditionElement = element.Element(BpmnXmlNames.Model + "conditionExpression");
            var condition = conditionElement?.Value;

            // IsDefault is filled in later: the default attribute is on the source node, and that
            // node may not have been read yet.
            _sequenceFlows.Add(new BpmnSequenceFlow(
                id,
                source,
                target,
                containerId,
                Attribute(element, "name"),
                string.IsNullOrWhiteSpace(condition) ? null : condition,
                IsDefault: false,
                conditionElement is null ? null : Attribute(conditionElement, "language"))
            {
                Extras = ReadExtras(element),
            });
        }

        private void ReadDataReference(XElement element, string localName, string containerId)
        {
            var id = Attribute(element, "id");
            if (id is null) return;

            if (localName == "dataObjectReference")
            {
                _dataObjectReferences.Add(new BpmnDataObjectReference(
                    id,
                    containerId,
                    Attribute(element, "name"),
                    Attribute(element, "dataObjectRef"),
                    element.Element(BpmnXmlNames.Model + "dataState") is { } state ? Attribute(state, "name") : null,
                    BoolAttribute(element, "isCollection") ?? false)
                {
                    Extras = ReadExtras(element),
                });
                return;
            }

            _dataStoreReferences.Add(new BpmnDataStoreReference(
                id,
                containerId,
                Attribute(element, "name"),
                Attribute(element, "dataStoreRef"),
                element.Element(BpmnXmlNames.Model + "dataState") is { } storeState
                    ? Attribute(storeState, "name")
                    : null)
            {
                Extras = ReadExtras(element),
            });
        }

        private void ReadDataAssociations(XElement activity, string containerId)
        {
            foreach (var (localName, kind) in new[]
                     {
                         ("dataInputAssociation", BpmnDataAssociationKind.Input),
                         ("dataOutputAssociation", BpmnDataAssociationKind.Output),
                     })
            {
                foreach (var association in activity.Elements(BpmnXmlNames.Model + localName))
                {
                    var id = Attribute(association, "id");
                    if (id is null) continue;

                    var sources = association.Elements(BpmnXmlNames.Model + "sourceRef")
                        .Select(source => source.Value.Trim())
                        .Where(value => value.Length > 0)
                        .ToList();

                    var target = association.Element(BpmnXmlNames.Model + "targetRef")?.Value.Trim();

                    // An Input Association reads into the Activity, an Output Association writes
                    // out of it, so the Activity supplies whichever endpoint the XML omits.
                    var activityId = Attribute(activity, "id") ?? containerId;
                    if (kind == BpmnDataAssociationKind.Input && string.IsNullOrEmpty(target)) target = activityId;
                    if (kind == BpmnDataAssociationKind.Output && sources.Count == 0) sources.Add(activityId);

                    if (string.IsNullOrEmpty(target)) continue;

                    _dataAssociations.Add(new BpmnDataAssociation(
                        id,
                        kind,
                        sources,
                        target,
                        containerId,
                        association.Element(BpmnXmlNames.Model + "transformation")?.Value)
                    {
                        OwnerId = activityId,
                        Assignments = association.Elements(BpmnXmlNames.Model + "assignment")
                            .Select(assignment => new BpmnDataAssignment(
                                ReadFormalExpression(assignment.Element(BpmnXmlNames.Model + "from"))
                                    ?? new BpmnFormalExpression(string.Empty),
                                ReadFormalExpression(assignment.Element(BpmnXmlNames.Model + "to"))
                                    ?? new BpmnFormalExpression(string.Empty)))
                            .ToList(),
                        Extras = ReadExtras(association),
                    });
                }
            }
        }

        private void ReadArtifacts(XElement container, string containerId)
        {
            foreach (var annotation in container.Elements(BpmnXmlNames.Model + "textAnnotation"))
            {
                var id = Attribute(annotation, "id");
                if (id is null) continue;

                _textAnnotations.Add(new BpmnTextAnnotation(
                    id,
                    annotation.Element(BpmnXmlNames.Model + "text")?.Value ?? string.Empty,
                    containerId,
                    Attribute(annotation, "textFormat"))
                {
                    Extras = ReadExtras(annotation),
                });
            }

            foreach (var group in container.Elements(BpmnXmlNames.Model + "group"))
            {
                var id = Attribute(group, "id");
                if (id is null) continue;

                _groups.Add(new BpmnGroup(
                    id,
                    containerId,
                    Attribute(group, "name"),
                    Attribute(group, "categoryValueRef"))
                {
                    Extras = ReadExtras(group),
                });
            }

            foreach (var association in container.Elements(BpmnXmlNames.Model + "association"))
            {
                var id = Attribute(association, "id");
                var source = Attribute(association, "sourceRef");
                var target = Attribute(association, "targetRef");
                if (id is null || source is null || target is null) continue;

                _associations.Add(new BpmnAssociation(
                    id,
                    source,
                    target,
                    containerId,
                    Attribute(association, "associationDirection") switch
                    {
                        "One" => BpmnAssociationDirection.One,
                        "Both" => BpmnAssociationDirection.Both,
                        _ => BpmnAssociationDirection.None,
                    })
                {
                    Extras = ReadExtras(association),
                });
            }
        }

        private BpmnCollaboration? ReadCollaboration()
        {
            var collaboration = definitions.Element(BpmnXmlNames.Model + "collaboration");
            if (collaboration is null) return null;

            var participants = new List<BpmnParticipant>();
            foreach (var participant in collaboration.Elements(BpmnXmlNames.Model + "participant"))
            {
                var id = Attribute(participant, "id");
                if (id is null) continue;

                // processRef is optional by schema: a black-box Participant is a valid Message
                // Flow endpoint with no internal content (section 9.1).
                participants.Add(new BpmnParticipant(
                    id,
                    Attribute(participant, "name"),
                    Attribute(participant, "processRef"))
                {
                    Extras = ReadExtras(participant),
                });
            }

            foreach (var flow in collaboration.Elements(BpmnXmlNames.Model + "messageFlow"))
            {
                var id = Attribute(flow, "id");
                var source = Attribute(flow, "sourceRef");
                var target = Attribute(flow, "targetRef");
                if (id is null || source is null || target is null) continue;

                _messageFlows.Add(new BpmnMessageFlow(
                    id,
                    source,
                    target,
                    Attribute(flow, "name"),
                    Attribute(flow, "messageRef"))
                {
                    Extras = ReadExtras(flow),
                });
            }

            return new BpmnCollaboration(
                Attribute(collaboration, "id") ?? "collaboration",
                Attribute(collaboration, "name"),
                participants)
            {
                Extras = ReadExtras(collaboration),
            };
        }

        /// <summary>
        /// Marks each Sequence Flow named by its source node's <c>default</c> attribute.
        /// </summary>
        /// <remarks>
        /// A projection, like Pool and Lane membership: BPMN puts the attribute on the Gateway or
        /// Activity, not on the flow, and the node stays authoritative. It is resolved at the end
        /// because a flow is usually declared after the node that defaults to it.
        /// </remarks>
        private List<BpmnSequenceFlow> ApplyDefaultFlows()
        {
            var defaults = _flowNodes
                .Select(node => node.DefaultFlowRef)
                .Where(reference => reference is not null)
                .ToHashSet(StringComparer.Ordinal);

            if (defaults.Count == 0) return _sequenceFlows;

            return _sequenceFlows
                .Select(flow => defaults.Contains(flow.Id) ? flow with { IsDefault = true } : flow)
                .ToList();
        }

        /// <summary>
        /// Stamps Pool and Lane membership onto flow nodes once both are known.
        /// </summary>
        /// <remarks>
        /// Both are projections. The Participant owns the Process reference and the Lane owns its
        /// <c>flowNodeRefs</c>; copying them onto the node is a lookup convenience for the
        /// renderer and the connection rules, and the originals stay authoritative.
        /// </remarks>
        private List<BpmnFlowNode> ApplyPoolAndLaneMembership(BpmnCollaboration? collaboration)
        {
            var poolOfProcess = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var participant in collaboration?.Participants ?? [])
            {
                if (participant.ProcessRef is { } processRef) poolOfProcess[processRef] = participant.Id;
            }

            var laneOfNode = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var lane in _lanes)
            {
                foreach (var nodeId in lane.FlowNodeRefs) laneOfNode[nodeId] = lane.Id;
            }

            return _flowNodes.ConvertAll(node =>
            {
                var rootProcess = _rootProcessOf.GetValueOrDefault(node.ContainerId, node.ContainerId);
                return node with
                {
                    PoolId = poolOfProcess.GetValueOrDefault(rootProcess),
                    LaneId = laneOfNode.GetValueOrDefault(node.Id),
                };
            });
        }

        /// <summary>
        /// Reads everything an element carries that the editor preserves without interpreting.
        /// </summary>
        /// <remarks>
        /// Foreign attributes are the easily-missed half. A vendor's <c>extensionElements</c> are
        /// obvious content sitting in an obvious container, but attributes like
        /// <c>zeebe:modelerTemplate</c> hide on elements a reader believes it fully understands,
        /// and are dropped without anyone noticing until a round trip loses them (section 10.5).
        /// </remarks>
        private BpmnElementExtras ReadExtras(XElement element)
        {
            var container = element.Element(BpmnXmlNames.Model + "extensionElements");

            var extensions = container is null
                ? []
                : container.Elements()
                    .Where(child => child.Name.Namespace != BpmnXmlNames.Model)
                    .Select(ReadExtensionElement)
                    .ToList();

            var foreign = element.Attributes()
                .Where(attribute => !attribute.IsNamespaceDeclaration && IsForeignNamespace(attribute.Name.Namespace))
                .ToDictionary(attribute => attribute.Name.ToString(), attribute => attribute.Value, StringComparer.Ordinal);

            var documentation = element.Element(BpmnXmlNames.Model + "documentation")?.Value;

            if (extensions.Count > 0 || foreign.Count > 0)
            {
                _diagnostics.Add(new BpmnImportDiagnostic(
                    BpmnImportSeverity.Info,
                    $"Retained {extensions.Count} vendor extension element(s) and {foreign.Count} foreign attribute(s) verbatim.",
                    Attribute(element, "id")));
            }

            if (extensions.Count == 0 && foreign.Count == 0 && string.IsNullOrWhiteSpace(documentation))
            {
                return BpmnElementExtras.None;
            }

            return new BpmnElementExtras
            {
                Documentation = string.IsNullOrWhiteSpace(documentation) ? null : documentation,
                ForeignAttributes = foreign,
                Extensions = extensions,
            };
        }

        /// <summary>
        /// True for a namespace this reader does not own.
        /// </summary>
        /// <remarks>
        /// The unnamespaced case is deliberately excluded: an unprefixed attribute belongs to the
        /// element's own type, and every BPMN attribute in the schema is unprefixed.
        /// </remarks>
        private static bool IsForeignNamespace(XNamespace ns) =>
            ns != XNamespace.None
            && ns != BpmnXmlNames.Model
            && ns != BpmnXmlNames.Diagram
            && ns != BpmnXmlNames.DiagramCommon
            && ns != BpmnXmlNames.DiagramInterchange;

        /// <summary>
        /// Reads the Event's <c>*EventDefinition</c> children with the references they carry.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="ReadEventDefinition"/>, which answers "what glyph". This one
        /// answers "what does it point at", and dropping it is how an export loses an
        /// <c>errorRef</c> while still drawing the right symbol.
        /// </remarks>
        private List<BpmnEventDefinition> ReadEventDefinitions(XElement element)
        {
            var definitions = new List<BpmnEventDefinition>();

            foreach (var child in element.Elements().Where(c => c.Name.Namespace == BpmnXmlNames.Model))
            {
                var type = BpmnXmlNames.ResolveEventDefinition(child.Name.LocalName);
                if (type is null) continue;

                var timer = BpmnXmlNames.TimerExpressions
                    .Select(name => child.Element(BpmnXmlNames.Model + name))
                    .FirstOrDefault(found => found is not null);

                var condition = child.Element(BpmnXmlNames.Model + "condition");
                var referenceAttribute = BpmnXmlNames.ReferenceAttributeFor(type.Value);

                definitions.Add(new BpmnEventDefinition(
                    type.Value,
                    Attribute(child, "id"),
                    referenceAttribute is null ? null : Attribute(child, referenceAttribute),
                    timer?.Value ?? condition?.Value,
                    timer?.Name.LocalName,
                    Attribute(child, "name"),
                    Attribute(child, "activityRef"),
                    BoolAttribute(child, "waitForCompletion"))
                {
                    Extras = ReadExtras(child),
                });
            }

            return definitions;
        }

        private static BpmnExtensionElement ReadExtensionElement(XElement element)
        {
            var attributes = element.Attributes()
                .Where(attribute => !attribute.IsNamespaceDeclaration)
                .ToDictionary(attribute => attribute.Name.ToString(), attribute => attribute.Value, StringComparer.Ordinal);

            var children = element.Elements().Select(ReadExtensionElement).ToList();

            // Text is only meaningful on a leaf; on a container it is whitespace between children.
            var text = children.Count == 0 && !string.IsNullOrWhiteSpace(element.Value) ? element.Value : null;

            return new BpmnExtensionElement(element.Name.NamespaceName, element.Name.LocalName, attributes, children, text);
        }

        // --- DI ---------------------------------------------------------------

        private void ReadDiagrams()
        {
            foreach (var diagram in definitions.Elements(BpmnXmlNames.Diagram + "BPMNDiagram"))
            {
                var plane = diagram.Element(BpmnXmlNames.Diagram + "BPMNPlane");
                if (plane is null)
                {
                    _diagnostics.Add(new BpmnImportDiagnostic(
                        BpmnImportSeverity.Dropped,
                        "A <BPMNDiagram> without a <BPMNPlane> has nothing to draw and was skipped.",
                        Attribute(diagram, "id")));
                    continue;
                }

                var shapes = new List<BpmnShape>();
                var edges = new List<BpmnEdge>();

                foreach (var shape in plane.Elements(BpmnXmlNames.Diagram + "BPMNShape"))
                {
                    if (ReadShape(shape) is { } read) shapes.Add(read);
                }

                foreach (var edge in plane.Elements(BpmnXmlNames.Diagram + "BPMNEdge"))
                {
                    if (ReadEdge(edge) is { } read) edges.Add(read);
                }

                _diagrams.Add(new BpmnDiagram(
                    Attribute(diagram, "id") ?? $"diagram_{_diagrams.Count.ToString(CultureInfo.InvariantCulture)}",
                    new BpmnPlane(
                        Attribute(plane, "id") ?? "plane",
                        Attribute(plane, "bpmnElement"),
                        shapes,
                        edges)
                    {
                        Extras = ReadDiagramExtras(plane),
                    },
                    Attribute(diagram, "name"))
                {
                    LabelStyles = ReadLabelStyles(diagram),
                    Extras = ReadDiagramExtras(diagram),
                });
            }
        }

        private BpmnShape? ReadShape(XElement shape)
        {
            var element = Attribute(shape, "bpmnElement");
            var bounds = ReadBounds(shape.Element(BpmnXmlNames.DiagramCommon + "Bounds"));

            if (element is null || bounds is null)
            {
                _diagnostics.Add(new BpmnImportDiagnostic(
                    BpmnImportSeverity.Dropped,
                    "A <BPMNShape> without a bpmnElement reference or <Bounds> cannot be drawn and was skipped.",
                    Attribute(shape, "id")));
                return null;
            }

            return new BpmnShape(
                Attribute(shape, "id") ?? $"{element}_di",
                element,
                bounds.Value,
                BoolAttribute(shape, "isHorizontal"),
                BoolAttribute(shape, "isExpanded"),
                BoolAttribute(shape, "isMarkerVisible"),
                ReadLabel(shape))
            {
                Extras = ReadDiagramExtras(shape),
            };
        }

        private BpmnEdge? ReadEdge(XElement edge)
        {
            var element = Attribute(edge, "bpmnElement");
            if (element is null)
            {
                _diagnostics.Add(new BpmnImportDiagnostic(
                    BpmnImportSeverity.Dropped,
                    "A <BPMNEdge> without a bpmnElement reference cannot be drawn and was skipped.",
                    Attribute(edge, "id")));
                return null;
            }

            var waypoints = edge.Elements(BpmnXmlNames.DiagramInterchange + "waypoint")
                .Select(point => new DiPoint(
                    DoubleAttribute(point, "x") ?? 0d,
                    DoubleAttribute(point, "y") ?? 0d))
                .ToList();

            if (waypoints.Count < 2)
            {
                // Reported rather than repaired: synthesising a route here would silently replace
                // the author's geometry on the next save (D015, sections 10.6 and 11).
                _diagnostics.Add(new BpmnImportDiagnostic(
                    BpmnImportSeverity.Degraded,
                    $"Connector '{element}' declares fewer than two waypoints; its route will be derived from its endpoints.",
                    element));
            }
            else if (waypoints.Distinct().Count() == 1)
            {
                // Real exporters emit this: every waypoint at the same point, usually the origin,
                // while the endpoint shapes sit elsewhere. The geometry cannot describe a route,
                // so the renderer derives one and says so rather than drawing a zero-length line.
                _diagnostics.Add(new BpmnImportDiagnostic(
                    BpmnImportSeverity.Degraded,
                    $"Connector '{element}' declares every waypoint at the same point "
                    + $"({waypoints[0].X}, {waypoints[0].Y}); its route will be derived from its endpoints.",
                    element));
            }

            return new BpmnEdge(Attribute(edge, "id") ?? $"{element}_di", element, waypoints, ReadLabel(edge))
            {
                Extras = ReadDiagramExtras(edge),
            };
        }

        private static BpmnLabel? ReadLabel(XElement owner)
        {
            var label = owner.Element(BpmnXmlNames.Diagram + "BPMNLabel");
            if (label is null) return null;

            var bounds = ReadBounds(label.Element(BpmnXmlNames.DiagramCommon + "Bounds"));

            // A BPMNLabel carries geometry and style only; the text lives on the semantic
            // element, so an empty label element just means "position it here".
            return new BpmnLabel(Attribute(label, "id"), bounds, Attribute(label, "labelStyle"));
        }

        /// <summary>
        /// Reads a DI element's <c>di:extension</c> content.
        /// </summary>
        /// <remarks>
        /// Section 10.5 lists DI-specific extension content alongside semantic extensions, and it
        /// arrives through a different door: <c>di:extension</c> on the diagram element rather than
        /// <c>bpmn:extensionElements</c> on the semantic one.
        /// </remarks>
        private static BpmnElementExtras ReadDiagramExtras(XElement element)
        {
            var extension = element.Element(BpmnXmlNames.DiagramInterchange + "extension");

            var children = extension is null
                ? []
                : extension.Elements().Select(ReadExtensionElement).ToList();

            var foreign = element.Attributes()
                .Where(attribute => !attribute.IsNamespaceDeclaration && IsForeignNamespace(attribute.Name.Namespace))
                .ToDictionary(attribute => attribute.Name.ToString(), attribute => attribute.Value, StringComparer.Ordinal);

            if (children.Count == 0 && foreign.Count == 0) return BpmnElementExtras.None;

            return new BpmnElementExtras { ForeignAttributes = foreign, Extensions = children };
        }

        private static List<BpmnLabelStyle> ReadLabelStyles(XElement diagram) =>
            diagram.Elements(BpmnXmlNames.Diagram + "BPMNLabelStyle")
                .Select(style =>
                {
                    var font = style.Element(BpmnXmlNames.DiagramCommon + "Font");

                    return new BpmnLabelStyle(
                        Attribute(style, "id") ?? string.Empty,
                        font is null ? null : Attribute(font, "name"),
                        font is null ? null : DoubleAttribute(font, "size"),
                        font is null ? null : BoolAttribute(font, "isBold"),
                        font is null ? null : BoolAttribute(font, "isItalic"),
                        font is null ? null : BoolAttribute(font, "isUnderline"),
                        font is null ? null : BoolAttribute(font, "isStrikeThrough"));
                })
                .Where(style => style.Id.Length > 0)
                .ToList();

        private static Bounds? ReadBounds(XElement? bounds)
        {
            if (bounds is null) return null;

            var x = DoubleAttribute(bounds, "x");
            var y = DoubleAttribute(bounds, "y");
            var width = DoubleAttribute(bounds, "width");
            var height = DoubleAttribute(bounds, "height");

            if (x is null || y is null || width is null || height is null) return null;
            return new Bounds(x.Value, y.Value, width.Value, height.Value);
        }

        // --- attribute helpers ------------------------------------------------

        private static string? Attribute(XElement element, string name) => element.Attribute(name)?.Value;

        private static bool? BoolAttribute(XElement element, string name) =>
            bool.TryParse(element.Attribute(name)?.Value, out var value) ? value : null;

        // Only a finite number: NaN, Infinity and anything that overflows parse, and serializing one throws, so a layout that carries one is read as if it had none.
        private static double? DoubleAttribute(XElement element, string name) =>
            double.TryParse(
                element.Attribute(name)?.Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value)
            && double.IsFinite(value)
                ? value
                : null;
    }
}
