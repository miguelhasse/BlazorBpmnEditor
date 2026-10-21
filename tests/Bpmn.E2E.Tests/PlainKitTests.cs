using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

/*
 * The standard kit's query, /editor?plain=true, runs the same tests as the Fluent kit's /editor. Each
 * subclass inherits every test of its base and KitRoute picks the route from the "Plain" prefix of
 * the class name, so a test written once is checked against both kits through role- and
 * name-based locators.
 */

public sealed class PlainCanvasSmokeTests(DemoAppFixture app) : CanvasSmokeTests(app);

public sealed class PlainDemoControlsTests(DemoAppFixture app) : DemoControlsTests(app);

public sealed class PlainCanvasNavigationTests(DemoAppFixture app) : CanvasNavigationTests(app);

public sealed class PlainPropertyPanelTests(DemoAppFixture app) : PropertyPanelTests(app);

public sealed class PlainReferenceBrowserTests(DemoAppFixture app) : ReferenceBrowserTests(app);

public sealed class PlainContextMenuTests(DemoAppFixture app) : ContextMenuTests(app);

public sealed class PlainLabelEditingTests(DemoAppFixture app) : LabelEditingTests(app);

public sealed class PlainStencilTests(DemoAppFixture app) : StencilTests(app);

public sealed class PlainValidationTests(DemoAppFixture app) : ValidationTests(app);

public sealed class PlainKeyboardNavigationTests(DemoAppFixture app) : KeyboardNavigationTests(app);

public sealed class PlainDiagramNavigationTests(DemoAppFixture app) : DiagramNavigationTests(app);

public sealed class PlainImportTests(DemoAppFixture app) : ImportTests(app);

public sealed class PlainExecutionOverlayTests(DemoAppFixture app) : ExecutionOverlayTests(app);

public sealed class PlainRoutingTests(DemoAppFixture app) : RoutingTests(app);

public sealed class PlainWebMcpTests(DemoAppFixture app) : WebMcpTests(app);

public sealed class PlainAnnotationConnectionTests(DemoAppFixture app) : AnnotationConnectionTests(app);

public sealed class PlainDataReferenceConnectionTests(DemoAppFixture app) : DataReferenceConnectionTests(app);
