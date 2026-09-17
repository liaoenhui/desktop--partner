using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using Forms=System.Windows.Forms;

namespace SilverWolfPet {
public partial class PetWindow {
    static Style cyberButton;
    static Style CyberButton() {
        if(cyberButton==null)cyberButton=(Style)XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>
          <Setter Property='Foreground' Value='#F4F1FF'/><Setter Property='FontSize' Value='12'/><Setter Property='FontFamily' Value='Microsoft YaHei'/><Setter Property='Cursor' Value='Hand'/><Setter Property='BorderBrush' Value='#8065C7'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Padding' Value='12,7'/>
          <Setter Property='Background'><Setter.Value><LinearGradientBrush StartPoint='0,0' EndPoint='1,1'><GradientStop Color='#3A285E' Offset='0'/><GradientStop Color='#211D3D' Offset='1'/></LinearGradientBrush></Setter.Value></Setter>
          <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='shell' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' CornerRadius='8,8,3,8' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' RecognizesAccessKey='False'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='shell' Property='BorderBrush' Value='#71E9FF'/><Setter TargetName='shell' Property='Background' Value='#403464'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='shell' Property='Background' Value='#165468'/><Setter TargetName='shell' Property='BorderBrush' Value='#A2F4FF'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='shell' Property='BorderBrush' Value='#71E9FF'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='shell' Property='Opacity' Value='0.45'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");
        return cyberButton;
    }
    static void ThemeWindow(Window window) {
        window.Icon=System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","silver-wolf.ico")));
        window.Background=new SolidColorBrush(Color.FromRgb(23,20,40));window.Foreground=Brushes.WhiteSmoke;
        window.Resources[typeof(Button)]=CyberButton();
        window.Loaded+=delegate { ThemeText(window); };
    }
    static void ThemeText(DependencyObject element) {
        var text=element as TextBlock;if(text!=null)text.Foreground=new SolidColorBrush(text.FontSize>=20?Color.FromRgb(119,233,255):Color.FromRgb(227,220,246));
        var check=element as CheckBox;if(check!=null)check.Foreground=Brushes.WhiteSmoke;
        // ComboBox popup items keep their native dark-on-light palette.
        if(element is ComboBox)return;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(element);i++)ThemeText(VisualTreeHelper.GetChild(element,i));
    }
    [StructLayout(LayoutKind.Sequential)] struct QuietRect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out QuietRect rect);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint process);
    [DllImport("user32.dll",SetLastError=true)] static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd,int id);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] static extern int GetWindowStyle(IntPtr hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongW",SetLastError=true)] static extern int SetWindowStyle(IntPtr hwnd,int index,int style);
    const int ShowHotkey=0x5101,PassHotkey=0x5102;
    bool gameDetected,autoHidden,manuallyHidden,quietOverride;
    double nextQuietPoll;
    string hotkeyStatus="快捷键尚未初始化";
    bool QuietActive { get { return prefs.AutoQuiet&&gameDetected&&!quietOverride; } }
    static bool BrowserExecutable(string file) { return new[]{"chrome.exe","msedge.exe","firefox.exe","brave.exe","opera.exe","iexplore.exe","360chrome.exe","360se.exe","qqbrowser.exe","sogouexplorer.exe"}.Contains(file.ToLowerInvariant()); }
    static bool GameExecutable(string file,string path,System.Collections.Generic.IEnumerable<string> games) {
        if(String.IsNullOrWhiteSpace(file)||BrowserExecutable(file)||games==null)return false;
        return games.Any(x=>!String.IsNullOrWhiteSpace(x)&&(System.IO.Path.IsPathRooted(x)?String.Equals(x,path,StringComparison.OrdinalIgnoreCase):String.Equals(x,file,StringComparison.OrdinalIgnoreCase)));
    }
    static bool CoversScreen(Rect window,Rect screen) {return window.Left<=screen.Left+2&&window.Top<=screen.Top+2&&window.Right>=screen.Right-2&&window.Bottom>=screen.Bottom-2;}
    uint lastGameProcess;
    DateTime gameProcessChecked;
    bool cachedGame;
    bool GameNow() {
        IntPtr hwnd=GetForegroundWindow();if(hwnd==IntPtr.Zero)return false;
        uint process;GetWindowThreadProcessId(hwnd,out process);if(process==(uint)System.Diagnostics.Process.GetCurrentProcess().Id)return gameDetected;
        if(process!=lastGameProcess||(DateTime.UtcNow-gameProcessChecked).TotalSeconds>=5) {
            lastGameProcess=process;gameProcessChecked=DateTime.UtcNow;cachedGame=false;
            try { using(var app=System.Diagnostics.Process.GetProcessById((int)process)) {
                string file=app.ProcessName+".exe",path="";
                if(BrowserExecutable(file))return false;
                if(prefs.QuietGames.Any(x=>!String.IsNullOrWhiteSpace(x)&&System.IO.Path.IsPathRooted(x)))try {path=app.MainModule.FileName;}catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}
                cachedGame=GameExecutable(file,path,prefs.QuietGames);
            }}catch(ArgumentException){}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}
        }
        if(!cachedGame)return false;
        QuietRect rect;if(!GetWindowRect(hwnd,out rect)||rect.Right<=rect.Left||rect.Bottom<=rect.Top)return false;
        var screen=Forms.Screen.FromHandle(hwnd).Bounds;
        return CoversScreen(new Rect(rect.Left,rect.Top,rect.Right-rect.Left,rect.Bottom-rect.Top),new Rect(screen.Left,screen.Top,screen.Width,screen.Height));
    }
    void InitExperience(IntPtr hwnd) {
        if(testing) { hotkeyStatus="预览／自检模式不注册全局快捷键";return; }
        bool show=RegisterHotKey(hwnd,ShowHotkey,0x4003,0x48),pass=RegisterHotKey(hwnd,PassHotkey,0x4003,0x50);
        hotkeyStatus=(show?"Ctrl + Alt + H：显示／隐藏":"显示快捷键注册失败，可能被占用")+"\n"+(pass?"Ctrl + Alt + P：鼠标穿透开关":"穿透快捷键注册失败，可能被占用");
        ApplyPassThrough();
    }
    void CloseExperience() { var hwnd=new WindowInteropHelper(this).Handle;if(hwnd!=IntPtr.Zero) { UnregisterHotKey(hwnd,ShowHotkey);UnregisterHotKey(hwnd,PassHotkey); } }
    void ApplyPassThrough() {
        var hwnd=new WindowInteropHelper(this).Handle;if(hwnd==IntPtr.Zero)return;
        int style=GetWindowStyle(hwnd,-20);SetWindowStyle(hwnd,-20,prefs.MousePassThrough?style|0x20:style&~0x20);
    }
    void TogglePassThrough() {
        pressed=false;dragging=false;pet.ReleaseMouseCapture();prefs.MousePassThrough=!prefs.MousePassThrough;ApplyPassThrough();Save();
        if(tray!=null)tray.ShowBalloonTip(3500,"鼠标穿透",prefs.MousePassThrough?"已开启：点击会落到桌宠后方。Ctrl + Alt + P 或托盘菜单可关闭。":"已关闭：可以点击和拖动桌宠。",Forms.ToolTipIcon.Info);
    }
    void HideManually() { manuallyHidden=true;autoHidden=false;Hide(); }
    void RevealManually() { manuallyHidden=false;autoHidden=false;quietOverride=gameDetected;Show();Clamp(); }
    void ToggleVisibility() { if(IsVisible)HideManually();else RevealManually(); }
    void QuietTick(double now) {
        if(testing||now<nextQuietPoll)return;nextQuietPoll=now+.5;
        ApplyGamePresence(GameNow());
    }
    void ApplyGamePresence(bool running) {
        gameDetected=running;if(!gameDetected)quietOverride=false;
        if(QuietActive) {
            if(IsVisible&&!pressed) { autoHidden=true;Hide(); }
            if(prefs.TimeReminders) {
                var due=ReminderSchedule.Due(pack.Spec.reminders,DateTime.Now,prefs.ReminderShown);
                if(due!=null) { string category="daily:"+due.id;prefs.ReminderShown[ReminderSchedule.Key(due)]=DateTime.Now.ToString("yyyy-MM-dd");if(!Muted(category)) { prefs.Deferred.RemoveAll(x=>x.Category==category);prefs.Deferred.Add(new DeferredNotice { Text=ChooseLine(category,due.lines),Category=category,Priority=2,AtUtc=DateTime.UtcNow }); }Save(); }
            }
        } else if(autoHidden) { autoHidden=false;if(!manuallyHidden)Show(); }
    }
    void AddExperienceSettings(Panel panel) {
        var quiet=new CheckBox { Content="仅全屏游戏时自动隐藏，切回其他应用恢复",IsChecked=prefs.AutoQuiet,Margin=new Thickness(0,8,0,10) };quiet.Click+=delegate { prefs.AutoQuiet=quiet.IsChecked==true;Save(); };panel.Children.Add(quiet);
        panel.Children.Add(new TextBlock {Text="只有下列游戏真正铺满所在屏幕时触发；窗口化游戏和浏览器全屏均不隐藏。\n未识别的游戏可添加实际运行的 .exe（非启动器）。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)});
        var games=new ComboBox {Margin=new Thickness(0,0,0,8),MaxDropDownHeight=180};panel.Children.Add(games);
        Action refreshGames=()=>{games.ItemsSource=null;games.ItemsSource=prefs.QuietGames.ToArray();if(games.Items.Count>0)games.SelectedIndex=0;gameProcessChecked=DateTime.MinValue;};refreshGames();
        AddButton(panel,"添加游戏…",delegate {
            var dialog=new Microsoft.Win32.OpenFileDialog {Title="选择游戏程序（不是启动器）",Filter="游戏程序 (*.exe)|*.exe",CheckFileExists=true};
            if(dialog.ShowDialog(settings)!=true)return;
            if(BrowserExecutable(System.IO.Path.GetFileName(dialog.FileName))){MessageBox.Show(settings,"浏览器保留通知，不加入游戏免打扰列表。","游戏免打扰");return;}
            if(!prefs.QuietGames.Contains(dialog.FileName,StringComparer.OrdinalIgnoreCase))prefs.QuietGames.Add(dialog.FileName);Save();refreshGames();
        });
        AddButton(panel,"移除选中的游戏",delegate {var selected=games.SelectedItem as string;if(selected==null)return;prefs.QuietGames.Remove(selected);Save();refreshGames();});
        var important=new CheckBox { Content="免打扰期间仍允许 Codex 托盘通知",IsChecked=prefs.ImportantDuringQuiet,Margin=new Thickness(0,0,0,10) };important.Click+=delegate { prefs.ImportantDuringQuiet=important.IsChecked==true;Save(); };panel.Children.Add(important);
        AddButton(panel,"切换鼠标穿透（也可用托盘菜单）",TogglePassThrough);
        panel.Children.Add(new TextBlock { Text=hotkeyStatus+"\n穿透状态："+(prefs.MousePassThrough?"开启":"关闭")+"（设置重新打开后更新）",TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,8,0,10) });
    }
    void ExperienceTests() {
        var games=new[]{"StarRail.exe",@"C:\Games\Custom\Play.exe","chrome.exe"};
        if(!GameExecutable("starrail.exe","",games)||!GameExecutable("Play.exe",@"C:\Games\Custom\Play.exe",games)||GameExecutable("Play.exe",@"C:\Work\Play.exe",games)||GameExecutable("chrome.exe","",games)||GameExecutable("msedge.exe","",games)||GameExecutable("POWERPNT.exe","",games)||GameExecutable("unknown.exe","",games)||GameExecutable("","",games))throw new Exception("game-only quiet classification failed");
        if(!CoversScreen(new Rect(-1920,0,1920,1080),new Rect(-1920,0,1920,1080))||CoversScreen(new Rect(0,0,1600,900),new Rect(0,0,1920,1080))||CoversScreen(new Rect(0,0,1920,1040),new Rect(0,0,1920,1080)))throw new Exception("fullscreen game geometry failed");
        gameDetected=true;prefs.AutoQuiet=true;quietOverride=false;if(!QuietActive)throw new Exception("quiet mode not active");quietOverride=true;if(QuietActive)throw new Exception("manual reveal override failed");quietOverride=false;prefs.AutoQuiet=false;if(QuietActive)throw new Exception("quiet toggle ignored");prefs.AutoQuiet=true;gameDetected=false;
        bool wasVisible=IsVisible,reminders=prefs.TimeReminders;prefs.TimeReminders=false;
        Show();ApplyGamePresence(GameExecutable("StarRail.exe","",games));if(IsVisible||!autoHidden||!QuietActive)throw new Exception("game did not hide pet");
        ApplyGamePresence(GameExecutable("chrome.exe","",games));if(!IsVisible||autoHidden||QuietActive)throw new Exception("browser did not restore pet and release Codex quiet gate");
        HideManually();ApplyGamePresence(true);ApplyGamePresence(false);if(IsVisible)throw new Exception("game transition overrode manual hide");manuallyHidden=false;
        if(wasVisible)Show();prefs.TimeReminders=reminders;
        var button=new Button { Style=CyberButton(),Content="测试" };button.ApplyTemplate();if(button.Template==null)throw new Exception("theme template missing");
        var handle=new WindowInteropHelper(this).EnsureHandle();
        int original=GetWindowStyle(handle,-20);prefs.MousePassThrough=true;ApplyPassThrough();if((GetWindowStyle(handle,-20)&0x20)==0)throw new Exception("native mouse pass-through was not applied");
        prefs.MousePassThrough=false;ApplyPassThrough();if((GetWindowStyle(handle,-20)&0x20)!=0)throw new Exception("native mouse pass-through was not removed");
        if((GetWindowStyle(handle,-20)&~0x20)!=(original&~0x20))throw new Exception("pass-through modified unrelated styles");
    }
}
}
