# Icod.LineEditor.Tools

`lineeditor` is the distribution router for the managed Icod.LineEditor command suite.

```text
lineeditor ed [OPTION]...
lineeditor red [OPTION]...
lineeditor sed [OPTION]...
```

The router dispatches directly to the managed command implementations and does not spawn the standalone executables. The standalone `ed`, `red`, and `sed` commands remain first-class build and release outputs.
