declare module '@bpmn-io/feel-editor' {
  import type { Extension } from '@codemirror/state';
  export interface Variable {
    name: string;
    detail?: string;
    info?: string;
    entries?: Variable[];
  }
  export default class FeelEditor {
    constructor(options: {
      container: HTMLElement;
      value: string;
      variables: Variable[];
      extensions: Extension[];
      contentAttributes: Record<string, string>;
      placeholder: string;
      onChange: (value: string) => void;
      onKeyDown: (event: KeyboardEvent) => void;
      onLint: (diagnostics: { message: string }[]) => void;
    });
    setValue(value: string): void;
    setVariables(variables: Variable[]): void;
  }
}
declare module '*.css' {
  const text: string;
  export default text;
}
