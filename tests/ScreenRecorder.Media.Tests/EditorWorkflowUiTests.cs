// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ScreenRecorder.Core.Localization;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Localization;
using ScreenRecorder.UI.Projects;
namespace ScreenRecorder.Media.Tests;
public sealed class EditorWorkflowUiTests
{
    [AvaloniaTheory]
    [InlineData(AppLanguage.ZhTw,1024,640,false)]
    [InlineData(AppLanguage.EnUs,1440,900,false)]
    [InlineData(AppLanguage.JaJp,1024,640,true)]
    public async Task ControlsStayInsidePreviewAndTextInputKeepsEditingKeys(AppLanguage language,int width,int height,bool dark)
    {
        var previous=LanguageManager.Instance.CurrentLanguage;LanguageManager.Instance.CurrentLanguage=language;
        var vm=new ProjectWorkspaceViewModel(new UiClient());await vm.RefreshAsync();vm.SelectedClip=vm.Clips[0];
        var window=new ProjectWorkspaceView{DataContext=vm,Width=width,Height=height,RequestedThemeVariant=dark?ThemeVariant.Dark:ThemeVariant.Light};
        try{
            window.Show();
            typeof(Window).GetMethod("HandleResized",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!
                .Invoke(window,[new Size(width,height),WindowResizeReason.User]);
            window.UpdateLayout();
            var preview=window.FindControl<StackPanel>("PreviewControls")!;
            Assert.True(preview.Bounds.Height>0);
            var meter=Assert.Single(preview.GetVisualDescendants().OfType<ProgressBar>());
            Assert.True(meter.TranslatePoint(default,preview)!.Value.X>=0);
            foreach(var button in preview.GetVisualDescendants().OfType<Button>().Where(b=>b.IsVisible))
            {
                var position=button.TranslatePoint(default,preview);
                Assert.NotNull(position);Assert.True(position!.Value.X+button.Bounds.Width<=preview.Bounds.Width+1);
            }
            if(!window.FindControl<Border>("InspectorPane")!.IsVisible)
                window.FindControl<Button>("PropertiesToggle")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            var editor=window.GetVisualDescendants().OfType<TextBox>().First(b=>b.IsEnabled&&b.IsEffectivelyVisible);
            Assert.True(editor.Focus());
            var before=vm.PlayheadTicks;
            window.KeyPressQwerty(Avalonia.Input.PhysicalKey.ArrowRight,Avalonia.Input.RawInputModifiers.None);
            window.KeyPressQwerty(Avalonia.Input.PhysicalKey.I,Avalonia.Input.RawInputModifiers.None);
            Assert.Equal(before,vm.PlayheadTicks);Assert.Null(vm.RangeStartTicks);
            if(Environment.GetEnvironmentVariable("OPENCAM_EDITOR_SCREENSHOTS") is {Length:>0} directory)
            {
                Directory.CreateDirectory(directory);
                using var bitmap=new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(width,height),new Vector(96,96));
                bitmap.Render(window);bitmap.Save(Path.Combine(directory,$"editor-{language}-{width}-{(dark?"dark":"light")}.png"));
            }
        }finally{window.Close();LanguageManager.Instance.CurrentLanguage=previous;}
    }
    private sealed class UiClient:IProjectClient
    {
        private readonly ProjectClip clip=new(){Id=Guid.NewGuid(),Name="教學示範：螢幕錄影與字幕 / Teaching demo",InPts=0,OutPts=10000};
        private readonly ProjectSnapshot state=new(Guid.NewGuid(),"OpenCam · 教材剪輯 / Course editing","/demo",0,0,ProjectMode.Ready,1,true,false){Canvas=new(1920,1080,new(30,1))};
        public Task<ProjectReply> SendAsync(string command,ProjectRequest request,CancellationToken ct=default)=>Task.FromResult(
            new ProjectReply(true,null,state,[clip]){TimelineClips=[new(clip.Id,0,100_000_000)]});
    }
}
