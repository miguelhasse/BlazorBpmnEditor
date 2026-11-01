# Bpmn.FeelSharp.Scripts

Private JavaScript SDK build project for the [FeelSharp extension](../Bpmn.FeelSharp/README.md).
Only TypeScript/esbuild development dependencies; no JavaScript parser or editor plugins.

```powershell
npm ci
npm run build -- --build-mode=Release
```

Edit `src\editor.ts`, not generated JavaScript. `dist\feelsharp-editor.js` provides native
selection/composition snapshots, guarded completion insertion, keyboard cancellation,
scroll synchronization, and DOM listener disposal. C# owns language logic and rendering.

The shared bridge also adapts to the Fluent extension's native textarea inside
`FluentTextArea`: shadow-root focus, native control discovery, overlay geometry,
accessibility forwarding and lifecycle cleanup.
