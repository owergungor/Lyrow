using System;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.Core.Tests;

public class UpdateSelfContainedTests
{
    [Fact]
    public void UpdateService_InstallerArguments_AreCompletelySilentAndSuppressDialogs()
    {
        // GitHubUpdateService silent arguments must ensure silent binary replacement without wizard UI
        string args = GitHubUpdateService.SilentInstallerArguments;
        Assert.Contains("/VERYSILENT", args);
        Assert.Contains("/SUPPRESSMSGBOXES", args);
        Assert.Contains("/NORESTART", args);
        Assert.Contains("/SP-", args);
    }
}
