namespace NativeBeam.Pdf.Cdp;

// --- Command parameters --------------------------------------------------

internal sealed record TargetCreateTargetParams(string Url);

internal sealed record TargetAttachToTargetParams(string TargetId, bool Flatten);

internal sealed record TargetCloseTargetParams(string TargetId);

internal sealed record PageSetDocumentContentParams(string FrameId, string Html);

internal sealed record PagePrintToPdfParams(
    bool PrintBackground,
    bool Landscape,
    double Scale,
    double PaperWidth,
    double PaperHeight,
    double MarginTop,
    double MarginBottom,
    double MarginLeft,
    double MarginRight);

// --- Command results -----------------------------------------------------

internal sealed record TargetCreateTargetResult(string TargetId);

internal sealed record TargetAttachToTargetResult(string SessionId);

internal sealed record PageGetFrameTreeResult(FrameTreeNode FrameTree);

internal sealed record FrameTreeNode(FrameInfo Frame);

internal sealed record FrameInfo(string Id);

internal sealed record PagePrintToPdfResult(string Data);

// --- Runtime.evaluate ----------------------------------------------------

internal sealed record RuntimeEvaluateParams(
    string Expression,
    bool ReturnByValue = true,
    bool AwaitPromise = true,
    bool UserGesture = false);

internal sealed record RuntimeEvaluateResult(
    CdpRemoteObject Result,
    CdpExceptionDetails? ExceptionDetails);

internal sealed record CdpRemoteObject(
    string Type,
    string? Subtype = null,
    string? ClassName = null,
    System.Text.Json.JsonElement Value = default,
    string? Description = null);

internal sealed record CdpExceptionDetails(
    int ExceptionId,
    string Text,
    int LineNumber,
    int ColumnNumber,
    CdpRemoteObject? Exception = null);

// --- Events --------------------------------------------------------------

internal sealed record PageLoadEventFiredEvent(double Timestamp);

// --- Error envelope ------------------------------------------------------

internal sealed record CdpErrorPayload(int Code, string Message);
