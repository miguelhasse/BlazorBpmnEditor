/**
 * Notation geometry from specification section 4.
 *
 * These are the BPMN notation defaults, not theme preferences: an Event is a 36px circle
 * and a Gateway a 50px diamond in every conforming renderer. Imported BPMNDI bounds
 * always win over them; they are the fallback when a document omits a shape, and the
 * basis for every marker placement calculation.
 */

/** Event circle diameter (section 4). */
export const EVENT_SIZE = 36;

/** Gateway diamond bounding box (section 4). */
export const GATEWAY_SIZE = 50;

/** Task bounding box and corner radius (section 4). */
export const TASK_WIDTH = 100;
export const TASK_HEIGHT = 80;
export const TASK_CORNER_RADIUS = 10;

/**
 * How far a connector's corners are rounded, in paper units.
 *
 * Drawing only: the stored waypoints stay the right-angled corners, so the document and the
 * interchange format are unaffected.
 */
export const CONNECTOR_CORNER_RADIUS = 8;

/** How near an endpoint has to be to a magnet point on an outline to be pulled onto it, in paper units. */
export const MAGNET_SNAP_RADIUS = 10;

/** Expanded Sub-Process default bounds, large enough to hold a small child flow. */
export const SUBPROCESS_WIDTH = 350;
export const SUBPROCESS_HEIGHT = 200;

/** Data Object / Data Store defaults (section 1.6). */
export const DATA_OBJECT_WIDTH = 36;
export const DATA_OBJECT_HEIGHT = 50;
export const DATA_STORE_WIDTH = 50;
export const DATA_STORE_HEIGHT = 50;

/** Text Annotation and Group defaults (section 1.6). */
export const TEXT_ANNOTATION_WIDTH = 100;
export const TEXT_ANNOTATION_HEIGHT = 30;
export const GROUP_WIDTH = 300;
export const GROUP_HEIGHT = 200;

/** Pool / Lane defaults and the header band thickness (section 1.5). */
export const POOL_WIDTH = 600;
export const POOL_HEIGHT = 250;
export const POOL_HEADER_SIZE = 30;

/** The smallest a Lane may be along the axis it subdivides; matches the document editor. */
export const LANE_MINIMUM_EXTENT = 40;

/** Event ring radii, as fractions of the 36px box (section 4.2). */
export const EVENT_OUTER_RADIUS = 17;
export const EVENT_INNER_RADIUS = 14;

/** Stroke weights that carry notational meaning (section 1.1). */
export const STROKE_THIN = 1.5;
export const STROKE_THICK = 3;

/**
 * Dash patterns. Each one is notational, not decorative — changing any of these changes
 * what the diagram means (section 4).
 */
export const DASH_NON_INTERRUPTING = '4 3';
export const DASH_EVENT_SUBPROCESS = '6 4';
export const DASH_MESSAGE_FLOW = '5 4';
export const DASH_ASSOCIATION = '2 3';
export const DASH_GROUP = '10 6 2 6';

/**
 * How far either side of a thin, unfilled outline a click still selects its shape, in model units.
 * Used by the shapes whose interior is click-through: a Group's outline and a Text Annotation's
 * bracket.
 */
export const OUTLINE_HIT_TOLERANCE = 6;

/** Bottom-centre Activity marker band (section 4.3). */
export const ACTIVITY_MARKER_BOX = 14;
export const ACTIVITY_MARKER_GAP = 5;
export const ACTIVITY_MARKER_BASELINE_OFFSET = 20;

/** Top-left Task type glyph (section 4.4). */
export const TASK_ICON_BOX = 18;
export const TASK_ICON_INSET = 8;

/** Event trigger glyph, centred in the rings (section 4.2). */
export const EVENT_ICON_BOX = 28;

/** An element-template icon drawn in place of an Event glyph: the same 18 units as on an Activity. */
export const EVENT_TEMPLATE_ICON_BOX = 18;

/** Gateway glyph, centred in the diamond (section 4.1). */
export const GATEWAY_ICON_BOX = 34;

/** Collection marker on a Data Object: glyph box and inset from the bottom edge (section 1.6). */
export const DATA_MARKER_BOX = 12;
export const DATA_MARKER_INSET = 1;

/** How far along a default Sequence Flow its 45-degree tick sits, from the source (section 4). */
export const DEFAULT_FLOW_TICK_OFFSET = 18;

/** Inset of a Transaction's second border (section 1.2). */
export const TRANSACTION_BORDER_INSET = 3;

/** Default label box for an externally-labelled shape (section 4.5). */
export const EXTERNAL_LABEL_WIDTH = 90;
export const EXTERNAL_LABEL_GAP = 4;

/**
 * How far an imported external label box may sit from its shape before it is distrusted.
 * Exporters occasionally write label bounds hundreds of pixels away (or off the diagram), and
 * a name nowhere near its shape reads as belonging to something else.
 */
export const EXTERNAL_LABEL_MAX_DISTANCE = 60;

/** The same distrust for a connector label, measured from the box centre to the nearest point of the route. */
export const EDGE_LABEL_MAX_DISTANCE = 100;

/** Label typography defaults; overridable through the label style contract. */
export const LABEL_FONT_FAMILY = 'var(--bpmn-font-family)';
export const LABEL_FONT_SIZE = 12;
export const LABEL_LINE_HEIGHT = 1.2;
export const LABEL_PADDING_X = 4;
export const LABEL_PADDING_Y = 2;
