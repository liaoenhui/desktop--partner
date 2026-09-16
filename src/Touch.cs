using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SilverWolfPet {
public enum TouchZone { Head, Face, Goggles, Body }
public partial class PetWindow {
    TouchZone pressedZone,lastTouchZone;
    bool heldReaction;
    double touchStarted,lastTouchTime=-10;
    int touchCombo;
    double touchFeedbackUntil;
    public static TouchZone ZoneAt(double x,double y) {
        if(x>=.25&&x<=.77&&y>=.14&&y<.25)return TouchZone.Goggles;
        if(x>=.27&&x<=.76&&y>=.25&&y<.43)return TouchZone.Face;
        return y<.43?TouchZone.Head:TouchZone.Body;
    }
    TouchZone HitZone(Point p) {
        double fit=Math.Min(pet.ActualWidth/cachedBitmap.PixelWidth,pet.ActualHeight/cachedBitmap.PixelHeight);
        double x=(p.X-(pet.ActualWidth-cachedBitmap.PixelWidth*fit)/2)/(cachedBitmap.PixelWidth*fit);
        double y=(p.Y-(pet.ActualHeight-cachedBitmap.PixelHeight*fit)/2)/(cachedBitmap.PixelHeight*fit);
        return ZoneAt(x,y);
    }
    void BeginTouch(Point p) {
        pressedZone=HitZone(p);touchStarted=elapsed.Elapsed.TotalSeconds;heldReaction=false;
        inputAnimating=false;keyboard.Visibility=Visibility.Collapsed;
        if(pressedZone!=lastTouchZone)touchCombo=0;
        Enter(pressedZone==TouchZone.Head?"touchHead":pressedZone==TouchZone.Face?"annoyed":pressedZone==TouchZone.Goggles?"headpat":"touchBody");
        ImmediateTouchFrame();
    }
    void CheckTouchHold(double now) {
        if(!pressed||dragging||heldReaction||now<touchStarted+.7)return;
        heldReaction=true;touchCombo=0;lastTouchTime=-10;TouchReaction(pressedZone,"hold");
    }
    void EndTouch(double now) {
        if(heldReaction)return;
        touchCombo=pressedZone==lastTouchZone&&now-lastTouchTime<=1.5?touchCombo+1:1;
        lastTouchZone=pressedZone;lastTouchTime=now;
        TouchReaction(pressedZone,touchCombo>=4?"repeat":"tap");
    }
    void TouchReaction(TouchZone zone,string gesture) {
        string part=zone.ToString().ToLowerInvariant(),key="touch."+part+"."+gesture;
        string message,action;
        if(zone==TouchZone.Head) {
            action="touchHead";
            message=gesture=="hold"?"还不松手？……行，就再给你一小会儿。":gesture=="repeat"?"摸头连击？隐藏成就已经拿到了。":"轻一点，满级玩家也要整理发型。";
        } else if(zone==TouchZone.Face) {
            action="annoyed";
            message=gesture=="hold"?"喂，别一直捏着。我的脸不是暂停键。":gesture=="repeat"?"连点器是吧？再戳就判你操作犯规。":"脸上可没有隐藏按钮。找我有事？";
        } else if(zone==TouchZone.Goggles) {
            action=gesture=="tap"?"headpat":"annoyed";
            message=gesture=="hold"?"别按着镜片，视野都被你挡住了。":gesture=="repeat"?"装备检查一次就够了，别反复点。":"想看我的装备？先说好，只能看，不能拆。";
        } else {
            action="touchBody";
            message=gesture=="hold"?"按住也不会加速加载。有事直接说。":gesture=="repeat"?"连击达成。鼠标耐久还够吗？":Pick(pack.Spec.clickLines,"这局，带你通关。");
        }
        string[] custom;if(pack.Spec.actionLines!=null&&pack.Spec.actionLines.TryGetValue(key,out custom))message=ChooseLine(key,custom);
        Enter(action);Say(message,gesture=="tap"?4:5,3);ScheduleIdle(elapsed.Elapsed.TotalSeconds);ImmediateTouchFrame();
    }
    void ImmediateTouchFrame() { touchFeedbackUntil=elapsed.Elapsed.TotalSeconds+2;Advance(elapsed.Elapsed.TotalSeconds);previous.Opacity=0;pet.Opacity=1; }
    void TouchTests(string root) {
        if(ZoneAt(.5,.19)!=TouchZone.Goggles||ZoneAt(.5,.32)!=TouchZone.Face||ZoneAt(.16,.18)!=TouchZone.Head||ZoneAt(.5,.6)!=TouchZone.Body)throw new Exception("touch regions failed");
        pressedZone=TouchZone.Face;touchStarted=10;pressed=true;dragging=false;heldReaction=false;
        CheckTouchHold(10.69);if(heldReaction)throw new Exception("hold fired early");
        CheckTouchHold(10.7);if(!heldReaction||state!="annoyed")throw new Exception("hold reaction missing");
        string hold=words.Text;EndTouch(11);if(words.Text!=hold)throw new Exception("release replaced hold reaction");
        heldReaction=false;dragging=true;CheckTouchHold(12);if(heldReaction)throw new Exception("drag fired touch hold");pressed=false;dragging=false;
        pressedZone=TouchZone.Goggles;lastTouchTime=-10;for(int i=0;i<4;i++)EndTouch(20+i*.3);if(touchCombo!=4||state!="annoyed")throw new Exception("repeat touch missing");
        pressedZone=TouchZone.Head;EndTouch(21);if(touchCombo!=1||state!="touchHead")throw new Exception("region switch did not reset combo");
        EndTouch(30);if(touchCombo!=1)throw new Exception("combo timeout failed");
        foreach(var zone in new[]{TouchZone.Face,TouchZone.Body,TouchZone.Goggles,TouchZone.Head,TouchZone.Body}) {
            TouchReaction(zone,"tap");string expected=zone==TouchZone.Face?"annoyed":zone==TouchZone.Head?"touchHead":zone==TouchZone.Goggles?"headpat":"touchBody";
            if(state!=expected||pet.Opacity!=1||previous.Opacity!=0)throw new Exception("rapid region change retained previous reaction");
        }
        heldReaction=false;pressed=false;dragging=false;lastTouchTime=-10;touchCombo=0;bubble.Visibility=Visibility.Collapsed;Enter("idle");
        Directory.CreateDirectory(Path.Combine(root,"qa"));
        var drawing=new DrawingVisual();using(var dc=drawing.RenderOpen()) {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(239,237,249)),null,new Rect(0,0,600,720));
            var zones=new[]{TouchZone.Head,TouchZone.Face,TouchZone.Goggles};string[] labels={"摸头 · 闭眼回应","戳脸 · 抱臂嫌弃","护目镜 · 扶镜回应"};
            for(int i=0;i<3;i++) {
                TouchReaction(zones[i],"tap");Advance(stateStart+.3);previous.Opacity=0;pet.Opacity=1;Scene.Measure(new Size(Width,Height));Scene.Arrange(new Rect(0,0,Width,Height));Scene.UpdateLayout();
                var frame=new RenderTargetBitmap((int)Width,(int)Height,96,96,PixelFormats.Pbgra32);frame.Render(Scene);
                dc.DrawImage(frame,new Rect(i*200,30,200,Height*200/Width));
                dc.DrawText(new FormattedText(labels[i],System.Globalization.CultureInfo.GetCultureInfo("zh-CN"),FlowDirection.LeftToRight,new Typeface("Microsoft YaHei"),14,Brushes.Indigo,1),new Point(i*200+20,410));
            }
            dc.DrawText(new FormattedText("轻点：分部位回应\n原地按住 0.7 秒：长按反应，只触发一次\n同一部位连续 4 次：专属连点台词\n移动超过 5 像素：拖拽优先",System.Globalization.CultureInfo.GetCultureInfo("zh-CN"),FlowDirection.LeftToRight,new Typeface("Microsoft YaHei"),18,Brushes.Indigo,1),new Point(40,490));
        }
        var bmp=new RenderTargetBitmap(600,720,96,96,PixelFormats.Pbgra32);bmp.Render(drawing);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using(var stream=File.Create(Path.Combine(root,"qa","touch.png")))png.Save(stream);
        bubble.Visibility=Visibility.Collapsed;Enter("idle");
    }
    void AddTouchSettings(System.Windows.Controls.Panel panel) {
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text="触碰互动：摸头、戳脸、碰护目镜。\n按住 0.7 秒触发长按，同一部位连续 4 次触发连点回应；移动鼠标优先拖拽。",TextWrapping=TextWrapping.Wrap,FontSize=12,Foreground=Brushes.DimGray,Margin=new Thickness(0,10,0,8) });
        var row=new System.Windows.Controls.WrapPanel();panel.Children.Add(row);
        var zones=new[]{TouchZone.Head,TouchZone.Face,TouchZone.Goggles};string[] names={"预览摸头","预览戳脸","预览碰镜片"};
        for(int i=0;i<zones.Length;i++) { var zone=zones[i];AddButton(row,names[i],delegate { TouchReaction(zone,"tap"); }); }
    }
}
}
