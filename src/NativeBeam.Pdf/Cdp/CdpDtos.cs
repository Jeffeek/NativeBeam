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

// --- Events --------------------------------------------------------------

internal sealed record PageLoadEventFiredEvent(double Timestamp);

// --- Error envelope ------------------------------------------------------

internal sealed record CdpErrorPayload(int Code, string Message);
