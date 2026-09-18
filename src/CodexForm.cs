using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SilverWolfPet {
public partial class PetWindow {
    BitmapSource[] codexFrames;
    Dictionary<string,AnimationClip> codexWorkClips;
    readonly HashSet<string> codexStateSpoken=new HashSet<string>();
    bool codexFormWorking,codexTaskRunning,codexActivationAnnounced,codexCutinVisible;
    bool codexStateBubblePending;
    string codexWorkState="thinking";
    double codexWorkStateStart;
    double codexLastTaskEnd=Double.PositiveInfinity,codexFormStart=-100,codexFormEnd=-100;
    readonly Image cutin=new Image { IsHitTestVisible=false,Stretch=Stretch.Uniform,Opacity=0,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Bottom };
    readonly TranslateTransform cutinMove=new TranslateTransform();
    readonly Popup cutinPopup=new Popup { AllowsTransparency=true,StaysOpen=true,IsHitTestVisible=false,Placement=PlacementMode.Absolute };
    readonly Canvas cutinCanvas=new Canvas { IsHitTestVisible=false };
    static BitmapSource ReadFormImage(string path) {
        var image=new BitmapImage();image.BeginInit();image.UriSource=new Uri(path);image.CacheOption=BitmapCacheOption.OnLoad;image.EndInit();image.Freeze();return image;
    }
    void LoadCodexForm(string root) {
        string dir=Path.Combine(root,"assets","codex-mode");
        var cross=ReadFormImage(Path.Combine(dir,"cross-original-action.png"));
        var daily=BodyBounds(pack.Base);
        double height=daily.Height*800/pack.Base.PixelHeight,feet=daily.Bottom*800/pack.Base.PixelHeight;
        codexFrames=new[]{pack.Base,RegisterForm(cross,height,feet),RegisterForm(ReadFormImage(Path.Combine(dir,"invincible-clean-foot.png")),height,feet)};
        codexWorkClips=new Dictionary<string,AnimationClip>();
        string work=Path.Combine(dir,"work-states");
        foreach(var spec in new[]{
            new {Key="thinking",Ms=620},new {Key="executing",Ms=260},new {Key="waiting-input",Ms=650},new {Key="failed",Ms=230},new {Key="completed",Ms=430}}) {
            var one=RegisterForm(ReadFormImage(Path.Combine(work,spec.Key+"-frame-1.png")),height,feet);
            var two=RegisterForm(ReadFormImage(Path.Combine(work,spec.Key+"-frame-2.png")),height,feet);
            codexWorkClips[spec.Key]=new AnimationClip {Frames=new[]{one,two,two,one},Milliseconds=new[]{spec.Ms,spec.Ms,spec.Ms,spec.Ms},Loop=true};
        }
        cutin.Source=ReadFormImage(Path.Combine(dir,"ultimate-cutin-transparent.png"));cutin.RenderTransform=cutinMove;
        cutinCanvas.Children.Add(cutin);cutinPopup.Child=cutinCanvas;
        Closing+=delegate {cutinPopup.IsOpen=false;};
        IsVisibleChanged+=delegate {if(!IsVisible)cutinPopup.IsOpen=false;};
    }
    static Rect BodyBounds(BitmapSource source) {
        var converted=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);
        byte[] pixels=new byte[source.PixelWidth*source.PixelHeight*4];converted.CopyPixels(pixels,source.PixelWidth*4,0);
        int top=source.PixelHeight,bottom=0;
        for(int y=0;y<source.PixelHeight;y++)for(int x=source.PixelWidth*35/100;x<source.PixelWidth*65/100;x++)
            if(pixels[(y*source.PixelWidth+x)*4+3]>100){top=Math.Min(top,y);bottom=Math.Max(bottom,y+1);}
        if(bottom<=top)throw new Exception("Empty transformation frame");
        return new Rect(0,top,source.PixelWidth,bottom-top);
    }
    static BitmapSource RegisterForm(BitmapSource source,double height,double feet) {
        Rect box=BodyBounds(source);double factor=height/box.Height;
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen())dc.DrawImage(source,new Rect(640-source.PixelWidth*.5*factor,feet-box.Bottom*factor,source.PixelWidth*factor,source.PixelHeight*factor));
        var target=new RenderTargetBitmap(1280,800,96,96,PixelFormats.Pbgra32);target.Render(visual);target.Freeze();return target;
    }
    void SetCodexForm(bool working) {
        if(codexTaskRunning==working)return;
        codexTaskRunning=working;
        if(working) {
            codexLastTaskEnd=Double.PositiveInfinity;
            codexStateSpoken.Clear();codexStateBubblePending=true;codexWorkState="thinking";codexWorkStateStart=elapsed.Elapsed.TotalSeconds;
            if(!codexFormWorking){codexFormWorking=true;codexActivationAnnounced=false;codexFormStart=elapsed.Elapsed.TotalSeconds;Enter("idle");ScheduleIdle(elapsed.Elapsed.TotalSeconds);}
        } else codexLastTaskEnd=elapsed.Elapsed.TotalSeconds;
    }
    void ExitCodexForm() {
        codexFormWorking=false;codexActivationAnnounced=false;codexCutinVisible=false;codexStateBubblePending=false;codexFormEnd=-100;cutin.Opacity=0;cutinPopup.IsOpen=false;
        Enter("idle");SetFrame(pack.Base);previous.Source=null;previous.Opacity=0;pet.Opacity=1;ScheduleIdle(elapsed.Elapsed.TotalSeconds);
        // Keep actual task state: repeated true updates cannot immediately re-enter.
    }
    bool WorkFormReply() {
        if(!codexFormWorking)return false;
        Enter("idle");Say(ChooseLine("working-touch",new[]{"别碰我，在忙呢。","这关还没结束，等我一下。","别挡操作，满级玩家正在处理。"}),3,3);return true;
    }
    void ApplyCodexCutinVisibility() {
        if(!codexCutinVisible)return;
        previous.Opacity=0;
        pet.Opacity=0;
    }
    void SetCodexWorkState(string next) {
        if(codexWorkClips==null||String.IsNullOrEmpty(next)||!codexWorkClips.ContainsKey(next))return;
        if(codexWorkState==next)return;
        codexWorkState=next;codexWorkStateStart=elapsed.Elapsed.TotalSeconds;codexStateBubblePending=!codexStateSpoken.Contains(next);
        if(codexFormWorking&&elapsed.Elapsed.TotalSeconds-codexFormStart>=1.65)SayCodexWorkState();
    }
    void SayCodexWorkState() {
        if(!codexStateBubblePending||codexStateSpoken.Contains(codexWorkState))return;
        string line;double seconds;int priority;
        switch(codexWorkState) {
            case "executing":line=ChooseLine("codex-executing",new[]{"操作中，别挡屏幕。","指令收到，开始跑流程。"});seconds=4;priority=3;break;
            case "waiting-input":line=ChooseLine("codex-waiting",new[]{"轮到你了，给个指令。","这里需要你的选择。"});seconds=10;priority=5;break;
            case "failed":line=ChooseLine("codex-failed",new[]{"啧，出错了。换条路线。","这步没通，我重新规划。"});seconds=7;priority=5;break;
            case "completed":line=ChooseLine("codex-completed",new[]{"通关。任务已经完成。","搞定，来验收结果。"});seconds=7;priority=5;break;
            default:line=ChooseLine("codex-thinking",new[]{"嗯……让我算算。","先别催，正在找最优解。"});seconds=4;priority=3;break;
        }
        codexStateSpoken.Add(codexWorkState);codexStateBubblePending=false;Say(line,seconds,priority);
    }
    BitmapSource CodexWorkFrame(double now) {
        AnimationClip clip;
        return codexWorkClips!=null&&codexWorkClips.TryGetValue(codexWorkState,out clip)?clip.At(Math.Max(0,now-codexWorkStateStart)):codexFrames[2];
    }
    BitmapSource CodexFormFrame(double now,BitmapSource daily) {
        cutin.Opacity=0;
        bool showCutin=codexFormWorking&&now-codexFormStart>=.55&&now-codexFormStart<1.65;
        codexCutinVisible=showCutin;
        if(!showCutin)cutinPopup.IsOpen=false;
        if(codexFrames==null||!String.IsNullOrEmpty(prefs.PackPath))return daily;
        if(codexFormWorking&&!codexTaskRunning&&now-codexLastTaskEnd>=600)ExitCodexForm();
        if(!codexFormWorking)return daily;
        double t=now-codexFormStart;
        if(t>=.55&&t<1.65) {
            double p=Bound((t-.55)/.32,0,1),q=Bound((t-1.25)/.4,0,1);
            double ease=p*p*(3-2*p),outEase=q*q*(3-2*q);
            var art=(BitmapSource)cutin.Source;
            var area=System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)((Left+Width/2)*dpiX),(int)((PetTop+70+prefs.Size/2)*dpiY))).WorkingArea;
            double maxW=area.Width/dpiX,maxH=area.Height/dpiY,desiredSide=prefs.Size*1.02;
            double artScale=Math.Min(desiredSide/art.PixelWidth,desiredSide/art.PixelHeight);
            artScale=Math.Min(artScale,Math.Min((maxW-48)/art.PixelWidth,(maxH-48)/art.PixelHeight));
            cutin.Width=art.PixelWidth*artScale;cutin.Height=art.PixelHeight*artScale;cutin.Margin=new Thickness(0);
            Canvas.SetLeft(cutin,16);Canvas.SetTop(cutin,16);cutinCanvas.Width=cutin.Width+32;cutinCanvas.Height=cutin.Height+32;
            // Center the complete illustration on the pet. The artwork character is
            // intentionally smaller because its background effects occupy most of it.
            cutinPopup.HorizontalOffset=Bound(Left+Width/2-cutinCanvas.Width/2,area.Left/dpiX,area.Right/dpiX-cutinCanvas.Width);
            cutinPopup.VerticalOffset=Bound(PetTop+70+prefs.Size/2-cutinCanvas.Height/2,area.Top/dpiY,area.Bottom/dpiY-cutinCanvas.Height);
            cutinMove.X=(1-ease)*12-outEase*8;cutin.Opacity=ease*(1-outEase);
            if(!testing&&IsVisible)cutinPopup.IsOpen=true;
        }
        if(t<.18)return pack.Base;
        if(t<1.12)return codexFrames[1];
        if(t>=1.65&&!codexActivationAnnounced) {
            codexActivationAnnounced=true;
            Say("无敌玩家，启动！",4,4);
        }
        if(codexStateBubblePending&&t>=4.8)SayCodexWorkState();
        return CodexWorkFrame(now);
    }
    void TestCodexForm(string root) {
        var artCheck=new FormatConvertedBitmap((BitmapSource)cutin.Source,PixelFormats.Bgra32,null,0);
        byte[] artPixels=new byte[artCheck.PixelWidth*artCheck.PixelHeight*4];artCheck.CopyPixels(artPixels,artCheck.PixelWidth*4,0);
        int clear=0;for(int i=3;i<artPixels.Length;i+=4)if(artPixels[i]<10)clear++;
        if(clear<artCheck.PixelWidth*artCheck.PixelHeight/5)throw new Exception("Cutin background is not transparent");
        if(codexFrames==null||codexFrames.Length!=3||!Object.ReferenceEquals(codexFrames[0],pack.Base)||codexWorkClips==null||codexWorkClips.Count!=5||codexWorkClips.Any(x=>x.Value.Frames.Length!=4||x.Value.Frames.Distinct().Count()!=2))throw new Exception("Original daily frame or work animations missing");
        if(codexFormWorking)throw new Exception("Started in work form without event");
        SetCodexForm(true);double start=codexFormStart;
        if(CodexFormFrame(start+.3,pack.Base)!=codexFrames[1])throw new Exception("Cross pose missing");
        CodexFormFrame(start+.95,pack.Base);pet.Opacity=previous.Opacity=1;ApplyCodexCutinVisibility();
        if(cutin.Opacity<.9||!codexCutinVisible||pet.Opacity!=0||previous.Opacity!=0||Math.Max(cutin.Width,cutin.Height)>prefs.Size*1.021)throw new Exception("Cutin missing, oversized, or character still visible");
        var finalFrame=CodexFormFrame(start+2,pack.Base);
        if(!codexWorkClips["thinking"].Frames.Contains(finalFrame)||cutin.Opacity!=0||codexCutinVisible||words.Text!="无敌玩家，启动！")throw new Exception("Final form announcement or work animation missing");
        double announcedUntil=bubbleUntil;CodexFormFrame(start+3,pack.Base);if(bubbleUntil!=announcedUntil)throw new Exception("Final form announcement repeated");
        SetCodexForm(false);double ended=codexLastTaskEnd;
        if(!codexWorkClips["thinking"].Frames.Contains(CodexFormFrame(ended+599,pack.Base)))throw new Exception("Grace too short");
        SetCodexForm(true);if(start!=codexFormStart)throw new Exception("Repeated transform");
        ExitCodexForm();SetCodexForm(true);if(codexFormWorking)throw new Exception("Manual exit undone");
        SetCodexForm(false);SetCodexForm(true);if(!codexFormWorking)throw new Exception("New task failed");
        TouchReaction(TouchZone.Head,"tap");if(state!="idle")throw new Exception("Work touch started normal action");
        SetCodexForm(false);ended=codexLastTaskEnd;CodexFormFrame(ended+601,pack.Base);if(codexFormWorking)throw new Exception("Grace did not expire");
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen()) {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(34,38,52)),null,new Rect(0,0,760,240));
            for(int i=0;i<2;i++){double size=i==0?160:180;dc.DrawImage(pack.Base,new Rect(i*380+8,24,size*pack.Base.PixelWidth/pack.Base.PixelHeight,size));dc.DrawImage(codexFrames[2],new Rect(i*380+85,24,size*1.6,size));}
        }
        var sample=new RenderTargetBitmap(760,240,96,96,PixelFormats.Pbgra32);sample.Render(visual);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(sample));
        Directory.CreateDirectory(Path.Combine(root,"qa"));using(var f=File.Create(Path.Combine(root,"qa","forms-size-check.png")))png.Save(f);
        var comparison=new DrawingVisual();using(var dc=comparison.RenderOpen()) {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(34,38,52)),null,new Rect(0,0,900,430));
            for(int row=0;row<2;row++)for(int col=0;col<3;col++){
                double size=row==0?160:180;BitmapSource frame=codexFrames[col];double w=size*frame.PixelWidth/frame.PixelHeight;
                dc.DrawImage(frame,new Rect(col*300+(300-w)/2,row*210+15,w,size));
            }
        }
        var compare=new RenderTargetBitmap(900,430,96,96,PixelFormats.Pbgra32);compare.Render(comparison);
        var comparePng=new PngBitmapEncoder();comparePng.Frames.Add(BitmapFrame.Create(compare));using(var f=File.Create(Path.Combine(root,"qa","daily-cross-working.png")))comparePng.Save(f);
        var stateNames=new[]{"thinking","executing","waiting-input","failed","completed"};
        var stateSheet=new DrawingVisual();using(var dc=stateSheet.RenderOpen()) {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(34,38,52)),null,new Rect(0,0,1000,440));
            for(int row=0;row<2;row++)for(int col=0;col<stateNames.Length;col++) {
                double size=row==0?160:180;var clip=codexWorkClips[stateNames[col]];var frame=clip.Frames[row==0?0:1];double w=size*frame.PixelWidth/frame.PixelHeight;
                dc.DrawImage(frame,new Rect(col*200+(200-w)/2,row*220+12,w,size));
                var label=new FormattedText(stateNames[col]+" · "+size.ToString("0"),System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),13,Brushes.White);
                dc.DrawText(label,new Point(col*200+(200-label.Width)/2,row*220+184));
            }
        }
        var statesSample=new RenderTargetBitmap(1000,440,96,96,PixelFormats.Pbgra32);statesSample.Render(stateSheet);
        var statesPng=new PngBitmapEncoder();statesPng.Frames.Add(BitmapFrame.Create(statesSample));using(var f=File.Create(Path.Combine(root,"qa","work-states-size-check.png")))statesPng.Save(f);
        SetCodexForm(true);bubble.Visibility=Visibility.Collapsed;BubbleSpace(0);
        SetFrame(CodexFormFrame(codexFormStart+.95,pack.Base));previous.Opacity=0;pet.Opacity=1;
        Scene.Measure(new Size(Width,Height));Scene.Arrange(new Rect(0,0,Width,Height));Scene.UpdateLayout();
        cutinCanvas.Measure(new Size(cutinCanvas.Width,cutinCanvas.Height));cutinCanvas.Arrange(new Rect(0,0,cutinCanvas.Width,cutinCanvas.Height));cutinCanvas.UpdateLayout();
        var shot=new RenderTargetBitmap((int)Math.Ceiling(cutinCanvas.Width),(int)Math.Ceiling(cutinCanvas.Height),96,96,PixelFormats.Pbgra32);shot.Render(cutinCanvas);
        var output=new PngBitmapEncoder();output.Frames.Add(BitmapFrame.Create(shot));using(var f=File.Create(Path.Combine(root,"qa","cutin.png")))output.Save(f);
        ExitCodexForm();SetCodexForm(false);
    }
}
}
