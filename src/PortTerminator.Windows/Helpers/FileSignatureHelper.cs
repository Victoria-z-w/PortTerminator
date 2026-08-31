using System.Security.Cryptography.X509Certificates;
using PortTerminator.Core.Models;

namespace PortTerminator.Windows.Helpers;

public static class FileSignatureHelper
{
    public static (string Publisher, SignatureStatus Status) Verify(string filePath)
    {
        try
        {
            var cert = X509Certificate.CreateFromSignedFile(filePath);
            if (cert is null)
                return ("未签名", SignatureStatus.Unsigned);

            var subject = cert.Subject;
            var publisher = ExtractPublisher(subject);
            return (publisher, SignatureStatus.Verified);
        }
        catch
        {
            return ("无法验证", SignatureStatus.CannotVerify);
        }
    }

    private static string ExtractPublisher(string subject)
    {
        var parts = subject.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                return trimmed[3..];
        }
        return subject;
    }
}
