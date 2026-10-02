namespace IELTop_Content_Server.Services;

/// <summary>
/// Exam protection service contract for digital watermarking and answer hiding.
/// </summary>
public interface IExamProtectionService
{
    string ProtectPaperJson(string rawJson, string paperId, string actor, string clientIp, bool hideAnswers);
    (bool Valid, string LicensedTo, string ClientIp, string IssuedAt, string Error) VerifyWatermark(string paperJson);
}

/// <summary>
/// Standard open-source fallback when the Center proprietary module is not loaded.
/// Passes through exam JSON cleanly without proprietary HMAC watermarking.
/// </summary>
public sealed class OpenSourceExamProtectionService : IExamProtectionService
{
    public string ProtectPaperJson(string rawJson, string paperId, string actor, string clientIp, bool hideAnswers)
    {
        return rawJson;
    }

    public (bool Valid, string LicensedTo, string ClientIp, string IssuedAt, string Error) VerifyWatermark(string paperJson)
    {
        return (false, string.Empty, string.Empty, string.Empty, "Watermark verification requires the Center proprietary module.");
    }
}
