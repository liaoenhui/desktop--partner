using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Forms=System.Windows.Forms;

namespace SilverWolfPet {
public sealed class QuotaSidebar : IDisposable {
    readonly Window handle,popup;
    readonly Window ownerWindow;
    EventHandler ownerRendered;
    bool ownerReady;
    readonly DispatcherTimer timer=new DispatcherTimer();
    readonly TextBlock percent=new TextBlock {FontSize=11,FontWeight=FontWeights.SemiBold,Foreground=Brushes.White,HorizontalAlignment=HorizontalAlignment.Center};
    readonly Border percentBadge;
    readonly Ellipse bean=new Ellipse {Width=28,Height=28};
    readonly Border mouth=new Border {Width=17,Height=3,CornerRadius=new CornerRadius(6),Background=new SolidColorBrush(Color.FromRgb(23,12,62)),VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,19,0,0)};
    readonly StackPanel rows=new StackPanel();
    readonly ScrollViewer rowsViewport;
    readonly TextBlock status=new TextBlock {FontSize=10,Foreground=new SolidColorBrush(Color.FromRgb(208,218,255)),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)};
    readonly Func<bool> visible,enabled;
    readonly Action<double,string> save;
    readonly Func<Rect[]> obstacles;
    readonly Func<Rect> companion;
    readonly Action<bool> saveDock;
    readonly Action greet;
    readonly System.Windows.Shapes.Path shell=new System.Windows.Shapes.Path(),outline=new System.Windows.Shapes.Path();
    readonly StackPanel content=new StackPanel();
    readonly StackPanel body=new StackPanel {VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};
    readonly Grid face=new Grid {Width=30,Height=30};
    bool? drawnDocked;
    bool? outlineRight;
    double outlineHeight=-1,outlineY=-1;
    bool docked;
    Point lastHandle=new Point(Double.NaN,Double.NaN);
    List<QuotaReading> readings=new List<QuotaReading>();
    DateTime updated,enterAt,leaveAt,lastDraw;
    string failure="",device;
    double fraction;
    bool pinned,hovering,disposed,dragged,canPlace=true,canShowHandle=true,working;
    Rect previousPlacement=Rect.Empty;
    Point? dragStart;
    public static Color QuotaColor(double remaining) {return remaining>=60?Color.FromRgb(255,222,104):remaining>=20?Color.FromRgb(100,217,255):Color.FromRgb(255,113,133);}
    static Brush Glass() {
        var brush=new LinearGradientBrush {StartPoint=new Point(0,0),EndPoint=new Point(1,1)};
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(245,19,38,94),0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(242,35,78,157),.48));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(242,91,43,154),1));return brush;
    }
    static Brush Accent() {
        var brush=new LinearGradientBrush {StartPoint=new Point(0,0),EndPoint=new Point(1,0)};
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(238,252,255),0));brush.GradientStops.Add(new GradientStop(Color.FromRgb(91,226,255),.55));brush.GradientStops.Add(new GradientStop(Color.FromRgb(185,132,255),1));return brush;
    }
    static Brush BeanBrush(double? remaining) {
        Color a,b,c;
        if(!remaining.HasValue){a=Color.FromRgb(224,231,255);b=Color.FromRgb(148,165,211);c=Color.FromRgb(96,77,164);}
        else if(remaining.Value>=60){a=Color.FromRgb(255,255,210);b=Color.FromRgb(255,222,95);c=Color.FromRgb(255,145,80);}
        else if(remaining.Value>=20){a=Color.FromRgb(199,255,255);b=Color.FromRgb(73,215,255);c=Color.FromRgb(116,76,255);}
        else {a=Color.FromRgb(255,213,231);b=Color.FromRgb(255,99,143);c=Color.FromRgb(153,49,193);}
        var brush=new RadialGradientBrush {Center=new Point(.34,.27),GradientOrigin=new Point(.25,.2),RadiusX=.8,RadiusY=.8};
        brush.GradientStops.Add(new GradientStop(a,0));brush.GradientStops.Add(new GradientStop(b,.58));brush.GradientStops.Add(new GradientStop(c,1));return brush;
    }
    static Window Surface(double width,double height,string title,bool inspect) {
        var w=new Window {Title=title,Width=width,Height=height,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=inspect,ShowActivated=false,Topmost=true,FontFamily=new FontFamily("Microsoft YaHei")};
        w.SourceInitialized+=delegate {var h=new WindowInteropHelper(w).Handle;SetWindowLong(h,-20,GetWindowLong(h,-20)|0x08000000|(inspect?0:0x80));};
        return w;
    }
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")]static extern int GetWindowLong(IntPtr h,int n);
    [DllImport("user32.dll",EntryPoint="SetWindowLongW")]static extern int SetWindowLong(IntPtr h,int n,int v);
    public QuotaSidebar(Func<bool> show,Func<bool> queryEnabled,double position,string screen,Action<double,string> persist,bool start=true,Func<Rect[]> avoid=null,bool inspect=false,Func<Rect> follow=null,bool dock=false,Action<bool> persistDock=null,Action onClick=null,Window owner=null) {
        companion=follow;docked=dock||follow==null;saveDock=persistDock??(v=>{});greet=onClick??(()=>{});
        obstacles=avoid??(()=>new Rect[0]);
        visible=show;enabled=queryEnabled;save=persist;fraction=Double.IsNaN(position)||Double.IsInfinity(position)?.45:Math.Max(0,Math.Min(1,position));device=screen;
        handle=Surface(40,108,"银狼 · 额度入口",inspect);popup=Surface(306,230,"银狼 · Codex 额度",inspect);
        ownerWindow=owner;
        var scene=new Grid {Background=Brushes.Transparent};handle.Content=scene;
        // Mirrored shoulders and a straight spine: no extra lobe under the bean.
        shell.Fill=Glass();shell.Stroke=new SolidColorBrush(Color.FromArgb(185,207,225,255));shell.StrokeThickness=1;scene.Children.Add(shell);
        scene.Children.Add(body);
        face.Children.Add(bean);
        var glasses=new Canvas {Width=30,Height=30,IsHitTestVisible=false};face.Children.Add(glasses);
        var ink=new SolidColorBrush(Color.FromRgb(35,29,96));
        glasses.Children.Add(new System.Windows.Shapes.Path {Data=Geometry.Parse("M2,7 L12,9 L12,15 L9,18 L4,16 Z M18,9 L28,7 L26,16 L21,18 L18,15 Z"),Fill=ink});
        var bridge=new Rectangle {Width=6,Height=2,Fill=ink};Canvas.SetLeft(bridge,12);Canvas.SetTop(bridge,10);glasses.Children.Add(bridge);
        foreach(var p in new[]{new Point(5,11),new Point(9,13),new Point(20,13),new Point(24,11)}) {var pixel=new Rectangle {Width=3,Height=3,Fill=new SolidColorBrush(p.X<15?Color.FromRgb(117,235,255):Color.FromRgb(235,128,255))};Canvas.SetLeft(pixel,p.X);Canvas.SetTop(pixel,p.Y);glasses.Children.Add(pixel);}
        face.Children.Add(mouth);
        percentBadge=new Border {Child=percent,CornerRadius=new CornerRadius(7),Padding=new Thickness(5,0,5,1),HorizontalAlignment=HorizontalAlignment.Center,Background=new LinearGradientBrush(Color.FromArgb(215,8,20,58),Color.FromArgb(205,48,27,101),0),BorderBrush=Accent(),BorderThickness=new Thickness(.7),Effect=new System.Windows.Media.Effects.DropShadowEffect {Color=Color.FromRgb(80,211,255),BlurRadius=7,ShadowDepth=0,Opacity=.55}};
        SetHandleStyle(docked);
        content.Margin=new Thickness(17,14,29,14);
        var popupScene=new Grid();outline.Fill=Glass();outline.Stroke=new SolidColorBrush(Color.FromArgb(195,191,213,255));outline.StrokeThickness=1;popupScene.Children.Add(outline);popupScene.Children.Add(content);popup.Content=popupScene;
        var head=new DockPanel {Margin=new Thickness(0,0,0,14)};
        var pin=new Button {Content="◇",ToolTip="固定／自动收起",Width=25,Height=23,Background=Brushes.Transparent,Foreground=Brushes.White,BorderThickness=new Thickness(0),Cursor=Cursors.Hand};DockPanel.SetDock(pin,Dock.Right);head.Children.Add(pin);
        pin.Click+=delegate {pinned=!pinned;pin.Content=pinned?"◆":"◇";};
        head.Children.Add(new TextBlock {Text="▪  CODEX 额度",Foreground=Accent(),FontSize=14,FontWeight=FontWeights.SemiBold,Effect=new System.Windows.Media.Effects.DropShadowEffect {Color=Color.FromRgb(55,206,255),BlurRadius=5,ShadowDepth=0,Opacity=.5},VerticalAlignment=VerticalAlignment.Center});content.Children.Add(head);
        rowsViewport=new ScrollViewer {Content=rows,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};content.Children.Add(rowsViewport);content.Children.Add(status);
        handle.MouseEnter+=delegate {hovering=true;enterAt=DateTime.UtcNow;};
        handle.MouseLeave+=delegate {hovering=false;leaveAt=DateTime.UtcNow;};
        popup.MouseLeave+=delegate {leaveAt=DateTime.UtcNow;};
        handle.MouseLeftButtonDown+=delegate {dragStart=new Point(Forms.Cursor.Position.X,Forms.Cursor.Position.Y);dragged=false;handle.CaptureMouse();};
        handle.MouseMove+=delegate {
            if(!dragStart.HasValue)return;var p=Forms.Cursor.Position;
            if((new Point(p.X,p.Y)-dragStart.Value).Length>5)dragged=true;
            if(!dragged)return;
            popup.Hide();Place();
        };
        handle.MouseLeftButtonUp+=delegate {
            bool wasDragged=dragged;dragStart=null;
            if(wasDragged){var p=Forms.Cursor.Position;var monitor=Forms.Screen.FromPoint(p);device=monitor.DeviceName;fraction=Math.Max(0,Math.Min(1,(p.Y-monitor.WorkingArea.Top)/(double)Math.Max(1,monitor.WorkingArea.Height)));docked=companion==null||p.X>=monitor.WorkingArea.Right-64;save(fraction,device);saveDock(docked);}
            handle.ReleaseMouseCapture();Place();
            hovering=false;enterAt=DateTime.UtcNow;leaveAt=DateTime.UtcNow;
            if(!wasDragged){greet();Expand();}
        };
        handle.LostMouseCapture+=delegate {dragStart=null;Place();};
        System.Windows.Automation.AutomationProperties.SetName(handle,"额度入口：悬停查看，拖动调整位置");
        timer.Interval=TimeSpan.FromMilliseconds(100);timer.Tick+=delegate {Tick();};
        Draw();
        if(owner==null){ownerReady=true;if(start)timer.Start();}
        else if(owner.IsVisible){BindOwner();if(start)timer.Start();}
        else {
            // WPF rejects Owner until the owner window has actually been shown.
            // Keep the entry hidden until then, so it can be safely attached.
            ownerRendered=delegate {owner.ContentRendered-=ownerRendered;ownerRendered=null;if(disposed)return;BindOwner();if(start)timer.Start();};
            owner.ContentRendered+=ownerRendered;
        }
    }
    void BindOwner() {handle.Owner=ownerWindow;popup.Owner=ownerWindow;ownerReady=true;}
    public void Update(List<QuotaReading> value) {if(disposed)return;readings=value;updated=DateTime.UtcNow;failure="";Draw();Place();}
    public void Fail(string message) {if(disposed)return;failure=message;Draw();Place();}
    public void SetWorking(bool value) {working=value;if(!value)AnimateMouth();}
    QuotaReading Primary() {return readings.Where(x=>!x.IsCredits&&(x.Minutes==300||x.Minutes==10080)&&x.ResetUtc>DateTime.UtcNow).OrderBy(x=>x.Minutes==300?0:1).ThenBy(x=>x.Bucket=="codex"?0:1).ThenBy(x=>x.Remaining).FirstOrDefault();}
    string PercentText() {return String.Concat(percent.Inlines.OfType<System.Windows.Documents.Run>().Select(x=>x.Text));}
    public void Expand() {if(disposed||!ownerReady||!visible())return;Draw();Place();if(!canShowHandle)return;if(!handle.IsVisible)handle.Show();if(canPlace)popup.Show();leaveAt=DateTime.UtcNow;}
    void Tick() {
        if(disposed)return;
        AnimateMouth();
        if(!visible()){handle.Hide();popup.Hide();return;}
        Place();if(!canShowHandle){handle.Hide();popup.Hide();return;}if(!handle.IsVisible)handle.Show();
        if(hovering!=handle.IsMouseOver){hovering=handle.IsMouseOver;if(hovering)enterAt=DateTime.UtcNow;else leaveAt=DateTime.UtcNow;}
        if(hovering&&!dragStart.HasValue&&!popup.IsVisible&&(DateTime.UtcNow-enterAt).TotalMilliseconds>=200)Expand();
        if(popup.IsVisible&&!pinned&&!handle.IsMouseOver&&!popup.IsMouseOver&&(DateTime.UtcNow-leaveAt).TotalMilliseconds>650)popup.Hide();
        if((DateTime.UtcNow-lastDraw).TotalSeconds>=5){Draw();Place();}
    }
    void AnimateMouth() {
        double open=working?2+7*Math.Abs(Math.Sin(DateTime.UtcNow.TimeOfDay.TotalMilliseconds/170.0)):3;
        mouth.Height=open;mouth.Margin=new Thickness(0,19-(open-3)/2,0,0);
        mouth.Background=new LinearGradientBrush(Color.FromRgb(20,10,57),Color.FromRgb(63,24,117),90);
    }
    public static Rect FindSpace(Rect area,Size size,Point preferred,Rect[] avoid,Rect previous) {
        if(size.Width>area.Width||size.Height>area.Height)return Rect.Empty;
        var blocked=avoid.Where(x=>!x.IsEmpty).Select(x=>{x.Inflate(12,12);return x;}).ToArray();
        Func<Rect,bool> free=r=>area.Contains(r)&&!blocked.Any(x=>x.IntersectsWith(r));
        // Hold the current safe position while open, preventing jumps when a bubble disappears.
        if(!previous.IsEmpty&&Math.Abs(previous.Width-size.Width)<.1&&Math.Abs(previous.Height-size.Height)<.1&&free(previous))return previous;
        Func<double,double,double,double> clamp=(v,min,max)=>Math.Max(min,Math.Min(max,v));
        double x0=clamp(preferred.X,area.Left,area.Right-size.Width),y0=clamp(preferred.Y,area.Top,area.Bottom-size.Height);
        var xs=new List<double>{x0,area.Left,area.Right-size.Width};var ys=new List<double>{y0,area.Top,area.Bottom-size.Height};
        foreach(var b in blocked){xs.Add(b.Left-size.Width-1);xs.Add(b.Right+1);ys.Add(b.Top-size.Height-1);ys.Add(b.Bottom+1);}
        // Preserve the handle's vertical rhythm. When wings or a dialogue occupy the
        // preferred slot, move farther left before considering an upward jump.
        var candidates=from x in xs from y in ys let r=new Rect(x,y,size.Width,size.Height) where free(r) orderby Math.Abs(y-y0)*4+Math.Abs(x-x0) select r;
        return candidates.DefaultIfEmpty(Rect.Empty).First();
    }
    void Place() {
        if(disposed)return;
        var source=PresentationSource.FromVisual(handle);var transform=source==null?Matrix.Identity:source.CompositionTarget.TransformToDevice;
        double dx=transform.M11,dy=transform.M22;
        var anchor=companion==null?Rect.Empty:companion();
        bool moving=dragStart.HasValue&&dragged;var cursor=Forms.Cursor.Position;
        var screen=moving?Forms.Screen.FromPoint(cursor):!docked&&!anchor.IsEmpty?Forms.Screen.FromPoint(new System.Drawing.Point((int)((anchor.Left+anchor.Width/2)*dx),(int)((anchor.Top+anchor.Height/2)*dy))):Forms.Screen.AllScreens.FirstOrDefault(x=>x.DeviceName==device)??Forms.Screen.PrimaryScreen;
        var area=screen.WorkingArea;
        SetHandleStyle(docked&&!moving);
        var work=new Rect(area.Left/dx,area.Top/dy,area.Width/dx,area.Height/dy);
        var reserved=obstacles();
        var preferred=new Point(work.Right-handle.Width,work.Top+work.Height*fraction-handle.Height/2);
        if(!docked&&!anchor.IsEmpty)preferred=new Point(anchor.Left+anchor.Width*.12-handle.Width,anchor.Bottom-handle.Height+4);
        if(moving)preferred=new Point(cursor.X/dx-handle.Width/2,cursor.Y/dy-handle.Height/2);
        // The follower occupies the sprite's transparent lower-left margin. Full window
        // avoidance is for the popup and docked entry, not this intentional attachment.
        var handlePlace=FindHandleSpace(work,new Size(handle.Width,handle.Height),preferred,docked&&!moving,reserved);
        canShowHandle=!handlePlace.IsEmpty;
        if(!handlePlace.IsEmpty)MoveWindow(handle,handlePlace.Left,handlePlace.Top,dx,dy);
        var here=new Point(handle.Left,handle.Top);
        if(here!=lastHandle)previousPlacement=Rect.Empty;lastHandle=here;
        var blockers=reserved.Concat(new[]{new Rect(handle.Left,handle.Top,handle.Width,handle.Height)}).ToArray();
        var chosen=FindSpace(work,new Size(popup.Width,popup.Height),new Point(handle.Left-popup.Width-13,handle.Top+handle.Height/2-popup.Height/2),blockers,popup.IsVisible?previousPlacement:Rect.Empty);
        canPlace=!chosen.IsEmpty;
        if(canPlace){MoveWindow(popup,chosen.Left,chosen.Top,dx,dy);previousPlacement=new Rect(popup.Left,popup.Top,popup.Width,popup.Height);}
        else {popup.Hide();}
        Outline();
    }
    static Rect FindHandleSpace(Rect work,Size size,Point preferred,bool edge,Rect[] obstacles) {
        var avoid=edge?obstacles:new Rect[0];
        var strip=edge?new Rect(work.Right-size.Width,work.Top,size.Width,work.Height):work;
        var place=FindSpace(strip,size,preferred,avoid,Rect.Empty);
        if(place.IsEmpty&&edge)place=FindSpace(work,size,preferred,avoid,Rect.Empty);
        // The entry is the only way to open the quota panel. Keep it reachable
        // even when a bubble or light wing covers every candidate position.
        if(place.IsEmpty)place=FindSpace(work,size,preferred,new Rect[0],Rect.Empty);
        return place;
    }
    static void MoveWindow(Window window,double x,double y,double dx,double dy) {
        x=Math.Round(x*dx)/dx;y=Math.Round(y*dy)/dy;
        if(Double.IsNaN(window.Left)||Math.Abs(window.Left-x)>.01)window.Left=x;
        if(Double.IsNaN(window.Top)||Math.Abs(window.Top-y)>.01)window.Top=y;
    }
    void SetHandleStyle(bool edge) {
        if(drawnDocked==edge)return;drawnDocked=edge;
        handle.Height=edge?108:64;
        shell.Visibility=edge?Visibility.Visible:Visibility.Collapsed;
        percent.FontFamily=new FontFamily("Bahnschrift SemiBold");percent.FontSize=edge?10:13;percent.Foreground=Accent();
        percentBadge.Padding=edge?new Thickness(4,0,4,1):new Thickness(6,0,6,1);
        bean.Effect=edge?null:new System.Windows.Media.Effects.DropShadowEffect {Color=Color.FromRgb(102,92,255),BlurRadius=8,ShadowDepth=0,Opacity=.75};
        shell.Data=edge?Geometry.Parse("M40,0 C40,15 3,15 3,35 L3,73 C3,93 40,93 40,108 Z"):new RectangleGeometry(new Rect(1,1,38,62),18,18);
        body.Margin=new Thickness(edge?5:0,0,0,0);body.Children.Clear();
        face.Margin=new Thickness(0,edge?0:4,0,edge?4:0);
        if(edge){body.Children.Add(face);body.Children.Add(percentBadge);}else {body.Children.Add(percentBadge);body.Children.Add(face);}
    }
    void Outline() {
        bool right=Double.IsNaN(handle.Left)||Double.IsNaN(popup.Left)||handle.Left+handle.Width/2>=popup.Left+popup.Width/2;
        double h=popup.Height,y=Math.Max(30,Math.Min(h-30,handle.Top+handle.Height/2-popup.Top));if(Double.IsNaN(y))y=h/2;
        y=Math.Round(y);
        if(outlineRight==right&&outlineHeight==h&&outlineY==y)return;outlineRight=right;outlineHeight=h;outlineY=y;
        var geometry=new StreamGeometry();using(var g=geometry.Open()) {
            g.BeginFigure(new Point(18,1),true,true);g.LineTo(new Point(276,1),true,false);g.QuadraticBezierTo(new Point(293,1),new Point(293,18),true,false);
            g.LineTo(new Point(293,y-13),true,false);g.QuadraticBezierTo(new Point(293,y-5),new Point(305,y),true,false);g.QuadraticBezierTo(new Point(293,y+5),new Point(293,y+13),true,false);
            g.LineTo(new Point(293,h-18),true,false);g.QuadraticBezierTo(new Point(293,h-1),new Point(276,h-1),true,false);g.LineTo(new Point(18,h-1),true,false);g.QuadraticBezierTo(new Point(1,h-1),new Point(1,h-18),true,false);g.LineTo(new Point(1,18),true,false);g.QuadraticBezierTo(new Point(1,1),new Point(18,1),true,false);
        }
        if(!right)geometry.Transform=new MatrixTransform(-1,0,0,1,306,0);outline.Data=geometry;content.Margin=right?new Thickness(17,14,29,14):new Thickness(29,14,17,14);
    }
    static TextBlock Text(string text,int size=12) {return new TextBlock {Text=text,FontSize=size,Foreground=size>=11?Accent():new SolidColorBrush(Color.FromRgb(197,224,255)),TextWrapping=TextWrapping.Wrap};}
    void Draw() {
        lastDraw=DateTime.UtcNow;rows.Children.Clear();var primary=enabled()?Primary():null;
        bool stale=failure.Length>0||(updated!=DateTime.MinValue&&(DateTime.UtcNow-updated).TotalMinutes>3);
        percent.Inlines.Clear();percent.Inlines.Add(new System.Windows.Documents.Run(primary==null?"—":primary.Remaining.ToString("0.#")));
        if(primary!=null)percent.Inlines.Add(new System.Windows.Documents.Run("%") {FontSize=7,Foreground=new SolidColorBrush(Color.FromRgb(117,226,255)),BaselineAlignment=BaselineAlignment.Center});
        bean.Fill=BeanBrush(primary==null?(double?)null:primary.Remaining);
        bean.Opacity=stale ? .6 : 1;percent.Opacity=stale ? .65 : 1;
        var values=enabled()?readings.OrderBy(x=>x.Bucket=="codex"?0:1).ThenBy(x=>x.Bucket).ThenBy(x=>x.IsCredits?2:x.Minutes==300?0:1).ToList():new List<QuotaReading>();
        foreach(var q in values) {
            if(q.IsCredits) {
                var creditLine=new DockPanel {Margin=new Thickness(0,1,0,11)};
                var balance=Text(q.Unlimited?"无限":q.Balance,11);DockPanel.SetDock(balance,Dock.Right);creditLine.Children.Add(balance);
                creditLine.Children.Add(Text("剩余额度"));rows.Children.Add(creditLine);continue;
            }
            bool expired=q.ResetUtc<=DateTime.UtcNow;
            var line=new DockPanel();var amount=Text(expired?"待更新":"剩余 "+q.Remaining.ToString("0.#")+"%",11);DockPanel.SetDock(amount,Dock.Right);line.Children.Add(amount);
            line.Children.Add(Text((q.Bucket=="codex"?"":q.Bucket+" · ")+(q.Minutes==300?"5 小时":"每周")));rows.Children.Add(line);
            var track=new Grid {Height=4,Margin=new Thickness(0,7,0,5),Background=new SolidColorBrush(Color.FromArgb(65,211,218,255))};
            track.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(expired?0:q.Remaining,GridUnitType.Star)});track.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(expired?100:100-q.Remaining,GridUnitType.Star)});
            track.Children.Add(new Border {Background=q.Remaining>=20&&q.Remaining<60?BeanBrush(q.Remaining):new SolidColorBrush(QuotaColor(q.Remaining)),CornerRadius=new CornerRadius(2)});rows.Children.Add(track);
            var until=q.ResetUtc-DateTime.UtcNow;
            var reset=Text(expired?"已到重置时间，等待查询":q.Minutes==300?q.ResetUtc.ToLocalTime().ToString("MM-dd HH:mm")+" 重置（"+((int)until.TotalHours)+"小时"+until.Minutes+"分后）":q.ResetUtc.ToLocalTime().ToString("MM-dd HH:mm")+" 重置",10);
            reset.Opacity=.75;reset.Margin=new Thickness(0,0,0,13);rows.Children.Add(reset);
        }
        if(values.Count==0)rows.Children.Add(Text(enabled()?"暂未取得额度数据":"额度查询已关闭",12));
        status.Text=!enabled()?"在桌宠设置中开启额度查询":failure.Length>0?(updated==DateTime.MinValue?"连接失败":"更新失败 · 上次数据")+"\n"+failure:updated==DateTime.MinValue?"等待连接本机 Codex…":values.Count==0?"接口未提供有效额度":stale?"上次数据 · "+updated.ToLocalTime().ToString("HH:mm:ss"):"● 已更新 "+updated.ToLocalTime().ToString("HH:mm:ss");
        double width=258;rows.InvalidateMeasure();rows.Measure(new Size(width,Double.PositiveInfinity));rowsViewport.Height=Math.Min(330,Math.Ceiling(rows.DesiredSize.Height));status.InvalidateMeasure();status.Measure(new Size(width,Double.PositiveInfinity));popup.Height=Math.Max(130,Math.Ceiling(30+37+rowsViewport.Height+status.DesiredSize.Height));Outline();
    }
    public void Dispose() {if(disposed)return;disposed=true;if(ownerWindow!=null&&ownerRendered!=null)ownerWindow.ContentRendered-=ownerRendered;timer.Stop();handle.Close();popup.Close();}
    public static void Tests(string root) {
        var petBounds=new Rect(700,200,180,260);
        var petWindow=new Window();
        using(var owned=new QuotaSidebar(()=>true,()=>true,.45,null,(a,b)=>{},false,null,false,()=>petBounds,false,null,null,petWindow)) {
            if(owned.ownerReady||owned.handle.Owner!=null||owned.popup.Owner!=null)throw new Exception("bean owner was bound before the pet appeared");
            petWindow.Show();
            Dispatcher.CurrentDispatcher.Invoke(new Action(()=>{}),DispatcherPriority.ApplicationIdle);
            if(owned.handle.Owner!=petWindow||owned.popup.Owner!=petWindow)throw new Exception("bean and panel lost pet window ownership");
        }
        petWindow.Close();
        using(var follower=new QuotaSidebar(()=>true,()=>true,.45,null,(a,b)=>{},false,()=>new[]{petBounds},false,()=>petBounds)) {
            follower.Update(new List<QuotaReading>{new QuotaReading {Bucket="codex",Minutes=300,Remaining=68,ResetUtc=DateTime.UtcNow.AddHours(1)}});
            if(follower.shell.Visibility!=Visibility.Collapsed||follower.PercentText()!="68%")throw new Exception("borderless follower label");
            if(!(follower.bean.Fill is RadialGradientBrush)||follower.mouth.Height!=3)throw new Exception("hacker bean style");
            follower.SetWorking(true);double low=Double.MaxValue,high=Double.MinValue;
            for(int i=0;i<6;i++){System.Threading.Thread.Sleep(45);follower.AnimateMouth();low=Math.Min(low,follower.mouth.Height);high=Math.Max(high,follower.mouth.Height);}
            if(high-low<.5)throw new Exception("working mouth did not animate");follower.SetWorking(false);
            if(follower.docked||follower.handle.Height!=64||follower.handle.Left>=petBounds.Left||Math.Abs(follower.handle.Top+64-petBounds.Bottom-4)>1||follower.body.Children[0]!=follower.percentBadge)throw new Exception("companion lower-left placement / label order");
            if(Math.Abs((follower.popup.Top+follower.popup.Height/2)-(follower.handle.Top+follower.handle.Height/2))>1)throw new Exception("follower popup shifted vertically without obstruction");
            var leftProperty=System.ComponentModel.DependencyPropertyDescriptor.FromProperty(Window.LeftProperty,typeof(Window));
            var topProperty=System.ComponentModel.DependencyPropertyDescriptor.FromProperty(Window.TopProperty,typeof(Window));
            int writes=0;EventHandler changed=(a,b)=>writes++;leftProperty.AddValueChanged(follower.handle,changed);topProperty.AddValueChanged(follower.handle,changed);
            var shellBefore=follower.shell.Data;var outlineBefore=follower.outline.Data;
            for(int i=0;i<300;i++)follower.Place();
            if(writes!=0||!Object.ReferenceEquals(shellBefore,follower.shell.Data)||!Object.ReferenceEquals(outlineBefore,follower.outline.Data))throw new Exception("stationary entry moves or recreates geometry");
            double x=follower.handle.Left;petBounds.Offset(30,0);follower.Place();
            if(Math.Abs(follower.handle.Left-x-30)>1||writes!=1)throw new Exception("entry did not move directly to final position");
            leftProperty.RemoveValueChanged(follower.handle,changed);topProperty.RemoveValueChanged(follower.handle,changed);
            if(new Rect(follower.popup.Left,follower.popup.Top,follower.popup.Width,follower.popup.Height).IntersectsWith(petBounds))throw new Exception("terminal covers companion");
            Directory.CreateDirectory(System.IO.Path.Combine(root,"qa"));Render(follower.handle,root,"sidebar-follow.png");
            follower.popup.Left=follower.handle.Left+60;follower.Outline();Render(follower.popup,root,"sidebar-pointer-left.png");
            follower.docked=true;follower.Place();if(follower.handle.Height!=108||follower.body.Children[1]!=follower.percentBadge)throw new Exception("edge docking shape / label order");
        }
        var area=new Rect(0,0,1280,720);var size=new Size(294,220);var ideal=new Point(946,350);
        var character=new Rect(1040,450,210,250);var balloon=new Rect(1010,290,240,150);
        var located=FindSpace(area,size,ideal,new[]{character,balloon},Rect.Empty);
        if(located.IsEmpty||!area.Contains(located)||located.IntersectsWith(character)||located.IntersectsWith(balloon))throw new Exception("sidebar covers character/bubble");
        var moved=new Rect(located.Left,located.Top,200,180);
        var adjusted=FindSpace(area,size,ideal,new[]{character,balloon,moved},located);
        if(adjusted.IsEmpty||adjusted.IntersectsWith(moved))throw new Exception("sidebar did not avoid newly appeared bubble");
        var wingBlocked=FindSpace(new Rect(0,0,1600,900),size,new Point(690,500),new[]{new Rect(620,430,450,310)},Rect.Empty);
        if(wingBlocked.IsEmpty||Math.Abs(wingBlocked.Top-500)>1||wingBlocked.Right>608)throw new Exception("sidebar moved upward instead of left of wings");
        if(FindSpace(area,size,ideal,new[]{area},Rect.Empty)!=Rect.Empty)throw new Exception("sidebar overlaps when no free space");
        var edgeArea=new Rect(0,0,1280,720);var edgeSize=new Size(40,108);
        var edgePlace=FindHandleSpace(edgeArea,edgeSize,new Point(1240,500),true,new[]{new Rect(1210,0,70,720)});
        if(edgePlace.IsEmpty||!edgeArea.Contains(edgePlace))throw new Exception("edge bean disappeared when its strip was blocked");
        var crowdedPlace=FindHandleSpace(edgeArea,edgeSize,new Point(1240,500),true,new[]{edgeArea});
        if(crowdedPlace.IsEmpty||!edgeArea.Contains(crowdedPlace))throw new Exception("bean disappeared when all safe slots were blocked");
        foreach(var a in new[]{new Rect(-1280,0,1280,720),new Rect(0,0,853,480)}) {
            var result=FindSpace(a,size,new Point(a.Right-334,a.Bottom-180),new[]{new Rect(a.Right-250,a.Bottom-260,250,260)},Rect.Empty);
            if(result.IsEmpty||!a.Contains(result))throw new Exception("sidebar screen edge placement");
        }
        if(QuotaColor(60)!=QuotaColor(100)||QuotaColor(20)!=QuotaColor(59.9)||QuotaColor(19.9)!=QuotaColor(0)||QuotaColor(20)==QuotaColor(19.9))throw new Exception("sidebar thresholds");
        using(var view=new QuotaSidebar(()=>true,()=>true,.45,null,(a,b)=>{},false)) {
            view.Update(new List<QuotaReading>{new QuotaReading {Bucket="codex",Minutes=300,Remaining=68,ResetUtc=DateTime.UtcNow.AddMinutes(102)},new QuotaReading {Bucket="codex",Minutes=10080,Remaining=42,ResetUtc=DateTime.UtcNow.AddDays(3)},new QuotaReading {Bucket="codex",IsCredits=true,Balance="381.18",ResetUtc=DateTime.MaxValue}});
            if(view.PercentText()!="68%")throw new Exception("sidebar primary"); if(view.rowsViewport.Height<110||view.popup.Height<200)throw new Exception("sidebar sizes "+view.rowsViewport.Height+" / "+view.popup.Height);
            Directory.CreateDirectory(System.IO.Path.Combine(root,"qa"));
            Render(view.popup,root,"sidebar.png");Render(view.handle,root,"sidebar-handle.png"); var last=(FrameworkElement)view.rows.Children[view.rows.Children.Count-1];if(last.TranslatePoint(new Point(0,last.ActualHeight),view.rows).Y>view.rowsViewport.Height+1)throw new Exception("last quota row clipped");
            view.Fail("网络不可用");if(!view.status.Text.Contains("上次数据")||view.PercentText()!="68%")throw new Exception("sidebar stale data");
            view.Update(new List<QuotaReading>());if(view.PercentText()!="—")throw new Exception("sidebar unknown shown as zero");
            view.Update(new List<QuotaReading>{new QuotaReading {Bucket="codex",Minutes=10080,Remaining=42,ResetUtc=DateTime.UtcNow.AddDays(2)}});if(view.PercentText()!="42%")throw new Exception("sidebar weekly fallback");
            view.Update(new List<QuotaReading>{new QuotaReading {Bucket="codex",Minutes=300,Remaining=10,ResetUtc=DateTime.UtcNow.AddSeconds(-1)}});if(view.PercentText()!="—")throw new Exception("sidebar expired data");
        }
    }
    static void Render(Window w,string root,string name) {
        var content=(FrameworkElement)w.Content;content.Measure(new Size(w.Width,w.Height));content.Arrange(new Rect(0,0,w.Width,w.Height));content.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)w.Width*2,(int)w.Height*2,192,192,PixelFormats.Pbgra32);bitmap.Render(content);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(System.IO.Path.Combine(root,"qa",name)))png.Save(file);
    }
}
}
