using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms=System.Windows.Forms;

namespace SilverWolfPet {
// Small vector mascot keeps the quota entry crisp without another large bitmap.
sealed class EdgeMochi : FrameworkElement {
    protected override void OnRender(DrawingContext d) {
        var ink=QuotaPanelChrome.Solid("#382455");var outline=new Pen(QuotaPanelChrome.Solid("#9B572E"),1);
        d.DrawGeometry(QuotaPanelChrome.Gradient("#FFF69B","#FFD35E","#FFC077"),outline,Geometry.Parse("M4,24 C-1,24 0,14 5,13 C5,0 29,0 31,13 C37,15 37,24 31,24 Z"));
        d.DrawGeometry(ink,null,Geometry.Parse("M5,10 L16,11 L15,18 L8,18 L6,16 Z M20,11 L31,10 L30,16 L27,18 L21,18 Z M15,12 L21,12 L21,14 L15,14 Z"));
        d.DrawRectangle(QuotaPanelChrome.Solid("#56E9FF"),null,new Rect(8,12,4,4));d.DrawRectangle(QuotaPanelChrome.Solid("#F78AD9"),null,new Rect(25,12,4,4));
        d.DrawEllipse(QuotaPanelChrome.Solid("#FFA882"),null,new Point(6,20),2.7,1.5);d.DrawEllipse(QuotaPanelChrome.Solid("#FFA882"),null,new Point(30,20),2.7,1.5);
        d.DrawGeometry(null,new Pen(ink,1.1),Geometry.Parse("M14,20 Q16,23 18,20 Q20,23 22,20"));
        d.DrawEllipse(QuotaPanelChrome.Solid("#FFE78B"),outline,new Point(9,24),4,2.8);d.DrawEllipse(QuotaPanelChrome.Solid("#FFE78B"),outline,new Point(27,24),4,2.8);
    }
}
public partial class PetWindow {
    bool edgeMode,edgeLeft;
    string edgeDevice;
    BitmapSource edgeFrame;
    const double EdgeWidth=84,EdgeHeight=144;
    internal void PrepareEdgeStartupCheck() {prefs.EdgeSide="right";prefs.EdgeScreen="";prefs.EdgeY=.65;}
    internal bool ReadyForEdgeStartupCheck {get{return ReadyForStartupCheck&&edgeMode&&Width==EdgeWidth&&Height==EdgeHeight&&sidebar.EdgeAttachedAt(EdgeBeanAnchor());}}
    void LoadEdgeMode(string root) {
        edgeFrame=ReadEdgeImage(Path.Combine(root,"assets","edge-mode","peek-right.png"));LoadEdgeAnimations(root);
    }
    static BitmapSource ReadEdgeImage(string path) {
        var image=new BitmapImage();image.BeginInit();image.UriSource=new Uri(path);image.DecodePixelWidth=256;image.CacheOption=BitmapCacheOption.OnLoad;image.EndInit();image.Freeze();
        var source=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);source.Freeze();
        var pixels=new byte[source.PixelWidth*source.PixelHeight*4];source.CopyPixels(pixels,source.PixelWidth*4,0);
        int l=source.PixelWidth,t=source.PixelHeight,r=0,b=0;
        for(int y=0;y<source.PixelHeight;y++)for(int x=0;x<source.PixelWidth;x++)if(pixels[(y*source.PixelWidth+x)*4+3]>20){l=Math.Min(l,x);t=Math.Min(t,y);r=Math.Max(r,x+1);b=Math.Max(b,y+1);}
        if(r<=l||b<=t)throw new InvalidDataException("Empty edge pose");
        var result=new CroppedBitmap(source,new Int32Rect(l,t,r-l,b-t));result.Freeze();return result;
    }
    static int EdgeCrossing(Rect character,Rect work) {
        double threshold=Math.Min(32,character.Width*.25);
        if(character.Right>work.Right+threshold)return 1;
        if(character.Left<work.Left-threshold)return -1;
        return 0;
    }
    bool TryEnterEdgeMode() {
        if(edgeMode)return true;
        var area=Forms.Screen.FromPoint(Forms.Cursor.Position).WorkingArea;
        var work=new Rect(area.Left/dpiX,area.Top/dpiY,area.Width/dpiX,area.Height/dpiY);
        var box=VisibleFrameBounds(cachedBitmap);box.Offset(Left,PetTop);
        int side=EdgeCrossing(box,work);if(side==0)return false;
        // Moving between adjacent monitors is an ordinary drag, not hiding offscreen.
        var monitor=Forms.Screen.FromPoint(Forms.Cursor.Position);
        var outside=new System.Drawing.Point(side<0?monitor.Bounds.Left-1:monitor.Bounds.Right,Forms.Cursor.Position.Y);
        if(Forms.Screen.AllScreens.Any(x=>x.DeviceName!=monitor.DeviceName&&x.Bounds.Contains(outside)))return false;
        double y=box.Top+box.Height*.35;Clamp();Save();
        EnterEdgeMode(side<0,Forms.Screen.FromPoint(Forms.Cursor.Position).DeviceName,y);return true;
    }
    void EnterEdgeMode(bool left,string device,double y) {
        CompleteCodexExit();codexTaskRunning=false;codexNotices.Clear();
        ClearCodexWaitingBubble();replies.Children.Clear();bubble.Visibility=Visibility.Collapsed;bubblePriority=0;bubble.IsHitTestVisible=false;BubbleSpace(0);
        edgeMode=true;edgeLeft=left;edgeDevice=device;
        if(sidebar!=null){sidebar.SetWorking(false);sidebar.ResetAttachment();}
        keyboard.Visibility=Visibility.Collapsed;inputAnimating=false;inputHeat=0;inputUntil=0;
        Width=EdgeWidth;Height=EdgeHeight;pet.Margin=new Thickness(0,42,0,3);previous.Margin=pet.Margin;
        pet.RenderTransformOrigin=new Point(.5,.5);scale.ScaleX=left?-1:1;scale.ScaleY=1;rotate.Angle=0;shift.Y=0;
        SetFrame(edgeFrame);previous.Source=null;previous.Opacity=0;pet.Opacity=1;
        SetPetWindowTop(y);ClampEdge();Enter("idle");ResetEdgeAnimation(elapsed.Elapsed.TotalSeconds);Save();
    }
    void ClampEdge() {
        var screen=Forms.Screen.AllScreens.FirstOrDefault(x=>x.DeviceName==edgeDevice)??Forms.Screen.PrimaryScreen;edgeDevice=screen.DeviceName;
        var a=screen.WorkingArea;Left=edgeLeft?a.Left/dpiX:a.Right/dpiX-Width;
        SetPetWindowTop(Bound(Top,a.Top/dpiY,a.Bottom/dpiY-Height));
    }
    void ExitEdgeMode() {
        if(!edgeMode)return;
        double center=Left+Width/2,bottom=Top+Height;
        edgeMode=false;edgeAction=null;prefs.EdgeSide="";edgeDevice=null;
        if(sidebar!=null)sidebar.ResetAttachment();
        Width=prefs.Size*1.6+28;Height=prefs.Size+85;pet.Margin=new Thickness(14,70,14,15);previous.Margin=pet.Margin;pet.RenderTransformOrigin=new Point(.5,.85);
        scale.ScaleX=scale.ScaleY=1;rotate.Angle=0;shift.Y=0;SetFrame(pack.Base);previous.Source=null;previous.Opacity=0;
        Left=center-Width/2;SetPetWindowTop(bottom-Height);timer.Interval=TimeSpan.FromMilliseconds(33);
        displayedTaskKey=displayedTaskState=displayedRequestId=displayedWaitingKind=null;
        var latest=codexTasks;
        if(latest!=null&&latest.Active.Any()) {
            ApplyCodexTasks(latest);codexIntroComplete=true;codexIntroQueuedState=null;codexIntroRelease=0;codexFormStart=elapsed.Elapsed.TotalSeconds-10;codexActivationAnnounced=true;
            if(latest.Display!=null)ApplyCodexWorkState(latest.Display.State,elapsed.Elapsed.TotalSeconds);
        }
        Enter("idle");ScheduleIdle(elapsed.Elapsed.TotalSeconds);Clamp();Save();
    }
    Rect EdgeBeanAnchor() {return edgeMode?new Rect(Left+(edgeLeft?EdgeWidth*.32:EdgeWidth*.68)-22,Top+2,44,54):Rect.Empty;}
    void SaveEdgePosition() {
        prefs.EdgeSide=edgeLeft?"left":"right";prefs.EdgeScreen=edgeDevice;
        var screen=Forms.Screen.AllScreens.FirstOrDefault(x=>x.DeviceName==edgeDevice)??Forms.Screen.PrimaryScreen;
        prefs.EdgeY=Bound((Top-screen.WorkingArea.Top/dpiY)/Math.Max(1,screen.WorkingArea.Height/dpiY-EdgeHeight),0,1);
    }
    void RestoreEdgeMode() {
        if(prefs.EdgeSide!="left"&&prefs.EdgeSide!="right")return;
        var screen=Forms.Screen.AllScreens.FirstOrDefault(x=>x.DeviceName==prefs.EdgeScreen)??Forms.Screen.PrimaryScreen;
        EnterEdgeMode(prefs.EdgeSide=="left",screen.DeviceName,screen.WorkingArea.Top/dpiY+Bound(Finite(prefs.EdgeY)?prefs.EdgeY:.6,0,1)*Math.Max(0,screen.WorkingArea.Height/dpiY-EdgeHeight));
    }
    void ShowEdgeMenu() {
        var menu=new ContextMenu();MenuItem(menu,"拖回桌面／退出贴边",ExitEdgeMode);MenuItem(menu,"查看 Codex 额度",ShowSidebar);
        MenuItem(menu,"开心一下",delegate{edgeHappyAfter=0;PlayEdgeAnimation("happy",elapsed.Elapsed.TotalSeconds);});
        MenuItem(menu,"打个瞌睡",delegate{PlayEdgeAnimation("sleepy",elapsed.Elapsed.TotalSeconds);});
        var expressions=new MenuItem {Header="表情预览"};menu.Items.Add(expressions);
        string[] names={"暗中观察","得意","疑惑","偷笑"},actions={"observe","smug","puzzled","snicker"};
        for(int i=0;i<names.Length;i++) {
            string action=actions[i];var item=new MenuItem {Header=names[i]};
            item.Click+=delegate{PlayEdgeAnimation(action,elapsed.Elapsed.TotalSeconds);};expressions.Items.Add(item);
        }
        MenuItem(menu,"隐藏到托盘",HideManually);MenuItem(menu,"退出",Close);
        menuOpen=true;menu.Closed+=delegate{menuOpen=false;};menu.IsOpen=true;
    }
    void EdgeModeTests(string root) {
        if(EdgeCrossing(new Rect(920,0,100,100),new Rect(0,0,1000,800))!=0||EdgeCrossing(new Rect(940,0,100,100),new Rect(0,0,1000,800))!=1||EdgeCrossing(new Rect(-40,0,100,100),new Rect(0,0,1000,800))!=-1)throw new Exception("edge entry threshold failed");
        double oldSize=prefs.Size,oldLeft=Left,oldTop=PetTop;var oldTasks=codexTasks;codexTasks=null;
        foreach(bool left in new[]{false,true}) {
            EnterEdgeMode(left,Forms.Screen.PrimaryScreen.DeviceName,180);
            if(Width>90||Height>150||!edgeMode||scale.ScaleX!=(left?-1:1))throw new Exception("compact mirrored edge layout failed");
            var task=new CodexTaskView {Key="edge-task",State="waiting-input",WaitingKind="input",WaitingRequestId="edge-q",Running=true,StartedUtc=DateTime.UtcNow};
            ApplyCodexTasks(new CodexTaskSnapshot {Display=task,Active=new[]{task}});SayCodex("must stay hidden",10);Tick();
            if(codexFormWorking||bubble.Visibility==Visibility.Visible||keyboard.Visibility==Visibility.Visible||cachedBitmap!=edgeFrame)throw new Exception("work presentation leaked into edge mode");
            Scene.Measure(new Size(Width,Height));Scene.Arrange(new Rect(0,0,Width,Height));Scene.UpdateLayout();
            var localFace=new Point(pet.ActualWidth*.65,pet.ActualHeight*.65);
            var sceneFace=pet.TranslatePoint(localFace,Scene);var restoredFace=Scene.TranslatePoint(sceneFace,pet);
            if((restoredFace-localFace).Length>.01||!Opaque(restoredFace))throw new Exception("mirrored edge face cannot be clicked or dragged");
            var shot=new RenderTargetBitmap((int)Width*3,(int)Height*3,288,288,PixelFormats.Pbgra32);shot.Render(Scene);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(shot));
            Directory.CreateDirectory(Path.Combine(root,"qa"));using(var f=File.Create(Path.Combine(root,"qa",left?"edge-left.png":"edge-right.png")))png.Save(f);
            QuotaSidebar.TestEdgeAttachment(EdgeBeanAnchor,SidebarAnchor,SidebarObstacles,Scene,root,left?"edge-left-review.png":"edge-right-review.png");
            SaveEdgePosition();string savedSide=prefs.EdgeSide;double savedY=prefs.EdgeY;
            if(savedSide!=(left?"left":"right")||savedY<0||savedY>1)throw new Exception("edge preference position invalid");
            ExitEdgeMode();if(edgeMode||prefs.Size!=oldSize||!codexFormWorking||codexWorkState!="waiting-input")throw new Exception("edge exit did not restore size and current task");
            CompleteCodexExit();codexTaskRunning=false;codexTasks=null;
        }
        codexTasks=oldTasks;Left=oldLeft;SetPetWindowTop(oldTop);Save();
    }
}
}
