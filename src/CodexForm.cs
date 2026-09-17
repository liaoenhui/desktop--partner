using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SilverWolfPet {
public partial class PetWindow {
    BitmapSource[] codexFrames;
    bool codexFormWorking,codexTaskRunning;
    double codexLastTaskEnd=Double.PositiveInfinity,codexFormStart=-100,codexFormEnd=-100;
    readonly Image cutin=new Image { IsHitTestVisible=false,Stretch=Stretch.Uniform,Opacity=0,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Bottom };
    readonly TranslateTransform cutinMove=new TranslateTransform();
    static BitmapSource ReadFormImage(string path) {
        var image=new BitmapImage();image.BeginInit();image.UriSource=new Uri(path);image.CacheOption=BitmapCacheOption.OnLoad;image.EndInit();image.Freeze();return image;
    }
    void LoadCodexForm(string root) {
        string dir=Path.Combine(root,"assets","codex-mode");
        var sheet=ReadFormImage(Path.Combine(dir,"transform-v4-keyframes.png"));
        var cross=new CroppedBitmap(sheet,new Int32Rect(0,350,512,340));
        var daily=BodyBounds(pack.Base);
        double height=daily.Height*800/pack.Base.PixelHeight,feet=daily.Bottom*800/pack.Base.PixelHeight;
        codexFrames=new[]{pack.Base,RegisterForm(cross,height,feet),RegisterForm(ReadFormImage(Path.Combine(dir,"invincible-original-style.png")),height,feet)};
        cutin.Source=ReadFormImage(Path.Combine(dir,"ultimate-cutin.jpg"));cutin.RenderTransform=cutinMove;
        Panel.SetZIndex(cutin,30);Scene.Children.Add(cutin);
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
            if(!codexFormWorking){codexFormWorking=true;codexFormStart=elapsed.Elapsed.TotalSeconds;Enter("idle");ScheduleIdle(elapsed.Elapsed.TotalSeconds);}
        } else codexLastTaskEnd=elapsed.Elapsed.TotalSeconds;
    }
    void ExitCodexForm() {
        codexFormWorking=false;codexFormEnd=-100;cutin.Opacity=0;
        Enter("idle");SetFrame(pack.Base);previous.Source=null;previous.Opacity=0;pet.Opacity=1;ScheduleIdle(elapsed.Elapsed.TotalSeconds);
        // Keep actual task state: repeated true updates cannot immediately re-enter.
    }
    bool WorkFormReply() {
        if(!codexFormWorking)return false;
        Enter("idle");Say(ChooseLine("working-touch",new[]{"别碰我，在忙呢。","这关还没结束，等我一下。","别挡操作，满级玩家正在处理。"}),3,3);return true;
    }
    BitmapSource CodexFormFrame(double now,BitmapSource daily) {
        cutin.Opacity=0;
        if(codexFrames==null||!String.IsNullOrEmpty(prefs.PackPath))return daily;
        if(codexFormWorking&&!codexTaskRunning&&now-codexLastTaskEnd>=600)ExitCodexForm();
        if(!codexFormWorking)return daily;
        double t=now-codexFormStart;
        if(t>=.55&&t<1.65) {
            double p=Bound((t-.55)/.32,0,1),q=Bound((t-1.25)/.4,0,1);
            double ease=p*p*(3-2*p),outEase=q*q*(3-2*q);
            cutin.Width=Math.Min(420,Width-12);cutin.Height=cutin.Width*.91;cutin.Margin=new Thickness(0,0,0,18);
            cutinMove.X=(1-ease)*32-outEase*18;cutin.Opacity=ease*(1-outEase);
        }
        if(t<.18)return pack.Base;
        if(t<1.12)return codexFrames[1];
        return codexFrames[2];
    }
    void TestCodexForm(string root) {
        if(codexFrames==null||codexFrames.Length!=3||!Object.ReferenceEquals(codexFrames[0],pack.Base))throw new Exception("Original daily frame not preserved");
        if(codexFormWorking)throw new Exception("Started in work form without event");
        SetCodexForm(true);double start=codexFormStart;
        if(CodexFormFrame(start+.3,pack.Base)!=codexFrames[1])throw new Exception("Cross pose missing");
        CodexFormFrame(start+.95,pack.Base);if(cutin.Opacity<.9)throw new Exception("Cutin missing");
        if(CodexFormFrame(start+2,pack.Base)!=codexFrames[2]||cutin.Opacity!=0)throw new Exception("Final form missing");
        SetCodexForm(false);double ended=codexLastTaskEnd;
        if(CodexFormFrame(ended+599,pack.Base)!=codexFrames[2])throw new Exception("Grace too short");
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
        SetCodexForm(true);bubble.Visibility=Visibility.Collapsed;BubbleSpace(0);
        SetFrame(CodexFormFrame(codexFormStart+.95,pack.Base));previous.Opacity=0;pet.Opacity=1;
        Scene.Measure(new Size(Width,Height));Scene.Arrange(new Rect(0,0,Width,Height));Scene.UpdateLayout();
        var shot=new RenderTargetBitmap((int)Width,(int)Height,96,96,PixelFormats.Pbgra32);shot.Render(Scene);
        var output=new PngBitmapEncoder();output.Frames.Add(BitmapFrame.Create(shot));using(var f=File.Create(Path.Combine(root,"qa","cutin.png")))output.Save(f);
        ExitCodexForm();SetCodexForm(false);
    }
}
}
