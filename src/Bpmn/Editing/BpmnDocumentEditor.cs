using System.Collections.Frozen;
using System.Globalization;

using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Model.Geometry;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>
/// Applies edit commands to a document.
/// </summary>
/// <remarks>
/// <para>
/// Pure and static: a command plus a document in, a new document out. Nothing here holds state, so
/// the same command applied to the same document always gives the same answer, and a rejected
/// command cannot have left anything half-changed.
/// </para>
/// <para>
/// It validates even though the canvas already did. The canvas has to, because it owns the live
/// geometry and must refuse a drag before it commits; this has to, because it is the model of
/// record and a command can also arrive from a host calling the API directly. The specification
/// makes the same argument for configuration, and for the same reason: neither side can assume the
/// other checked.
/// </para>
/// </remarks>
public static partial class BpmnDocumentEditor
{
    /// <summary>Applies a command with the built-in editing options.</summary>
    /// <param name="document">The document to edit.</param>
    /// <param name="command">The edit to apply.</param>
    /// <returns>The new document, or the original with the reasons it was refused.</returns>
    public static BpmnEditResult Apply(BpmnDocument document, BpmnEditCommand command) =>
        Apply(document, command, EditingOptions.Default);

    /// <summary>Applies a command.</summary>
    /// <param name="document">The document to edit.</param>
    /// <param name="command">The edit to apply.</param>
    /// <param name="options">Collision clearance and whether it is enforced.</param>
    /// <returns>The new document, or the original with the reasons it was refused.</returns>
    public static BpmnEditResult Apply(BpmnDocument document, BpmnEditCommand command, EditingOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(options);

        var result = ApplyCommand(document, command, options);

        if (!result.IsApplied || command is CompoundCommand or CreatePoolCommand) return result;

        result = RemoveOrphanedAnnotations(document, result);

        return FollowChangedShapes(document, command, result);
    }

    private static BpmnEditResult RemoveOrphanedAnnotations(BpmnDocument document, BpmnEditResult result)
    {
        // Connector reconciliation (for example a cross-Pool transfer) may delete a target flow
        // without going through DeleteElementsCommand.
        var orphanedNotes = document.TextAnnotations
            .Where(annotation => BpmnAnnotationAttachments.UniqueConnection(document, annotation.Id) is { } connection
                && result.Document.Find(BpmnAnnotationAttachments.OtherEnd(connection, annotation.Id)) is null
                && result.Document.Find(annotation.Id) is not null)
            .Select(annotation => annotation.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (orphanedNotes.Count > 0)
        {
            var diagnostics = result.Diagnostics.ToList();
            var removed = ExpandDeletion(result.Document, orphanedNotes, diagnostics);
            result = result with
            {
                Document = RemoveElements(result.Document, removed, []),
                Diagnostics = diagnostics,
                DeletedElementIds = removed.Concat(result.DeletedElementIds ?? FrozenSet<string>.Empty).ToFrozenSet(StringComparer.Ordinal),
            };
        }

        return result;
    }

    private static BpmnEditResult FollowChangedShapes(
        BpmnDocument document,
        BpmnEditCommand command,
        BpmnEditResult result)
    {
        // First-Lane wrapping preserves content routes, but connections to an existing Pool must
        // follow its enlarged outline.
        var preserveRoutes = command is CreateLaneCommand lane
            && !document.LaneSets.Any(set => set.ContainerId == lane.ContainerId && set.LaneIds.Count > 0);

        // Ends attached to a shape that changed size keep their place relative to its box first,
        // and only then are put on the outline: normalising alone would re-project an end onto the
        // nearest point, which for a side that has moved away can be a different side.
        var routed = command is ResizeShapeCommand { Routes: { Count: > 0 } routes }
            ? routes.Select(route => route.ElementId).ToHashSet(StringComparer.Ordinal)
            : null;

        var positioned = command is MoveShapesCommand moveCommand
            ? moveCommand.Moves.Select(move => move.ShapeId).ToHashSet(StringComparer.Ordinal) : null;
        var attached = ConnectorEndpoints.FollowAnnotationShapes(document, result.Document, positioned);
        var followed = ConnectorEndpoints.FollowResizedShapes(document, attached, routed);

        return result with
        {
            Document = ConnectorEndpoints.FollowFlowAttachments(
                preserveRoutes ? followed : ConnectorEndpoints.Normalize(followed), document, positionedAnnotations: positioned),
        };
    }

    private static BpmnEditResult ApplyCommand(BpmnDocument document, BpmnEditCommand command, EditingOptions options)
    {
        var geometryDiagramId = command switch
        {
            MoveShapesCommand move => move.DiagramId,
            ResizeShapeCommand resize => resize.DiagramId,
            CreateConnectorCommand create => create.DiagramId,
            ReconnectConnectorCommand reconnect => reconnect.DiagramId,
            UpdateWaypointsCommand waypoints => waypoints.DiagramId,
            _ => null,
        };
        if (geometryDiagramId is { } requestedDiagramId
            && !document.Diagrams.Any(diagram => diagram.Id == requestedDiagramId))
        {
            return BpmnEditResult.Reject(document, new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected, "CT-001", $"No diagram '{requestedDiagramId}' exists to edit."));
        }

        (string? DiagramId, string? ContainerId, string? ElementId) = command switch
        {
            CreateElementCommand create => (create.DiagramId, create.ContainerId, create.Id),
            CreateArtifactCommand create => (create.DiagramId, create.ContainerId, create.Id),
            CreateDataReferenceCommand create => (create.DiagramId, create.ContainerId, create.Id),
            CreateParticipantCommand create => (create.DiagramId, document.Collaboration?.Id, create.Id),
            // Pool/Lane bootstrap validates its destination after establishing the structural roots.
            _ => ((string?)null, (string?)null, (string?)null),
        };

        if (DiagramId is { } diagramId && ElementId is { } elementId
            && CreationDestinationProblem(document, diagramId, ContainerId, elementId) is { } problem)
        {
            return BpmnEditResult.Reject(document, problem);
        }

        return command switch
        {
            MoveShapesCommand move => ApplyMove(document, move, options),
            ResizeShapeCommand resize => ApplyResize(document, resize, options),
            MoveLaneSplitterCommand splitter => ApplyMoveLaneSplitter(document, splitter),
            CreateElementCommand create => ApplyCreateElement(document, create, options),
            CreateArtifactCommand artifact => ApplyCreateArtifact(document, artifact),
            CreateDataReferenceCommand data => ApplyCreateDataReference(document, data),
            ReparentElementCommand reparent => ApplyReparent(document, reparent, options),
            CreateConnectorCommand create => ApplyCreateConnector(document, create),
            PasteCommand paste => ApplyPaste(document, paste, options),
            ReconnectConnectorCommand reconnect => ApplyReconnect(document, reconnect),
            UpdateWaypointsCommand waypoints => ApplyWaypoints(document, waypoints),
            CreateProcessCommand process => ApplyCreateProcess(document, process),
            CreateParticipantCommand participant => ApplyCreateParticipant(document, participant, options),
            CreatePoolCommand pool => ApplyCreatePool(document, pool, options),
            CreateLaneCommand lane => ApplyCreateLane(document, lane, options),
            ExpandSubProcessCommand expand => ApplyExpand(document, expand, options),
            CollapseSubProcessCommand collapse => ApplyCollapse(document, collapse, options),
            UpdatePropertiesCommand properties => ApplyProperties(document, properties, options),
            SetExtensionValueCommand extension => ApplyExtensionValue(document, extension, options),
            SetLoopExtensionValueCommand extension => ApplyLoopExtensionValue(document, extension, options),
            ReplaceCamundaCollectionCommand camunda => ApplyCamundaCollection(document, camunda, options),
            ReplaceExtensionCollectionCommand collection => ApplyExtensionCollection(document, collection, options),
            AddExtensionElementCommand extension => ApplyAddExtension(document, extension, options),
            RemoveExtensionElementCommand extension => ApplyRemoveExtension(document, extension),
            SetForeignAttributeCommand extension => ApplyForeignAttribute(document, extension),
            ApplyElementTemplateCommand or SetTemplatePropertyCommand or UnlinkElementTemplateCommand or RemoveElementTemplateCommand or UpgradeElementTemplateCommand => ApplyTemplateCommand(document, command, options),
            RenameElementCommand rename => ApplyRename(document, rename),
            SwitchElementTypeCommand switchType => ApplySwitchType(document, switchType, options),
            DeleteElementsCommand delete => ApplyDelete(document, delete),
            CompoundCommand compound => ApplyCompound(document, compound, options),
            _ => BpmnEditResult.Reject(
                document,
                new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "EDIT-001",
                    $"'{command.GetType().Name}' is not a command this editor knows how to apply.")),
        };
    }

    /// <summary>
    /// Applies several commands as one.
    /// </summary>
    /// <remarks>
    /// Each step sees the result of the last, so a compound can move a shape and then delete
    /// something that depended on where it ended up. The first refusal abandons the whole thing and
    /// returns the document that went in, not the partial result.
    /// </remarks>
    private static BpmnEditResult ApplyCompound(
        BpmnDocument document,
        CompoundCommand compound,
        EditingOptions options)
    {
        var current = document;
        var diagnostics = new List<BpmnEditDiagnostic>();

        foreach (var step in compound.Commands)
        {
            var result = Apply(current, step, options);
            diagnostics.AddRange(result.Diagnostics);

            if (!result.IsApplied) return BpmnEditResult.Reject(document, [.. diagnostics]);

            current = result.Document;
        }

        return BpmnEditResult.Applied(current, diagnostics);
    }

    // --- move -----------------------------------------------------------------

    private static BpmnEditResult ApplyMove(
        BpmnDocument document,
        MoveShapesCommand command,
        EditingOptions options)
    {
        if (command.Moves.Count == 0)
        {
            return BpmnEditResult.Reject(
                document,
                new BpmnEditDiagnostic(BpmnEditSeverity.Rejected, "EDIT-010", "A move must name at least one shape."));
        }

        var geometry = CreationGeometry(document, command.DiagramId);
        var shapes = geometry.Diagrams
            .SelectMany(diagram => diagram.Plane.Shapes)
            .ToFrozenDictionary(shape => shape.Id, StringComparer.Ordinal);

        var requested = new Dictionary<string, Bounds>(StringComparer.Ordinal);

        foreach (var move in command.Moves)
        {
            if (!shapes.TryGetValue(move.ShapeId, out _))
            {
                return BpmnEditResult.Reject(
                    document,
                    new BpmnEditDiagnostic(
                        BpmnEditSeverity.Rejected,
                        "EDIT-011",
                        $"No shape '{move.ShapeId}' exists on any plane.",
                        move.ShapeId));
            }

            if (Invalid(move.Bounds) is { } invalid)
            {
                return BpmnEditResult.Reject(document, invalid with { ElementId = move.ShapeId });
            }

            // Last writer wins rather than rejecting, because a drag that reports the same shape
            // twice is describing one final position, not two conflicting ones.
            requested[move.ShapeId] = move.Bounds;
        }

        if (LaneMovedAlone(geometry, shapes, requested) is { } stranded)
        {
            return BpmnEditResult.Reject(document, stranded);
        }

        // Include shape-owned notes before transfer and enclosure calculations. Explicit note
        // geometry always wins, including a deliberately unchanged position.
        var carriedAnnotations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var annotation in document.TextAnnotations)
        {
            if (BpmnAnnotationAttachments.UniqueConnection(document, annotation.Id) is not { } connection) continue;
            var note = shapes.Values.FirstOrDefault(shape => shape.BpmnElement == annotation.Id);
            var targetId = BpmnAnnotationAttachments.OtherEnd(connection, annotation.Id);
            var target = shapes.Values.FirstOrDefault(shape => shape.BpmnElement == targetId);
            if (note is null || target is null || requested.ContainsKey(note.Id)
                || !requested.TryGetValue(target.Id, out var bounds)
                || bounds.Width != target.Bounds.Width || bounds.Height != target.Bounds.Height) continue;
            requested[note.Id] = note.Bounds with
            {
                X = note.Bounds.X + bounds.X - target.Bounds.X,
                Y = note.Bounds.Y + bounds.Y - target.Bounds.Y,
            };
            carriedAnnotations.Add(annotation.Id);
        }

        var original = document;
        var asked = new Dictionary<string, Bounds>(requested, StringComparer.Ordinal);
        var diagnostics = new List<BpmnEditDiagnostic>();

        // Before expansion: content dropped in another Pool belongs to it, and must not grow the
        // Pool it left to reach the drop point.
        document = TransferAcrossPools(document, shapes, asked, carriedAnnotations, diagnostics, out var refused);
        if (refused is not null) return BpmnEditResult.Reject(original, refused);

        var grown = options.AutoExpandContainers
            ? ExpandContainers(CreationGeometry(document, command.DiagramId), shapes, requested, options)
            : 0;

        // After expansion, so a node dropped past the Pool's edge joins the edge Lane that grew to
        // hold it, rather than staying in a Lane it is nowhere near.
        var rejoined = JoinLanesUnderMovedNodes(document, shapes, asked, requested, out var joined);

        if (options.PreventCollisions
            && FindCollision(rejoined, shapes, requested, options.CollisionClearance) is { } collision)
        {
            return BpmnEditResult.Reject(original, collision);
        }

        /*
         * Reported, not silent. A caller that applied the move itself — the canvas, which has
         * already dragged the cell — needs to know the result contains geometry it has not drawn,
         * or the grown container stays at its old size on screen for ever.
         */
        if (grown > 0)
        {
            diagnostics.Add(new BpmnEditDiagnostic(
                BpmnEditSeverity.Info,
                "EDIT-053",
                $"Grew {grown} container(s) to keep content that was dragged past the edge."));
        }

        if (joined > 0)
        {
            diagnostics.Add(new BpmnEditDiagnostic(
                BpmnEditSeverity.Info,
                "EDIT-055",
                $"Moved {joined} node(s) into the Lane they were dropped in."));
        }

        var moved = WithShapeBounds(rejoined, requested, command.DiagramId);

        /*
         * The connectors the drag pulled on, squared up again. Their ends followed the shape and
         * their bends did not, so without this every move leaves diagonal segments behind it — and
         * they are geometry the user has just changed, not geometry an import supplied, so
         * the rule (sections 10.6 and 11) about leaving a file's own waypoints alone does not reach them.
         */
        foreach (var route in command.Routes ?? [])
        {
            if (RouteProblem(route.Waypoints) is not null) continue;

            moved = WithWaypoints(moved, route.ElementId, route.Waypoints, command.DiagramId);
        }

        return BpmnEditResult.Applied(moved, diagnostics);
    }

    /// <summary>
    /// Grows a container that content has been dragged past the edge of.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Grow only, never shrink. A container that tightened around its content on every drag would
    /// make deliberate white space impossible to keep, and the user never asked for the container
    /// to change at all — this is the minimum needed to stop the drag producing a diagram whose
    /// geometry contradicts its own containment.
    /// </para>
    /// <para>
    /// Growing never moves anything else and never changes what any other element is inside. A
    /// Lane is not grown on its own, because every Lane boundary inside a Pool is shared with a
    /// sibling: moving one would put that sibling's content in a different Lane or drag it along.
    /// The Pool grows instead, and only the Lane at the edge it grew past takes up the new space —
    /// the inner boundaries stay exactly where they were (section 1.5.3).
    /// </para>
    /// </remarks>
    /// <param name="document">The document whose containers are checked.</param>
    /// <param name="shapes">The current shapes, by id.</param>
    /// <param name="requested">The bounds after the edit, by shape id; grown containers are written back.</param>
    /// <param name="options">The editing options supplying the expansion margin.</param>
    /// <returns>How many containers had to grow.</returns>
    private static int ExpandContainers(
        BpmnDocument document,
        FrozenDictionary<string, BpmnShape> shapes,
        Dictionary<string, Bounds> requested,
        EditingOptions options)
    {
        var expanded = new HashSet<string>(StringComparer.Ordinal);

        foreach (var container in shapes.Values)
        {
            var isPool = document.Find(container.BpmnElement) is BpmnParticipant;
            var isOwner = document.FindFlowNode(container.BpmnElement) is { FlowElementsOwned: true };
            if (!isPool && !isOwner) continue;

            // Only containers whose content actually moved. Recomputing every Pool on every drag
            // would grow one the user never touched, the moment an unrelated import left content
            // slightly outside it.
            var content = ContainedShapes(document, shapes, container.BpmnElement).ToList();
            if (!content.Exists(child => requested.ContainsKey(child.Id))) continue;

            var current = requested.GetValueOrDefault(container.Id, container.Bounds);
            var grown = current;

            // Only the moved content: an untouched shape that already sat within the margin of an
            // edge would otherwise grow the Pool on a drag somewhere else entirely.
            foreach (var child in content)
            {
                if (!requested.TryGetValue(child.Id, out var box)) continue;
                grown = Union(grown, Inflate(box, options.AutoExpandMargin));
            }

            if (grown != current)
            {
                requested[container.Id] = grown;
                expanded.Add(container.Id);
            }

            if (!isPool) continue;

            var horizontal = container.IsHorizontal != false;
            var lanes = BpmnLaneLayout.LanesOf(document, container.BpmnElement, horizontal);
            if (lanes.Count == 0) continue;
            if (grown == current && !lanes.Any(lane => requested.ContainsKey(lane.Id))) continue;

            var laneBounds = lanes
                .Select(lane => requested.GetValueOrDefault(lane.Id, lane.Bounds))
                .ToArray();

            // Fitted to the Pool as requested first — a no-op for Lanes that already tile it —
            // then only the outermost Lanes take the growth.
            var tiled = GrowEdgeLanes(BpmnLaneLayout.Retile(current, laneBounds, horizontal), current, grown, horizontal);

            for (var index = 0; index < lanes.Count; index++)
            {
                requested[lanes[index].Id] = tiled[index];
            }
        }

        return expanded.Count;
    }

    /// <summary>
    /// Re-fits a Pool's Lanes to the Pool's grown bounds without moving any inner Lane boundary.
    /// </summary>
    /// <remarks>
    /// Growth across the stacking axis stretches every Lane, which moves no boundary between them.
    /// Growth along it goes to the first Lane (growth before the Pool's start) or the last
    /// (growth past its end).
    /// </remarks>
    private static Bounds[] GrowEdgeLanes(
        IReadOnlyList<Bounds> lanes, Bounds before, Bounds after, bool horizontal)
    {
        var result = lanes.ToArray();
        var last = result.Length - 1;

        if (horizontal)
        {
            var start = before.Y - after.Y;
            var end = after.Y + after.Height - (before.Y + before.Height);

            for (var index = 0; index < result.Length; index++)
            {
                result[index] = result[index] with
                {
                    X = after.X + BpmnLaneLayout.HeaderSize,
                    Width = after.Width - BpmnLaneLayout.HeaderSize,
                };
            }

            result[0] = result[0] with { Y = result[0].Y - start, Height = result[0].Height + start };
            result[last] = result[last] with { Height = result[last].Height + end };
        }
        else
        {
            var start = before.X - after.X;
            var end = after.X + after.Width - (before.X + before.Width);

            for (var index = 0; index < result.Length; index++)
            {
                result[index] = result[index] with
                {
                    Y = after.Y + BpmnLaneLayout.HeaderSize,
                    Height = after.Height - BpmnLaneLayout.HeaderSize,
                };
            }

            result[0] = result[0] with { X = result[0].X - start, Width = result[0].Width + start };
            result[last] = result[last] with { Width = result[last].Width + end };
        }

        return result;
    }

    /// <summary>
    /// Membership follows the drop: a moved node whose centre lands in another Lane of its
    /// Process joins that Lane.
    /// </summary>
    /// <remarks>
    /// Judged against the Lanes' final bounds, so a node dropped past the Pool's edge joins the
    /// edge Lane that grew to hold it. A node whose centre is in no Lane keeps the one it had.
    /// </remarks>
    /// <param name="document">The document before the move.</param>
    /// <param name="shapes">The current shapes, by id.</param>
    /// <param name="asked">The bounds the caller asked for, before any container grew.</param>
    /// <param name="requested">The final bounds, including grown Pools and re-fitted Lanes.</param>
    /// <param name="joined">How many nodes changed Lane.</param>
    private static BpmnDocument JoinLanesUnderMovedNodes(
        BpmnDocument document,
        FrozenDictionary<string, BpmnShape> shapes,
        Dictionary<string, Bounds> asked,
        Dictionary<string, Bounds> requested,
        out int joined)
    {
        joined = 0;
        var next = document;

        // First shape wins, as FirstOrDefault did; built once instead of scanned per node and Lane.
        var shapeByElement = new Dictionary<string, BpmnShape>(shapes.Count, StringComparer.Ordinal);
        foreach (var shape in shapes.Values) shapeByElement.TryAdd(shape.BpmnElement, shape);

        foreach (var (shapeId, bounds) in asked)
        {
            if (next.FindFlowNode(shapes[shapeId].BpmnElement) is not { AttachedToRef: null } node) continue;

            var processLaneSets = next.LaneSets
                .Where(set => set.ContainerId == node.ContainerId)
                .Select(set => set.Id)
                .ToHashSet(StringComparer.Ordinal);
            if (processLaneSets.Count == 0) continue;

            // A Pool dragged as a whole carries its Lanes; where its nodes land says nothing
            // about membership, and Lanes a caller left out of the move are still at the old spot.
            var poolShape = next.Collaboration?.Participants
                .Where(participant => participant.ProcessRef == node.ContainerId)
                .Select(participant => shapeByElement.GetValueOrDefault(participant.Id))
                .FirstOrDefault(shape => shape is not null);
            if (poolShape is not null
                && asked.TryGetValue(poolShape.Id, out var poolMove)
                && poolMove != poolShape.Bounds) continue;

            var (X, Y) = (bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));

            var target = next.Lanes
                .Where(lane => processLaneSets.Contains(lane.LaneSetId))
                .Select(lane => (Lane: lane, Shape: shapeByElement.GetValueOrDefault(lane.Id)))
                .Where(entry => entry.Shape is not null)
                .Select(entry => (entry.Lane, Box: requested.GetValueOrDefault(entry.Shape!.Id, entry.Shape.Bounds)))
                .Where(entry => X >= entry.Box.X && X <= entry.Box.X + entry.Box.Width
                    && Y >= entry.Box.Y && Y <= entry.Box.Y + entry.Box.Height)
                .OrderBy(entry => entry.Box.Width * entry.Box.Height)
                .Select(entry => entry.Lane)
                .FirstOrDefault();

            if (target is null || target.FlowNodeRefs.Contains(node.Id)) continue;

            next = next with
            {
                FlowNodes = [.. next.FlowNodes.Select(candidate =>
                    candidate.Id == node.Id ? candidate with { LaneId = target.Id } : candidate)],
                Lanes = [.. next.Lanes.Select(lane => Remembering(lane, node.Id, target.Id))],
            };
            joined++;
        }

        return next;
    }

    /// <summary>The smallest rectangle covering both, which is all "grow to fit" means.</summary>
    private static Bounds Union(Bounds first, Bounds second)
    {
        var left = Math.Min(first.X, second.X);
        var top = Math.Min(first.Y, second.Y);
        var right = Math.Max(first.X + first.Width, second.X + second.Width);
        var bottom = Math.Max(first.Y + first.Height, second.Y + second.Height);

        return new Bounds(left, top, right - left, bottom - top);
    }

    private static Bounds Inflate(Bounds bounds, double margin) => new(
        bounds.X - margin,
        bounds.Y - margin,
        bounds.Width + (2 * margin),
        bounds.Height + (2 * margin));

    // --- resize ---------------------------------------------------------------

    /// <summary>
    /// Resizes a shape, with the consequences its geometry has for other shapes.
    /// </summary>
    /// <remarks>
    /// Three refusals, in order of how badly they would corrupt the document: bounds that are not
    /// drawable at all, a container shrunk off its own contents, and a Lane change no neighbour can
    /// absorb. Each leaves the document exactly as it was, which immutability gives for free.
    /// </remarks>
    private static BpmnEditResult ApplyResize(
        BpmnDocument document,
        ResizeShapeCommand command,
        EditingOptions options)
    {
        var geometry = CreationGeometry(document, command.DiagramId);
        var shapes = geometry.Diagrams
            .SelectMany(diagram => diagram.Plane.Shapes)
            .ToFrozenDictionary(shape => shape.Id, StringComparer.Ordinal);

        if (!shapes.TryGetValue(command.ShapeId, out var shape))
        {
            return BpmnEditResult.Reject(
                document,
                new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "EDIT-011",
                    $"No shape '{command.ShapeId}' exists on any plane.",
                    command.ShapeId));
        }

        if (Invalid(command.Bounds) is { } invalid)
        {
            return BpmnEditResult.Reject(document, invalid with { ElementId = command.ShapeId });
        }

        if (command.Bounds.Width < MinimumSize || command.Bounds.Height < MinimumSize)
        {
            return BpmnEditResult.Reject(
                document,
                new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "EDIT-050",
                    $"A shape may not be smaller than {MinimumSize} units.",
                    shape.BpmnElement));
        }

        var requested = new Dictionary<string, Bounds>(StringComparer.Ordinal)
        {
            [shape.Id] = command.Bounds,
        };

        if (SpreadResize(geometry, shape, command.Bounds, requested) is { } refusal)
        {
            return BpmnEditResult.Reject(document, refusal);
        }

        if (EnclosedContentEscapes(geometry, shapes, shape, command.Bounds) is { } escape)
        {
            return BpmnEditResult.Reject(document, escape);
        }

        if (options.PreventCollisions
            && FindCollision(document, shapes, requested, options.CollisionClearance) is { } collision)
        {
            return BpmnEditResult.Reject(document, collision);
        }

        var resized = WithShapeBounds(document, requested, command.DiagramId);

        // The flows attached to the shape, routed by the canvas against its new size.
        foreach (var route in command.Routes ?? [])
        {
            if (RouteProblem(route.Waypoints) is not null) continue;

            resized = WithWaypoints(resized, route.ElementId, route.Waypoints, command.DiagramId);
        }

        return BpmnEditResult.Applied(resized);
    }

    /// <summary>
    /// Finds a Lane that is moved without its Pool.
    /// </summary>
    /// <remarks>
    /// A Lane fills its Pool, so it has no position of its own to change: it travels with the Pool,
    /// which reports its Lanes in the same move. A Lane on its own would leave the Pool and its
    /// Lanes describing different rectangles.
    /// </remarks>
    private static BpmnEditDiagnostic? LaneMovedAlone(
        BpmnDocument document,
        FrozenDictionary<string, BpmnShape> shapes,
        Dictionary<string, Bounds> requested)
    {
        foreach (var (shapeId, bounds) in requested)
        {
            var shape = shapes[shapeId];
            if (document.Find(shape.BpmnElement) is not BpmnLane) continue;
            if (shape.Bounds == bounds) continue;

            var pool = BpmnLaneLayout.PoolOfLane(document, shape.BpmnElement);
            var poolShape = pool is null ? null : ShapeFor(document, pool.Id);

            if (poolShape is not null && requested.ContainsKey(poolShape.Id)) continue;

            return new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "EDIT-054",
                "A Lane fills its Pool and moves only with it.",
                shape.BpmnElement);
        }

        return null;
    }

    /// <summary>The smallest either dimension of any shape may become.</summary>
    private const double MinimumSize = 20;

    /// <summary>
    /// Adds the shapes that have to move because this one changed size.
    /// </summary>
    /// <remarks>
    /// Only Pools and Lanes have such shapes, and only because section 1.5.3 makes their geometry
    /// one fact expressed twice. A Sub-Process's children do not move when it is resized — they
    /// keep their positions and the container simply has to remain big enough for them, which is
    /// what the containment check downstream is for.
    /// </remarks>
    private static BpmnEditDiagnostic? SpreadResize(
        BpmnDocument document,
        BpmnShape shape,
        Bounds bounds,
        Dictionary<string, Bounds> requested)
    {
        if (document.Find(shape.BpmnElement) is BpmnParticipant participant)
        {
            var horizontal = shape.IsHorizontal != false;
            var lanes = BpmnLaneLayout.LanesOf(document, participant.Id, horizontal);
            var tiled = BpmnLaneLayout.Retile(bounds, [.. lanes.Select(lane => lane.Bounds)], horizontal);

            for (var index = 0; index < lanes.Count; index++)
            {
                requested[lanes[index].Id] = tiled[index];
            }

            return null;
        }

        // A Lane always fills its Pool, so it is never resized on its own: the splitter between
        // two Lanes is the only thing that changes how the Pool is shared out.
        if (document.Find(shape.BpmnElement) is not BpmnLane) return null;

        return new BpmnEditDiagnostic(
            BpmnEditSeverity.Rejected,
            "EDIT-051",
            "A Lane fills its Pool and is sized with the splitter between Lanes, not on its own.",
            shape.BpmnElement);
    }

    /// <summary>
    /// Moves the splitter between a Lane and the one after it, leaving the Pool as it was.
    /// </summary>
    private static BpmnEditResult ApplyMoveLaneSplitter(
        BpmnDocument document,
        MoveLaneSplitterCommand command)
    {
        var pool = BpmnLaneLayout.PoolOfLane(document, command.UpperLaneId);

        if (pool is null || !double.IsFinite(command.Position))
        {
            return BpmnEditResult.Reject(
                document,
                new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "EDIT-053",
                    $"'{command.UpperLaneId}' is not a Lane of a Pool, so it has no splitter.",
                    command.UpperLaneId));
        }

        var isHorizontal = ShapeFor(document, pool.Id)?.IsHorizontal != false;
        var lanes = BpmnLaneLayout.LanesOf(document, pool.Id, isHorizontal);
        var boundary = lanes.ToList().FindIndex(lane => lane.BpmnElement == command.UpperLaneId);

        var moved = BpmnLaneLayout.MoveSplitter(
            [.. lanes.Select(lane => lane.Bounds)],
            boundary,
            command.Position,
            isHorizontal);

        if (moved is null)
        {
            return BpmnEditResult.Reject(
                document,
                new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "EDIT-051",
                    $"A Lane may not be narrower than {BpmnLaneLayout.MinimumLaneExtent} units, and the last Lane has no splitter after it.",
                    command.UpperLaneId));
        }

        var requested = new Dictionary<string, Bounds>(StringComparer.Ordinal);

        for (var index = 0; index < lanes.Count; index++)
        {
            requested[lanes[index].Id] = moved[index];
        }

        return BpmnEditResult.Applied(WithShapeBounds(document, requested));
    }

    /// <summary>
    /// Whether shrinking a container would leave its content outside it.
    /// </summary>
    /// <remarks>
    /// Refused rather than quietly dragging the content along. A container's children have
    /// positions the user chose, and section 11 has geometry diagnosed rather than silently rewritten — moving
    /// them to accommodate a resize would discard a decision nobody asked to change. Growing is
    /// always allowed; only shrinking can strand anything.
    /// </remarks>
    private static BpmnEditDiagnostic? EnclosedContentEscapes(
        BpmnDocument document,
        FrozenDictionary<string, BpmnShape> shapes,
        BpmnShape shape,
        Bounds bounds)
    {
        var contained = ContainedShapes(document, shapes, shape.BpmnElement).ToList();
        if (contained.Count == 0) return null;

        foreach (var child in contained)
        {
            var box = child.Bounds;
            var inside = box.X >= bounds.X
                && box.Y >= bounds.Y
                && box.X + box.Width <= bounds.X + bounds.Width
                && box.Y + box.Height <= bounds.Y + bounds.Height;

            if (inside) continue;

            return new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "EDIT-052",
                $"'{child.BpmnElement}' would be left outside '{shape.BpmnElement}'.",
                shape.BpmnElement);
        }

        return null;
    }

    /// <summary>The shapes a container draws inside itself.</summary>
    /// <remarks>
    /// Only shapes on the same plane as the container, in the same coordinates. A Sub-Process's
    /// children are drawn on a plane of their own when it is collapsed, and that plane has its own
    /// origin, so comparing them with the collapsed box on the parent plane finds content "outside"
    /// a container it is nowhere near. A collapsed container draws nothing inside itself, whatever
    /// DI happens to be present.
    /// </remarks>
    private static IEnumerable<BpmnShape> ContainedShapes(
        BpmnDocument document,
        FrozenDictionary<string, BpmnShape> shapes,
        string containerId)
    {
        var owner = document.Find(containerId) switch
        {
            BpmnParticipant participant => participant.ProcessRef,
            BpmnLane ownerLane => ownerLane.Id,
            BpmnFlowNode { FlowElementsOwned: true } node => node.Id,
            _ => null,
        };

        if (owner is null) yield break;

        var laneMembers = document.Find(containerId) is BpmnLane lane
            ? lane.FlowNodeRefs.ToHashSet(StringComparer.Ordinal)
            : [];

        foreach (var plane in document.Diagrams.Select(diagram => diagram.Plane))
        {
            var container = plane.Shapes.FirstOrDefault(shape => shape.BpmnElement == containerId);
            if (container is null || container.IsExpanded == false) continue;

            foreach (var candidate in plane.Shapes)
            {
                // A Boundary Event sits on its host's outline by DI-010, which puts it half outside
                // any container the host is flush against. It is not content that can be stranded.
                if (document.FindFlowNode(candidate.BpmnElement) is { AttachedToRef: not null }) continue;

                // The current geometry, from the same dictionary every caller resolved requests against.
                var element = document.Find(candidate.BpmnElement);
                var isOwned = laneMembers.Contains(candidate.BpmnElement)
                    || element switch
                    {
                        BpmnFlowNode node => node.ContainerId == owner,
                        BpmnTextAnnotation annotation => annotation.ContainerId == owner,
                        BpmnGroup group => group.ContainerId == owner,
                        BpmnDataObjectReference reference => reference.ContainerId == owner,
                        BpmnDataStoreReference reference => reference.ContainerId == owner,
                        _ => false,
                    };

                if (isOwned && shapes.TryGetValue(candidate.Id, out var current))
                {
                    yield return current;
                }
            }
        }
    }

    /// <summary>DI-002 and DI-003: bounds must be finite and have positive area.</summary>
    private static BpmnEditDiagnostic? Invalid(Bounds bounds)
    {
        if (!double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y)
            || !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height))
        {
            return new BpmnEditDiagnostic(BpmnEditSeverity.Rejected, "DI-003", "Bounds must be finite.");
        }

        return bounds.IsPositive
            ? null
            : new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "DI-002",
                "Bounds must have positive width and height.");
    }

    /// <summary>
    /// DI-011: two peer Flow Nodes may not overlap once the configured clearance is applied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Peers only. Two nodes in different Pools or different Sub-Processes are not laid out against
    /// each other, and a node overlapping the container it lives in is the normal case — checking
    /// either would make ordinary diagrams unbuildable.
    /// </para>
    /// <para>
    /// Boundary Events are exempt by definition: DI-010 requires one to sit <em>on</em> its host's
    /// outline, which is an overlap.
    /// </para>
    /// </remarks>
    private static BpmnEditDiagnostic? FindCollision(
        BpmnDocument document,
        FrozenDictionary<string, BpmnShape> shapes,
        Dictionary<string, Bounds> requested,
        double clearance)
    {
        var nodesById = document.FlowNodes.ToFrozenDictionary(node => node.Id, StringComparer.Ordinal);

        // Effective geometry: the requested bounds where a shape is moving, its current bounds
        // otherwise, so a move is checked against where everything will actually be.
        var geometry = new List<(BpmnFlowNode Node, Bounds Bounds)>();

        foreach (var shape in shapes.Values)
        {
            if (!nodesById.TryGetValue(shape.BpmnElement, out var node)) continue;
            if (!ParticipatesInCollisions(node)) continue;

            geometry.Add((node, requested.GetValueOrDefault(shape.Id, shape.Bounds)));
        }

        var moving = shapes.Values
            .Where(shape => requested.ContainsKey(shape.Id))
            .Select(shape => shape.BpmnElement)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (node, bounds) in geometry.Where(entry => moving.Contains(entry.Node.Id)))
        {
            foreach (var (peer, peerBounds) in geometry)
            {
                if (peer.Id == node.Id || peer.ContainerId != node.ContainerId) continue;

                // Both moving: compared once, by taking only the pair in id order.
                if (moving.Contains(peer.Id) && string.CompareOrdinal(peer.Id, node.Id) < 0) continue;

                if (!Overlaps(bounds, peerBounds, clearance)) continue;

                return new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "DI-011",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"'{node.Id}' would overlap '{peer.Id}' with the configured clearance of {clearance}."),
                    node.Id);
            }
        }

        return null;
    }

    internal static bool ParticipatesInCollisions(BpmnFlowNode node) =>
        node.Type != BpmnFlowNodeType.BoundaryEvent && !node.FlowElementsOwned;

    /// <summary>Positive-area overlap after growing both rectangles by half the clearance each.</summary>
    internal static bool Overlaps(Bounds first, Bounds second, double clearance)
    {
        var pad = clearance / 2d;

        return first.X - pad < second.X + second.Width + pad
            && second.X - pad < first.X + first.Width + pad
            && first.Y - pad < second.Y + second.Height + pad
            && second.Y - pad < first.Y + first.Height + pad;
    }

    private static BpmnDocument WithShapeBounds(
        BpmnDocument document, Dictionary<string, Bounds> requested, string? diagramId = null) =>
        document with
        {
            Diagrams = [.. document.Diagrams.Select(diagram => diagramId is not null && diagram.Id != diagramId
                ? diagram : diagram with
            {
                Plane = diagram.Plane with
                {
                    Shapes = [.. diagram.Plane.Shapes.Select(shape =>
                        requested.TryGetValue(shape.Id, out var bounds) ? shape with { Bounds = bounds } : shape)],
                },
            })],
        };


    // --- create ---------------------------------------------------------------

    /// <summary>
    /// Creates a flow node and its shape in one transaction (section 9.3).
    /// </summary>
    /// <remarks>
    /// Everything is validated before anything is built, so a refusal leaves no partial semantic
    /// object and no orphaned DI. That is section 9.3's requirement, and immutability is what makes
    /// it free rather than something to unwind.
    /// </remarks>
    private static BpmnEditResult ApplyCreateElement(
        BpmnDocument document,
        CreateElementCommand command,
        EditingOptions options)
    {
        if (document.Find(command.Id) is not null)
        {
            return BpmnEditResult.Reject(
                document,
                new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "EDIT-030",
                    $"'{command.Id}' is already used by another element.",
                    command.Id));
        }

        if (Invalid(command.Bounds) is { } invalidBounds)
        {
            return BpmnEditResult.Reject(document, invalidBounds with { ElementId = command.Id });
        }

        // A Flow Node must belong to exactly one flow-elements container before it can take part
        // in Sequence Flow (section 9.3), so an unknown container is a refusal rather than a
        // container invented on the spot.
        var containerExists = document.Processes.Any(process => process.Id == command.ContainerId)
            || document.FlowNodes.Any(node => node.Id == command.ContainerId && node.FlowElementsOwned);

        if (!containerExists)
        {
            return BpmnEditResult.Reject(
                document,
                new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "CT-001",
                    $"'{command.ContainerId}' is not a Process or an embedded Sub-Process in this document.",
                    command.Id));
        }

        // A Boundary Event cannot exist free-floating; creation sets attachedToRef immediately.
        if (command.Type == BpmnFlowNodeType.BoundaryEvent)
        {
            var host = command.AttachedToRef is null ? null : document.FindFlowNode(command.AttachedToRef);

            if (host is null || !IsActivity(host.Type))
            {
                return BpmnEditResult.Reject(
                    document,
                    new BpmnEditDiagnostic(
                        BpmnEditSeverity.Rejected,
                        "EV-001",
                        "A Boundary Event must be attached to an Activity.",
                        command.Id));
            }
        }
        else if (command.AttachedToRef is not null)
        {
            return BpmnEditResult.Reject(
                document,
                new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "EV-002",
                    "Only a Boundary Event may be attached to an Activity.",
                    command.Id));
        }

        /*
         * CT-010. A Call Activity is an opaque Activity whose whole content is the definition it
         * invokes, so creating one without naming that definition would produce an Activity that
         * calls nothing — and section 9.1 forbids inventing a hidden embedded graph to fill the
         * gap. The caller picks first; the command refuses rather than guessing.
         */
        if (command.Type == BpmnFlowNodeType.CallActivity
            && CalledElementProblem(document, command.CalledElementRef) is { } uncallable)
        {
            return BpmnEditResult.Reject(document, uncallable with { ElementId = command.Id });
        }

        if (command.Type != BpmnFlowNodeType.CallActivity && command.CalledElementRef is not null)
        {
            return BpmnEditResult.Reject(
                document,
                new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "CT-010",
                    "Only a Call Activity may reference a called element.",
                    command.Id));
        }

        var shapeId = command.ShapeId ?? BpmnId.ForDiagramElement(command.Id);
        var ownsContent = command.Type is BpmnFlowNodeType.SubProcess
            or BpmnFlowNodeType.EventSubProcess
            or BpmnFlowNodeType.Transaction
            or BpmnFlowNodeType.AdHocSubProcess;

        var node = new BpmnFlowNode(
            command.Id,
            command.Type,
            command.ContainerId,
            command.Name,
            command.EventDefinition,
            command.IsInterrupting,
            AttachedToRef: command.AttachedToRef,
            PoolId: command.PoolId,
            CalledElementRef: command.CalledElementRef,
            FlowElementsOwned: ownsContent);

        /*
         * An embedded container is created expanded, and says so.
         *
         * The size it is created at already assumes it: a Sub-Process is dropped at 350x200, which
         * is a container with room inside, not the 100x80 leaf a collapsed one draws as. Leaving
         * isExpanded unstated made it neither — the renderer read the silence as collapsed and drew
         * the plus marker and a centred label on a rectangle three times too big, while the stencil
         * read it as expanded and offered to create inside something whose inside was not drawn.
         *
         * Stated rather than left to a default, so a file this editor writes cannot be read back
         * differently by a tool that defaults the other way.
         */
        var created = document with
        {
            FlowNodes = [.. document.FlowNodes, node],
            Diagrams = WithShape(
                document,
                new BpmnShape(shapeId, command.Id, command.Bounds, IsExpanded: ownsContent ? true : null),
                command.DiagramId),
        };

        var diagnostics = new List<BpmnEditDiagnostic>();
        if (BpmnElementInspector.CategoryOf(node.Type) == BpmnElementCategory.Event)
        {
            var legalNode = ApplyEventLegality(created, node, options, diagnostics, out var eventProblem);
            if (eventProblem is not null) return BpmnEditResult.Reject(document, eventProblem);
            node = legalNode!;
            created = created with
            {
                FlowNodes = [.. created.FlowNodes.Select(candidate => candidate.Id == node.Id ? node : candidate)],
            };
        }

        // DI-011 is checked against the document as it would be, not as it is: a drop that lands on
        // a peer is rejected and leaves nothing behind (section 9.3).
        if (options.PreventCollisions)
        {
            var shapes = CreationGeometry(created, command.DiagramId).Diagrams
                .SelectMany(diagram => diagram.Plane.Shapes)
                .ToFrozenDictionary(shape => shape.Id, StringComparer.Ordinal);

            var requested = new Dictionary<string, Bounds>(StringComparer.Ordinal) { [shapeId] = command.Bounds };

            if (FindCollision(created, shapes, requested, options.CollisionClearance) is { } collision)
            {
                return BpmnEditResult.Reject(document, collision);
            }
        }

        return BpmnEditResult.Applied(created, diagnostics);
    }

    /// <summary>
    /// Whether a Call Activity's target is one it may invoke.
    /// </summary>
    /// <remarks>
    /// A reusable global Process or a Global Task, and nothing else (section 9.1). Pointing at a
    /// Task in the same diagram, or at a Message, would produce a document no engine can resolve.
    /// </remarks>
    private static BpmnEditDiagnostic? CalledElementProblem(BpmnDocument document, string? calledElementRef)
    {
        if (calledElementRef is null)
        {
            return new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "CT-010",
                "A Call Activity must name the global Process or Global Task it calls.");
        }

        var callable = document.Find(calledElementRef) is BpmnProcess or BpmnGlobalTask;

        return callable
            ? null
            : new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "CT-010",
                $"'{calledElementRef}' is not a global Process or Global Task.");
    }

    /// <summary>
    /// The definitions a Call Activity may call.
    /// </summary>
    /// <remarks>
    /// Offered to the caller so a picker can be built from the document rather than from a guess.
    /// A Process that a Participant already draws as a Pool is still callable — BPMN does not
    /// forbid it — so nothing is filtered out here beyond what the type rules exclude.
    /// </remarks>
    /// <param name="document">The document being edited.</param>
    /// <returns>Each candidate's id and display name, in document order.</returns>
    public static IReadOnlyList<(string Id, string? Name)> CallableDefinitions(BpmnDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return
        [
            .. document.Processes.Select(process => (process.Id, process.Name)),
            .. document.Catalog.GlobalTasks.Select(task => (task.Id, task.Name)),
        ];
    }

    private static bool IsActivity(BpmnFlowNodeType type) => type is BpmnFlowNodeType.Task
        or BpmnFlowNodeType.UserTask or BpmnFlowNodeType.ServiceTask or BpmnFlowNodeType.ScriptTask
        or BpmnFlowNodeType.ManualTask or BpmnFlowNodeType.BusinessRuleTask or BpmnFlowNodeType.SendTask
        or BpmnFlowNodeType.ReceiveTask or BpmnFlowNodeType.SubProcess or BpmnFlowNodeType.EventSubProcess
        or BpmnFlowNodeType.Transaction or BpmnFlowNodeType.AdHocSubProcess or BpmnFlowNodeType.CallActivity;

    /// <summary>
    /// Creates a connector and its DI edge.
    /// </summary>
    /// <remarks>
    /// The section 5.1 matrix is evaluated on the canvas against the shared rule table before the
    /// gesture is reported, because a drag has to be judged at pointer speed. What is checked here
    /// is what can be checked without duplicating those predicates — re-implementing them in C#
    /// is the drift the shared table exists to prevent.
    /// </remarks>
    private static BpmnEditResult ApplyCreateConnector(BpmnDocument document, CreateConnectorCommand command)
    {
        if (document.Find(command.Id) is not null)
        {
            return BpmnEditResult.Reject(
                document,
                new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "EDIT-030",
                    $"'{command.Id}' is already used by another element.",
                    command.Id));
        }

        foreach (var endpoint in (string[])[command.SourceRef, command.TargetRef])
        {
            if (document.Find(endpoint) is not null) continue;

            return BpmnEditResult.Reject(
                document,
                new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "EDIT-031",
                    $"No element '{endpoint}' exists to connect.",
                    command.Id));
        }

        if (string.Equals(command.SourceRef, command.TargetRef, StringComparison.Ordinal))
        {
            return BpmnEditResult.Reject(
                document,
                new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "EDIT-032",
                    "A connector cannot start and end at the same element.",
                    command.Id));
        }

        // DI-001: BPMNDI requires at least two waypoints, and a connector with fewer cannot be
        // drawn or round-tripped.
        if (command.Waypoints.Count < 2)
        {
            return BpmnEditResult.Reject(
                document,
                new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "DI-001",
                    "A connector needs at least two waypoints.",
                    command.Id));
        }

        foreach (var point in command.Waypoints)
        {
            if (double.IsFinite(point.X) && double.IsFinite(point.Y)) continue;

            return BpmnEditResult.Reject(
                document,
                new BpmnEditDiagnostic(BpmnEditSeverity.Rejected, "DI-003", "Waypoints must be finite.", command.Id));
        }

        if (AnnotationConnectionProblem(document, command.Kind, command.SourceRef, command.TargetRef, command.Id) is { } connectionProblem)
        {
            return BpmnEditResult.Reject(document, connectionProblem);
        }

        var edgeId = command.EdgeId ?? BpmnId.ForDiagramElement(command.Id);
        var container = command.ContainerId ?? (document.Find(command.SourceRef) switch
        {
            BpmnFlowNode node => node.ContainerId,
            BpmnTextAnnotation annotation => annotation.ContainerId,
            BpmnSequenceFlow flow => flow.ContainerId,
            BpmnDataObjectReference reference => reference.ContainerId,
            BpmnDataStoreReference reference => reference.ContainerId,
            _ => command.Kind == BpmnConnectorKind.Association
                && document.Find(command.TargetRef) is BpmnTextAnnotation targetAnnotation ? targetAnnotation.ContainerId : null,
        });

        if (command.DiagramId is { } diagramId)
        {
            var owner = command.Kind == BpmnConnectorKind.MessageFlow ? document.Collaboration?.Id : container;
            if (CreationDestinationProblem(document, diagramId, owner, command.Id) is { } problem)
            {
                return BpmnEditResult.Reject(document, problem);
            }

            var geometry = CreationGeometry(document, diagramId);
            foreach (var endpoint in (string[])[command.SourceRef, command.TargetRef])
            {
                if (DrawsEndpoint(geometry, endpoint, command.Kind)) continue;

                return BpmnEditResult.Reject(document, new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected, "EDIT-031",
                    $"No shape or permitted flow draws endpoint '{endpoint}' on diagram '{diagramId}'.", command.Id));
            }
        }

        var withConnector = command.Kind switch
        {
            BpmnConnectorKind.SequenceFlow when container is not null => document with
            {
                SequenceFlows = [.. document.SequenceFlows,
                    new BpmnSequenceFlow(command.Id, command.SourceRef, command.TargetRef, container)],
            },
            BpmnConnectorKind.MessageFlow => document with
            {
                MessageFlows = [.. document.MessageFlows,
                    new BpmnMessageFlow(command.Id, command.SourceRef, command.TargetRef)],
            },
            BpmnConnectorKind.Association when container is not null => document with
            {
                Associations = [.. document.Associations,
                    new BpmnAssociation(command.Id, command.SourceRef, command.TargetRef, container)],
            },
            BpmnConnectorKind.DataAssociation when container is not null => document with
            {
                DataAssociations = [.. document.DataAssociations, DataAssociationFor(document, command, container)],
            },
            _ => null,
        };

        if (withConnector is null)
        {
            return BpmnEditResult.Reject(
                document,
                new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "EDIT-033",
                    $"A {command.Kind} needs an owning container, and none was given or could be derived.",
                    command.Id));
        }

        return BpmnEditResult.Applied(withConnector with
        {
            Diagrams = WithEdge(withConnector, new BpmnEdge(edgeId, command.Id,
                BpmnAnnotationAttachments.IsAnnotationAssociation(withConnector, command.Id)
                    ? [command.Waypoints[0], command.Waypoints[^1]] : command.Waypoints), command.DiagramId),
        });
    }

    /// <summary>
    /// Works out which way a Data Association runs.
    /// </summary>
    /// <remarks>
    /// Data into an Activity is an Input Association and data out of one is an Output Association
    /// (section 2.9); the same gesture means different things depending on which end the data
    /// reference is.
    /// </remarks>
    private static BpmnDataAssociation DataAssociationFor(
        BpmnDocument document,
        CreateConnectorCommand command,
        string container)
    {
        var sourceIsData = document.Find(command.SourceRef) is BpmnDataObjectReference or BpmnDataStoreReference;

        return sourceIsData
            ? new BpmnDataAssociation(
                command.Id,
                BpmnDataAssociationKind.Input,
                [command.SourceRef],
                command.TargetRef,
                container)
            : new BpmnDataAssociation(
                command.Id,
                BpmnDataAssociationKind.Output,
                [command.SourceRef],
                command.TargetRef,
                container);
    }

    /// <summary>Adds a shape to the requested plane, or the first declared one when omitted.</summary>
    private static IReadOnlyList<BpmnDiagram> WithShape(BpmnDocument document, BpmnShape shape, string? diagramId = null) =>
        AddToActivePlane(document, plane => plane with { Shapes = [.. plane.Shapes, shape] }, diagramId);

    private static BpmnDocument CreationGeometry(BpmnDocument document, string? diagramId) =>
        diagramId is null ? document : document with
        {
            Diagrams = [.. document.Diagrams.Where(diagram => diagram.Id == diagramId)],
        };

    private static BpmnEditDiagnostic? CreationDestinationProblem(
        BpmnDocument document, string diagramId, string? containerId, string elementId)
    {
        var diagram = document.Diagrams.FirstOrDefault(candidate => candidate.Id == diagramId);
        if (diagram is null)
        {
            return new BpmnEditDiagnostic(BpmnEditSeverity.Rejected, "CT-001",
                $"No diagram '{diagramId}' exists to draw '{elementId}'.", elementId);
        }

        var owner = diagram.Plane.BpmnElement;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (containerId is not null && visited.Add(containerId))
        {
            if (containerId == owner) return null;
            if (document.Collaboration is { } collaboration && owner == collaboration.Id
                && collaboration.Participants.Any(participant => participant.ProcessRef == containerId))
            {
                return null;
            }

            containerId = document.FindFlowNode(containerId)?.ContainerId;
        }

        return new BpmnEditDiagnostic(BpmnEditSeverity.Rejected, "CT-001",
            $"'{elementId}' does not belong to the content drawn by diagram '{diagramId}'.", elementId);
    }

    private static IReadOnlyList<BpmnDiagram> WithEdge(
        BpmnDocument document, BpmnEdge edge, string? diagramId = null) =>
        AddToActivePlane(document, plane => plane with { Edges = [.. plane.Edges, edge] }, diagramId);

    /// <summary>
    /// Applies a change to the requested diagram, defaulting to the first and creating one if absent.
    /// </summary>
    /// <remarks>
    /// Explicit destinations are validated before applying a creation command. Legacy callers
    /// without a destination still draw on the first plane (section 9.3).
    /// </remarks>
    private static IReadOnlyList<BpmnDiagram> AddToActivePlane(
        BpmnDocument document,
        Func<BpmnPlane, BpmnPlane> change,
        string? diagramId = null)
    {
        if (document.Diagrams.Count == 0)
        {
            var owner = document.Processes.Count > 0 ? document.Processes[0].Id : null;
            var plane = change(new BpmnPlane("Plane_1", owner, [], []));
            return [new BpmnDiagram("Diagram_1", plane)];
        }

        if (diagramId is not null)
        {
            return [.. document.Diagrams.Select(diagram => diagram.Id == diagramId
                ? diagram with { Plane = change(diagram.Plane) }
                : diagram)];
        }

        return
        [
            document.Diagrams[0] with { Plane = change(document.Diagrams[0].Plane) },
            .. document.Diagrams.Skip(1),
        ];
    }

    // --- reparenting --------------------------------------------------------------

    /// <summary>
    /// Moves an element into a different container, with everything that implies.
    /// </summary>
    /// <remarks>
    /// Validated in full before anything changes, because CT-008 treats a half-applied reparent as
    /// an error of its own: a node whose <c>containerId</c> moved while its Lane membership or its
    /// flows did not is a document that will not round-trip.
    /// </remarks>
    private static BpmnEditResult ApplyReparent(
        BpmnDocument document,
        ReparentElementCommand command,
        EditingOptions options)
    {
        if (document.FindFlowNode(command.ElementId) is not { } node)
        {
            return BpmnEditResult.Reject(
                document,
                new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "CT-008",
                    $"No flow node '{command.ElementId}' exists to move.",
                    command.ElementId));
        }

        if (Invalid(command.Bounds) is { } invalid)
        {
            return BpmnEditResult.Reject(document, invalid with { ElementId = command.ElementId });
        }

        if (MissingContainer(document, command.ContainerId, allowCollaboration: false) is { } missing)
        {
            return BpmnEditResult.Reject(document, missing with { Code = "CT-008" });
        }

        /*
         * A Boundary Event belongs to its host, not to a container the user can drop it into.
         * Section 2.5 makes attachedToRef the authority and DI-010 pins it to the host's outline,
         * so moving one is a re-attachment gesture rather than a reparent.
         */
        if (node.AttachedToRef is not null)
        {
            return BpmnEditResult.Reject(
                document,
                new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "CT-005",
                    "A Boundary Event moves by re-attaching to a host, not by changing container.",
                    command.ElementId));
        }

        // A Sub-Process cannot be moved inside itself or anything it contains.
        if (Contains(document, node.Id, command.ContainerId))
        {
            return BpmnEditResult.Reject(
                document,
                new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "CT-008",
                    "A container cannot be moved inside itself.",
                    command.ElementId));
        }

        if (LaneProblem(document, command.LaneId, command.ContainerId) is { } laneProblem)
        {
            return BpmnEditResult.Reject(document, laneProblem);
        }

        var moving = Subtree(document, node.Id);

        /*
         * SF-007. A Sequence Flow lives inside one container, so a node that keeps flows to
         * elements left behind cannot cross the boundary. Refused rather than silently deleting
         * them: the flows are the user's, and a reparent is not a delete.
         */
        var stranded = document.SequenceFlows
            .Where(flow => moving.Contains(flow.SourceRef) != moving.Contains(flow.TargetRef))
            .Select(flow => flow.Id)
            .ToList();

        if (stranded.Count > 0 && node.ContainerId != command.ContainerId)
        {
            return BpmnEditResult.Reject(
                document,
                new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "SF-007",
                    $"Moving this would leave {stranded.Count} Sequence Flow(s) crossing a container boundary.",
                    command.ElementId));
        }

        var poolId = PoolForContainer(document, command.ContainerId);

        var next = document with
        {
            FlowNodes =
            [
                .. document.FlowNodes.Select(candidate => candidate.Id == node.Id
                    ? candidate with { ContainerId = command.ContainerId, PoolId = poolId, LaneId = command.LaneId }

                    // The subtree keeps pointing at the node that moved; only its own Pool scope
                    // follows, because that is a property of where it is drawn.
                    : moving.Contains(candidate.Id) ? candidate with { PoolId = poolId } : candidate),
            ],
            Lanes = [.. document.Lanes.Select(lane => Remembering(lane, node.Id, command.LaneId))],
        };

        var requested = new Dictionary<string, Bounds>(StringComparer.Ordinal);
        var shape = next.Diagrams
            .SelectMany(diagram => diagram.Plane.Shapes)
            .FirstOrDefault(candidate => candidate.BpmnElement == node.Id);

        if (shape is not null) requested[shape.Id] = command.Bounds;

        var reparented = WithShapeBounds(next, requested);
        var diagnostics = new List<BpmnEditDiagnostic>();

        if (BpmnElementInspector.CategoryOf(node.Type) == BpmnElementCategory.Event
            && (node.ContainerId != command.ContainerId || node.AttachedToRef is not null))
        {
            var candidate = reparented.FindFlowNode(node.Id)!;
            var legal = ApplyEventLegality(reparented, candidate, options, diagnostics, out var eventProblem);
            if (eventProblem is not null) return BpmnEditResult.Reject(document, eventProblem);
            reparented = WithElement(reparented, legal!);
        }

        return BpmnEditResult.Applied(reparented, diagnostics);
    }

    /// <summary>Rewrites one Lane's membership for a node that has moved.</summary>
    private static BpmnLane Remembering(BpmnLane lane, string nodeId, string? joining)
    {
        var listed = lane.FlowNodeRefs.Contains(nodeId);
        var wanted = lane.Id == joining;

        if (listed == wanted) return lane;

        return wanted
            ? lane with { FlowNodeRefs = [.. lane.FlowNodeRefs, nodeId] }
            : lane with { FlowNodeRefs = [.. lane.FlowNodeRefs.Where(id => id != nodeId)] };
    }

    /// <summary>Whether a Lane exists and belongs to the container being moved into.</summary>
    private static BpmnEditDiagnostic? LaneProblem(BpmnDocument document, string? laneId, string containerId)
    {
        if (laneId is null) return null;

        if (document.Find(laneId) is not BpmnLane lane)
        {
            return new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "CT-008",
                $"No Lane '{laneId}' exists.",
                laneId);
        }

        var owner = document.LaneSets.FirstOrDefault(set => set.Id == lane.LaneSetId)?.ContainerId;

        return owner == containerId
            ? null
            : new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "CT-008",
                $"Lane '{laneId}' does not belong to '{containerId}'.",
                laneId);
    }

    /// <summary>An element and everything it owns, to a fixed point.</summary>
    private static HashSet<string> Subtree(BpmnDocument document, string rootId)
    {
        var inside = new HashSet<string>(StringComparer.Ordinal) { rootId };
        var added = true;

        while (added)
        {
            added = false;

            foreach (var node in document.FlowNodes)
            {
                if (inside.Contains(node.Id)) continue;
                if (!inside.Contains(node.ContainerId)
                    && (node.AttachedToRef is null || !inside.Contains(node.AttachedToRef)))
                {
                    continue;
                }

                inside.Add(node.Id);
                added = true;
            }
        }

        return inside;
    }

    /// <summary>Whether a container is the element itself or something it owns.</summary>
    private static bool Contains(BpmnDocument document, string rootId, string candidateId) =>
        Subtree(document, rootId).Contains(candidateId);

    /// <summary>The Participant whose Pool draws a container, when there is one.</summary>
    private static string? PoolForContainer(BpmnDocument document, string containerId)
    {
        var process = document.Find(containerId) switch
        {
            BpmnProcess owner => owner.Id,
            BpmnFlowNode node => RootProcessOf(document, node),
            _ => null,
        };

        return process is null
            ? null
            : document.Collaboration?.Participants
                .FirstOrDefault(participant => participant.ProcessRef == process)?.Id;
    }

    /// <summary>Walks a nested node up to the Process that ultimately owns it.</summary>
    private static string? RootProcessOf(BpmnDocument document, BpmnFlowNode node)
    {
        var current = node;

        for (var depth = 0; depth < 64; depth++)
        {
            if (document.Processes.Any(process => process.Id == current.ContainerId)) return current.ContainerId;
            if (document.FindFlowNode(current.ContainerId) is not { } parent) return null;

            current = parent;
        }

        return null;
    }

    // --- artifacts and data -----------------------------------------------------

    /// <summary>
    /// Creates a Text Annotation or a Group, with its DI shape.
    /// </summary>
    /// <remarks>
    /// No collision check. Neither is a Flow Node, and DI-011 is explicit that it governs Events,
    /// Activities and Gateways — an annotation is meant to sit beside the thing it annotates, and a
    /// Group is meant to enclose things.
    /// </remarks>
    private static BpmnEditResult ApplyCreateArtifact(BpmnDocument document, CreateArtifactCommand command)
    {
        if (Taken(document, command.Id) is { } taken) return BpmnEditResult.Reject(document, taken);

        if (Invalid(command.Bounds) is { } invalid)
        {
            return BpmnEditResult.Reject(document, invalid with { ElementId = command.Id });
        }

        if (MissingContainer(document, command.ContainerId, allowCollaboration: true) is { } missing)
        {
            return BpmnEditResult.Reject(document, missing);
        }

        var shape = new BpmnShape(
            command.ShapeId ?? BpmnId.ForDiagramElement(command.Id),
            command.Id,
            command.Bounds);

        var next = command.Kind == BpmnArtifactKind.TextAnnotation
            ? document with
            {
                TextAnnotations =
                [
                    .. document.TextAnnotations,
                    new BpmnTextAnnotation(command.Id, command.Text ?? string.Empty, command.ContainerId),
                ],
            }
            : document with
            {
                Groups = [.. document.Groups, new BpmnGroup(command.Id, command.ContainerId, command.Name)],
            };

        return BpmnEditResult.Applied(next with { Diagrams = WithShape(next, shape, command.DiagramId) }) with
        {
            CreatedIds = [command.Id],
        };
    }

    /// <summary>
    /// Creates a data reference, and its definition when the caller asked for a new one.
    /// </summary>
    /// <remarks>
    /// The "new definition or existing one" choice is required rather than inferred, because
    /// section 9.1 forbids duplicating definitions by accident and an editor that guessed would do
    /// exactly that every time a name happened to match.
    /// </remarks>
    private static BpmnEditResult ApplyCreateDataReference(
        BpmnDocument document,
        CreateDataReferenceCommand command)
    {
        if (Taken(document, command.Id) is { } taken) return BpmnEditResult.Reject(document, taken);

        if (Invalid(command.Bounds) is { } invalid)
        {
            return BpmnEditResult.Reject(document, invalid with { ElementId = command.Id });
        }

        if (MissingContainer(document, command.ContainerId, allowCollaboration: false) is { } missing)
        {
            return BpmnEditResult.Reject(document, missing);
        }

        var hasExisting = command.DefinitionRef is not null;
        var hasNew = command.NewDefinitionId is not null;

        if (hasExisting == hasNew)
        {
            return BpmnEditResult.Reject(
                document,
                new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "CT-011",
                    "A data reference must either name an existing definition or ask for exactly one new one.",
                    command.Id));
        }

        if (command.DefinitionRef is { } reference && !DefinitionExists(document, command.Kind, reference))
        {
            return BpmnEditResult.Reject(
                document,
                new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "CT-011",
                    $"No {(command.Kind == BpmnDataKind.DataObject ? "Data Object" : "Data Store")} '{reference}' exists.",
                    reference));
        }

        if (command.NewDefinitionId is { } fresh && Taken(document, fresh) is { } clash)
        {
            return BpmnEditResult.Reject(document, clash);
        }

        var definitionId = command.DefinitionRef ?? command.NewDefinitionId!;
        var next = document;

        if (command.NewDefinitionId is { } created)
        {
            next = command.Kind == BpmnDataKind.DataObject
                ? next with
                {
                    DataObjects = [.. next.DataObjects, new BpmnDataObject(created, command.ContainerId, command.Name)],
                }
                : next with
                {
                    Catalog = next.Catalog with
                    {
                        DataStores = [.. next.Catalog.DataStores, new BpmnDataStore(created, command.Name)],
                    },
                };
        }

        next = command.Kind == BpmnDataKind.DataObject
            ? next with
            {
                DataObjectReferences =
                [
                    .. next.DataObjectReferences,
                    new BpmnDataObjectReference(command.Id, command.ContainerId, command.Name, definitionId),
                ],
            }
            : next with
            {
                DataStoreReferences =
                [
                    .. next.DataStoreReferences,
                    new BpmnDataStoreReference(command.Id, command.ContainerId, command.Name, definitionId),
                ],
            };

        var shape = new BpmnShape(
            command.ShapeId ?? BpmnId.ForDiagramElement(command.Id),
            command.Id,
            command.Bounds);

        return BpmnEditResult.Applied(next with { Diagrams = WithShape(next, shape, command.DiagramId) }) with
        {
            CreatedIds = [command.Id],
        };
    }

    /// <summary>Whether a data definition of the right kind exists to be referenced.</summary>
    private static bool DefinitionExists(BpmnDocument document, BpmnDataKind kind, string id) => kind switch
    {
        BpmnDataKind.DataObject => document.DataObjects.Any(candidate => candidate.Id == id),
        _ => document.Catalog.DataStores.Any(candidate => candidate.Id == id),
    };

    /// <summary>EDIT-030: an id may be used once.</summary>
    private static BpmnEditDiagnostic? Taken(BpmnDocument document, string id) =>
        document.Find(id) is null
            ? null
            : new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "EDIT-030",
                $"The id '{id}' is already used.",
                id);

    /// <summary>
    /// Whether a container exists and is one that may hold what is being created.
    /// </summary>
    /// <remarks>
    /// Artifacts may live on a Collaboration; data references may not. Section 9.1 is explicit
    /// that Text Annotations and Groups created on a Collaboration plane belong to its artifact
    /// collection, while Data Objects belong inside a Participant's referenced Process.
    /// </remarks>
    private static BpmnEditDiagnostic? MissingContainer(
        BpmnDocument document,
        string containerId,
        bool allowCollaboration)
    {
        var owner = document.Find(containerId);

        var usable = owner switch
        {
            BpmnProcess => true,
            BpmnFlowNode { FlowElementsOwned: true } => true,
            BpmnCollaboration => allowCollaboration,
            _ => false,
        };

        return usable
            ? null
            : new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "EDIT-031",
                $"'{containerId}' cannot hold this element.",
                containerId);
    }

    // --- connector editing ------------------------------------------------------

    /// <summary>
    /// Moves one end of a connector, keeping everything else about it.
    /// </summary>
    /// <remarks>
    /// Each connector kind stores its ends differently — a Data Association has a list of sources
    /// — so the rewrite is per kind rather than through a shared interface the model does not have.
    /// </remarks>
    private static BpmnEditResult ApplyReconnect(BpmnDocument document, ReconnectConnectorCommand command)
    {
        if (document.Find(command.Id) is not { } connector)
        {
            return BpmnEditResult.Reject(
                document,
                new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "EDIT-060",
                    $"No connector '{command.Id}' exists.",
                    command.Id));
        }

        if (RouteProblem(command.Waypoints) is { } badRoute)
        {
            return BpmnEditResult.Reject(document, badRoute with { ElementId = command.Id });
        }

        var (currentSource, currentTarget) = EndsOf(connector);
        var source = command.SourceRef ?? currentSource;
        var target = command.TargetRef ?? currentTarget;

        if (source is null || target is null)
        {
            return BpmnEditResult.Reject(
                document,
                new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "EDIT-061",
                    "A connector must keep both of its ends.",
                    command.Id));
        }

        foreach (var endpoint in (string[])[source, target])
        {
            if (document.Find(endpoint) is null
                || command.DiagramId is not null
                    && !DrawsEndpoint(CreationGeometry(document, command.DiagramId), endpoint,
                        connector is BpmnAssociation ? BpmnConnectorKind.Association : BpmnConnectorKind.SequenceFlow))
            {
                return BpmnEditResult.Reject(
                    document,
                    new BpmnEditDiagnostic(
                        BpmnEditSeverity.Rejected,
                        "EDIT-062",
                        $"No element '{endpoint}' exists to connect to.",
                        endpoint));
            }
        }

        if (string.Equals(source, target, StringComparison.Ordinal))
        {
            return BpmnEditResult.Reject(
                document,
                new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "EDIT-063",
                    "A connector may not start and end on the same element.",
                    command.Id));
        }

        var kind = connector switch
        {
            BpmnAssociation => BpmnConnectorKind.Association,
            BpmnMessageFlow => BpmnConnectorKind.MessageFlow,
            BpmnDataAssociation => BpmnConnectorKind.DataAssociation,
            _ => BpmnConnectorKind.SequenceFlow,
        };
        if (AnnotationConnectionProblem(document, kind, source, target, command.Id) is { } connectionProblem)
        {
            return BpmnEditResult.Reject(document, connectionProblem);
        }

        if (command.DiagramId is not null
            && !CreationGeometry(document, command.DiagramId).Diagrams
                .SelectMany(diagram => diagram.Plane.Edges).Any(edge => edge.BpmnElement == command.Id))
        {
            return BpmnEditResult.Reject(document, new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected, "EDIT-064",
                $"No edge draws '{command.Id}' on diagram '{command.DiagramId}'.", command.Id));
        }

        var next = connector switch
        {
            BpmnSequenceFlow flow => document with
            {
                SequenceFlows = [.. document.SequenceFlows.Select(candidate =>
                    candidate.Id == flow.Id ? candidate with { SourceRef = source, TargetRef = target } : candidate)],
            },
            BpmnMessageFlow flow => document with
            {
                MessageFlows = [.. document.MessageFlows.Select(candidate =>
                    candidate.Id == flow.Id ? candidate with { SourceRef = source, TargetRef = target } : candidate)],
            },
            BpmnAssociation association => document with
            {
                Associations = [.. document.Associations.Select(candidate =>
                    candidate.Id == association.Id
                        ? candidate with { SourceRef = source, TargetRef = target }
                        : candidate)],
            },
            BpmnDataAssociation association => document with
            {
                DataAssociations = [.. document.DataAssociations.Select(candidate =>
                    candidate.Id == association.Id
                        ? candidate with { SourceRefs = [source], TargetRef = target }
                        : candidate)],
            },
            _ => null,
        };

        if (next is null)
        {
            return BpmnEditResult.Reject(
                document,
                new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "EDIT-060",
                    $"'{command.Id}' is not a connector.",
                    command.Id));
        }

        return BpmnEditResult.Applied(WithWaypoints(next, command.Id, command.Waypoints, command.DiagramId));
    }

    /// <summary>Replaces a connector's route, and nothing else.</summary>
    private static BpmnEditResult ApplyWaypoints(BpmnDocument document, UpdateWaypointsCommand command)
    {
        var edge = CreationGeometry(document, command.DiagramId).Diagrams
            .SelectMany(diagram => diagram.Plane.Edges)
            .FirstOrDefault(candidate => candidate.BpmnElement == command.Id);

        if (edge is null)
        {
            return BpmnEditResult.Reject(
                document,
                new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "EDIT-064",
                    $"No edge draws '{command.Id}'.",
                    command.Id));
        }

        if (RouteProblem(command.Waypoints) is { } badRoute)
        {
            return BpmnEditResult.Reject(document, badRoute with { ElementId = command.Id });
        }

        if (BpmnAnnotationAttachments.IsAnnotationAssociation(document, command.Id) && command.Waypoints.Count != 2)
        {
            return BpmnEditResult.Reject(document, new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected, "AN-002", "Editor policy requires a straight annotation Association with two endpoints.", command.Id));
        }

        return BpmnEditResult.Applied(WithWaypoints(document, command.Id, command.Waypoints, command.DiagramId));
    }

    /// <summary>DI-001 and DI-003, for a route.</summary>
    private static BpmnEditDiagnostic? RouteProblem(IReadOnlyList<DiPoint> waypoints)
    {
        if (waypoints.Count < 2)
        {
            return new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "DI-001",
                "An edge needs at least two waypoints.");
        }

        return waypoints.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y))
            ? new BpmnEditDiagnostic(BpmnEditSeverity.Rejected, "DI-003", "Waypoints must be finite.")
            : null;
    }

    /// <summary>The two ends of whichever connector kind this is.</summary>
    private static (string? Source, string? Target) EndsOf(object connector) => connector switch
    {
        BpmnSequenceFlow flow => (flow.SourceRef, flow.TargetRef),
        BpmnMessageFlow flow => (flow.SourceRef, flow.TargetRef),
        BpmnAssociation association => (association.SourceRef, association.TargetRef),
        BpmnDataAssociation association => (
            association.SourceRefs.Count > 0 ? association.SourceRefs[0] : null,
            association.TargetRef),
        _ => (null, null),
    };

    /// <summary>Writes a route onto the requested plane, or every view when omitted.</summary>
    private static BpmnDocument WithWaypoints(
        BpmnDocument document,
        string connectorId,
        IReadOnlyList<DiPoint> waypoints,
        string? diagramId = null) => document with
        {
            Diagrams =
            [
                .. document.Diagrams.Select(diagram => diagramId is not null && diagram.Id != diagramId
                    ? diagram : diagram with
                {
                    Plane = diagram.Plane with
                    {
                        Edges =
                        [
                            .. diagram.Plane.Edges.Select(edge =>
                                edge.BpmnElement == connectorId ? edge with
                                {
                                    Waypoints = waypoints.Count > 2 && BpmnAnnotationAttachments.IsAnnotationAssociation(document, connectorId)
                                        ? [waypoints[0], waypoints[^1]] : waypoints,
                                } : edge),
                        ],
                    },
                }),
            ],
        };

    // --- delete ---------------------------------------------------------------

    /// <summary>
    /// Adds a clipboard fragment, with fresh ids and stepped clear of what is already there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The offset is a starting point, not a promise. Pasting on top of the original would collide
    /// with it and DI-011 would refuse the whole paste — correct and useless, because the user
    /// asked for a copy and has no way to say where. So the fragment is nudged until it lands
    /// clear, exactly as a keyboard-armed stencil tool is, and only then judged.
    /// </para>
    /// <para>
    /// Ids are minted here rather than in the command so that <see cref="Apply(BpmnDocument,
    /// BpmnEditCommand)"/> stays repeatable from the caller's point of view: the same payload
    /// pasted twice produces two distinct sets, which is the whole point of a clipboard.
    /// </para>
    /// </remarks>
    private static BpmnEditResult ApplyPaste(
        BpmnDocument document,
        PasteCommand command,
        EditingOptions options)
    {
        if (command.Payload.IsEmpty)
        {
            return BpmnEditResult.Reject(
                document,
                new BpmnEditDiagnostic(BpmnEditSeverity.Rejected, "EDIT-040", "The clipboard is empty."));
        }

        if (document.Find(command.ContainerId) is null)
        {
            return BpmnEditResult.Reject(
                document,
                new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "EDIT-041",
                    $"No container '{command.ContainerId}' exists to paste into.",
                    command.ContainerId));
        }

        var step = options.CollisionClearance > 0 ? options.CollisionClearance + PasteNudge : PasteNudge;

        for (var attempt = 0; attempt <= PasteAttempts; attempt++)
        {
            var offset = new DiPoint(
                command.Offset.X + (attempt * step),
                command.Offset.Y + (attempt * step));

            var fragment = BpmnClipboard.Rebuild(
                command.Payload,
                command.ContainerId,
                command.PoolId,
                offset,
                BpmnId.New);

            var result = AddFragment(document, fragment, options);

            // Only a collision is worth retrying further out. Anything else is a property of the
            // fragment or the target and will fail identically at every offset.
            if (result.IsApplied || !result.Rejections.Any(rejection => rejection.Code == "DI-011"))
            {
                return result;
            }
        }

        return BpmnEditResult.Reject(
            document,
            new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "DI-011",
                "There is no clear space near the original to paste into."));
    }

    /// <summary>How far each retry moves the fragment when the last position collided.</summary>
    private const double PasteNudge = 20;

    /// <summary>
    /// Bounded, so a densely packed plane cannot turn one keystroke into an unbounded search.
    /// </summary>
    private const int PasteAttempts = 40;

    /// <summary>Adds an already-renamed fragment, or refuses the whole thing.</summary>
    private static BpmnEditResult AddFragment(
        BpmnDocument document,
        BpmnClipboardPayload fragment,
        EditingOptions options)
    {
        foreach (var shape in fragment.Shapes)
        {
            if (Invalid(shape.Bounds) is { } invalid)
            {
                return BpmnEditResult.Reject(document, invalid with { ElementId = shape.BpmnElement });
            }
        }

        var next = document with
        {
            FlowNodes = [.. document.FlowNodes, .. fragment.FlowNodes],
            SequenceFlows = [.. document.SequenceFlows, .. fragment.SequenceFlows],
            Associations = [.. document.Associations, .. fragment.Associations],
            DataAssociations = [.. document.DataAssociations, .. fragment.DataAssociations],
            TextAnnotations = [.. document.TextAnnotations, .. fragment.TextAnnotations],
            Groups = [.. document.Groups, .. fragment.Groups],
            DataObjectReferences = [.. document.DataObjectReferences, .. fragment.DataObjectReferences],
            DataStoreReferences = [.. document.DataStoreReferences, .. fragment.DataStoreReferences],
        };

        var landing = fragment.Planes.FirstOrDefault(plane => plane.OwnerId is null);

        if (landing is not null)
        {
            next = next with
            {
                Diagrams = AddToActivePlane(next, plane => plane with
                {
                    Shapes = [.. plane.Shapes, .. landing.Shapes],
                    Edges = [.. plane.Edges, .. landing.Edges],
                }),
            };
        }

        /*
         * A plane that draws a copied element's interior becomes a diagram of its own, the way the
         * original was. Folding it into the active plane instead spilled a collapsed Sub-Process's
         * whole nested graph onto the top level, where nothing was hiding it.
         */
        foreach (var nested in fragment.Planes.Where(plane => plane.OwnerId is not null))
        {
            next = next with
            {
                Diagrams =
                [
                    .. next.Diagrams,
                    new BpmnDiagram(
                        BpmnId.New("Diagram"),
                        new BpmnPlane(BpmnId.New("Plane"), nested.OwnerId, nested.Shapes, nested.Edges)),
                ],
            };
        }

        var landed = landing?.Shapes ?? [];

        if (options.PreventCollisions
            && FindCollisionAmong(next, landed, options.CollisionClearance) is { } collision)
        {
            return BpmnEditResult.Reject(document, collision);
        }

        try
        {
            var pastedIds = fragment.FlowNodes.Select(element => element.Id).Concat(fragment.SequenceFlows.Select(element => element.Id))
                .Concat(fragment.Associations.Select(element => element.Id)).Concat(fragment.DataAssociations.Select(element => element.Id))
                .Concat(fragment.TextAnnotations.Select(element => element.Id)).Concat(fragment.Groups.Select(element => element.Id))
                .Concat(fragment.DataObjectReferences.Select(element => element.Id)).Concat(fragment.DataStoreReferences.Select(element => element.Id)).ToArray();
            foreach (var transform in options.PasteTransforms) next = transform.Transform(next, pastedIds, options);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or FormatException)
        {
            return BpmnEditResult.Reject(document, new BpmnEditDiagnostic(BpmnEditSeverity.Rejected, "EDIT-111", $"Paste transform refused: {exception.Message}"));
        }

        return BpmnEditResult.Applied(
            next,
            [
                new BpmnEditDiagnostic(
                    BpmnEditSeverity.Info,
                    "EDIT-042",
                    $"Pasted {fragment.Count} element(s)."),
            ]) with
        {
            CreatedIds = fragment.RootIds,
        };
    }

    /// <summary>
    /// DI-011 for a set of newly added shapes.
    /// </summary>
    /// <remarks>
    /// The same rule and the same exemptions as a move, expressed against the document the paste
    /// would produce: each new shape is checked where it will actually be, against everything that
    /// is already there.
    /// </remarks>
    private static BpmnEditDiagnostic? FindCollisionAmong(
        BpmnDocument document,
        IReadOnlyList<BpmnShape> added,
        double clearance)
    {
        var shapes = document.Diagrams
            .SelectMany(diagram => diagram.Plane.Shapes)
            .ToFrozenDictionary(shape => shape.Id, StringComparer.Ordinal);

        var requested = added.ToDictionary(shape => shape.Id, shape => shape.Bounds, StringComparer.Ordinal);

        return FindCollision(document, shapes, requested, clearance);
    }

    /// <summary>
    /// Everything a delete of these elements would remove: what was named and everything that cannot outlive it.
    /// </summary>
    /// <remarks>
    /// For a question put to a person before the delete runs: deleting a Sub-Process takes its nested graph and the flows attached to what goes, so
    /// "one element" is rarely the whole of it. Ids that name nothing are ignored, and the delete itself still refuses them.
    /// </remarks>
    /// <param name="document">The document the delete would run on.</param>
    /// <param name="elementIds">The elements named.</param>
    /// <returns>The ids of every element and connector that would go.</returns>
    public static IReadOnlySet<string> DeletionClosure(BpmnDocument document, IEnumerable<string> elementIds)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(elementIds);

        var roots = new HashSet<string>(elementIds.Where(id => document.Find(id) is not null), StringComparer.Ordinal);
        return ExpandDeletion(document, roots, []);
    }

    private static BpmnEditResult ApplyDelete(BpmnDocument document, DeleteElementsCommand command)
    {
        if (command.ElementIds.Count == 0)
        {
            return BpmnEditResult.Reject(
                document,
                new BpmnEditDiagnostic(BpmnEditSeverity.Rejected, "EDIT-020", "A delete must name at least one element."));
        }

        var diagnostics = new List<BpmnEditDiagnostic>();
        var lanes = new HashSet<string>(StringComparer.Ordinal);
        var roots = new HashSet<string>(StringComparer.Ordinal);

        foreach (var id in command.ElementIds)
        {
            switch (document.Find(id))
            {
                case null:
                    return BpmnEditResult.Reject(
                        document,
                        new BpmnEditDiagnostic(
                            BpmnEditSeverity.Rejected,
                            "EDIT-021",
                            $"No element '{id}' exists in this document.",
                            id));

                case BpmnLane:
                    lanes.Add(id);
                    break;

                // Deleting a Pool has consequences the specification does not pin down — whether
                // its Process and that Process's nodes go with it. Refusing is better than
                // guessing at something irreversible.
                case BpmnParticipant:
                case BpmnProcess:
                    return BpmnEditResult.Reject(
                        document,
                        new BpmnEditDiagnostic(
                            BpmnEditSeverity.Rejected,
                            "EDIT-022",
                            $"Deleting '{id}' is not supported yet; Pool and Process deletion needs its own semantics.",
                            id));

                default:
                    roots.Add(id);
                    break;
            }
        }

        roots.UnionWith(lanes);
        var removed = ExpandDeletion(document, roots, diagnostics);
        var pools = lanes
            .Select(id => BpmnLaneLayout.PoolOfLane(document, id))
            .OfType<BpmnParticipant>()
            .DistinctBy(pool => pool.Id)
            .ToList();

        var edited = RefillPools(RemoveElements(document, removed, lanes), pools);

        removed.UnionWith(lanes);
        return BpmnEditResult.Applied(edited, diagnostics) with
        {
            DeletedElementIds = removed.ToFrozenSet(StringComparer.Ordinal),
        };
    }

    /// <summary>
    /// Re-tiles the Lanes left in each Pool so they still fill it.
    /// </summary>
    /// <remarks>
    /// Deleting a Lane frees its share of the Pool; the survivors take it in proportion to their
    /// sizes rather than leaving a gap.
    /// </remarks>
    private static BpmnDocument RefillPools(BpmnDocument document, IReadOnlyList<BpmnParticipant> pools)
    {
        var requested = new Dictionary<string, Bounds>(StringComparer.Ordinal);

        foreach (var pool in pools)
        {
            if (ShapeFor(document, pool.Id) is not { } poolShape) continue;

            var isHorizontal = poolShape.IsHorizontal != false;
            var lanes = BpmnLaneLayout.LanesOf(document, pool.Id, isHorizontal);
            var tiled = BpmnLaneLayout.Retile(poolShape.Bounds, [.. lanes.Select(lane => lane.Bounds)], isHorizontal);

            for (var index = 0; index < lanes.Count; index++)
            {
                requested[lanes[index].Id] = tiled[index];
            }
        }

        return requested.Count == 0 ? document : WithShapeBounds(document, requested);
    }

    /// <summary>
    /// Grows the deletion set to everything that cannot outlive it.
    /// </summary>
    /// <remarks>
    /// Transitive on purpose. Deleting a Sub-Process takes its nested graph (section 2.7), whose
    /// nodes may themselves host Boundary Events, whose own flows then have nothing to connect.
    /// Iterating to a fixed point is what makes one pass enough.
    /// </remarks>
    private static HashSet<string> ExpandDeletion(
        BpmnDocument document,
        HashSet<string> roots,
        List<BpmnEditDiagnostic> diagnostics)
    {
        var removed = new HashSet<string>(roots, StringComparer.Ordinal);
        var added = true;

        while (added)
        {
            added = false;

            foreach (var node in document.FlowNodes)
            {
                if (removed.Contains(node.Id)) continue;

                // A Boundary Event cannot exist without its host, and an embedded Sub-Process owns
                // its content outright — a Call Activity does not, so its target is untouched.
                var orphaned = (node.AttachedToRef is { } host && removed.Contains(host))
                    || removed.Contains(node.ContainerId);

                if (!orphaned) continue;

                removed.Add(node.Id);
                added = true;
            }

            foreach (var flow in document.SequenceFlows)
            {
                if (removed.Contains(flow.Id)) continue;
                if (!removed.Contains(flow.SourceRef) && !removed.Contains(flow.TargetRef)
                    && !removed.Contains(flow.ContainerId))
                {
                    continue;
                }

                removed.Add(flow.Id);
                added = true;
            }
        }

        foreach (var flow in document.MessageFlows)
        {
            if (removed.Contains(flow.Id)) continue;
            if (!removed.Contains(flow.SourceRef) && !removed.Contains(flow.TargetRef)) continue;

            removed.Add(flow.Id);
        }

        foreach (var annotation in document.TextAnnotations)
        {
            if (!removed.Contains(annotation.Id) && removed.Contains(annotation.ContainerId)) removed.Add(annotation.Id);
        }

        foreach (var group in document.Groups)
        {
            if (!removed.Contains(group.Id) && removed.Contains(group.ContainerId)) removed.Add(group.Id);
        }

        foreach (var reference in document.DataObjectReferences)
        {
            if (!removed.Contains(reference.Id) && removed.Contains(reference.ContainerId)) removed.Add(reference.Id);
        }

        foreach (var reference in document.DataStoreReferences)
        {
            if (!removed.Contains(reference.Id) && removed.Contains(reference.ContainerId)) removed.Add(reference.Id);
        }

        foreach (var annotation in document.TextAnnotations)
        {
            if (BpmnAnnotationAttachments.UniqueConnection(document, annotation.Id) is { } connection
                && removed.Contains(BpmnAnnotationAttachments.OtherEnd(connection, annotation.Id)))
            {
                removed.Add(annotation.Id);
            }
        }

        foreach (var association in document.Associations)
        {
            if (removed.Contains(association.SourceRef) || removed.Contains(association.TargetRef)
                || removed.Contains(association.ContainerId)) removed.Add(association.Id);
        }

        foreach (var association in document.DataAssociations)
        {
            if (removed.Contains(association.TargetRef) || association.SourceRefs.Any(removed.Contains)
                || removed.Contains(association.ContainerId)) removed.Add(association.Id);
        }

        var cascaded = removed.Count - roots.Count;
        if (cascaded > 0)
        {
            diagnostics.Add(new BpmnEditDiagnostic(
                BpmnEditSeverity.Info,
                "EDIT-023",
                $"Deleted {cascaded} further element(s) that could not outlive the selection."));
        }

        return removed;
    }

    private static bool DrawsEndpoint(BpmnDocument geometry, string endpoint, BpmnConnectorKind kind) =>
        ShapeFor(geometry, endpoint) is not null
        || kind == BpmnConnectorKind.Association
            && geometry.Find(endpoint) is BpmnSequenceFlow or BpmnMessageFlow
            && geometry.Diagrams.Any(diagram => diagram.Plane.Edges.Any(edge => edge.BpmnElement == endpoint));

    private static BpmnEditDiagnostic? AnnotationConnectionProblem(
        BpmnDocument document, BpmnConnectorKind kind, string source, string target, string connectorId)
    {
        var from = BpmnConnectionRules.EndpointOf(document, source);
        var to = BpmnConnectionRules.EndpointOf(document, target);
        if (from is null || to is null) return null;
        if (document.Find(source) is not (BpmnTextAnnotation or BpmnSequenceFlow or BpmnMessageFlow or BpmnAssociation or BpmnDataAssociation)
            && document.Find(target) is not (BpmnTextAnnotation or BpmnSequenceFlow or BpmnMessageFlow or BpmnAssociation or BpmnDataAssociation)) return null;

        var violation = BpmnConnectionRules.Check(kind, from, to).Violations
            .FirstOrDefault(item => item.Severity == BpmnSeverity.Error);
        if (violation is not null) return new BpmnEditDiagnostic(
            BpmnEditSeverity.Rejected, violation.RuleId, violation.Message, connectorId);

        if (kind == BpmnConnectorKind.Association)
        {
            foreach (var id in (string[])[source, target])
            {
                if (document.Find(id) is BpmnTextAnnotation
                    && BpmnAnnotationAttachments.Connections(document, id).Any(association => association.Id != connectorId))
                {
                    return new BpmnEditDiagnostic(BpmnEditSeverity.Rejected, "AN-001",
                        "Editor policy allows one target per Text Annotation; reconnect its existing Association instead.", id);
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Removes the elements, their Lane membership and every DI entry that would dangle.
    /// </summary>
    /// <remarks>
    /// Deleting a Lane keeps the Pool, the Process and the Flow Nodes, dropping only the Lane and
    /// its <c>flowNodeRefs</c> — the nodes stay owned by the Process at their existing positions
    /// (section 2.8). That is the one deletion BPMN specifies as non-cascading, and the easy
    /// mistake is to treat a Lane like any other container.
    /// </remarks>
    private static BpmnDocument RemoveElements(
        BpmnDocument document,
        HashSet<string> removed,
        HashSet<string> lanes)
    {
        var gone = new HashSet<string>(removed, StringComparer.Ordinal);
        gone.UnionWith(lanes);

        var remainingLanes = document.Lanes
            .Where(lane => !lanes.Contains(lane.Id))
            .Select(lane => lane with
            {
                FlowNodeRefs = [.. lane.FlowNodeRefs.Where(reference => !removed.Contains(reference))],
            })
            .ToList();

        return document with
        {
            FlowNodes = [.. document.FlowNodes
                .Where(node => !removed.Contains(node.Id))
                .Select(node => lanes.Contains(node.LaneId ?? string.Empty) ? node with { LaneId = null } : node)],
            SequenceFlows = [.. document.SequenceFlows.Where(flow => !removed.Contains(flow.Id))],
            MessageFlows = [.. document.MessageFlows.Where(flow => !removed.Contains(flow.Id))],
            Associations = [.. document.Associations.Where(item => !removed.Contains(item.Id))],
            DataAssociations = [.. document.DataAssociations.Where(item => !removed.Contains(item.Id))],
            TextAnnotations = [.. document.TextAnnotations.Where(item => !removed.Contains(item.Id))],
            Groups = [.. document.Groups.Where(item => !removed.Contains(item.Id))],
            DataObjectReferences = [.. document.DataObjectReferences.Where(item => !removed.Contains(item.Id))],
            DataStoreReferences = [.. document.DataStoreReferences.Where(item => !removed.Contains(item.Id))],
            Lanes = remainingLanes,
            LaneSets = [.. document.LaneSets.Select(set => set with
            {
                LaneIds = [.. set.LaneIds.Where(id => !lanes.Contains(id))],
            })],
            Diagrams = [.. document.Diagrams.Select(diagram => diagram with
            {
                Plane = diagram.Plane with
                {
                    // Section 10.1: a shape or edge whose bpmnElement no longer resolves must not
                    // survive. Leaving it would produce exactly the orphaned DI that made the
                    // previous interchange package unusable.
                    Shapes = [.. diagram.Plane.Shapes.Where(shape => !gone.Contains(shape.BpmnElement))],
                    Edges = [.. diagram.Plane.Edges.Where(edge => !gone.Contains(edge.BpmnElement))],
                },
            })],
        };
    }
}
