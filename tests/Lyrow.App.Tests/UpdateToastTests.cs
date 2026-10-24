using System;
using System.Collections.Generic;
using Lyrow.App.Controls;
using Xunit;

namespace Lyrow.App.Tests;

public class UpdateToastTests
{
    [Fact]
    public void UpdateToastState_EnumContainsRequiredLifecycleStates()
    {
        var states = new HashSet<UpdateToastState>
        {
            UpdateToastState.Available,
            UpdateToastState.Downloading,
            UpdateToastState.Downloaded,
            UpdateToastState.Installing,
            UpdateToastState.Completed,
            UpdateToastState.DownloadFailed,
            UpdateToastState.InstallFailed
        };

        Assert.Equal(7, states.Count);
        Assert.Contains(UpdateToastState.Available, states);
        Assert.Contains(UpdateToastState.Downloading, states);
        Assert.Contains(UpdateToastState.Downloaded, states);
        Assert.Contains(UpdateToastState.Completed, states);
        Assert.Contains(UpdateToastState.DownloadFailed, states);
    }

    [Fact]
    public void ProgressToastHandle_InvokesCallbacksCorrectly()
    {
        double reportedPct = -1;
        string? reportedStatus = null;
        UpdateToastState? reportedState = null;
        bool retryTriggered = false;
        bool dismissed = false;

        var handle = new ProgressToastHandle(
            updateProgress: (pct, status) =>
            {
                reportedPct = pct;
                reportedStatus = status;
            },
            updateState: (state, msg, retry) =>
            {
                reportedState = state;
                if (retry != null) retry();
            },
            dismiss: () => dismissed = true);

        // 1. Report progress
        handle.ReportProgress(45.5, "Downloading 45%");
        Assert.Equal(45.5, reportedPct);
        Assert.Equal("Downloading 45%", reportedStatus);

        // 2. Report state transition with retry action
        handle.SetState(UpdateToastState.DownloadFailed, "Network timeout", () => retryTriggered = true);
        Assert.Equal(UpdateToastState.DownloadFailed, reportedState);
        Assert.True(retryTriggered);

        // 3. Dismiss
        handle.Dismiss();
        Assert.True(dismissed);
    }
}
