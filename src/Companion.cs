using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using System.IO;
using Forms=System.Windows.Forms;

namespace SilverWolfPet {
public class DeferredNotice { public string Text,Category;public DateTime AtUtc;public int Priority; }
public sealed class ActivityPulse : IDisposable {
    delegate IntPtr HookProc(int code,IntPtr message,IntPtr data);
    readonly HookProc callback;IntPtr keyboardHook;int pulses;
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int kind,HookProc proc,IntPtr module,uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("kernel32.dll",CharSet=CharSet.Auto)] static extern IntPtr GetModuleHandle(string name);
    public ActivityPulse() {
        callback=delegate(int code,IntPtr message,IntPtr data) {
            int kind=message.ToInt32();
            if(code>=0&&(kind==0x100||kind==0x104))System.Threading.Interlocked.Increment(ref pulses);
            return CallNextHookEx(IntPtr.Zero,code,message,data);
        };
        keyboardHook=SetWindowsHookEx(13,callback,GetModuleHandle(null),0);
    }
    public int Take() { return System.Threading.Interlocked.Exchange(ref pulses,0); }
    public void Dispose() { if(keyboardHook!=IntPtr.Zero)UnhookWindowsHookEx(keyboardHook);keyboardHook=IntPtr.Zero; }
}
public sealed class UseClock {
    public double ActiveSeconds;
    double last=-1;
    public void Sample(double now,double idle) {
        if(last>=0) { double delta=now-last;if(delta>15||delta<0||idle>=300)ActiveSeconds=0;else if(idle<60)ActiveSeconds+=delta; }
        last=now;
    }
}
public partial class PetWindow {
    readonly StackPanel replies=new StackPanel { Orientation=Orientation.Vertical,Margin=new Thickness(0,6,0,0) };
    readonly DispatcherTimer companionTimer=new DispatcherTimer();
    readonly UseClock useClock=new UseClock();
    readonly Border keyboard=new Border { Width=112,Height=32,CornerRadius=new CornerRadius(5),Background=new SolidColorBrush(Color.FromArgb(220,45,25,82)),BorderBrush=Brushes.MediumPurple,BorderThickness=new Thickness(1),VerticalAlignment=VerticalAlignment.Bottom,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,45,65),IsHitTestVisible=false,Visibility=Visibility.Collapsed };
    readonly List<System.Windows.Shapes.Rectangle> keyLights=new List<System.Windows.Shapes.Rectangle>();

    ActivityPulse activityPulse;
    bool isAway,hadAway,inputAnimating;
    double nextCompanionSecond,inputUntil,lastCompanionSample,lastBreakActive,bubbleExtra,inputHeat;
    double bubbleAnchorTop=Double.NaN;
    double PetTop { get { return bubbleExtra==0||Double.IsNaN(bubbleAnchorTop)?Top:bubbleAnchorTop; } }
    void SetPetWindowTop(double top) {
        bubbleAnchorTop=top+bubbleExtra;
        Top=top;
    }
    void BubbleSpace(double extra) {
        double difference=extra-bubbleExtra;if(difference==0)return;
        // HWND coordinates are rounded to physical pixels. Never feed that rounding
        // back into the next balloon resize: retain the unexpanded character anchor.
        if(bubbleExtra==0||Double.IsNaN(bubbleAnchorTop))bubbleAnchorTop=Top;
        bubbleExtra=extra;
        Height=prefs.Size+85+extra;
        if(!Double.IsNaN(bubbleAnchorTop))Top=bubbleAnchorTop-extra;
        if(pressed)startTop-=difference;
        pet.Margin=new Thickness(14,70+extra,14,15);previous.Margin=pet.Margin;
    }
    void LayoutBubble() {
        if(bubble.Visibility!=Visibility.Visible)return;
        bool hasReplies=replies.Children.Count>0;
        replies.Visibility=hasReplies?Visibility.Visible:Visibility.Collapsed;
        replies.Margin=hasReplies?new Thickness(0,6,0,0):new Thickness(0);
        bubble.Width=Math.Max(80,Math.Min(265,Width-8));
        foreach(Button button in replies.Children) {
            var label=button.Content as TextBlock;
            if(label!=null)label.MaxWidth=Math.Max(30,bubble.Width-bubble.Padding.Left-bubble.Padding.Right-button.Padding.Left-button.Padding.Right-4);
        }
        double verticalTrim=bubble.Padding.Top+bubble.Padding.Bottom+bubble.BorderThickness.Top+bubble.BorderThickness.Bottom;
        double contentWidth=Math.Max(1,bubble.Width-bubble.Padding.Left-bubble.Padding.Right-bubble.BorderThickness.Left-bubble.BorderThickness.Right);
        words.InvalidateMeasure();words.Measure(new Size(contentWidth,Double.PositiveInfinity));
        double contentHeight=words.DesiredSize.Height+(hasReplies?replies.Margin.Top+replies.Margin.Bottom:0);
        if(linkLabel.Visibility==Visibility.Visible) { linkLabel.Measure(new Size(contentWidth,Double.PositiveInfinity));contentHeight+=linkLabel.DesiredSize.Height; }
        foreach(Button button in replies.Children) { button.InvalidateMeasure();button.Measure(new Size(contentWidth,Double.PositiveInfinity));contentHeight+=button.DesiredSize.Height; }
        // Reserve the complete balloon height plus a gap above the image viewport.
        BubbleSpace(Math.Max(0,Math.Ceiling(contentHeight+verticalTrim)+12-70));
    }
    [StructLayout(LayoutKind.Sequential)] struct LastInput { public uint cbSize,dwTime; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LastInput info);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    static double IdleSeconds() { var info=new LastInput { cbSize=8 };return GetLastInputInfo(ref info)?unchecked((uint)Environment.TickCount-info.dwTime)/1000.0:Double.NaN; }
    readonly TextBlock linkLabel=new TextBlock { Text="银狼  /  LV.999",FontSize=10,Foreground=new SolidColorBrush(Color.FromRgb(111,231,255)),Margin=new Thickness(0,0,0,7) };
    void InitCompanionBubble() {
        bubble.Background=new LinearGradientBrush(Color.FromRgb(37,28,65),Color.FromRgb(16,20,36),45);bubble.BorderBrush=new SolidColorBrush(Color.FromRgb(123,104,205));bubble.CornerRadius=new CornerRadius(14,14,4,14);bubble.Padding=new Thickness(13,11,13,11);
        words.FontFamily=new FontFamily("Microsoft YaHei");words.FontSize=12;words.TextAlignment=TextAlignment.Left;words.LineHeight=19;
        var content=new StackPanel();content.Children.Add(linkLabel);content.Children.Add(words);content.Children.Add(replies);bubble.Child=content;
    }
    void InitCompanion() {
        if(!testing&&prefs.InputReaction)activityPulse=new ActivityPulse();
        var grid=new System.Windows.Controls.Primitives.UniformGrid { Rows=2,Columns=8,Margin=new Thickness(5) };
        for(int i=0;i<16;i++) { var key=new System.Windows.Shapes.Rectangle { Margin=new Thickness(1),RadiusX=1,RadiusY=1,Fill=Brushes.MediumPurple };keyLights.Add(key);grid.Children.Add(key); }
        keyboard.Child=grid;Scene.Children.Insert(2,keyboard);
        companionTimer.Interval=TimeSpan.FromMilliseconds(100);companionTimer.Tick+=delegate { CompanionTick(); };if(!testing)companionTimer.Start();
    }
    bool Muted(string category) { string day;return prefs.MutedDays.TryGetValue(category,out day)&&day==DateTime.Now.ToString("yyyy-MM-dd"); }
    void Reply(string label,Action action) {
        var button=new Button { Content=new TextBlock { Text=label,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center },Style=CyberButton(),Margin=new Thickness(0,5,0,0) };
        button.Click+=delegate { bubble.Visibility=Visibility.Collapsed;replies.Children.Clear();action(); };replies.Children.Add(button);bubble.IsHitTestVisible=true;
        LayoutBubble();
    }
    void Prompt(string message,string category,int priority) {
        if(Muted(category))return;
        if(bubble.Visibility==Visibility.Visible&&elapsed.Elapsed.TotalSeconds<bubbleUntil&&priority<bubblePriority)return;
        Say(message,25,priority);
        Reply("知道了",delegate { Say("收到。接下来就看你的操作了。",3); });
        Reply("10 分钟后",delegate {
            prefs.Deferred.RemoveAll(x=>x==null||x.Category==category);
            prefs.Deferred.Add(new DeferredNotice { Text=message,Category=category,AtUtc=DateTime.UtcNow.AddMinutes(10),Priority=priority });Save();Say("计时开始，十分钟后叫你。",3);
        });
        Reply("今天不提醒",delegate {
            prefs.MutedDays[category]=DateTime.Now.ToString("yyyy-MM-dd");prefs.Deferred.RemoveAll(x=>x==null||x.Category==category);Save();Say("这类提醒今天静音。明天再上线。",3);
        });
    }
    static bool InvitationReady(DateTime now,DateTime next,double roll) {
        return now>=next&&roll<0.20;
    }
    void MaybeInviteReplies() {
        DateTime now=DateTime.UtcNow;
        if(!InvitationReady(now,prefs.NextInviteUtc,random.NextDouble()))return;
        InviteReplies();
        prefs.NextInviteUtc=now.AddMinutes(10);Save();
    }
    void InviteReplies() {
        bubbleUntil=elapsed.Elapsed.TotalSeconds+15;
        Reply("好啊，陪你一会儿",delegate { Enter("game");Say("那就坐稳，看我操作。",4); });
        Reply("等我忙完",delegate { Enter("headpat");Say("行，你先过眼前这关。我等着。",4); });
    }
    void CompanionTick() {
        double now=elapsed.Elapsed.TotalSeconds;QuietTick(now);double idle=IdleSeconds();
        if(Double.IsNaN(idle))return;
        useClock.Sample(now,idle);
        if(useClock.ActiveSeconds<lastBreakActive)lastBreakActive=0;
        bool away=idle>=300;
        if(lastCompanionSample>0&&now-lastCompanionSample>=300)hadAway=true;
        lastCompanionSample=now;
        if(away)hadAway=true;
        isAway=away&&prefs.AwayReaction;
        if(isAway&&!pressed&&state!="sleep") { Enter("sleep");keyboard.Visibility=Visibility.Collapsed;inputAnimating=false; }
        if(!away&&hadAway) {
            hadAway=false;
            if(prefs.AwayReaction&&IsVisible&&!pressed) { Enter("stretch");Say("挂机结束了？欢迎回来，轮到你操作了。",6,2); }
        }
        if(!isAway&&state=="sleep")Enter("idle");
        int pulses=activityPulse!=null?activityPulse.Take():0;
        inputHeat=inputHeat*.85+pulses;bool input=pulses>0;
        if(prefs.InputReaction&&IsVisible&&!away&&!pressed&&!menuOpen&&settings==null) {
            // Virtual keys 1-6 include mouse buttons; keyboard keys start at 8.
            // Only sample held state. Never translate or retain keys.
            for(int key=8;key<255;key++)if((GetAsyncKeyState(key)&0x8000)!=0) { input=true;break; }
        }
        if(input)inputUntil=now+.65;
        bool animate=prefs.InputReaction&&IsVisible&&!isAway&&!pressed&&!menuOpen&&settings==null&&now<inputUntil;
        if(animate&&(state=="idle"||state=="blink"||(inputAnimating&&state=="game"))) {
            if(!inputAnimating||state!="game") { Enter("game");inputAnimating=true; }
            keyboard.Visibility=Visibility.Visible;
            keyboard.Margin=new Thickness(0,0,30,prefs.Size*.4);
            for(int i=0;i<keyLights.Count;i++)keyLights[i].Fill=i==(int)(now*14)%16||i==((int)(now*14)+7)%16?Brushes.Cyan:Brushes.MediumPurple;
        } else {
            keyboard.Visibility=Visibility.Collapsed;
            if(inputAnimating) { inputAnimating=false;if(state=="game") { Enter("idle");ScheduleIdle(now); } }
        }
        if(now<nextCompanionSecond)return;nextCompanionSecond=now+1;

        if(bubble.Visibility==Visibility.Visible&&now>bubbleUntil)bubble.Visibility=Visibility.Collapsed;
        bool free=IsVisible&&!pressed&&!menuOpen&&settings==null&&!isAway&&bubble.Visibility!=Visibility.Visible;
        if(free) {
            var due=prefs.Deferred.FirstOrDefault(x=>x!=null&&x.AtUtc<=DateTime.UtcNow&&(x.Category!="break"||prefs.BreakReminders)&&(!x.Category.StartsWith("daily:")||prefs.TimeReminders));
            if(due!=null) { prefs.Deferred.Remove(due);Save();Prompt(due.Text,due.Category,due.Priority);return; }
            if(prefs.BreakReminders&&!away&&!Muted("break")&&useClock.ActiveSeconds-lastBreakActive>=prefs.BreakMinutes*60) {
                lastBreakActive=useClock.ActiveSeconds;Enter("stretch");Prompt("连续在线够久了。起来喝口水、活动一下，别把体力条打空。","break",2);
            }
        }
    }
    void AddCompanionSettings(Panel panel) {
        Action<string,bool,Action<bool>> toggle=(label,value,change)=> { var box=new CheckBox { Content=label,IsChecked=value,Margin=new Thickness(0,0,0,10) };box.Click+=delegate { change(box.IsChecked==true);Save(); };panel.Children.Add(box); };
        toggle("连续使用电脑后提醒喝水、活动",prefs.BreakReminders,v=>prefs.BreakReminders=v);
        var minutes=new ComboBox { Margin=new Thickness(0,0,0,10),ItemsSource=new[]{15,30,45,60,90,120,180},SelectedItem=prefs.BreakMinutes };
        panel.Children.Add(new TextBlock { Text="提醒间隔（活跃分钟；离开 5 分钟重计）",Margin=new Thickness(0,0,0,4) });panel.Children.Add(minutes);
        minutes.SelectionChanged+=delegate { if(minutes.SelectedItem!=null) { prefs.BreakMinutes=(int)minutes.SelectedItem;Save(); } };
        toggle("键盘联动 · 全息键盘（不记录输入文字）",prefs.InputReaction,v=> { prefs.InputReaction=v;if(activityPulse!=null) { activityPulse.Dispose();activityPulse=null; }if(v&&!testing)activityPulse=new ActivityPulse(); });
        toggle("离开 5 分钟打盹，回来时回应",prefs.AwayReaction,v=>prefs.AwayReaction=v);
        toggle("显示随身额度终端",prefs.SidebarEnabled,v=>prefs.SidebarEnabled=v); AddButton(panel,"展开额度终端",ShowSidebar);
        AddButton(panel,"测试可回应的休息提醒",delegate { prefs.MutedDays.Remove("test");Prompt("【测试】起来喝口水，活动一下吧。","test",4); });
        AddButton(panel,"恢复今天静音的提醒",delegate { prefs.MutedDays.Clear();Save();Say("提醒频道已恢复。",3); });
    }
    void CompanionTests(string root) {
        var invitationTime=new DateTime(2026,9,11,0,0,0,DateTimeKind.Utc);
        if(InvitationReady(invitationTime,invitationTime.AddMinutes(10),0)||InvitationReady(invitationTime,invitationTime,0.20)||!InvitationReady(invitationTime,invitationTime,0.1999))throw new Exception("invitation probability/cooldown failed");
        var savedInvite=new Preferences { NextInviteUtc=invitationTime.AddMinutes(10) };
        var inviteJson=new System.Web.Script.Serialization.JavaScriptSerializer();
        var restoredInvite=inviteJson.Deserialize<Preferences>(inviteJson.Serialize(savedInvite));
        if(InvitationReady(invitationTime,restoredInvite.NextInviteUtc,0))throw new Exception("invitation cooldown lost after reload");
        var clock=new UseClock();clock.Sample(0,0);clock.Sample(10,0);clock.Sample(20,65);if(clock.ActiveSeconds!=10)throw new Exception("idle counted as active");clock.Sample(30,300);if(clock.ActiveSeconds!=0)throw new Exception("break did not reset");clock.Sample(40,0);clock.Sample(600,0);if(clock.ActiveSeconds!=0)throw new Exception("suspend counted as use");
        Prompt("测试提醒","test",4);if(replies.Children.Count!=3||!bubble.IsHitTestVisible)throw new Exception("reply controls missing");
        ((Button)replies.Children[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(prefs.Deferred.Count!=1||prefs.Deferred[0].AtUtc<DateTime.UtcNow.AddMinutes(9))throw new Exception("snooze failed");
        bubble.Visibility=Visibility.Collapsed;Prompt("测试提醒","test",4);((Button)replies.Children[2]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(!Muted("test")||prefs.Deferred.Count!=0)throw new Exception("day mute failed");
        prefs.MutedDays.Clear();bubble.Visibility=Visibility.Collapsed;Prompt("连续在线够久了。起来喝口水，活动一下吧。","test",4);
        Scene.Measure(new Size(Width,Height));Scene.Arrange(new Rect(0,0,Width,Height));Scene.UpdateLayout();
        var frame=new RenderTargetBitmap((int)Width*2,(int)Height*2,192,192,PixelFormats.Pbgra32);frame.Render(Scene);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(frame));Directory.CreateDirectory(Path.Combine(root,"qa"));using(var file=File.Create(Path.Combine(root,"qa","replies.png")))png.Save(file);
        bubble.Visibility=Visibility.Collapsed;replies.Children.Clear();bubblePriority=0;BubbleSpace(0);
        isAway=true;Enter("sleep");Advance(stateStart+60);if(state!="sleep")throw new Exception("away sleep did not hold");isAway=false;Enter("idle");
    }
    void BubblePositionTests() {
        bubble.Visibility=Visibility.Collapsed;BubbleSpace(0);
        double originalTop=Top;
        foreach(double dpi in new[]{1.0,1.25,1.5,1.75,2.0}) {
            SetPetWindowTop(240);double baseline=Top;
            for(int i=0;i<100;i++) {
                BubbleSpace(19+i%5);
                // Model native HWND pixel rounding independently of the logical layout.
                Top=Math.Ceiling(Top*dpi)/dpi;
                BubbleSpace(47+i%7);
                Top=Math.Ceiling(Top*dpi)/dpi;
                BubbleSpace(0);
                if(Math.Abs(Top-baseline)>.001)throw new Exception("bubble position drift at DPI "+dpi+", cycle "+i+": "+(Top-baseline));
            }
        }
        BubbleSpace(51);SetPetWindowTop(300-bubbleExtra);
        BubbleSpace(87);BubbleSpace(0);
        if(Math.Abs(Top-300)>.001)throw new Exception("bubble reset undid intentional movement");
        SetPetWindowTop(originalTop);
    }
    void BubbleLayoutTests(string root) {
        double size=prefs.Size,left=Left,top=Top;
        foreach(int requested in new[]{160,300,600}) {
            bubble.Visibility=Visibility.Collapsed;BubbleSpace(0);ResizePet(requested);
            Say("哈啊……这加载也太慢了。找点游戏玩？",30,4);InviteReplies();LayoutBubble();
            Scene.Measure(new Size(Width,Height));Scene.Arrange(new Rect(0,0,Width,Height));Scene.UpdateLayout();
            if(pet.Margin.Top<bubble.ActualHeight+11)throw new Exception("reply bubble overlaps character viewport: size="+requested+", top="+pet.Margin.Top+", bubble="+bubble.ActualHeight);
            foreach(Button button in replies.Children) {
                var label=button.Content as TextBlock;
                if(label==null||label.TextWrapping!=TextWrapping.Wrap||label.ActualWidth>button.ActualWidth-button.Padding.Left-button.Padding.Right+1)throw new Exception("reply text clipped");
            }
            if(requested==160) {
                var label=(TextBlock)((Button)replies.Children[0]).Content;
                if(label.ActualHeight<24)throw new Exception("small pet label did not wrap");
                RenderDialogCheck(root,"dialog-small.png");
            }
            bubble.Visibility=Visibility.Collapsed;
            TouchReaction(TouchZone.Goggles,"tap");LayoutBubble();
            Scene.Measure(new Size(Width,Height));Scene.Arrange(new Rect(0,0,Width,Height));Scene.UpdateLayout();
            if(pet.Margin.Top<bubble.ActualHeight+11||linkLabel.Visibility!=Visibility.Collapsed)throw new Exception("plain bubble overlaps or exposes header");
            if(requested==160)RenderDialogCheck(root,"dialog-plain.png");
        }
        bubble.Visibility=Visibility.Collapsed;BubbleSpace(0);ResizePet(size);Left=left;Top=top;Enter("idle");
    }
    void RenderDialogCheck(string root,string name) {
        previous.Opacity=0;pet.Opacity=1;
        var frame=new RenderTargetBitmap((int)Math.Ceiling(Width)*2,(int)Math.Ceiling(Height)*2,192,192,PixelFormats.Pbgra32);frame.Render(Scene);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(frame));using(var file=File.Create(Path.Combine(root,"qa",name)))png.Save(file);
    }
}
}
