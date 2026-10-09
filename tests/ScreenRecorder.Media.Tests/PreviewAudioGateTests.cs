using ScreenRecorder.Media.Projects;

namespace ScreenRecorder.Media.Tests;

public class PreviewAudioGateTests
{
    [Fact]
    public void PreviewMutePreservesSourceAndCanResumeSound()
    {
        var gate = new PreviewAudioGate();
        var input = Enumerable.Repeat((byte)123, 8192).ToArray();
        var output = new byte[8192];
        gate.Copy(input, output); Assert.Equal(input, output);
        gate.Muted = true; gate.Copy(input, output); Assert.All(output, b => Assert.Equal(0, b));
        Assert.All(input, b => Assert.Equal(123, b));
        gate.Muted = false; gate.Copy(input, output); Assert.Equal(input, output);
    }
}
