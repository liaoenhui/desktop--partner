using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace SilverWolfPet {
public class StartupLinkState {
    public bool Present, Current;
    public string Target, Arguments, WorkingDirectory;
}
public sealed class StartupLink {
    readonly string executable;
    public readonly string LinkPath;
    public StartupLink(string folder,string executablePath) {
        if(String.IsNullOrWhiteSpace(folder))throw new IOException("无法找到 Windows 启动文件夹。");
        executable=System.IO.Path.GetFullPath(executablePath);
        LinkPath=System.IO.Path.Combine(System.IO.Path.GetFullPath(folder),"SilverWolfPet.lnk");
    }
    public static StartupLink ForCurrentUser() {
        return new StartupLink(Environment.GetFolderPath(Environment.SpecialFolder.Startup),System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"SilverWolfPet.exe"));
    }
    static void Release(object value) { if(value!=null&&Marshal.IsComObject(value))Marshal.FinalReleaseComObject(value); }
    public StartupLinkState Read() {
        if(!File.Exists(LinkPath))return new StartupLinkState();
        object shell=null,shortcut=null;
        try {
            shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell",true));
            shortcut=((dynamic)shell).CreateShortcut(LinkPath);
            string target=((dynamic)shortcut).TargetPath, arguments=((dynamic)shortcut).Arguments, working=((dynamic)shortcut).WorkingDirectory;
            return new StartupLinkState { Present=true, Target=target, Arguments=arguments, WorkingDirectory=working,
                Current=String.Equals(target,executable,StringComparison.OrdinalIgnoreCase)&&arguments=="--autostart" };
        } finally { Release(shortcut);Release(shell); }
    }
    public void SetEnabled(bool enabled) {
        if(!enabled) { if(File.Exists(LinkPath))File.Delete(LinkPath); return; }
        if(!File.Exists(executable))throw new FileNotFoundException("找不到当前桌宠程序，请从解压后的文件夹运行。",executable);
        if(Read().Current)return;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(LinkPath));
        object shell=null,shortcut=null;
        try {
            shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell",true));
            shortcut=((dynamic)shell).CreateShortcut(LinkPath);
            dynamic link=shortcut;
            link.TargetPath=executable;
            link.Arguments="--autostart";
            link.WorkingDirectory=System.IO.Path.GetDirectoryName(executable);
            link.Description="银狼 LV.999 桌宠 · 登录 Windows 后启动";
            link.WindowStyle=1;
            link.Save();
        } finally { Release(shortcut);Release(shell); }
        if(!Read().Current)throw new IOException("启动项未能保存，请重试。");
    }
}
public class Pack {
    public Dictionary<string,string[]> actionLines { get; set; }
    public DailyReminder[] reminders { get; set; }
    public string name { get; set; }
    public string image { get; set; }
    public int fps { get; set; }
    public string[] clickLines { get; set; }
    public string[] idleLines { get; set; }
    public Dictionary<string,string[]> animations { get; set; }
    public Dictionary<string,AtlasSpec> atlases { get; set; }
    public Dictionary<string,ClipSpec> clips { get; set; }
    public string baseAtlas { get; set; }
    public int baseFrame { get; set; }
}
public class DailyReminder {
    public string id { get; set; }
    public string time { get; set; }
    public string action { get; set; }
    public string[] lines { get; set; }
}
public static class ReminderSchedule {
    public static string Key(DailyReminder item) { return item.id+"@"+item.time; }
    public static DailyReminder Due(DailyReminder[] items,DateTime localNow,Dictionary<string,string> shown) {
        if(items==null)return null;
        string date=localNow.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
        foreach(var item in items) {
            TimeSpan at;
            if(!TimeSpan.TryParseExact(item.time,@"hh\:mm",System.Globalization.CultureInfo.InvariantCulture,out at))continue;
            double age=(localNow-(localNow.Date+at)).TotalSeconds; string previousDate;
            if(age>=0&&age<300&&(!shown.TryGetValue(Key(item),out previousDate)||previousDate!=date))return item;
        }
        return null;
    }
}
public class AtlasSpec {
    public string file { get; set; }
    public int columns { get; set; }
    public int rows { get; set; }
}
public class ClipSpec {
    public string atlas { get; set; }
    public int[] frames { get; set; }
    public int[] milliseconds { get; set; }
    public bool loop { get; set; }
}
public class AnimationClip {
    public BitmapSource[] Frames;
    public int[] Milliseconds;
    public bool Loop;
    public double Duration { get { return Milliseconds.Sum()/1000.0; } }
    public BitmapSource At(double time) {
        double total=Milliseconds.Sum(), ms=Math.Max(0,time*1000);
        if(Loop)ms%=total;
        for(int i=0;i<Frames.Length;i++) { if(ms<Milliseconds[i])return Frames[i]; ms-=Milliseconds[i]; }
        return Frames[Frames.Length-1];
    }
}
public class Preferences {
    public bool SidebarEnabled=true; public bool SidebarDocked=false; public double SidebarPosition=.45; public string SidebarScreen="";
    public bool AutoQuiet=true, ImportantDuringQuiet=false, MousePassThrough=false;
    public List<string> QuietGames=new List<string>{"StarRail.exe","YuanShen.exe","GenshinImpact.exe","ZenlessZoneZero.exe","cs2.exe","League of Legends.exe","VALORANT-Win64-Shipping.exe"};
    public bool BreakReminders=true, InputReaction=true, AwayReaction=true;
    public int BreakMinutes=60;
    public Dictionary<string,string> MutedDays=new Dictionary<string,string>();
    public List<DeferredNotice> Deferred=new List<DeferredNotice>();
    public bool CodexUsage = true, CodexTasks = true;
    public bool ActionSpeech = true, TimeReminders = true;
    public DateTime NextInviteUtc = DateTime.MinValue;
    public Dictionary<string,string> ReminderShown = new Dictionary<string,string>();
    public double Left = -99999, Top = -99999, Size = 300;
    public bool Topmost = true, Idle = true;
    public string PackPath = "";
}
public class LoadedPack {
    public Pack Spec;
    public BitmapSource Base;
    public Dictionary<string,BitmapSource[]> Frames = new Dictionary<string,BitmapSource[]>();
    public Dictionary<string,AnimationClip> Clips = new Dictionary<string,AnimationClip>();
    public string Path;
    public static LoadedPack Load(string path) {
        if (new FileInfo(path).Length > 128 * 1024) throw new Exception("配置文件不能超过 128 KB。");
        Pack p = new JavaScriptSerializer().Deserialize<Pack>(File.ReadAllText(path));
        if (p == null || String.IsNullOrWhiteSpace(p.image)) throw new Exception("pet.json 缺少 image 字段。");
        if(p.actionLines!=null)foreach(var lines in p.actionLines.Values)ValidateLines(lines);
        if(p.reminders!=null) {
            if(p.reminders.Length>32)throw new Exception("每天最多配置 32 条定时提醒。");
            var ids=new HashSet<string>();
            foreach(var reminder in p.reminders) {
                TimeSpan at;
                if(reminder==null||String.IsNullOrWhiteSpace(reminder.id)||reminder.id.Length>64||!ids.Add(reminder.id)||!TimeSpan.TryParseExact(reminder.time,@"hh\:mm",System.Globalization.CultureInfo.InvariantCulture,out at)||at.TotalHours>=24)throw new Exception("提醒需要唯一 id 和有效的 HH:mm 时间。");
                ValidateLines(reminder.lines);
            }
        }
        var result = new LoadedPack { Spec = p, Path = System.IO.Path.GetFullPath(path) };
        string root = System.IO.Path.GetDirectoryName(result.Path) + System.IO.Path.DirectorySeparatorChar;
        result.Base = ReadImage(root, p.image);
        int count = 0;
        if (p.animations != null) foreach(var pair in p.animations) {
            if (!new[]{"idle","click","headpat","annoyed","drag","land","sleep","blink","yawn","stretch","game","rhythm"}.Contains(pair.Key)) continue;
            if (pair.Value == null || pair.Value.Length == 0) continue;
            count += pair.Value.Length;
            if(count > 120) throw new Exception("动画总帧数不能超过 120。");
            result.Frames[pair.Key] = pair.Value.Select(x => ReadImage(root,x)).ToArray();
        }
        p.fps = Math.Max(1,Math.Min(30,p.fps == 0 ? 8 : p.fps));
        foreach(var legacy in result.Frames) result.Clips[legacy.Key]=new AnimationClip { Frames=legacy.Value, Milliseconds=Enumerable.Repeat(1000/p.fps,legacy.Value.Length).ToArray(), Loop=true };
        var atlases=new Dictionary<string,BitmapSource[]>();
        long pixels=0;
        if(p.atlases!=null) foreach(var pair in p.atlases) {
            var a=pair.Value;
            if(a==null||a.columns<1||a.rows<1||a.columns>12||a.rows>12||a.columns*a.rows>36)throw new Exception("图集网格必须在 1 至 36 格之间。");
            var sheet=ReadImage(root,a.file,4096); pixels+=(long)sheet.PixelWidth*sheet.PixelHeight;
            if(pixels>32000000)throw new Exception("图集总像素过大。");
            var cells=new BitmapSource[a.columns*a.rows];
            for(int i=0;i<cells.Length;i++) {
                int col=i%a.columns,row=i/a.columns;
                int x=col*sheet.PixelWidth/a.columns,y=row*sheet.PixelHeight/a.rows;
                var cell=new CroppedBitmap(sheet,new Int32Rect(x,y,(col+1)*sheet.PixelWidth/a.columns-x,(row+1)*sheet.PixelHeight/a.rows-y));
                cell.Freeze(); cells[i]=cell;
            }
            atlases.Add(pair.Key,cells);
        }
        if(!String.IsNullOrEmpty(p.baseAtlas)) {
            BitmapSource[] cells;
            if(!atlases.TryGetValue(p.baseAtlas,out cells)||p.baseFrame<0||p.baseFrame>=cells.Length)throw new Exception("默认图集帧不存在。");
            result.Base=cells[p.baseFrame];
        }
        if(p.clips!=null)foreach(var pair in p.clips) {
            var c=pair.Value; BitmapSource[] cells;
            if(c==null||String.IsNullOrEmpty(c.atlas)||!atlases.TryGetValue(c.atlas,out cells)||c.frames==null||c.frames.Length==0||c.frames.Length>120)throw new Exception("动作图集或帧配置无效。");
            if(c.milliseconds==null||c.milliseconds.Length!=c.frames.Length||c.milliseconds.Any(x=>x<40||x>10000)||c.frames.Any(x=>x<0||x>=cells.Length))throw new Exception("动作帧索引或持续时间无效。");
            result.Clips[pair.Key]=new AnimationClip { Frames=c.frames.Select(x=>cells[x]).ToArray(), Milliseconds=c.milliseconds, Loop=c.loop };
        }
        return result;
    }
    static void ValidateLines(string[] lines) {
        if(lines==null||lines.Length==0||lines.Length>30||lines.Any(x=>String.IsNullOrWhiteSpace(x)||x.Length>200))throw new Exception("台词需要 1 至 30 条非空文本，每条最多 200 字。");
    }
    static BitmapSource ReadImage(string root,string relative,int maxSize=2048) {
        if(String.IsNullOrWhiteSpace(relative))throw new Exception("图片路径为空。");
        string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(root,relative));
        if (!full.StartsWith(root,StringComparison.OrdinalIgnoreCase) || System.IO.Path.IsPathRooted(relative)) throw new Exception("图片必须位于素材包文件夹内。");
        if (!String.Equals(System.IO.Path.GetExtension(full),".png",StringComparison.OrdinalIgnoreCase)) throw new Exception("请使用 PNG 图片。");
        if (new FileInfo(full).Length > 16*1024*1024) throw new Exception("单张图片不能超过 16 MB。");
        BitmapSource src;
        using(var stream = File.OpenRead(full)) {
            var decoder = BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
            src = decoder.Frames[0];
            if(src.PixelWidth > maxSize || src.PixelHeight > maxSize) throw new Exception("图片最长边不能超过 "+maxSize+" 像素。");
            src = new FormatConvertedBitmap(src,PixelFormats.Bgra32,null,0); src.Freeze();
        }
        return src;
    }
}
public partial class PetWindow : Window {
    public readonly Grid Scene = new Grid();
    readonly Image pet = new Image { Stretch = Stretch.Uniform };
    readonly Image previous = new Image { Stretch = Stretch.Uniform, IsHitTestVisible=false, Opacity=0 };
    readonly Grid bubble = new Grid { VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 265, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly Border bubblePanel = new Border { CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1.25), Padding = new Thickness(14,11,14,11) };
    readonly System.Windows.Shapes.Path bubbleTail = new System.Windows.Shapes.Path { Width=20,Height=11,Stretch=Stretch.Fill,HorizontalAlignment=HorizontalAlignment.Center,Data=Geometry.Parse("M0,0 L20,0 L10,11 Z") };
    readonly TextBlock words = new TextBlock { Foreground = Brushes.White, FontSize = 13, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
    readonly ScaleTransform scale = new ScaleTransform(1,1);
    readonly RotateTransform rotate = new RotateTransform();
    readonly TranslateTransform shift = new TranslateTransform();
    readonly DispatcherTimer timer = new DispatcherTimer();
    readonly Random random = new Random();
    readonly Stopwatch elapsed = Stopwatch.StartNew();
    readonly string settingsPath, defaultPack;
    readonly bool testing;
    Preferences prefs;
    LoadedPack pack;
    QuotaSidebar sidebar;
    Forms.NotifyIcon tray;
    System.Drawing.Icon trayIcon;
    Window settings;
    bool pressed, dragging, pressHead, menuOpen;
    Point pressCursor;
    double startLeft,startTop, stateStart, bubbleUntil, lastAction, lastClick = -10;
    int combo;
    string state = "idle";
    string lastIdle = "";
    double nextIdle=12, nextBlink=3, frameChanged;
    double nextReminderCheck;
    int bubblePriority;
    CodexMonitor codex;
    string codexStatus="正在连接本机 Codex…";
    TextBlock codexStatusText;
    readonly Queue<string> codexNotices=new Queue<string>();
    readonly DispatcherTimer codexTimer=new DispatcherTimer();
    readonly Dictionary<string,string> lastSpoken = new Dictionary<string,string>();
    readonly Dictionary<BitmapSource,byte[]> alphaCache = new Dictionary<BitmapSource,byte[]>();
    readonly Dictionary<BitmapSource,Rect> visibleBoundsCache = new Dictionary<BitmapSource,Rect>();
    BitmapSource cachedBitmap;
    byte[] alpha;
    double dpiX = 1, dpiY = 1;
    delegate bool EnumWindowsCallback(IntPtr handle,IntPtr parameter);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsCallback callback,IntPtr parameter);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr handle);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr handle,int command);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr handle);
    [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr handle);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr handle,StringBuilder text,int maximum);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr handle,StringBuilder text,int maximum);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr handle);
    static int CodexWindowScore(string processName,string title) {
        processName=(processName??"").Trim().ToLowerInvariant();title=(title??"").Trim();
        int score=0;
        if(processName=="chatgpt")score=300;
        else if(processName=="codex")score=260;
        else if(processName.IndexOf("chatgpt",StringComparison.Ordinal)>=0)score=220;
        else if(processName.IndexOf("codex",StringComparison.Ordinal)>=0)score=200;
        else return 0;
        if(title.IndexOf("Codex",StringComparison.OrdinalIgnoreCase)>=0)score+=80;
        if(title.IndexOf("ChatGPT",StringComparison.OrdinalIgnoreCase)>=0)score+=30;
        return score;
    }
    static int CodexWindowScore(string processName,string title,string className,int width,int height,bool iconic) {
        if(String.Equals(className,"ConsoleWindowClass",StringComparison.OrdinalIgnoreCase))return 0;
        if(!iconic&&(width<480||height<320))return 0;
        return CodexWindowScore(processName,title);
    }
    bool TryActivateCodexWindow() {
        IntPtr own=new WindowInteropHelper(this).Handle,best=IntPtr.Zero;int bestScore=0,order=0,bestOrder=Int32.MaxValue;
        EnumWindows(delegate(IntPtr handle,IntPtr parameter) {
            if(handle==own||!IsWindowVisible(handle))return true;
            int length=GetWindowTextLength(handle);
            var title=new StringBuilder(Math.Max(1,Math.Min(length+1,1024)));GetWindowText(handle,title,title.Capacity);
            var className=new StringBuilder(256);GetClassName(handle,className,className.Capacity);
            QuietRect rect;if(!GetWindowRect(handle,out rect))return true;
            uint processId;GetWindowThreadProcessId(handle,out processId);string processName="";
            try {using(var process=Process.GetProcessById((int)processId))processName=process.ProcessName;}catch{return true;}
            int score=CodexWindowScore(processName,title.ToString(),className.ToString(),rect.Right-rect.Left,rect.Bottom-rect.Top,IsIconic(handle));
            // EnumWindows is ordered from top to bottom. Keep the most recently
            // visible matching window when several Codex windows have equal rank.
            if(score>bestScore||(score==bestScore&&score>0&&order<bestOrder)){best=handle;bestScore=score;bestOrder=order;}
            order++;return true;
        },IntPtr.Zero);
        if(best==IntPtr.Zero)return false;
        if(IsIconic(best))ShowWindowAsync(best,9); // SW_RESTORE
        bool brought=BringWindowToTop(best),foreground=SetForegroundWindow(best);
        // Windows can report that the foreground request was denied even after
        // BringWindowToTop has visibly activated the target. Either result means
        // the click reached a usable Codex window.
        return foreground||brought;
    }
    public PetWindow(string root, bool test, bool preview=false) {
        testing=test||preview;
        defaultPack=System.IO.Path.Combine(root,"assets","default","pet.json");
        settingsPath=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SilverWolfPet","settings.json");
        prefs=ReadPreferences();
        string warning=null;
        try { pack=LoadedPack.Load(String.IsNullOrEmpty(prefs.PackPath)?defaultPack:prefs.PackPath); }
        catch(Exception ex) { pack=LoadedPack.Load(defaultPack); prefs.PackPath=""; warning="自定义素材无法载入，已恢复默认形象。"; Debug.WriteLine(ex); }
        Title="银狼 LV.999 桌宠 · 2.6.23 多任务预览版"; WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize;
        Icon=BitmapFrame.Create(new Uri(System.IO.Path.Combine(root,"assets","silver-wolf.ico")));
        AllowsTransparency=true; Background=Brushes.Transparent; ShowInTaskbar=false;ShowActivated=false; Topmost=prefs.Topmost;
        if(preview) { ShowInTaskbar=true; Title="银狼 LV.999 · 动作测试"; }
        Width=prefs.Size*1.6+28; Height=prefs.Size+85;
        Content=Scene; InitCompanionBubble(); LoadCodexForm(root);
        pet.Margin=new Thickness(14,70,14,15); pet.RenderTransformOrigin=new Point(.5,.85);
        var transforms=new TransformGroup(); transforms.Children.Add(scale); transforms.Children.Add(rotate); transforms.Children.Add(shift); pet.RenderTransform=transforms;
        previous.Margin=pet.Margin; previous.RenderTransformOrigin=pet.RenderTransformOrigin; previous.RenderTransform=transforms;
        Scene.Children.Add(previous); Scene.Children.Add(pet); Scene.Children.Add(bubble); SetFrame(pack.Base);
        SourceInitialized += delegate {
            var source=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            dpiX=source.CompositionTarget.TransformToDevice.M11; dpiY=source.CompositionTarget.TransformToDevice.M22;
            source.AddHook(Hook);
            InitExperience(source.Handle);
            var area=Forms.Screen.PrimaryScreen.WorkingArea;
            var visible=VisibleFrameBounds(cachedBitmap);
            double screenRight=area.Right/dpiX;
            bool wasAtRightEdge=prefs.Left>-90000&&(Math.Abs(prefs.Left-(screenRight-Width))<=6||Math.Abs(prefs.Left-(screenRight-Width-18))<=8||prefs.Left+visible.Right>=screenRight-30);
            Left=prefs.Left < -90000||wasAtRightEdge ? AccessorySafeLeft(screenRight) : prefs.Left;
            SetPetWindowTop(prefs.Top < -90000 ? area.Bottom/dpiY-Height-20 : prefs.Top);
            Clamp();
        };
        pet.MouseLeftButtonDown += Down;
        pet.MouseMove += Move;
        pet.MouseLeftButtonUp += Up;
        pet.LostMouseCapture += delegate { if(pressed) { bool moved=dragging; pressed=false; dragging=false; Clamp(); Enter(moved?"land":"idle"); ScheduleIdle(elapsed.Elapsed.TotalSeconds); Save(); } };
        pet.MouseRightButtonUp += delegate(object sender,MouseButtonEventArgs e) { ShowMenu(); e.Handled=true; };
        pet.MouseWheel += delegate(object sender,MouseWheelEventArgs e) { ResizePet(prefs.Size+(e.Delta>0?20:-20)); e.Handled=true; };
        timer.Interval=TimeSpan.FromMilliseconds(33); timer.Tick+=delegate { Tick(); }; if(!test) timer.Start();
        IsVisibleChanged+=delegate { if(!testing) { if(IsVisible) { Enter("idle"); ScheduleIdle(elapsed.Elapsed.TotalSeconds); timer.Start(); } else timer.Stop(); } };
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged+=DisplayChanged;
        Closing+=delegate { if(sidebar!=null)sidebar.Dispose();Save();CloseExperience(); timer.Stop(); companionTimer.Stop();if(activityPulse!=null)activityPulse.Dispose(); codexTimer.Stop();if(codex!=null)codex.Dispose(); Microsoft.Win32.SystemEvents.DisplaySettingsChanged-=DisplayChanged; if(settings!=null) settings.Close(); if(tray!=null) tray.Dispose(); if(trayIcon!=null)trayIcon.Dispose(); };
        if(!test) { CreateTray(); Loaded+=delegate { Say(warning??"玩家上线。戳我互动，右键打开菜单。",5,3,true); }; }
        if(!testing) { sidebar=new QuotaSidebar(()=>prefs.SidebarEnabled&&!QuietActive&&!manuallyHidden,()=>prefs.CodexUsage,prefs.SidebarPosition,prefs.SidebarScreen,(position,screen)=>{prefs.SidebarPosition=position;prefs.SidebarScreen=screen;Save();},true,SidebarObstacles,false,SidebarAnchor,prefs.SidebarDocked,v=>{prefs.SidebarDocked=v;Save();},SidebarGreet);StartCodex(); }
        if(preview) { sidebar=new QuotaSidebar(()=>true,()=>true,.45,null,(position,screen)=>{},true,SidebarObstacles,true,SidebarAnchor,false,null,SidebarGreet);sidebar.Update(new List<QuotaReading>{new QuotaReading {Bucket="预览示例",Minutes=300,Remaining=68,ResetUtc=DateTime.UtcNow.AddMinutes(102)},new QuotaReading {Bucket="预览示例",Minutes=10080,Remaining=42,ResetUtc=DateTime.UtcNow.AddDays(3)},new QuotaReading {Bucket="预览示例",IsCredits=true,Balance="381.18",ResetUtc=DateTime.MaxValue}});sidebar.SetWorking(true); }
        InitCompanion();
    }
    void StartCodex() {
        codex=new CodexMonitor(System.IO.Path.GetDirectoryName(settingsPath));
        codex.Quotas=delegate(List<QuotaReading> value) { Dispatcher.BeginInvoke(new Action(delegate {if(sidebar!=null)sidebar.Update(value);})); };
        codex.Failure=delegate(string value) { Dispatcher.BeginInvoke(new Action(delegate {if(sidebar!=null)sidebar.Fail(value);})); };
        codex.Tasks=delegate(CodexTaskSnapshot value) { Dispatcher.BeginInvoke(new Action(delegate {ApplyCodexTasks(value);})); };
        codex.UsageEnabled=prefs.CodexUsage;codex.TasksEnabled=prefs.CodexTasks;
        codex.Status=delegate(string status) { Dispatcher.BeginInvoke(new Action(delegate { codexStatus=status;if(codexStatusText!=null)codexStatusText.Text=status; })); };
        codex.Notice=delegate(string title,string message) { Dispatcher.BeginInvoke(new Action(delegate {
            if(Muted("codex"))return;
            if(title=="Codex 任务运行结束"&&IsVisible&&(codexFormWorking||codexWorkState=="completed"))return;
            if(QuietActive) { if(prefs.ImportantDuringQuiet&&tray!=null)tray.ShowBalloonTip(10000,title,message,Forms.ToolTipIcon.Info);else if(codexNotices.Count<30)codexNotices.Enqueue(message);return; }
            if(IsVisible) { if(codexNotices.Count<30)codexNotices.Enqueue(message); }
            else if(tray!=null)tray.ShowBalloonTip(10000,title,message,Forms.ToolTipIcon.Info);
        })); };
        codexTimer.Interval=TimeSpan.FromSeconds(1);codexTimer.Tick+=delegate {
            if(QuietActive)return;
            if(codexNotices.Count==0||pressed||menuOpen)return;
            if(!IsVisible) { if(tray!=null)tray.ShowBalloonTip(10000,"Codex 提醒",codexNotices.Dequeue(),Forms.ToolTipIcon.Info);return; }
            if(bubble.Visibility==Visibility.Visible)return;
            Enter("headpat");Prompt(codexNotices.Dequeue(),"codex",4);
        };codexTimer.Start();codex.Start();
    }
    Preferences ReadPreferences() {
        if(testing) return new Preferences();
        try { var p=new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(settingsPath));
            if(p==null || !Finite(p.Left) || !Finite(p.Top) || !Finite(p.Size)) return new Preferences();
            p.Size=Math.Max(160,Math.Min(600,p.Size));
            if(p.ReminderShown==null)p.ReminderShown=new Dictionary<string,string>();
            if(p.MutedDays==null)p.MutedDays=new Dictionary<string,string>();
            if(p.Deferred==null)p.Deferred=new List<DeferredNotice>();
            if(p.QuietGames==null)p.QuietGames=new Preferences().QuietGames;
            p.Deferred=p.Deferred.Where(x=>x!=null&&!String.IsNullOrEmpty(x.Category)&&x.Text!=null).Take(64).ToList();
            p.BreakMinutes=Math.Max(15,Math.Min(180,p.BreakMinutes));
            return p;
        } catch { return new Preferences(); }
    }
    static bool Finite(double x) { return !Double.IsNaN(x)&&!Double.IsInfinity(x); }
    void Save() {
        if(testing)return;
        prefs.Left=Left; prefs.Top=PetTop;
        try { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(settingsPath));
            string temp=settingsPath+".tmp"; File.WriteAllText(temp,new JavaScriptSerializer().Serialize(prefs),Encoding.UTF8);
            if(File.Exists(settingsPath)) File.Replace(temp,settingsPath,null); else File.Move(temp,settingsPath);
        } catch(Exception ex) { Debug.WriteLine(ex); }
    }
    void DisplayChanged(object sender,EventArgs e) { Dispatcher.BeginInvoke(new Action(Clamp)); }
    Point CursorPoint() { var p=Forms.Cursor.Position; return new Point(p.X/dpiX,p.Y/dpiY); }
    public static double Bound(double n,double min,double max) { return Math.Max(min,Math.Min(Math.Max(min,max),n)); }
    Rect VisibleFrameBounds(BitmapSource image) {
        Rect found;if(image==null)return new Rect(14,70,Math.Max(1,Width-28),prefs.Size);
        if(!visibleBoundsCache.TryGetValue(image,out found)) {
            byte[] pixels;if(!alphaCache.TryGetValue(image,out pixels)) { pixels=new byte[image.PixelWidth*image.PixelHeight*4];image.CopyPixels(pixels,image.PixelWidth*4,0);alphaCache[image]=pixels; }
            int left=image.PixelWidth,top=image.PixelHeight,right=-1,bottom=-1;
            for(int y=0;y<image.PixelHeight;y++)for(int x=0;x<image.PixelWidth;x++)if(pixels[(y*image.PixelWidth+x)*4+3]>20) {
                if(x<left)left=x;if(x>right)right=x;if(y<top)top=y;if(y>bottom)bottom=y;
            }
            found=right>=left?new Rect(left,top,right-left+1,bottom-top+1):new Rect(0,0,image.PixelWidth,image.PixelHeight);
            visibleBoundsCache[image]=found;
        }
        double viewportWidth=Math.Max(1,Width-28),viewportHeight=prefs.Size;
        double fit=Math.Min(viewportWidth/image.PixelWidth,viewportHeight/image.PixelHeight);
        double originX=14+(viewportWidth-image.PixelWidth*fit)/2,originY=70+(viewportHeight-image.PixelHeight*fit)/2;
        return new Rect(originX+found.Left*fit,originY+found.Top*fit,found.Width*fit,found.Height*fit);
    }
    static double ClampVisibleHorizontal(double left,double screenLeft,double screenRight,Rect visible,double padding) {
        return Bound(left,screenLeft+padding-visible.Left,screenRight-padding-visible.Right);
    }
    void Clamp() {
        var visible=VisibleFrameBounds(cachedBitmap);
        var screen=Forms.Screen.FromPoint(new System.Drawing.Point((int)((Left+visible.Left+visible.Width/2)*dpiX),(int)((PetTop+visible.Top+visible.Height/2)*dpiY)));
        var r=screen.WorkingArea;
        Left=ClampVisibleHorizontal(Left,r.Left/dpiX,r.Right/dpiX,visible,3); SetPetWindowTop(Bound(PetTop-bubbleExtra,r.Top/dpiY,r.Bottom/dpiY-Height));
    }
    void Down(object sender,MouseButtonEventArgs e) {
        if(!Opaque(e.GetPosition(pet)))return;
        pressed=true; dragging=false; pressHead=e.GetPosition(pet).Y<pet.ActualHeight*.46; pressCursor=CursorPoint(); startLeft=Left; startTop=Top; pet.CaptureMouse(); lastAction=elapsed.Elapsed.TotalSeconds;BeginTouch(e.GetPosition(pet)); ScheduleIdle(lastAction); e.Handled=true;
    }
    void Move(object sender,MouseEventArgs e) {
        if(!pressed)return; var p=CursorPoint(); var delta=p-pressCursor;
        if(!dragging && delta.Length>5) { dragging=true;touchCombo=0;lastTouchTime=-10; if(!WorkFormReply()){Enter("drag"); Say("喂——这不是传送点！",2);} }
        if(dragging) { Left=startLeft+delta.X; SetPetWindowTop(startTop+delta.Y); }
    }
    void Up(object sender,MouseButtonEventArgs e) {
        if(!pressed)return; bool moved=dragging; pressed=false; dragging=false; pet.ReleaseMouseCapture();
        if(moved) { Clamp(); if(!WorkFormReply()){Enter("land"); Say("落地成功。操作还行。",2);}Save(); }
        else EndTouch(elapsed.Elapsed.TotalSeconds);
        ScheduleIdle(elapsed.Elapsed.TotalSeconds);
        e.Handled=true;
    }
    void React(bool head) {
        if(WorkFormReply())return;
        double now=elapsed.Elapsed.TotalSeconds;
        if(now-lastClick<.18)return;
        combo=now-lastClick<1.5?combo+1:1; lastClick=now; lastAction=now;
        Enter(combo>=4?"annoyed":head?"headpat":"click"); ScheduleIdle(now);
        Say(combo>=4?"连击达成！鼠标耐久还够吗？":head?"摸头？这可是隐藏交互。":Pick(pack.Spec.clickLines,"这局，带你通关。"),3);
    }
    string Pick(string[] lines,string fallback) { return lines==null||lines.Length==0?fallback:lines[random.Next(lines.Length)]; }
    void Enter(string next) { state=next; stateStart=elapsed.Elapsed.TotalSeconds; }
    void ScheduleIdle(double now) { nextIdle=now+18+random.NextDouble()*14; nextBlink=now+3+random.NextDouble()*4; }
    void Say(string message,double seconds,int priority=3,bool welcome=false,bool workSpeech=false) {
        if(codexFormWorking&&!workSpeech)return;
        if(bubble.Visibility==Visibility.Visible&&elapsed.Elapsed.TotalSeconds<bubbleUntil&&priority<bubblePriority)return;
        replies.Children.Clear();bubble.IsHitTestVisible=false;
        ApplyBubbleTheme();
        linkLabel.Text="银狼  /  LV.999";
        linkLabel.Visibility=welcome?Visibility.Visible:Visibility.Collapsed;
        UpdateTaskHeader();
        BubbleSpace(0);
        words.Text=(message??"").Substring(0,Math.Min(200,(message??"").Length)); bubble.Visibility=Visibility.Visible; bubbleUntil=elapsed.Elapsed.TotalSeconds+seconds; bubblePriority=priority;
        LayoutBubble();
    }
    string ChooseLine(string key,string[] lines) {
        string previousLine; lastSpoken.TryGetValue(key,out previousLine);
        var options=lines.Where(x=>x!=previousLine).ToArray();
        string selected=options.Length==0?lines[0]:options[random.Next(options.Length)];lastSpoken[key]=selected;return selected;
    }
    void SpeakAction(string action) {
        if(WorkFormReply())return;
        string[] lines;
        if(prefs.ActionSpeech&&pack.Spec.actionLines!=null&&pack.Spec.actionLines.TryGetValue(action,out lines)) {
            string line=ChooseLine(action,lines); Say(line,5,1);
            if(action=="yawn"&&bubblePriority==1&&words.Text==line&&bubble.Visibility==Visibility.Visible)MaybeInviteReplies();
        }
    }
    void CheckReminders(DateTime localNow) {
        if(codexFormWorking||!prefs.TimeReminders||!IsVisible||pressed||menuOpen||settings!=null||state!="idle"||bubble.Visibility==Visibility.Visible)return;
        var due=ReminderSchedule.Due(pack.Spec.reminders,localNow,prefs.ReminderShown);if(due==null)return;
        if(!String.IsNullOrEmpty(due.action)&&pack.Clips.ContainsKey(due.action))Enter(due.action);
        Prompt(ChooseLine("reminder:"+due.id,due.lines),"daily:"+due.id,2);ScheduleIdle(elapsed.Elapsed.TotalSeconds);
        string date=localNow.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
        foreach(string key in prefs.ReminderShown.Where(x=>String.CompareOrdinal(x.Value,localNow.AddDays(-7).ToString("yyyy-MM-dd"))<0).Select(x=>x.Key).ToArray())prefs.ReminderShown.Remove(key);
        prefs.ReminderShown[ReminderSchedule.Key(due)]=date;Save();
    }
    void SetFrame(BitmapSource image) {
        if(Object.ReferenceEquals(image,cachedBitmap))return;
        previous.Source=cachedBitmap; previous.Opacity=0; frameChanged=elapsed.Elapsed.TotalSeconds;
        cachedBitmap=image; pet.Source=image;
        if(!alphaCache.TryGetValue(image,out alpha)) { alpha=new byte[image.PixelWidth*image.PixelHeight*4]; image.CopyPixels(alpha,image.PixelWidth*4,0); alphaCache[image]=alpha; }
    }
    bool Opaque(Point p) {
        if(cachedBitmap==null||pet.ActualWidth<=0||pet.ActualHeight<=0)return false;
        double fit=Math.Min(pet.ActualWidth/cachedBitmap.PixelWidth,pet.ActualHeight/cachedBitmap.PixelHeight);
        int x=(int)Math.Floor((p.X-(pet.ActualWidth-cachedBitmap.PixelWidth*fit)/2)/fit);
        int y=(int)Math.Floor((p.Y-(pet.ActualHeight-cachedBitmap.PixelHeight*fit)/2)/fit);
        return x>=0&&y>=0&&x<cachedBitmap.PixelWidth&&y<cachedBitmap.PixelHeight&&alpha[(y*cachedBitmap.PixelWidth+x)*4+3]>20;
    }
    IntPtr Hook(IntPtr hwnd,int msg,IntPtr wp,IntPtr lp,ref bool handled) {
        if(msg==0x0312) { if(wp.ToInt32()==ShowHotkey)ToggleVisibility();else if(wp.ToInt32()==PassHotkey)TogglePassThrough();handled=true;return IntPtr.Zero; }
        if(msg==0x0084 && !pressed) {
            if(prefs.MousePassThrough) { handled=true;return new IntPtr(-1); }
            long v=lp.ToInt64(); var screen=new Point((short)(v&0xffff),(short)((v>>16)&0xffff));
            if(bubble.Visibility==Visibility.Visible&&bubble.IsHitTestVisible) { var bp=bubble.PointFromScreen(screen);if(new Rect(bubble.RenderSize).Contains(bp))return IntPtr.Zero; }
            var local=pet.PointFromScreen(screen);
            if(!Opaque(local)) { handled=true; return new IntPtr(-1); }
        }
        return IntPtr.Zero;
    }
    void Tick() {
        double now=elapsed.Elapsed.TotalSeconds;CheckTouchHold(now);Advance(now);
        if(!testing&&now>=nextReminderCheck) { nextReminderCheck=now+1;CheckReminders(DateTime.Now); }
    }
    double ActionDuration(string action) {
        AnimationClip clip; if(pack.Clips.TryGetValue(action,out clip)&&!clip.Loop)return clip.Duration;
        switch(action) { case "dragPreview":return 2.5; case "blink": return .18; case "land":return .55; case "headpat":return 1.4; case "annoyed":return 1.8; case "yawn":return 4.4; case "stretch":return 3; case "game":return 8; case "rhythm":return 6; case "sleep":return 7; default:return 1.1; }
    }
    void Advance(double now) {
        double t=Math.Max(0,now-stateStart);
        if(bubble.Visibility==Visibility.Visible&&now>bubbleUntil)bubble.Visibility=Visibility.Collapsed;
        if(bubble.Visibility!=Visibility.Visible)BubbleSpace(0);
        if(state!="idle"&&state!="drag"&&!(state=="sleep"&&isAway)&&!pressed&&t>=ActionDuration(state)) {
            bool blinking=state=="blink";
            state="idle"; stateStart=now; t=0;
            if(blinking)nextBlink=now+3+random.NextDouble()*4; else ScheduleIdle(now);
        }
        if(!codexFormWorking&&prefs.Idle&&!isAway&&!pressed&&!menuOpen&&settings==null&&state=="idle") {
            if(now>=nextIdle) {
                var available=new[]{"yawn","stretch","game","rhythm"}.Where(x=>pack.Clips.ContainsKey(x)&&x!=lastIdle).ToArray();
                if(available.Length>0) { state=available[random.Next(available.Length)]; lastIdle=state; stateStart=now; t=0; SpeakAction(state); }
                else nextIdle=now+25;
            } else if(now>=nextBlink&&pack.Clips.ContainsKey("blink")) { state="blink"; stateStart=now; t=0; }
        }
        AnimationClip active;
        if(inputAnimating&&state=="game"&&inputHeat>5)t*=1.7;
        if(!pack.Clips.TryGetValue(state=="dragPreview"?"drag":state,out active)&& (state=="headpat"||state=="annoyed"))pack.Clips.TryGetValue("click",out active);
        if(active==null&&state=="idle")pack.Clips.TryGetValue("idle",out active);
        if(state=="sleep"&&active==null&&pack.Clips.TryGetValue("blink",out active))t=.08;
        if(state=="touchHead"&&pack.Clips.TryGetValue("blink",out active))t=.08;
        SetFrame(CodexFormFrame(now,active!=null?active.At(t):pack.Base));
        // A short dissolve softens large key-pose changes; blink remains crisp.
        double blend=Bound((elapsed.Elapsed.TotalSeconds-frameChanged)/((codexFormWorking||now-codexFormEnd<.4)?.09:.075),0,1);
        // During keyboard input, blending two hand poses makes the fixed keyboard
        // appear to jump between foreground and background. Keep one character
        // frame visible while the keyboard overlay remains on its fixed layer.
        bool dissolve=!inputAnimating&&now>touchFeedbackUntil&&state!="blink"&&previous.Source!=null&&blend<1;
        previous.Opacity=dissolve?1:0; pet.Opacity=dissolve?blend:1;
        ApplyCodexCutinVisibility();
        scale.ScaleX=1; scale.ScaleY=1; rotate.Angle=0; shift.Y=0;
        if(state=="touchHead") { rotate.Angle=-3;scale.ScaleY=.99; }
        if(state=="touchBody") { shift.Y=Math.Sin(t*5)*2;rotate.Angle=Math.Sin(t*4)*2; }
        if(state=="drag"||state=="dragPreview") { rotate.Angle=Math.Sin(t*7)*4; shift.Y=-4+Math.Sin(t*5)*2; }
        else if(state=="click"||state=="headpat") { shift.Y=-Math.Abs(Math.Sin(t*6))*5*Math.Max(0,1-t); rotate.Angle=Math.Sin(t*9)*1.5*Math.Max(0,1-t); }
        else if(state=="annoyed") { rotate.Angle=Math.Sin(t*15)*2*Math.Max(0,1-t); }
        else if(state=="land") { scale.ScaleX=1+Math.Sin(t*8)*.06*Math.Max(0,1-t*2); scale.ScaleY=1-Math.Sin(t*8)*.08*Math.Max(0,1-t*2); }
        else if(state=="rhythm") { shift.Y=-Math.Abs(Math.Sin(t*Math.PI*2.5))*4; rotate.Angle=Math.Sin(t*Math.PI*2.5)*2; }
        else if(state=="game") { rotate.Angle=Math.Sin(t*2.5)*.7; }
        else if(state=="yawn") { rotate.Angle=-Math.Sin(Math.PI*Bound(t/ActionDuration(state),0,1))*3; }
        else if(state=="stretch") { scale.ScaleY=1+Math.Sin(Math.PI*Bound(t/ActionDuration(state),0,1))*.015; }
        else if(prefs.Idle) { scale.ScaleY=1+Math.Sin(now*2)*.007; shift.Y=Math.Sin(now*1.7)*1.5; if(state=="sleep")rotate.Angle=-4; }
        if(codexFormWorking&&!pressed&&state!="drag") {
            double lift=Bound((now-codexFormStart-1.4)/.8,0,1);lift=lift*lift*(3-2*lift);
            scale.ScaleX=scale.ScaleY=1;rotate.Angle=0;
            shift.Y=lift*(-prefs.Size*.012+Math.Sin((now-codexFormStart)*1.6)*Math.Max(.65,prefs.Size*.005));
        }
    }
    void PreviewAction(string action) { if(pressed)return; Enter(action); ScheduleIdle(elapsed.Elapsed.TotalSeconds); bubble.Visibility=Visibility.Collapsed; SpeakAction(action=="dragPreview"?"drag":action); }
    void ResizePet(double size) { double bottom=PetTop+prefs.Size+85; prefs.Size=Bound(size,160,600); Width=prefs.Size*1.6+28; Height=prefs.Size+85+bubbleExtra; SetPetWindowTop(bottom-Height);LayoutBubble(); Clamp(); Save(); }
    void ShowPet() { RevealManually(); Activate(); SetForegroundWindow(new WindowInteropHelper(this).Handle); }
    Rect AccessoryBounds() {
        double left=(Width-WorkBubbleMaximumWidth())/2,right=(Width+WorkBubbleMaximumWidth())/2;
        left=Math.Min(left,(Width-keyboard.Width)/2);right=Math.Max(right,(Width+keyboard.Width)/2);
        var images=new List<BitmapSource>();
        if(pack!=null&&pack.Base!=null)images.Add(pack.Base);
        if(codexFrames!=null)images.AddRange(codexFrames.Where(x=>x!=null));
        if(codexWorkClips!=null)foreach(var clip in codexWorkClips.Values)if(clip!=null&&clip.Frames!=null)images.AddRange(clip.Frames.Where(x=>x!=null));
        foreach(var image in images.Distinct()) {var box=VisibleFrameBounds(image);left=Math.Min(left,box.Left);right=Math.Max(right,box.Right);}
        return new Rect(left,0,Math.Max(1,right-left),1);
    }
    double AccessorySafeLeft(double screenRight) {return screenRight-3-AccessoryBounds().Right;}
    void KeepAccessoriesOnScreen() {
        var screen=Forms.Screen.FromPoint(new System.Drawing.Point((int)((Left+Width/2)*dpiX),(int)((PetTop+70+prefs.Size/2)*dpiY)));var area=screen.WorkingArea;var bounds=AccessoryBounds();
        Left=Bound(Left,area.Left/dpiX+3-bounds.Left,area.Right/dpiX-3-bounds.Right);
    }
    void Recover() { var area=Forms.Screen.PrimaryScreen.WorkingArea;Left=AccessorySafeLeft(area.Right/dpiX); SetPetWindowTop(area.Bottom/dpiY-Height-20);Clamp();ShowPet(); Save(); }
    Rect[] SidebarObstacles() {
        if(!IsVisible||Double.IsNaN(Left)||Double.IsNaN(Top))return new Rect[0];
        var result=new List<Rect>();
        var character=SidebarAnchor();
        if(codexFormWorking) {
            var visible=VisibleFrameBounds(cachedBitmap);
            character=new Rect(Left+visible.Left,PetTop+visible.Top,visible.Width,visible.Height);
        }
        result.Add(character);
        if(bubble.Visibility==Visibility.Visible&&bubble.ActualWidth>0&&bubble.ActualHeight>0)
            result.Add(new Rect(Left+(Width-bubble.ActualWidth)/2,Top,bubble.ActualWidth,bubble.ActualHeight));
        return result.ToArray();
    }
    Rect SidebarAnchor() { return new Rect(Left+(Width-prefs.Size*.66)/2,PetTop+70,prefs.Size*.66,prefs.Size); }
    void SidebarGreet() { if(!pressed) { Enter("headpat");ScheduleIdle(elapsed.Elapsed.TotalSeconds); } }
    void ToggleSidebar() { prefs.SidebarEnabled=!prefs.SidebarEnabled;Save(); }
    void ShowSidebar() { prefs.SidebarEnabled=true;Save();if(sidebar!=null)sidebar.Expand(); }
    void CreateTray() {
        trayIcon=new System.Drawing.Icon(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","silver-wolf.ico"),Forms.SystemInformation.SmallIconSize);
        tray=new Forms.NotifyIcon { Icon=trayIcon, Text="银狼 LV.999 · v2.6.22 预览版 · 双击找回桌宠", Visible=true };
        var menu=new Forms.ContextMenuStrip();
        menu.Items.Add("显示 / 找回桌宠",null,delegate { Dispatcher.Invoke(new Action(Recover)); });
        menu.Items.Add("隐藏桌宠",null,delegate { Dispatcher.Invoke(new Action(HideManually)); });
        var passItem=menu.Items.Add("鼠标穿透 · Ctrl+Alt+P",null,delegate { Dispatcher.Invoke(new Action(TogglePassThrough)); }) as Forms.ToolStripMenuItem;
        menu.Opening+=delegate { passItem.Checked=prefs.MousePassThrough; };
        menu.Items.Add("设置与自定义形象",null,delegate { Dispatcher.Invoke(new Action(Settings)); });
        var quotaItem=menu.Items.Add("隐藏额度终端",null,delegate { Dispatcher.Invoke(new Action(ToggleSidebar)); }); menu.Opening+=delegate { quotaItem.Text=prefs.SidebarEnabled?"隐藏额度终端":"显示额度终端"; };
        menu.Items.Add("退出",null,delegate { Dispatcher.Invoke(new Action(Close)); });
        tray.ContextMenuStrip=menu; tray.DoubleClick+=delegate { Dispatcher.Invoke(new Action(Recover)); };
    }
    void MenuItem(ContextMenu menu,string title,Action action) { var item=new System.Windows.Controls.MenuItem { Header=title }; item.Click+=delegate { action(); }; menu.Items.Add(item); }
    void ShowMenu() {
        var menu=new ContextMenu();
        MenuItem(menu,"✦  " + (pack.Spec.name??"桌宠"),delegate { React(false); });
        MenuItem(menu,"互动一下",delegate { React(false); });
        if(codexFormWorking)MenuItem(menu,"退出无敌玩家状态",ExitCodexForm);
        if(codexTasks!=null&&codexTasks.Active.Length>1)MenuItem(menu,"切换关注的任务…",ShowCodexTaskMenu);
        MenuItem(menu,"预览无敌玩家变身",delegate { if(codexFrames!=null){codexFormWorking=true;codexActivationAnnounced=false;codexFormStart=elapsed.Elapsed.TotalSeconds;if(!codexTaskRunning)codexLastTaskEnd=elapsed.Elapsed.TotalSeconds-590;Enter("idle");} });
        MenuItem(menu,prefs.SidebarEnabled?"隐藏额度终端":"显示额度终端",ToggleSidebar);
        var actions=new System.Windows.Controls.MenuItem { Header="动作预览" };
        string[] actionKeys={"headpat","annoyed","drag","yawn","stretch","game","rhythm"};
        string[] actionLabels={"扶镜回应","抱臂嫌弃","被提起","打哈欠","伸懒腰","打游戏","音游节拍"};
        for(int i=0;i<actionKeys.Length;i++) {
            string key=actionKeys[i]; var item=new System.Windows.Controls.MenuItem { Header=actionLabels[i], IsEnabled=pack.Clips.ContainsKey(key) };
            item.Click+=delegate { PreviewAction(key=="drag"?"dragPreview":key); }; actions.Items.Add(item);
        }
        menu.Items.Add(actions);
        MenuItem(menu,prefs.Topmost?"取消置顶":"始终置顶",delegate { prefs.Topmost=!prefs.Topmost; Topmost=prefs.Topmost; Save(); });
        MenuItem(menu,"放大",delegate { ResizePet(prefs.Size+30); }); MenuItem(menu,"缩小",delegate { ResizePet(prefs.Size-30); });
        MenuItem(menu,"鼠标穿透 · Ctrl+Alt+P",TogglePassThrough);
        MenuItem(menu,"设置与自定义形象…",Settings); MenuItem(menu,"隐藏到托盘",HideManually); MenuItem(menu,"退出",Close);
        menuOpen=true; menu.Closed+=delegate { menuOpen=false; }; menu.IsOpen=true;
    }
    void Settings() {
        if(settings!=null) { settings.Activate(); return; }
        settings=new Window { Title="银狼 LV.999 · 设置", Width=410, Height=640, ResizeMode=ResizeMode.NoResize, WindowStartupLocation=WindowStartupLocation.CenterScreen, Background=new SolidColorBrush(Color.FromRgb(245,243,252)), Topmost=true };
        ThemeWindow(settings);
        var panel=new StackPanel { Margin=new Thickness(24) }; settings.Content=new ScrollViewer { Content=panel, VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text="PLAYER SETTINGS  /  2.6.18 预览版", FontSize=20, FontWeight=FontWeights.Bold, Foreground=new SolidColorBrush(Color.FromRgb(76,54,136)) });
        var packLabel=new TextBlock { Text="当前形象："+(pack.Spec.name??"自定义"), Margin=new Thickness(0,12,0,16) }; panel.Children.Add(packLabel);
        panel.Children.Add(new TextBlock { Text="桌宠大小（也可在角色上滚动鼠标滚轮）" });
        var slider=new Slider { Minimum=160, Maximum=600, Value=prefs.Size, Margin=new Thickness(0,8,0,16), TickFrequency=20, IsSnapToTickEnabled=true }; slider.ValueChanged+=delegate { ResizePet(slider.Value); }; panel.Children.Add(slider);
        var top=new CheckBox { Content="始终置顶", IsChecked=prefs.Topmost, Margin=new Thickness(0,0,0,12) }; top.Click+=delegate { prefs.Topmost=top.IsChecked==true; Topmost=prefs.Topmost; Save(); }; panel.Children.Add(top);
        AddStartupSetting(panel);
        AddCompanionSettings(panel);
        AddTouchSettings(panel);
        AddExperienceSettings(panel);
        var usage=new CheckBox { Content="Codex 额度提醒：剩余低于 50% / 20% / 5%",IsChecked=prefs.CodexUsage,Margin=new Thickness(0,0,0,8) };
        usage.Click+=delegate { prefs.CodexUsage=usage.IsChecked==true;if(codex!=null)codex.UsageEnabled=prefs.CodexUsage;Save(); };panel.Children.Add(usage);
        var tasks=new CheckBox { Content="本机 Codex 每轮任务结束时提醒",IsChecked=prefs.CodexTasks,Margin=new Thickness(0,0,0,8) };
        tasks.Click+=delegate { prefs.CodexTasks=tasks.IsChecked==true;if(codex!=null)codex.TasksEnabled=prefs.CodexTasks;Save(); };panel.Children.Add(tasks);
        codexStatusText=new TextBlock { Text=testing?"预览模式不连接 Codex。":codexStatus,TextWrapping=TextWrapping.Wrap,FontSize=12,Foreground=Brushes.DimGray,Margin=new Thickness(0,0,0,8) };panel.Children.Add(codexStatusText);
        panel.Children.Add(new TextBlock { Text="读取已登录的本机 Codex；约每分钟更新额度。\n不包含 ChatGPT 网页聊天额度或云端任务。隐藏时使用 Windows 托盘通知。",TextWrapping=TextWrapping.Wrap,FontSize=12,Foreground=Brushes.DimGray,Margin=new Thickness(0,0,0,12) });
        AddButton(panel,"测试 Codex 提醒气泡",delegate { Enter("headpat");Say("【测试】这轮搞定了。去验收战利品吧！",8,4); });
        var idle=new CheckBox { Content="开启眨眼与随机待机动作", IsChecked=prefs.Idle, Margin=new Thickness(0,0,0,12) }; idle.Click+=delegate { prefs.Idle=idle.IsChecked==true; Enter("idle"); ScheduleIdle(elapsed.Elapsed.TotalSeconds); Save(); }; panel.Children.Add(idle);
        var speech=new CheckBox { Content="动作时显示台词气泡", IsChecked=prefs.ActionSpeech, Margin=new Thickness(0,0,0,12) }; speech.Click+=delegate { prefs.ActionSpeech=speech.IsChecked==true;Save(); };panel.Children.Add(speech);
        var reminders=new CheckBox { Content="定时问候与作息提醒（按电脑本地时间）", IsChecked=prefs.TimeReminders, Margin=new Thickness(0,0,0,8) }; reminders.Click+=delegate { prefs.TimeReminders=reminders.IsChecked==true;Save(); };panel.Children.Add(reminders);
        string reminderTimes=pack.Spec.reminders==null||pack.Spec.reminders.Length==0?"当前素材包没有定时提醒。":String.Join(" · ",pack.Spec.reminders.Select(x=>x.time));
        panel.Children.Add(new TextBlock { Text=reminderTimes+"\n每条每天一次；隐藏时不弹，错过超过 5 分钟不补发。", FontSize=12, Foreground=Brushes.DimGray, Margin=new Thickness(0,0,0,12), TextWrapping=TextWrapping.Wrap });
        var previews=new WrapPanel(); panel.Children.Add(previews);
        string[] previewKeys={"yawn","stretch","game","rhythm"},previewLabels={"打哈欠","伸懒腰","打游戏","音游"};
        for(int i=0;i<previewKeys.Length;i++) { string key=previewKeys[i]; var b=new Button { Content=previewLabels[i], Padding=new Thickness(8,6,8,6), Margin=new Thickness(0,0,6,10) }; b.Click+=delegate { if(pack.Clips.ContainsKey(key))PreviewAction(key); else MessageBox.Show(settings,"当前素材包未提供这个动作。","动作预览"); }; previews.Children.Add(b); }
        AddButton(panel,"导入角色包（选择 pet.json）",delegate {
            var dialog=new Microsoft.Win32.OpenFileDialog { Filter="角色配置|*.json", Title="选择素材包中的 pet.json" };
            if(dialog.ShowDialog(settings)==true) { try { var next=LoadedPack.Load(dialog.FileName); pack=next; alphaCache.Clear();visibleBoundsCache.Clear(); prefs.PackPath=next.Path; SetFrame(pack.Base); Enter("idle"); ScheduleIdle(elapsed.Elapsed.TotalSeconds); Clamp();Save(); packLabel.Text="当前形象："+(pack.Spec.name??"自定义"); Say("新形象已载入。",3); } catch(Exception ex) { MessageBox.Show(settings,"导入失败，已保留当前形象。\n"+ex.Message,"素材包检查"); } }
        });
        AddButton(panel,"恢复默认银狼形象",delegate { pack=LoadedPack.Load(defaultPack); alphaCache.Clear();visibleBoundsCache.Clear(); prefs.PackPath=""; SetFrame(pack.Base); Enter("idle"); ScheduleIdle(elapsed.Elapsed.TotalSeconds); Clamp();Save(); packLabel.Text="当前形象："+pack.Spec.name; });
        AddButton(panel,"找回桌宠到主屏幕",Recover);
        panel.Children.Add(new TextBlock { Text="短按互动 · 按住拖动 · 右键菜单\n台词为二创文案。设置保存在本机。", FontSize=12, Foreground=Brushes.DimGray, Margin=new Thickness(0,16,0,0) });
        settings.Closed+=delegate { settings=null; }; settings.Show();
    }
    void AddButton(Panel p,string label,Action action) { var b=new Button { Content=label,Style=CyberButton(), Padding=new Thickness(10,7,10,7), Margin=new Thickness(0,4,0,0) }; b.Click+=delegate { action(); }; p.Children.Add(b); }
    void AddStartupSetting(Panel panel) {
        var toggle=new CheckBox { Content="开机自启动（登录 Windows 后）", Margin=new Thickness(0,0,0,5), IsEnabled=!testing };
        var hint=new TextBlock { TextWrapping=TextWrapping.Wrap, FontSize=12, Foreground=Brushes.DimGray, Margin=new Thickness(0,0,0,12) };
        var update=new Button { Content="自启动改用当前版本", Padding=new Thickness(8,4,8,4), Margin=new Thickness(0,0,0,12), Visibility=Visibility.Collapsed };
        panel.Children.Add(toggle);panel.Children.Add(hint);panel.Children.Add(update);
        Action refresh=delegate {
            if(testing) { hint.Text="测试窗口不会修改开机启动设置。";return; }
            try {
                var status=StartupLink.ForCurrentUser().Read();
                toggle.IsChecked=status.Present;toggle.IsEnabled=true;
                update.Visibility=status.Present&&!status.Current?Visibility.Visible:Visibility.Collapsed;
                hint.Text=status.Present&&!status.Current?"已开启，但关联了其他位置的版本。可更新到当前版本。":"移动程序文件夹后，请在新位置重新开启。";
            } catch(Exception ex) { toggle.IsEnabled=false;update.Visibility=Visibility.Collapsed;hint.Text="无法读取启动设置："+ex.Message; }
        };
        Action<bool> change=delegate(bool enabled) {
            try { StartupLink.ForCurrentUser().SetEnabled(enabled); }
            catch(Exception ex) { MessageBox.Show(settings,"无法修改开机自启动：\n"+ex.Message,"开机自启动"); }
            refresh();
        };
        toggle.Click+=delegate { change(toggle.IsChecked==true); };
        update.Click+=delegate { change(true); };
        settings.Activated+=delegate { refresh(); };refresh();
    }
    public void SelfTest(string root) {
        var checks=new List<string>();
        QuotaSidebar.Tests(root);checks.Add("PASS sidebar quota colors, primary selection, stale/unknown/expired readings and rendering");
        ExperienceTests();checks.Add("PASS fullscreen-game-only quiet classification, windowed-game/browser exclusion, quiet preference and manual override, themed button template");
        TouchTests(root);checks.Add("PASS touch regions, hold threshold, single hold, drag priority, repeat touches and region/timeout reset");
        CompanionTests(root);checks.Add("PASS active-use timing, five-minute break reset, resume gap, clickable replies, snooze and daily mute");
        BubblePositionTests();checks.Add("PASS 500 bubble resize/hide cycles with native pixel rounding at 100-200% DPI");
        BubbleLayoutTests(root);checks.Add("PASS reply wrapping and plain/reply bubble separation at sizes 160, 300 and 600; no touch diagnostic label");
        CodexMonitor.SelfTest(root);checks.Add("PASS Codex thresholds, same-window quota deduplication, completion debounce, long quiet task state, historical suppression and mute");
        checks.Add("PASS independent concurrent task state/timers, stable main selection, manual switch, result handoff, approval confirmation and request resolution");
        var reminderCases=new[]{new DailyReminder { id="offwork",time="18:00" },new DailyReminder { id="midnight",time="00:00" }};
        var shown=new Dictionary<string,string>();var day=new DateTime(2026,9,10);
        if(ReminderSchedule.Due(reminderCases,day.AddHours(18).AddSeconds(-1),shown)!=null)throw new Exception("reminder fired early");
        if(ReminderSchedule.Due(reminderCases,day.AddHours(18),shown)!=reminderCases[0])throw new Exception("18:00 missing");
        if(ReminderSchedule.Due(reminderCases,day.AddHours(18).AddMinutes(4),shown)!=reminderCases[0]||ReminderSchedule.Due(reminderCases,day.AddHours(18).AddMinutes(5),shown)!=null)throw new Exception("reminder grace window failed");
        shown[ReminderSchedule.Key(reminderCases[0])]="2026-09-10";
        var persisted=new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(new JavaScriptSerializer().Serialize(shown));
        if(ReminderSchedule.Due(reminderCases,day.AddHours(18).AddMinutes(1),persisted)!=null)throw new Exception("reminder repeated after restart");
        if(ReminderSchedule.Due(reminderCases,day.AddDays(1).AddHours(18),persisted)!=reminderCases[0])throw new Exception("next day reminder missing");
        if(ReminderSchedule.Due(reminderCases,day.AddDays(1),persisted)!=reminderCases[1])throw new Exception("midnight date boundary failed");
        checks.Add("PASS local-time reminder boundaries, five-minute grace, persisted deduplication and next-day reset");
        if(pack.Spec.actionLines!=null&&pack.Spec.actionLines.ContainsKey("yawn")) {
            bubble.Visibility=Visibility.Collapsed;SpeakAction("yawn");
            if(!pack.Spec.actionLines["yawn"].Contains(words.Text)||bubble.Visibility!=Visibility.Visible)throw new Exception("yawn dialogue missing");
            string first=words.Text;bubble.Visibility=Visibility.Collapsed;SpeakAction("yawn");if(words.Text==first)throw new Exception("repeated action dialogue");
            Say("priority reminder",8,2);SpeakAction("game");if(words.Text!="priority reminder")throw new Exception("ambient replaced reminder");
            prefs.ActionSpeech=false;bubble.Visibility=Visibility.Collapsed;SpeakAction("yawn");if(bubble.Visibility==Visibility.Visible)throw new Exception("disabled action speech appeared");prefs.ActionSpeech=true;
            checks.Add("PASS action dialogue, non-repetition, reminder priority and speech toggle");
        }
        // Exercise real .lnk creation/readback/removal in QA, never the user's
        // actual Startup folder. Include Unicode and spaces in the target path.
        string startupFixture=System.IO.Path.Combine(root,"qa","startup-"+Guid.NewGuid().ToString("N"));
        string startupExe=System.IO.Path.Combine(startupFixture,"测试 空格","SilverWolfPet.exe");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(startupExe));
        File.Copy(System.IO.Path.Combine(root,"SilverWolfPet.exe"),startupExe);
        var startup=new StartupLink(startupFixture,startupExe);
        string unrelated=System.IO.Path.Combine(startupFixture,"unrelated.txt");File.WriteAllText(unrelated,"preserve");
        try {
            if(startup.Read().Present)throw new Exception("startup should default off");
            startup.SetEnabled(true);var registered=startup.Read();
            if(!registered.Current||registered.Arguments!="--autostart"||registered.WorkingDirectory!=System.IO.Path.GetDirectoryName(startupExe))throw new Exception("startup shortcut roundtrip failed");
            startup.SetEnabled(true);if(Directory.GetFiles(startupFixture,"*.lnk").Length!=1)throw new Exception("duplicate startup links");
            var relocated=new StartupLink(startupFixture,System.IO.Path.Combine(root,"SilverWolfPet.exe"));
            if(!relocated.Read().Present||relocated.Read().Current)throw new Exception("relocated startup not detected");
            relocated.SetEnabled(true);if(!relocated.Read().Current)throw new Exception("startup relocation failed");
            startup.SetEnabled(false);startup.SetEnabled(false);
            if(startup.Read().Present||!File.Exists(unrelated))throw new Exception("startup removal damaged another file");
            checks.Add("PASS startup defaults off; Unicode/spaced target, arguments and working directory roundtrip");
            checks.Add("PASS startup idempotency, relocation, disable and unrelated-file preservation");
        } finally { startup.SetEnabled(false); }
        string fixtures=System.IO.Path.Combine(root,"qa","fixtures"); Directory.CreateDirectory(fixtures);
        string fixture=System.IO.Path.Combine(fixtures,"pet.json");
        File.WriteAllText(fixture,"{\"image\":\"../escape.png\"}");
        bool rejected=false; try { LoadedPack.Load(fixture); } catch(Exception e) { rejected=e.Message.Contains("文件夹内"); }
        if(!rejected)throw new Exception("pack traversal was accepted"); checks.Add("PASS pack rejects paths outside its folder");
        File.WriteAllText(fixture,"{broken json"); rejected=false; try { LoadedPack.Load(fixture); } catch { rejected=true; }
        if(!rejected)throw new Exception("broken JSON was accepted"); checks.Add("PASS malformed pack rejected");
        File.Copy(System.IO.Path.Combine(root,"assets","default","pet.png"),System.IO.Path.Combine(fixtures,"pet.png"),true);
        File.WriteAllText(fixture,"{\"image\":\"pet.png\",\"fps\":999,\"animations\":{\"click\":[\"pet.png\"]}}");
        var valid=LoadedPack.Load(fixture); if(valid.Spec.fps!=30||valid.Frames["click"].Length!=1)throw new Exception("animated pack load failed"); checks.Add("PASS custom animation pack and FPS limit");
        if(Bound(-200,0,100)!=0||Bound(250,0,100)!=100||Bound(20,0,100)!=20) throw new Exception("bounds failed");
        var visibleFrame=VisibleFrameBounds(pack.Base);
        var accessoryBounds=AccessoryBounds();
        if(visibleFrame.IsEmpty||visibleFrame.Width>=Width-29||Math.Abs(ClampVisibleHorizontal(999,0,1000,new Rect(20,0,100,100),3)-877)>.01||Math.Abs(AccessorySafeLeft(1920)+accessoryBounds.Right-1917)>.01||accessoryBounds.Right<(Width+WorkBubbleMaximumWidth())/2)throw new Exception("visible pixel edge clamp or accessory-safe startup placement failed");
        checks.Add("PASS screen bounds use visible pixels instead of transparent frame padding");
        if(pack.Base.PixelWidth==0)throw new Exception("empty asset");
        int clear=0,opaque=0; for(int i=3;i<alpha.Length;i+=4){if(alpha[i]==0)clear++;if(alpha[i]>200)opaque++;}
        if(clear<alpha.Length/4*.08||opaque<alpha.Length/4*.08)throw new Exception("asset lacks usable alpha"); checks.Add("PASS transparent alpha and visible character");
        if(pack.Spec.baseAtlas=="reactions") {
            for(int y=210;y<230;y++)for(int x=200;x<220;x++)if(alpha[(y*cachedBitmap.PixelWidth+x)*4+3]<245)throw new Exception("face was damaged during background removal");
            checks.Add("PASS pale facial details remain opaque");
        }
        Scene.Measure(new Size(Width,Height)); Scene.Arrange(new Rect(0,0,Width,Height)); Scene.UpdateLayout();
        if(Opaque(new Point(0,0)))throw new Exception("corner hit testing failed"); checks.Add("PASS transparent corner hit test");
        React(false); if(state!="click"||bubble.Visibility!=Visibility.Visible)throw new Exception("click failed"); checks.Add("PASS click state and speech bubble");
        Enter("drag"); Tick(); if(state!="drag")throw new Exception("drag failed"); Enter("land"); Tick(); checks.Add("PASS drag and landing state transitions");
        if(pack.Clips.ContainsKey("yawn")) {
            var yawn=pack.Clips["yawn"];
            if(Object.ReferenceEquals(yawn.At(.1),yawn.At(1.1)))throw new Exception("yawn keyframes do not change");
            if(!Object.ReferenceEquals(yawn.At(100),yawn.Frames.Last()))throw new Exception("one-shot must hold last frame");
            checks.Add("PASS yawn keyframes advance and one-shot holds last frame");
            Enter("yawn"); Advance(stateStart+yawn.Duration+.01); if(state!="idle")throw new Exception("yawn failed to return idle");
            checks.Add("PASS completed idle action returns to neutral");
            Enter("drag"); nextIdle=0; nextBlink=0; Advance(stateStart+100); if(state!="drag")throw new Exception("idle interrupted drag");
            var drag=pack.Clips["drag"];
            if(Object.ReferenceEquals(drag.At(.1),drag.At(.6)))throw new Exception("drag expression did not change");
            checks.Add("PASS held drag changes expression and blocks automatic idle");
            Enter("yawn"); lastClick=-10; React(true); if(state!="headpat")throw new Exception("input failed to interrupt idle");
            checks.Add("PASS head interaction interrupts yawn");
            Enter("idle"); nextIdle=0; nextBlink=Double.MaxValue; prefs.Idle=false; Advance(stateStart+.1); if(state!="idle")throw new Exception("disabled idle ran");
            prefs.Idle=true; lastIdle="yawn"; nextIdle=0; Advance(stateStart+.2); if(state=="idle"||state=="yawn")throw new Exception("idle random selection repeated");
            checks.Add("PASS idle toggle and non-repeating random selection");
            RenderActionSheet(root);
        }
        Say("玩家上线。今天也要满级通关。",30); Enter("idle"); Tick();
        Scene.Measure(new Size(Width,Height)); Scene.Arrange(new Rect(0,0,Width,Height)); Scene.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)Width*2,(int)Height*2,192,192,PixelFormats.Pbgra32); bitmap.Render(Scene);
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(System.IO.Path.Combine(root,"qa")); using(var f=File.Create(System.IO.Path.Combine(root,"qa","preview.png")))encoder.Save(f);
        TestCodexForm(root); checks.Add("PASS transformation start, repeat update, working hold and daily restore"); File.WriteAllLines(System.IO.Path.Combine(root,"qa","checks.txt"),checks);
    }
    void RenderActionSheet(string root) {
        string[] states={"idle","headpat","annoyed","drag","drag","yawn","stretch","game","rhythm"};
        string[] labels={"待机 / 眨眼","扶镜回应","连戳 / 抱臂","拖起 / 惊讶","悬空 / 嫌弃","掩嘴哈欠","伸懒腰","打游戏","音游节拍"};
        double[] times={0,.4,.5,.1,.7,1.5,.8,1.2,1.3};
        var drawing=new DrawingVisual();
        using(var dc=drawing.RenderOpen()) {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(239,237,249)),null,new Rect(0,0,900,1260));
            for(int i=0;i<states.Length;i++) {
                Enter(states[i]); nextIdle=Double.MaxValue; nextBlink=Double.MaxValue; Advance(stateStart+times[i]); bubble.Visibility=Visibility.Collapsed; previous.Opacity=0; pet.Opacity=1;
                Scene.Measure(new Size(Width,Height)); Scene.Arrange(new Rect(0,0,Width,Height)); Scene.UpdateLayout();
                var frame=new RenderTargetBitmap((int)Width,(int)Height,96,96,PixelFormats.Pbgra32); frame.Render(Scene);
                double x=(i%3)*300,y=(i/3)*420;
                dc.DrawImage(frame,new Rect(x+37,y-25,Width,Height));
                var text=new FormattedText(labels[i],System.Globalization.CultureInfo.GetCultureInfo("zh-CN"),FlowDirection.LeftToRight,new Typeface("Microsoft YaHei"),17,new SolidColorBrush(Color.FromRgb(66,46,120)),1);
                dc.DrawText(text,new Point(x+(300-text.Width)/2,y+370));
            }
        }
        var bitmap=new RenderTargetBitmap(900,1260,96,96,PixelFormats.Pbgra32); bitmap.Render(drawing);
        var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using(var file=File.Create(System.IO.Path.Combine(root,"qa","actions.png")))png.Save(file);
    }
}
public static class Program {
    [STAThread] public static int Main(string[] args) {
        string root=AppDomain.CurrentDomain.BaseDirectory;
        if(args.Contains("--codex-check")) {
            Directory.CreateDirectory(System.IO.Path.Combine(root,"qa"));
            try { File.WriteAllText(System.IO.Path.Combine(root,"qa","codex-connection.txt"),CodexMonitor.Probe(System.IO.Path.Combine(root,"qa")),Encoding.UTF8);return 0; }
            catch(Exception e) { File.WriteAllText(System.IO.Path.Combine(root,"qa","codex-connection.txt"),e.Message,Encoding.UTF8);return 1; }
        }
        bool test=args.Contains("--self-test");
        bool preview=args.Contains("--preview");
        bool autoStart=args.Contains("--autostart");
        try {
            bool created;
            using(var mutex=new System.Threading.Mutex(true,test?"Local\\SilverWolfPet.Test":preview?"Local\\SilverWolfPet.Preview":"Local\\SilverWolfPet.Desktop",out created)) {
                if(!created) { if(!autoStart)MessageBox.Show("已有桌宠在运行。若要升级，请先在旧版托盘菜单点击退出，再启动 v2.6.23 多任务预览版。","银狼 LV.999"); return 0; }
                var app=new Application { ShutdownMode=ShutdownMode.OnMainWindowClose };
                var window=new PetWindow(root,test,preview); app.MainWindow=window;
                if(test) { window.SelfTest(root); window.Close(); return 0; }
                app.Run(window);
            }
            return 0;
        } catch(Exception ex) {
            if(test)File.WriteAllText(System.IO.Path.Combine(root,"test-error.txt"),ex.ToString());
            else MessageBox.Show("启动失败："+ex.Message,"银狼 LV.999");
            return 1;
        }
    }
}
}
