import FeelEditor from '@bpmn-io/feel-editor';
import type { Variable } from '@bpmn-io/feel-editor';
import { HighlightStyle, syntaxHighlighting } from '@codemirror/language';
import { tags } from '@lezer/highlight';
import { Compartment, EditorState } from '@codemirror/state';
import { EditorView, ViewPlugin } from '@codemirror/view';
import css from './feel-editor.css';

const style = document.createElement('style');
style.textContent = css;
document.head.append(style);

interface DotNetReference {
  invokeMethodAsync(method: string, ...args: unknown[]): Promise<unknown>;
}
interface VariableHint {
  name: string;
  detail: string | null;
  info: string | null;
  entries: VariableHint[];
}
interface Options {
  value: string;
  variables: VariableHint[];
  readOnly: boolean;
  label: string;
  describedBy: string;
  invalid: boolean;
  placeholder: string;
  revision: number;
}
export interface EditorHandle {
  setValue(value: string, revision: number): void;
  setVariables(variables: VariableHint[]): void;
  setReadOnly(readOnly: boolean): void;
  setAttributes(label: string, describedBy: string, invalid: boolean): void;
  dispose(): void;
}
function mapVariables(variables: VariableHint[]): Variable[] {
  return variables.map(variable => ({
    name: variable.name,
    ...(variable.detail === null ? {} : { detail: variable.detail }),
    ...(variable.info === null ? {} : { info: variable.info }),
    entries: mapVariables(variable.entries),
  }));
}

export function create(container: HTMLElement, dotnet: DotNetReference, options: Options): EditorHandle {
  let view: EditorView | undefined;
  let value = options.value;
  let revision = options.revision;
  let readOnly = options.readOnly;
  let disposed = false;
  let labelAttribute = options.label;
  let descriptionAttribute = options.describedBy;
  let invalidAttribute = options.invalid;
  let committed = value;
  const editable = new Compartment();
  const attributes = new Compartment();
  const notify = (method: string, ...args: unknown[]): void => {
    if (!disposed) void dotnet.invokeMethodAsync(method, ...args).catch(() => { /* Circuit closed. */ });
  };
  const commit = (): void => {
    if (readOnly || value === committed) return;
    committed = value;
    notify('CommitExpressionAsync', value, revision);
  };
  const editor = new FeelEditor({
    container,
    value,
    variables: mapVariables(options.variables),
    placeholder: options.placeholder,
    contentAttributes: {},
    extensions: [
      EditorView.lineWrapping,
      syntaxHighlighting(HighlightStyle.define([
        { tag: [tags.keyword, tags.bool, tags.null, tags.typeName], class: 'bpmn-feel-keyword' },
        { tag: [tags.string, tags.special(tags.string)], class: 'bpmn-feel-string' },
        { tag: tags.number, class: 'bpmn-feel-number' },
        { tag: [tags.variableName, tags.propertyName, tags.operator], class: 'bpmn-feel-name' },
        { tag: [tags.lineComment, tags.blockComment], class: 'bpmn-feel-comment' },
      ])),
      editable.of([EditorView.editable.of(!readOnly), EditorState.readOnly.of(readOnly)]),
      attributes.of(EditorView.contentAttributes.of({
        'aria-label': options.label,
        'aria-describedby': options.describedBy,
        'aria-invalid': String(options.invalid),
      })),
      ViewPlugin.define(editorView => { view = editorView; return {}; }),
    ],
    onChange: text => { value = text; },
    onKeyDown: event => {
      if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
        event.preventDefault();
        event.stopPropagation();
        commit();
      }
    },
    onLint: diagnostics => { notify('ReportLintAsync', diagnostics.map(item => item.message), revision); },
  });
  const blur = (event: FocusEvent): void => {
    if (!(event.relatedTarget instanceof Node) || !container.contains(event.relatedTarget)) commit();
  };
  container.addEventListener('focusout', blur);
  // Blazor may remove the element before .NET disposal can reach JavaScript.
  const observer = new MutationObserver(() => { if (!container.isConnected) dispose(); });
  observer.observe(document.body, { childList: true, subtree: true });
  const dispose = (): void => {
    if (disposed) return;
    disposed = true;
    observer.disconnect();
    container.removeEventListener('focusout', blur);
    view?.destroy();
  };
  return {
    setValue(text, nextRevision): void {
      if (disposed) return;
      revision = nextRevision;
      committed = text;
      value = text;
      if (view?.state.doc.toString() !== text) editor.setValue(text);
    },
    setVariables(variables): void { if (!disposed) editor.setVariables(mapVariables(variables)); },
    setReadOnly(nextReadOnly): void {
      if (disposed || readOnly === nextReadOnly) return;
      readOnly = nextReadOnly;
      view?.dispatch({ effects: editable.reconfigure([
        EditorView.editable.of(!readOnly), EditorState.readOnly.of(readOnly),
      ]) });
    },
    setAttributes(label, describedBy, invalid): void {
      if (disposed || label === labelAttribute && describedBy === descriptionAttribute && invalid === invalidAttribute) return;
      labelAttribute = label;
      descriptionAttribute = describedBy;
      invalidAttribute = invalid;
      view?.dispatch({ effects: attributes.reconfigure(EditorView.contentAttributes.of({
        'aria-label': label, 'aria-describedby': describedBy, 'aria-invalid': String(invalid),
      })) });
    },
    dispose,
  };
}
