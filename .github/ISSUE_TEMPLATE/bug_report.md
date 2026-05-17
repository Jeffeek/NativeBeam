---
name: Bug Report
about: Report a bug to help us improve NativeBeam
title: '[BUG] '
labels: 'bug'
assignees: ''

---

## Bug Description
A clear and concise description of the bug.

## To Reproduce
Minimal code example that reproduces the issue:

```csharp
var renderer = new AotPdfRenderer();
var options = PdfOptions.Default with { ... };

byte[] pdf = await renderer.RenderAsync("<html>...</html>", options);
```

**Steps**:
1. Run the code above
2. Observe the behavior
3. See error/unexpected result

## Expected Behavior
A clear description of what you expected to happen.

## Actual Behavior
What actually happened instead.

## Environment
**NativeBeam.Pdf Version**: [e.g., 0.2.0]
**Target Framework**: [e.g., net9.0, net8.0]
**.NET SDK Version**: [e.g., 9.0.100]
**Operating System**: [e.g., Windows 11, Ubuntu 22.04, macOS 14]
**Runtime**: [e.g., CoreCLR, NativeAOT]
**Chromium Version**: [e.g., 124.0.6367.82 — output of `google-chrome --version`]

## Stack Trace
If applicable, include the full stack trace:

```
System.InvalidOperationException: ...
   at NativeBeam.Pdf.AotPdfRenderer.RenderAsync(...)
   at MyApp.GenerateReportAsync(...)
```

## Additional Context

### Configuration
If using custom options, include your full configuration:

```csharp
var options = PdfOptions.Default with
{
    PageWidth = 210,
    PageHeight = 297,
    // ... other options
};
```

### Frequency
- [ ] Happens every time
- [ ] Happens intermittently (race condition?)
- [ ] Happens only under specific conditions

### Impact
- [ ] Blocks development
- [ ] Production issue
- [ ] Performance degradation
- [ ] Minor inconvenience

### Workaround
If you found a workaround, please share it here to help others.

## Checklist
- [ ] I have searched existing issues to ensure this is not a duplicate
- [ ] I have provided a minimal code example that reproduces the issue
- [ ] I have included my environment details (.NET version, OS, Chromium version, NativeBeam version)
- [ ] I have included stack traces if applicable
