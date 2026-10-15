// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Audio;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Core.Tests;

public class MicrophoneSilenceReminderTests
{
    [Fact]
    public void ContinuousSilenceWarnsAtThirtySecondsAndSoundClearsIt()
    {
        var reminder = new MicrophoneSilenceReminder();
        for (var second = 0; second < 30; second++)
            Assert.False(reminder.Update(true, AudioMeterState.Silent, TimeSpan.FromSeconds(second)));
        Assert.True(reminder.Update(true, AudioMeterState.Silent, TimeSpan.FromSeconds(30)));
        Assert.True(reminder.Update(true, AudioMeterState.Silent, TimeSpan.FromSeconds(31)));
        Assert.False(reminder.Update(true, AudioMeterState.Live, TimeSpan.FromSeconds(32)));
        Assert.False(reminder.Update(true, AudioMeterState.Silent, TimeSpan.FromSeconds(33)));
    }

    [Theory]
    [InlineData(AudioMeterState.Live)]
    [InlineData(AudioMeterState.Off)]
    [InlineData(AudioMeterState.Paused)]
    [InlineData(AudioMeterState.Unavailable)]
    public void NonSilentSamplesRestartTheWholeInterval(AudioMeterState state)
    {
        var reminder = new MicrophoneSilenceReminder();
        for (var second = 0; second < 30; second++)
            reminder.Update(true, AudioMeterState.Silent, TimeSpan.FromSeconds(second));
        Assert.False(reminder.Update(true, state, TimeSpan.FromSeconds(30)));
        Assert.False(reminder.Update(true, AudioMeterState.Silent, TimeSpan.FromSeconds(31)));
        Assert.False(reminder.Update(true, AudioMeterState.Silent, TimeSpan.FromSeconds(32)));
    }

    [Fact]
    public void MissingPollsClockRollbackAndDisableCannotAccumulateSilence()
    {
        var reminder = new MicrophoneSilenceReminder();
        reminder.Update(true, AudioMeterState.Silent, TimeSpan.Zero);
        Assert.False(reminder.Update(true, AudioMeterState.Silent, TimeSpan.FromSeconds(40)));
        for (var second = 41; second <= 70; second++)
            reminder.Update(true, AudioMeterState.Silent, TimeSpan.FromSeconds(second));
        Assert.True(reminder.Update(true, AudioMeterState.Silent, TimeSpan.FromSeconds(71)));
        Assert.False(reminder.Update(false, AudioMeterState.Silent, TimeSpan.FromSeconds(72)));
        Assert.False(reminder.Update(true, AudioMeterState.Silent, TimeSpan.FromSeconds(73)));
        Assert.False(reminder.Update(true, AudioMeterState.Silent, TimeSpan.Zero));
        reminder.Reset();
        Assert.False(reminder.Update(true, AudioMeterState.Silent, TimeSpan.FromSeconds(74)));
    }
}
