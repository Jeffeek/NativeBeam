---
name: Feature Request
about: Suggest a new feature or enhancement for NativeBeam
title: '[FEATURE] '
labels: 'enhancement'
assignees: ''

---

## Problem / Use Case
A clear description of the problem this feature would solve or the use case it enables.

**Example**:
"I need to render PDFs with custom headers and footers per page, but NativeBeam doesn't currently support page-level templates..."

## Proposed Solution

### API Design
How would you like to use this feature? Show your ideal API:

```csharp
var options = PdfOptions.Default with
{
    HeaderTemplate = "<div style='font-size:10px'>Page <span class='pageNumber'></span></div>",
    FooterTemplate = "<div style='font-size:10px; text-align:center'>Confidential</div>",
    DisplayHeaderFooter = true,
};

byte[] pdf = await renderer.RenderAsync(html, options);
```

### Behavior
Describe how this feature should behave:
- What should happen when...?
- How should errors be handled?
- What are the performance characteristics?
- Should this be opt-in or opt-out?
- Is this AOT-compatible? (NativeBeam requires all public API to be AOT-safe)

## Alternatives Considered

Have you considered other approaches? Why is your proposed solution better?

1. **Alternative 1**: [Description]
   - Pros: ...
   - Cons: ...

2. **Alternative 2**: [Description]
   - Pros: ...
   - Cons: ...

## Workaround (if any)
If you have a workaround for this feature, please share it:

```csharp
// Current workaround (if any)
```

## Impact and Priority

### Who benefits from this feature?
- [ ] All NativeBeam users
- [ ] Users in specific scenarios (which ones?)
- [ ] Advanced users only
- [ ] Enterprise/production users

### Priority (from your perspective)
- [ ] Critical - Blocking my project
- [ ] High - Would significantly improve my workflow
- [ ] Medium - Nice to have
- [ ] Low - Future enhancement

### Breaking Changes
- [ ] This feature would require breaking changes
- [ ] This feature can be added without breaking changes
- [ ] Unsure

## Additional Context

### Related Issues/Features
- Related to #...
- Builds on #...
- Blocks #...

### AOT Compatibility
NativeBeam is Native AOT-only. Any new public API must not use reflection, `dynamic`, or types that prevent trimming.
- [ ] I have considered AOT/trimming implications
- [ ] I am unsure — happy to discuss in the issue

### References
Any relevant documentation, blog posts, or examples:
- [Link to Chromium DevTools Protocol docs]
- [Blog post about this pattern]

## Checklist
- [ ] I have searched existing issues/feature requests to avoid duplicates
- [ ] I have provided a clear use case and motivation
- [ ] I have proposed an API design (or described desired behavior)
- [ ] I have considered alternatives and trade-offs
- [ ] I am willing to contribute to this feature (optional but appreciated!)
