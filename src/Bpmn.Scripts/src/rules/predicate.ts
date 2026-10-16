/**
 * The declarative predicate language `rules/bpmn-rules.json` is written in.
 *
 * The table is shared with C# on purpose: a rule code that means one thing in a drag tooltip and
 * another in a validation report is worse than no rule code. Predicates are a small expression
 * tree rather than code precisely so both runtimes can interpret them identically — which only
 * holds if this evaluator and the C# one agree on every operator, so each one is defined once
 * here and tested against the real table.
 *
 * **A predicate is true when the rule is violated.** That inversion reads oddly in isolation and
 * is what the table means: a rule describes what is wrong, not what is required.
 */

/** One `op` node. Unknown shapes are rejected at evaluation time rather than assumed. */
export type Predicate =
  | { readonly op: 'and'; readonly args: readonly Predicate[] }
  | { readonly op: 'or'; readonly args: readonly Predicate[] }
  | { readonly op: 'not'; readonly arg: Predicate }
  | { readonly op: 'eq'; readonly field: string; readonly value: unknown }
  | { readonly op: 'in'; readonly field: string; readonly values: readonly unknown[] }
  | { readonly op: 'notIn'; readonly field: string; readonly values: readonly unknown[] }
  | { readonly op: 'exists'; readonly field: string }
  | { readonly op: 'fieldsEqual'; readonly left: string; readonly right: string }
  | { readonly op: 'fieldsNotEqual'; readonly left: string; readonly right: string };

/** The facts a predicate reads, flattened to the dotted paths the table uses. */
export type RuleFacts = Readonly<Record<string, unknown>>;

/** Thrown for a predicate this evaluator does not understand. */
export class PredicateError extends Error {}

/**
 * Evaluates a predicate against a set of facts.
 *
 * @returns `true` when the rule is **violated**.
 */
export function evaluate(predicate: Predicate, facts: RuleFacts): boolean {
  switch (predicate.op) {
    case 'and':
      // An empty `and` is vacuously true, which would make a malformed rule fire on everything.
      requireArgs(predicate.args, predicate.op);
      return predicate.args.every((arg) => evaluate(arg, facts));

    case 'or':
      requireArgs(predicate.args, predicate.op);
      return predicate.args.some((arg) => evaluate(arg, facts));

    case 'not':
      return !evaluate(predicate.arg, facts);

    case 'eq':
      return same(facts[predicate.field], predicate.value);

    case 'in':
      return predicate.values.some((value) => same(facts[predicate.field], value));

    case 'notIn':
      return !predicate.values.some((value) => same(facts[predicate.field], value));

    case 'exists':
      return present(facts[predicate.field]);

    case 'fieldsEqual':
      return comparePair(facts[predicate.left], facts[predicate.right], true);

    case 'fieldsNotEqual':
      return comparePair(facts[predicate.left], facts[predicate.right], false);
  }
}

/**
 * Compares two fields.
 *
 * A missing value never satisfies either comparison. Two absent Pool ids are not "in the same
 * Pool", and an absent one is not "in a different Pool" either — treating `undefined` as a value
 * would make MF-002 fire on every pair of unplaced shapes.
 */
function comparePair(left: unknown, right: unknown, wantEqual: boolean): boolean {
  if (!present(left) || !present(right)) return false;

  return wantEqual ? same(left, right) : !same(left, right);
}

function requireArgs(args: readonly Predicate[], op: string): void {
  if (args.length === 0) throw new PredicateError(`'${op}' needs at least one argument.`);
}

/** Absent means `null` or `undefined`; an empty string is a value, not an absence. */
const present = (value: unknown): boolean => value !== null && value !== undefined;

/** Strict equality, with absent values treated as interchangeable so `eq: null` works. */
const same = (left: unknown, right: unknown): boolean =>
  !present(left) && !present(right) ? true : left === right;
