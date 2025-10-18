using System.Security;

namespace SRF.Network.Knx;

/// <summary>
/// <para>Library configuration and locator for further configuration files.
/// It's foreseen to be used with <see cref="Microsoft.Extensions.Options"/>.</para>
/// <para>See <see cref="SRF.Network.Knx.Domain"/> and <see cref="IDomainConfigurationFactory"/>
/// for ETS / project specific configuration and loading thereof.</para>
/// </summary>
public class KnxConfiguration
{
    public static readonly string SectionName = "Knx";

    public string ConnectionString { get; set; } = "Type=IpRouting";

    public string EtsGAExportFile { get; set; } = "GroupAddressExport.xml";

    public string KnxDomainConfigFile { get; set; } = "KnxDomainConfig.json";

    public string KnxMasterFolder { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

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
