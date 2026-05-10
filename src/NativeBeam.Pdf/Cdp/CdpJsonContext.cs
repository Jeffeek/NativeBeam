using System.Text.Json.Serialization;

namespace NativeBeam.Pdf.Cdp;

/// <summary>
/// Source-generated System.Text.Json context covering every CDP DTO used by
/// NativeBeam. No reflection-based serialization is performed at runtime;
/// every type listed here gets a compile-time <see cref="JsonTypeInfo"/>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(TargetCreateTargetParams))]
[JsonSerializable(typeof(TargetAttachToTargetParams))]
[JsonSerializable(typeof(TargetCloseTargetParams))]
[JsonSerializable(typeof(PageSetDocumentContentParams))]
[JsonSerializable(typeof(PagePrintToPdfParams))]
[JsonSerializable(typeof(TargetCreateTargetResult))]
[JsonSerializable(typeof(TargetAttachToTargetResult))]
[JsonSerializable(typeof(PageGetFrameTreeResult))]
[JsonSerializable(typeof(FrameTreeNode))]
[JsonSerializable(typeof(FrameInfo))]
[JsonSerializable(typeof(PagePrintToPdfResult))]
[JsonSerializable(typeof(CdpErrorPayload))]
internal sealed partial class CdpJsonContext : JsonSerializerContext
{
}
