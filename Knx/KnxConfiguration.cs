using System.Security;

namespace SRF.Network.Knx;

public class KnxConfiguration
{
    public static readonly string SectionName = "Knx";

    public string ConnectionString { get; set; } = "Type=IpRouting";

    public class CommSecuritySettings
    {
        public bool UseCommSecurity => !string.IsNullOrEmpty(KeyRingFile);
        public string? KeyRingFile { get; set; }
        public SecureString? KeyRingPassword { get; set; }
        public string? SequenceControlFile { get; set; }
        public SecureString? SequenceControlPassword { get; set; }
    }
    public CommSecuritySettings CommSecurity { get; set; } = new CommSecuritySettings();
}
