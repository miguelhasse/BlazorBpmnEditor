/**
 * The interop contract version shared by this module and the Blazor side.
 *
 * This is the single source of truth: `BpmnInteropContract.Version` on the C# side is
 * asserted equal to this literal by a unit test, and `BpmnCanvas` refuses to run against
 * a module reporting a different version rather than failing later in a confusing way.
 *
 * Bump it whenever a payload shape in `contracts.ts` changes incompatibly.
 */
export const INTEROP_CONTRACT_VERSION = '2.3';
